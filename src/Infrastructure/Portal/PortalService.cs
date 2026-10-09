using DocOps.Application.Abstractions;
using DocOps.Application.Documents;
using DocOps.Application.Portal;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Notifications;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Portal;

/// <summary>
/// The external portal, server side.
///
/// Two rules shape everything here.
///
/// The first is that the portal has **no authorisation of its own**. Which documents a supplier may
/// see is decided by <see cref="DocumentAccessResolver.BuildVisibilityPredicate"/> — the same
/// predicate the internal list uses, which already says that an external user reaches a document
/// only by owning it or by an explicit grant, never by a sensitivity label. A portal that decided
/// this for itself would be a second set of rules to keep in agreement with the first, and the two
/// would drift.
///
/// The second is that **the reduction is the feature**. Everything this service returns is built by
/// hand into small DTOs rather than reusing the internal ones, because the internal ones carry
/// owner, department, classification, permissions and the whole process timeline. Projecting
/// deliberately is what keeps an internal note from reaching a supplier by accident the day someone
/// adds a field to a shared DTO.
/// </summary>
public sealed class PortalService(
    DocOpsDbContext dbContext,
    IDocumentService documentService,
    DocumentAccessResolver accessResolver,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IPortalService
{
    /// <summary>Where portal submissions land: the department that receives supplier paperwork.</summary>
    private const string ReceivingDepartmentCode = "FIN";

    public async Task<IReadOnlyList<PortalSubmissionDto>> GetSubmissionsAsync(
        CancellationToken cancellationToken = default)
    {
        AccessSubject subject = await accessResolver.GetSubjectAsync(cancellationToken);

        var rows = await dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, clock.UtcNow))
            .OrderByDescending(document => document.CreatedAtUtc)
            .Select(document => new
            {
                document.Id,
                document.Reference,
                document.Title,
                document.Status,
                document.CreatedAtUtc,
                document.OwnerUserId,
                DocumentNumber = document.MetadataValues
                    .Where(value => value.Field!.Code == "invoiceNumber" || value.Field!.Code == "orderNumber")
                    .Select(value => value.TextValue)
                    .FirstOrDefault(),
                InstanceStatus = dbContext.WorkflowInstances
                    .Where(instance => instance.DocumentId == document.Id)
                    .OrderByDescending(instance => instance.StartedAtUtc)
                    .Select(instance => (WorkflowInstanceStatus?)instance.Status)
                    .FirstOrDefault(),
                // "Waiting on you" has to mean the same thing here as on the submission itself:
                // the last thing said was a question, and this supplier has not answered it yet.
                // Reading it off the open task instead would leave the flag raised for ever, because
                // the task stays open until an internal approver decides — long after the supplier
                // has done its part.
                LastMessageFromOrganisation = dbContext.WorkflowEvents
                    .Where(candidate => dbContext.WorkflowInstances
                        .Any(instance => instance.Id == candidate.WorkflowInstanceId
                            && instance.DocumentId == document.Id))
                    .Where(candidate => candidate.EventType == WorkflowEventTypes.InformationRequested
                        || candidate.EventType == WorkflowEventTypes.InformationProvided)
                    .OrderByDescending(candidate => candidate.OccurredAtUtc)
                    .Select(candidate => (string?)candidate.EventType)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        Guid userId = subject.UserId;

        return
        [
            .. rows.Select(row => new PortalSubmissionDto
            {
                Id = row.Id,
                Reference = row.Reference,
                Title = row.Title,
                Status = row.Status,
                SubmittedAtUtc = row.CreatedAtUtc,
                DocumentNumber = row.DocumentNumber,
                SubmittedHere = row.OwnerUserId == userId,
                Progress = PortalProgressMap.From(row.InstanceStatus, row.Status),
                AwaitingResponse = row.LastMessageFromOrganisation == WorkflowEventTypes.InformationRequested
            })
        ];
    }

    public async Task<PortalSubmissionDetailDto> GetSubmissionAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);

        return await BuildDetailAsync(document, cancellationToken);
    }

    public async Task<PortalSubmissionDto> SubmitAsync(
        SubmitToPortalRequest request, PortalUpload upload, CancellationToken cancellationToken = default)
    {
        Guid tenantId = currentUser.TenantId
            ?? throw new DomainException("Sessão sem organização.", "tenant_missing");

        // Submitted as "unclassified" on purpose, and not as a supplier invoice: the invoice type
        // requires supplier, number, date and total, and a portal that demanded all four before
        // accepting a file would be a worse front door than e-mail. The document lands where the
        // document intelligence picks it up.
        Guid typeId = await dbContext.DocumentTypes
            .Where(type => type.TenantId == tenantId && type.Code == DocumentTypeCodes.Unclassified)
            .Select(type => type.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (typeId == Guid.Empty)
        {
            throw new DomainException("O tipo «Por classificar» não existe nesta organização.", "intake_type_missing");
        }

        string organisation = await OrganisationNameAsync(cancellationToken);

        // A submission has to land somewhere a person will see it. The intake type is confidential
        // by default — nobody has read the file yet — and a confidential document with no department
        // is visible to its owner alone, so without this the supplier would submit into a void.
        // Naming the receiving department is what opens it to the people who handle supplier
        // paperwork, through the classification rule that already exists, rather than by granting a
        // special permission the portal invented. It is finance, because that is who receives
        // supplier invoices, and it is not configurable.
        var receiving = await dbContext.Departments
            .Where(department => department.TenantId == tenantId && department.Code == ReceivingDepartmentCode)
            .Select(department => new { department.Id, department.Name })
            .FirstOrDefaultAsync(cancellationToken);

        DocumentDetailDto created = await documentService.CreateAsync(
            new CreateDocumentRequest
            {
                Title = request.Title.Trim(),
                DocumentTypeId = typeId,
                DepartmentId = receiving?.Id,
                // The supplier says none of the rest, and is not asked to: classification and
                // metadata are the organisation's to decide once someone has read what arrived.
                Description = Description(request, organisation),
                BusinessEntity = organisation
            },
            new DocumentUpload(upload.Content, upload.FileName, upload.ContentType, upload.SizeBytes),
            cancellationToken);

        // Reading it is not enough: starting a process on a document requires Edit, and the
        // classification rule only ever grants the owning department Reader. Without this the
        // submission would arrive, be visible, and be untouchable — the supplier would have posted
        // it into a display case. The grant goes in here rather than through the sharing API on
        // purpose: this is the organisation's standing policy about its own front door, not a
        // supplier deciding who may act on what it sent.
        if (receiving is not null)
        {
            dbContext.DocumentPermissions.Add(new DocumentPermission
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                DocumentId = created.Summary.Id,
                SubjectType = PermissionSubjectType.Department,
                SubjectId = receiving.Id,
                SubjectDisplayName = receiving.Name,
                AccessLevel = DocumentAccessLevel.Contributor,
                GrantedByUserId = currentUser.UserId,
                GrantedAtUtc = clock.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.PortalDocumentSubmitted,
                EntityType = AuditEntityTypes.Document,
                EntityId = created.Summary.Id,
                EntityDisplayName = created.Summary.Reference,
                Summary = $"{organisation} submeteu «{created.Summary.Title}» pelo portal externo.",
                Metadata = new Dictionary<string, object?>
                {
                    ["organisation"] = organisation,
                    ["fileName"] = upload.FileName
                }
            },
            cancellationToken);

        return new PortalSubmissionDto
        {
            Id = created.Summary.Id,
            Reference = created.Summary.Reference,
            Title = created.Summary.Title,
            Status = created.Summary.Status,
            SubmittedAtUtc = created.Summary.CreatedAtUtc,
            DocumentNumber = null,
            SubmittedHere = true,
            Progress = PortalProgress.Received,
            AwaitingResponse = false
        };
    }

    public async Task<PortalSubmissionDetailDto> ProvideInformationAsync(
        Guid documentId, ProvideInformationRequest request, CancellationToken cancellationToken = default)
    {
        Document document = await LoadAsync(documentId, cancellationToken);

        WorkflowInstance? instance = await dbContext.WorkflowInstances
            .Include(candidate => candidate.Tasks)
            .Where(candidate => candidate.DocumentId == document.Id)
            .OrderByDescending(candidate => candidate.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        WorkflowTask? asking = instance?.Tasks.FirstOrDefault(task =>
            task.Decision == WorkflowTaskDecision.InformationRequested
            && task.Status != WorkflowTaskStatus.Completed
            && task.Status != WorkflowTaskStatus.Cancelled);

        if (instance is null || asking is null)
        {
            // Answering a question nobody asked would put text on an approver's timeline out of
            // nowhere. Refusing is the honest outcome, and the portal only offers the box when the
            // same condition holds.
            throw new DomainException(
                "Não há nenhum pedido de informação em aberto para este documento.", "no_open_question");
        }

        string body = request.Body.Trim();
        string organisation = await OrganisationNameAsync(cancellationToken);

        dbContext.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = instance.TenantId,
            WorkflowInstanceId = instance.Id,
            OccurredAtUtc = clock.UtcNow,
            NodeKey = asking.NodeKey,
            EventType = WorkflowEventTypes.InformationProvided,
            Title = $"Resposta de {organisation}",
            Detail = body,
            ActorUserId = currentUser.UserId,
            ActorDisplayName = currentUser.DisplayName ?? organisation
        });

        // Whoever asked is waiting on an answer they have no way of knowing arrived. The timeline
        // entry alone would mean going back to look; the notification is what closes the loop.
        //
        // The recipient is the person who asked, taken from the question's own timeline entry — not
        // the task's assignee, which is empty whenever the step is offered to a department rather
        // than to one person, and that is the common case.
        Guid? asker = await dbContext.WorkflowEvents
            .Where(candidate => candidate.WorkflowInstanceId == instance.Id
                && candidate.EventType == WorkflowEventTypes.InformationRequested)
            .OrderByDescending(candidate => candidate.OccurredAtUtc)
            .Select(candidate => candidate.ActorUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (asker is { } waiting)
        {
            dbContext.Notifications.Add(new Notification
            {
                Id = Guid.CreateVersion7(),
                TenantId = instance.TenantId,
                RecipientUserId = waiting,
                Kind = NotificationKind.Information,
                Title = $"Resposta de {organisation}",
                Body = asking.Title,
                Link = $"/app/workflows/instances/{instance.Id}",
                WorkflowInstanceId = instance.Id,
                WorkflowTaskId = asking.Id,
                DocumentId = document.Id
            });
        }

        // The answer does not advance the process. Whoever asked reads it and decides — a supplier
        // must never be able to approve its own invoice by replying to a question about it.
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.PortalInformationProvided,
                EntityType = AuditEntityTypes.Document,
                EntityId = document.Id,
                EntityDisplayName = document.Reference,
                Summary = $"{organisation} respondeu a um pedido de informação pelo portal externo.",
                Metadata = new Dictionary<string, object?>
                {
                    ["organisation"] = organisation,
                    ["workflowInstanceId"] = instance.Id,
                    ["taskId"] = asking.Id
                }
            },
            cancellationToken);

        return await BuildDetailAsync(document, cancellationToken);
    }

    public async Task<DocumentDownload> DownloadAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        // Loaded through the portal's own check first, so that a document this supplier cannot see is
        // a 404 here rather than whatever the internal download would say about it.
        await LoadAsync(documentId, cancellationToken);

        return await documentService.DownloadAsync(documentId, null, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------------------------

    private async Task<Document> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        AccessSubject subject = await accessResolver.GetSubjectAsync(cancellationToken);

        Document? document = await dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, clock.UtcNow))
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken);

        return document
            ?? throw new EntityNotFoundException(nameof(Document), documentId);
    }

    private async Task<PortalSubmissionDetailDto> BuildDetailAsync(
        Document document, CancellationToken cancellationToken)
    {
        var version = await dbContext.DocumentVersions
            .Where(candidate => candidate.DocumentId == document.Id)
            .OrderByDescending(candidate => candidate.VersionNumber)
            .Select(candidate => new { candidate.FileName, candidate.FileSizeBytes })
            .FirstOrDefaultAsync(cancellationToken);

        var instance = await dbContext.WorkflowInstances
            .Where(candidate => candidate.DocumentId == document.Id)
            .OrderByDescending(candidate => candidate.StartedAtUtc)
            .Select(candidate => new { candidate.Id, candidate.Status })
            .FirstOrDefaultAsync(cancellationToken);

        List<PortalMessageDto> messages = [];

        if (instance is not null)
        {
            // Only the two event types addressed to this supplier. Everything else on the timeline —
            // approvals, rejections and their reasons, escalations, who holds what — is internal,
            // and the way to keep it internal is to never select it in the first place.
            messages =
            [
                .. await dbContext.WorkflowEvents
                    .Where(candidate => candidate.WorkflowInstanceId == instance.Id)
                    .Where(candidate => candidate.EventType == WorkflowEventTypes.InformationRequested
                        || candidate.EventType == WorkflowEventTypes.InformationProvided)
                    .OrderBy(candidate => candidate.OccurredAtUtc)
                    .Select(candidate => new PortalMessageDto
                    {
                        Id = candidate.Id,
                        FromOrganisation = candidate.EventType == WorkflowEventTypes.InformationRequested,
                        Body = candidate.Detail ?? string.Empty,
                        OccurredAtUtc = candidate.OccurredAtUtc
                    })
                    .ToListAsync(cancellationToken)
            ];
        }

        bool awaiting = messages.Count > 0 && messages[^1].FromOrganisation;

        return new PortalSubmissionDetailDto
        {
            Summary = new PortalSubmissionDto
            {
                Id = document.Id,
                Reference = document.Reference,
                Title = document.Title,
                Status = document.Status,
                SubmittedAtUtc = document.CreatedAtUtc,
                DocumentNumber = null,
                SubmittedHere = document.OwnerUserId == currentUser.UserId,
                Progress = PortalProgressMap.From(instance?.Status, document.Status),
                AwaitingResponse = awaiting
            },
            Description = document.Summary,
            FileName = version?.FileName ?? string.Empty,
            FileSizeBytes = version?.FileSizeBytes ?? 0,
            Messages = messages
        };
    }

    private static string Description(SubmitToPortalRequest request, string organisation)
    {
        string note = string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $" {request.Note.Trim()}";

        return $"Submetido por {organisation} através do portal externo.{note}";
    }

    private async Task<string> OrganisationNameAsync(CancellationToken cancellationToken)
    {
        Guid? userId = currentUser.UserId;

        string? organisation = userId is null
            ? null
            : await dbContext.Users
                .Where(user => user.Id == userId.Value)
                .Select(user => user.ExternalOrganisation)
                .FirstOrDefaultAsync(cancellationToken);

        return organisation ?? currentUser.DisplayName ?? "Parceiro externo";
    }
}
