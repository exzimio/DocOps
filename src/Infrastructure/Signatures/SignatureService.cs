using DocOps.Application.Abstractions;
using DocOps.Application.Signatures;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Signatures;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Signatures;

/// <summary>
/// Signature requests.
///
/// **A portfolio demonstration with no legal validity.** Nothing here is a qualified or advanced
/// electronic signature; no private key signs anything and no certificate authority is involved.
/// What this service does is keep an honest record — who was asked, who agreed, when, and over
/// exactly which bytes — and refuse to let that record drift away from the document it describes.
///
/// That refusal is most of the logic. A signature is bound to one version and one content hash, and
/// the service will not sign a request whose document has moved on since it was made: consenting to
/// a document is consenting to its content, and the content changed. Equally, a new version after
/// signing does not rewrite the old record — it makes it stop matching, which the screens say out
/// loud rather than quietly re-pointing the signature at content nobody agreed to.
/// </summary>
public sealed class SignatureService(
    DocOpsDbContext dbContext,
    DocumentAccessResolver accessResolver,
    IAuditService auditService,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : ISignatureService
{
    public async Task<IReadOnlyList<SignatureRequestDto>> GetForDocumentAsync(
        Guid documentId, CancellationToken cancellationToken = default)
    {
        // Seeing a document's signatures requires being able to see the document. The evaluator
        // decides that, as everywhere else.
        await EnsureAccessAsync(documentId, DocumentAccessLevel.View, cancellationToken);

        return await ProjectAsync(
            dbContext.SignatureRequests.Where(request => request.DocumentId == documentId),
            cancellationToken);
    }

    public async Task<IReadOnlyList<SignatureRequestDto>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        Guid userId = CurrentUserId();

        return await ProjectAsync(
            dbContext.SignatureRequests.Where(request =>
                request.SignerUserId == userId && request.Status == SignatureRequestStatus.Pending),
            cancellationToken);
    }

    public async Task<SignatureRequestDto> RequestAsync(
        Guid documentId, RequestSignatureRequest request, CancellationToken cancellationToken = default)
    {
        // Asking for a signature is a change to the document's life, not a read of it.
        Document document = await EnsureAccessAsync(documentId, DocumentAccessLevel.Edit, cancellationToken);

        if (document.CurrentVersionId is not { } versionId)
        {
            throw new DomainException("O documento não tem conteúdo para assinar.", "document_has_no_content");
        }

        DocumentVersion version = await dbContext.DocumentVersions
            .FirstAsync(candidate => candidate.Id == versionId, cancellationToken);

        bool alreadyOpen = await dbContext.SignatureRequests.AnyAsync(
            candidate => candidate.DocumentId == documentId
                && candidate.Status == SignatureRequestStatus.Pending,
            cancellationToken);

        if (alreadyOpen)
        {
            // Two open requests on one document would make "is this signed?" ambiguous, and the
            // second signer would not know whether their signature was the one that counted.
            throw new DomainException(
                "Já existe um pedido de assinatura em aberto para este documento.", "signature_already_open");
        }

        var signer = await dbContext.Users
            .Where(user => user.Id == request.SignerUserId)
            .Select(user => new { user.Id, Name = user.FirstName + " " + user.LastName, user.Email })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException("O signatário indicado não existe.", "signer_not_found");

        // Asking someone to sign a document they cannot open creates a request that can never be
        // fulfilled: the signer would see it in their list and get a refusal on every attempt. The
        // organisation has to grant sight of the document first — that is a deliberate act, and it
        // is not this service's to perform on someone's behalf.
        AccessSubject signerSubject = await accessResolver.BuildSubjectForAsync(signer.Id, cancellationToken);

        if (!signerSubject.TenantPermissions.Contains(Permissions.SignaturesSign))
        {
            // The other half of the same problem: a person who may see the document but holds no
            // right to sign would get a refusal on every attempt, and the request would sit in
            // their list for ever.
            throw new DomainException(
                $"{signer.Name} não tem permissão para assinar documentos.",
                "signer_cannot_sign");
        }

        EffectiveAccess signerAccess = await accessResolver.EvaluateAsync(
            document, clock.UtcNow, signer.Id, cancellationToken);

        if (!signerAccess.Allows(DocumentAccessLevel.View))
        {
            throw new DomainException(
                $"{signer.Name} não tem acesso a este documento e por isso não o poderia assinar. "
                + "Conceda-lhe acesso antes de pedir a assinatura.",
                "signer_cannot_see_document");
        }

        SignatureRequest created = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = document.TenantId,
            DocumentId = document.Id,
            DocumentVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            RequestedContentHash = version.ContentHash,
            SignerUserId = signer.Id,
            SignerDisplayName = signer.Name,
            SignerEmail = signer.Email ?? string.Empty,
            RequestedByUserId = CurrentUserId(),
            RequestedByDisplayName = currentUser.DisplayName ?? "-",
            RequestedAtUtc = clock.UtcNow,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            Status = SignatureRequestStatus.Pending
        };

        dbContext.SignatureRequests.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken);

        // The document says it is waiting, so that it reads the same on every screen that shows it.
        await ApplyStatusAsync(document, DocumentStatus.SignatureRequired, "Pedido de assinatura", cancellationToken);

        await AuditAsync(
            created,
            AuditActions.SignatureRequested,
            $"Assinatura pedida a {created.SignerDisplayName} sobre a versão {created.VersionNumber}.",
            cancellationToken);

        return await SingleAsync(created.Id, cancellationToken);
    }

    public async Task<SignatureRequestDto> SignAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        SignatureRequest request = await LoadAsync(requestId, cancellationToken);
        Guid userId = CurrentUserId();

        if (!SignatureLifecycle.CanSign(request, userId))
        {
            // Not "forbidden because you lack a permission": a signature is a statement by one named
            // person, and nobody makes it for them — an administrator included.
            throw new ForbiddenOperationException(
                "Só o signatário indicado pode assinar este pedido, e apenas enquanto está pendente.");
        }

        string currentHash = await CurrentHashAsync(request.DocumentId, cancellationToken);

        if (SignatureLifecycle.ContentHasChanged(request, currentHash))
        {
            // The document moved on between the request and now. Signing would record consent to
            // content the signer was never shown.
            //
            // Defence in depth rather than the first line: a document awaiting signature does not
            // accept new versions in the first place (DocumentLifecycle.AllowsContentChange). This
            // check is what keeps the guarantee true if that ever changes, and costs one comparison.
            throw new DomainException(
                "O documento mudou desde que a assinatura foi pedida. Peça um novo pedido sobre a versão atual.",
                "signature_content_changed");
        }

        request.Status = SignatureRequestStatus.Signed;
        request.CompletedAtUtc = clock.UtcNow;
        request.SignedContentHash = currentHash;

        await dbContext.SaveChangesAsync(cancellationToken);

        Document document = await dbContext.Documents.FirstAsync(
            candidate => candidate.Id == request.DocumentId, cancellationToken);

        await ApplyStatusAsync(document, DocumentStatus.Signed, "Documento assinado", cancellationToken);

        await AuditAsync(
            request,
            AuditActions.SignatureSigned,
            $"Assinado por {request.SignerDisplayName} sobre a versão {request.VersionNumber}.",
            cancellationToken);

        return await SingleAsync(request.Id, cancellationToken);
    }

    public async Task<SignatureRequestDto> DeclineAsync(
        Guid requestId, DeclineSignatureRequest body, CancellationToken cancellationToken = default)
    {
        SignatureRequest request = await LoadAsync(requestId, cancellationToken);

        if (!SignatureLifecycle.CanSign(request, CurrentUserId()))
        {
            throw new ForbiddenOperationException("Só o signatário indicado pode recusar este pedido.");
        }

        request.Status = SignatureRequestStatus.Declined;
        request.CompletedAtUtc = clock.UtcNow;
        request.DeclineReason = body.Reason.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        // The document is deliberately left in "awaiting signature". A refusal closes this request,
        // not the matter: the organisation may ask someone else, revise the document, or reject it.
        // Deciding that here would be this service making a business decision that is not its own.
        await AuditAsync(
            request,
            AuditActions.SignatureDeclined,
            $"Recusado por {request.SignerDisplayName}: {request.DeclineReason}",
            cancellationToken);

        return await SingleAsync(request.Id, cancellationToken);
    }

    public async Task<SignatureRequestDto> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        SignatureRequest request = await LoadAsync(requestId, cancellationToken);

        if (!SignatureLifecycle.CanCancel(request, CurrentUserId()))
        {
            throw new ForbiddenOperationException(
                "Só quem pediu a assinatura ou o próprio signatário podem retirar o pedido.");
        }

        request.Status = SignatureRequestStatus.Cancelled;
        request.CompletedAtUtc = clock.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditAsync(request, AuditActions.SignatureCancelled, "Pedido de assinatura retirado.", cancellationToken);

        return await SingleAsync(request.Id, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Moves the document to the state the signature implies.
    ///
    /// Deliberately not through `IDocumentService.ChangeStatusAsync`, which applies the
    /// caller's own edit rights: **signing is not editing**. A lawyer asked to sign a finance
    /// document has every business signing it and none editing it, and requiring `Edit` to sign
    /// would mean handing out edit rights for the sole purpose of collecting a signature.
    ///
    /// The transition is still the document's to allow — <see cref="DocumentLifecycle"/> decides,
    /// exactly as it does for the workflow engine, which applies statuses the same way and for the
    /// same reason: the status follows from something that happened, not from who is asking.
    /// </summary>
    private async Task ApplyStatusAsync(
        Document document, DocumentStatus target, string reason, CancellationToken cancellationToken)
    {
        if (document.Status == target || !DocumentLifecycle.CanTransition(document.Status, target))
        {
            return;
        }

        DocumentStatus previous = document.Status;
        document.Status = target;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            new AuditEntry
            {
                Action = AuditActions.DocumentStatusChanged,
                EntityType = AuditEntityTypes.Document,
                EntityId = document.Id,
                EntityDisplayName = document.Reference,
                Summary = $"{reason}: de '{previous}' para '{target}'.",
                Before = new { Status = previous.ToString() },
                After = new { Status = target.ToString() }
            },
            cancellationToken);
    }

    private Guid CurrentUserId() =>
        currentUser.UserId ?? throw new ForbiddenOperationException("Sessão sem utilizador.");

    /// <summary>
    /// Loads the document and checks the caller against it. The access evaluator takes the entity
    /// rather than an id because the decision depends on the document's own facts — owner,
    /// department, classification — and this keeps that lookup in one place.
    /// </summary>
    private async Task<Document> EnsureAccessAsync(
        Guid documentId, DocumentAccessLevel required, CancellationToken cancellationToken)
    {
        Document document = await dbContext.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Document), documentId);

        await accessResolver.EnsureAsync(document, required, clock.UtcNow, cancellationToken);

        return document;
    }

    private async Task<SignatureRequest> LoadAsync(Guid requestId, CancellationToken cancellationToken)
    {
        SignatureRequest request = await dbContext.SignatureRequests
            .FirstOrDefaultAsync(candidate => candidate.Id == requestId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SignatureRequest), requestId);

        // Even the named signer has to be able to see the document: being asked to sign something
        // does not by itself grant sight of it, and the request would be meaningless if it did.
        await EnsureAccessAsync(request.DocumentId, DocumentAccessLevel.View, cancellationToken);

        return request;
    }

    /// <summary>
    /// The hash of the document's current content. It lives on the version, not on the document:
    /// asking the document for "its" hash would be asking a question with no fixed answer.
    /// </summary>
    private async Task<string> CurrentHashAsync(Guid documentId, CancellationToken cancellationToken) =>
        await dbContext.DocumentVersions
            .Where(version => version.DocumentId == documentId)
            .OrderByDescending(version => version.VersionNumber)
            .Select(version => version.ContentHash)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

    private async Task<SignatureRequestDto> SingleAsync(Guid requestId, CancellationToken cancellationToken) =>
        (await ProjectAsync(
            dbContext.SignatureRequests.Where(request => request.Id == requestId), cancellationToken)).Single();

    private async Task<IReadOnlyList<SignatureRequestDto>> ProjectAsync(
        IQueryable<SignatureRequest> query, CancellationToken cancellationToken)
    {
        Guid? userId = currentUser.UserId;

        var rows = await query
            .OrderByDescending(request => request.RequestedAtUtc)
            .Select(request => new
            {
                Request = request,
                Reference = dbContext.Documents
                    .Where(document => document.Id == request.DocumentId)
                    .Select(document => document.Reference)
                    .FirstOrDefault(),
                Title = dbContext.Documents
                    .Where(document => document.Id == request.DocumentId)
                    .Select(document => document.Title)
                    .FirstOrDefault(),
                CurrentHash = dbContext.DocumentVersions
                    .Where(version => version.DocumentId == request.DocumentId)
                    .OrderByDescending(version => version.VersionNumber)
                    .Select(version => version.ContentHash)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new SignatureRequestDto
            {
                Id = row.Request.Id,
                DocumentId = row.Request.DocumentId,
                DocumentReference = row.Reference ?? "-",
                DocumentTitle = row.Title ?? "-",
                VersionNumber = row.Request.VersionNumber,
                RequestedContentHash = row.Request.RequestedContentHash,
                SignedContentHash = row.Request.SignedContentHash,
                ContentStillMatches = !SignatureLifecycle.ContentHasChanged(row.Request, row.CurrentHash ?? string.Empty),
                SignerUserId = row.Request.SignerUserId,
                SignerDisplayName = row.Request.SignerDisplayName,
                SignerEmail = row.Request.SignerEmail,
                RequestedByDisplayName = row.Request.RequestedByDisplayName,
                RequestedAtUtc = row.Request.RequestedAtUtc,
                Reason = row.Request.Reason,
                Status = row.Request.Status,
                CompletedAtUtc = row.Request.CompletedAtUtc,
                DeclineReason = row.Request.DeclineReason,
                CanSign = userId is { } id && SignatureLifecycle.CanSign(row.Request, id),
                CanCancel = userId is { } cancelId && SignatureLifecycle.CanCancel(row.Request, cancelId)
            })
        ];
    }

    /// <summary>
    /// Writes the signature event against the **document**, not against the signature request.
    ///
    /// A signature needs an audit history, and the place people go looking for a document's history is
    /// the document. Anchoring these entries to the request id would put them somewhere technically
    /// correct and practically invisible: the document's trail would show the status change and give
    /// no hint of who signed or over what content. The request's own identity is kept in the
    /// metadata, where it is still queryable.
    /// </summary>
    private Task AuditAsync(
        SignatureRequest request, string action, string summary, CancellationToken cancellationToken) =>
        auditService.WriteAsync(
            new AuditEntry
            {
                Action = action,
                EntityType = AuditEntityTypes.Document,
                EntityId = request.DocumentId,
                EntityDisplayName = $"Assinatura de {request.SignerDisplayName}",
                Summary = summary,
                Metadata = new Dictionary<string, object?>
                {
                    ["signatureRequestId"] = request.Id,
                    ["versionNumber"] = request.VersionNumber,
                    ["contentHash"] = request.RequestedContentHash,
                    ["signer"] = request.SignerEmail
                }
            },
            cancellationToken);
}
