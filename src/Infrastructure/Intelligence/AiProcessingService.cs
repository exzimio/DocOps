using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Intelligence;
using DocOps.Domain.Auditing;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Identity;
using DocOps.Domain.Intelligence;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Intelligence;

/// <inheritdoc cref="IAiProcessingService"/>
public sealed class AiProcessingService(
    DocOpsDbContext dbContext,
    AiDocumentAnalyzer analyzer,
    IAiDocumentService provider,
    DocumentAccessResolver accessResolver,
    DocumentMetadataWriter metadataWriter,
    IAuditService auditService,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock) : IAiProcessingService
{
    public AiProviderDto GetProvider()
    {
        AiProviderInfo info = provider.Provider;
        return new AiProviderDto(info.Source, info.Name, info.Description, info.Source == AiSource.Demo);
    }

    // -----------------------------------------------------------------------------------------
    // Processing
    // -----------------------------------------------------------------------------------------

    public async Task<AiRunDto> ProcessAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadDocumentAsync(documentId, cancellationToken);

        // Authorised before the provider reads a word: processing exists to produce values to write,
        // so it needs the right to edit, and a document whose metadata is locked is not worth reading.
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.Edit, clock.UtcNow, cancellationToken);
        DocumentLifecycle.EnsureCanChangeMetadata(document.Status);

        DocumentVersion version = document.Versions.Single(v => v.IsCurrent);
        DateTimeOffset now = clock.UtcNow;

        // One review at a time per document: a new analysis replaces any that is still pending.
        List<AiProcessingRun> superseded = await dbContext.AiProcessingRuns
            .Where(r => r.DocumentId == document.Id && r.Status == AiRunStatus.AwaitingReview)
            .ToListAsync(cancellationToken);

        foreach (AiProcessingRun old in superseded)
        {
            old.Status = AiRunStatus.Discarded;
            old.FailureReason = "Substituída por uma nova análise do documento.";
        }

        AiProcessingRun run = new()
        {
            TenantId = tenantContext.TenantId,
            DocumentId = document.Id,
            DocumentVersionId = version.Id,
            OriginalDocumentTypeId = document.DocumentTypeId,
            Origin = AiRunOrigin.User,
            Status = AiRunStatus.Queued
        };

        await analyzer.AnalyzeAsync(run, version, now, cancellationToken);

        dbContext.AiProcessingRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        await AuditRunAsync(run, document, cancellationToken);

        return await GetRunAsync(run.Id, cancellationToken);
    }

    public async Task<AiRunDto> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        AiProcessingRun run = await LoadRunAsync(runId, cancellationToken);
        EffectiveAccess access = await accessResolver.EnsureAsync(run.Document!, DocumentAccessLevel.View, clock.UtcNow, cancellationToken);

        return await BuildRunAsync(run, access, cancellationToken);
    }

    public async Task<AiRunDto?> GetLatestRunAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Document document = await LoadDocumentAsync(documentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.View, clock.UtcNow, cancellationToken);

        Guid? latest = await dbContext.AiProcessingRuns
            .Where(r => r.DocumentId == documentId && r.Status != AiRunStatus.Queued)
            .OrderByDescending(r => r.CreatedAtUtc)

            // «A mais recente» tem de ser uma só: com duas corridas no mesmo instante, sem
            // desempate a resposta muda de consulta para consulta. O identificador é da versão 7,
            // ordenado pelo tempo, portanto o maior é mesmo o último.
            .ThenByDescending(r => r.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return latest is { } id ? await GetRunAsync(id, cancellationToken) : null;
    }

    public async Task<PagedResult<AiRunListItemDto>> SearchRunsAsync(
        AiRunSearchRequest request, CancellationToken cancellationToken = default)
    {
        // The queue shows only runs on documents the caller could open anyway.
        IQueryable<AiProcessingRun> query = await VisibleRunsAsync(cancellationToken);

        if (request.Statuses.Length > 0)
        {
            query = query.Where(r => request.Statuses.Contains(r.Status));
        }

        long total = await query.LongCountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(r => r.CreatedAtUtc)

            // Desempate final: analisar varios documentos de seguida cria corridas no mesmo
            // instante.
            .ThenByDescending(r => r.Id)
            .Skip(request.Paging.Skip)
            .Take(request.Paging.NormalizedPageSize)
            .Select(r => new
            {
                r.Id,
                r.DocumentId,
                r.Document!.Reference,
                r.Document.Title,
                TypeName = r.Document.DocumentType!.Name,
                r.Status,
                r.Origin,
                r.Source,
                r.SuggestedDocumentTypeId,
                r.ClassificationConfidence,
                r.OriginalDocumentTypeId,
                r.ClassificationDecision,
                r.CreatedAtUtc,
                r.CreatedByUserId,
                Fields = r.Fields.Select(f => new { f.DocumentTypeId, Found = f.SuggestedValue != null, f.Decision }).ToList()
            })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, string> typeNames = await dbContext.DocumentTypes.ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        Dictionary<Guid, string> userNames = await UserNamesAsync(rows.Select(r => r.CreatedByUserId), cancellationToken);

        List<AiRunListItemDto> items =
        [
            .. rows.Select(r =>
            {
                // Counts are for the type the document is heading to, which is what the reviewer sees.
                Guid target = r.ClassificationDecision == AiSuggestionDecision.Rejected || r.SuggestedDocumentTypeId is null
                    ? r.OriginalDocumentTypeId
                    : r.SuggestedDocumentTypeId.Value;

                var fields = r.Fields.Where(f => f.DocumentTypeId == target).ToList();

                return new AiRunListItemDto(
                    r.Id,
                    r.DocumentId,
                    r.Reference,
                    r.Title,
                    r.TypeName,
                    r.Status,
                    r.Origin,
                    r.Source,
                    r.SuggestedDocumentTypeId is { } s ? typeNames.GetValueOrDefault(s) : null,
                    r.ClassificationConfidence,
                    fields.Count(f => f.Found),
                    fields.Count(f => !f.Found),
                    fields.Count(f => f.Decision != AiSuggestionDecision.Pending),
                    r.CreatedAtUtc,
                    r.CreatedByUserId is { } u ? userNames.GetValueOrDefault(u) : null);
            })
        ];

        return new PagedResult<AiRunListItemDto>(items, request.Paging.NormalizedPage, request.Paging.NormalizedPageSize, total);
    }

    public async Task<AiStatisticsDto> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset since = clock.UtcNow.AddDays(-30);

        // Counted over the documents the caller can see, like the queue: even a count of reviews on
        // documents someone cannot open says something about those documents.
        IQueryable<AiProcessingRun> runs = await VisibleRunsAsync(cancellationToken);

        int awaiting = await runs.CountAsync(r => r.Status == AiRunStatus.AwaitingReview, cancellationToken);
        int applied = await runs.CountAsync(
            r => r.Status == AiRunStatus.Applied && r.AppliedAtUtc >= since, cancellationToken);

        IQueryable<Guid> runIds = runs.Select(r => r.Id);

        var decisions = await dbContext.AiFieldSuggestions
            .Where(f => runIds.Contains(f.RunId) && f.Decision != AiSuggestionDecision.Pending)
            .GroupBy(f => f.Decision)
            .Select(g => new { Decision = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int accepted = decisions.FirstOrDefault(d => d.Decision == AiSuggestionDecision.Accepted)?.Count ?? 0;
        int edited = decisions.FirstOrDefault(d => d.Decision == AiSuggestionDecision.Edited)?.Count ?? 0;
        int rejected = decisions.FirstOrDefault(d => d.Decision == AiSuggestionDecision.Rejected)?.Count ?? 0;
        int decided = accepted + edited + rejected;

        // A corrected value is not an accepted one: the rate measures how often the suggestion was right as given.
        decimal? rate = decided == 0 ? null : Math.Round(accepted / (decimal)decided, 3);

        return new AiStatisticsDto(awaiting, applied, decided, accepted, edited, rejected, rate);
    }

    // -----------------------------------------------------------------------------------------
    // Review
    // -----------------------------------------------------------------------------------------

    public async Task<AiRunDto> DecideFieldAsync(
        Guid runId, Guid suggestionId, DecideFieldRequest request, CancellationToken cancellationToken = default)
    {
        (AiProcessingRun run, EffectiveAccess access) = await LoadForReviewAsync(runId, cancellationToken);

        AiFieldSuggestion suggestion = run.Fields.FirstOrDefault(f => f.Id == suggestionId)
            ?? throw new EntityNotFoundException(nameof(AiFieldSuggestion), suggestionId);

        DocumentType type = await LoadTypeAsync(suggestion.DocumentTypeId, cancellationToken);
        DocumentMetadataField field = type.MetadataFields.First(f =>
            string.Equals(f.Code, suggestion.FieldCode, StringComparison.OrdinalIgnoreCase));

        string? normalised = request.Value;

        if (request.Decision == AiSuggestionDecision.Edited && !string.IsNullOrWhiteSpace(request.Value))
        {
            // Checked now, with the field's own rules, rather than failing later when the run is applied.
            DocumentMetadataValue probe = new() { FieldId = field.Id };
            MetadataValueBinder.Assign(field, probe, request.Value.Trim());
            normalised = probe.TextValue;
        }

        string? suggested = suggestion.SuggestedValue;
        AiReview.Decide(run, suggestion, request.Decision, normalised, RequireUser(), clock.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        (string action, string verb) = request.Decision switch
        {
            AiSuggestionDecision.Accepted => (AuditActions.AiSuggestionAccepted, "aceite"),
            AiSuggestionDecision.Edited => (AuditActions.AiSuggestionEdited, "corrigida"),
            _ => (AuditActions.AiSuggestionRejected, "rejeitada")
        };

        await auditService.WriteAsync(
            DocumentAudit(
                run.Document!,
                action,
                $"Sugestão de IA para '{field.Label}' {verb}",
                before: new { Field = field.Code, Suggested = suggested, suggestion.Confidence },
                after: new { Field = field.Code, Final = suggestion.FinalValue, Decision = suggestion.Decision.ToString() },
                run),
            cancellationToken);

        return await BuildRunAsync(run, access, cancellationToken);
    }

    public async Task<AiRunDto> DecideClassificationAsync(
        Guid runId, DecideClassificationRequest request, CancellationToken cancellationToken = default)
    {
        (AiProcessingRun run, EffectiveAccess access) = await LoadForReviewAsync(runId, cancellationToken);

        AiReview.DecideClassification(run, request.Accept);
        await dbContext.SaveChangesAsync(cancellationToken);

        DocumentType suggested = await LoadTypeAsync(run.SuggestedDocumentTypeId!.Value, cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                run.Document!,
                request.Accept ? AuditActions.AiClassificationAccepted : AuditActions.AiClassificationRejected,
                request.Accept
                    ? $"Classificação sugerida '{suggested.Name}' aceite"
                    : $"Classificação sugerida '{suggested.Name}' rejeitada",
                before: null,
                after: new { SuggestedType = suggested.Code, run.ClassificationConfidence, Accepted = request.Accept },
                run),
            cancellationToken);

        return await BuildRunAsync(run, access, cancellationToken);
    }

    public async Task<AiRunDto> AcceptAllAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        (AiProcessingRun run, EffectiveAccess access) = await LoadForReviewAsync(runId, cancellationToken);

        bool classificationPending = run.SuggestedDocumentTypeId is not null
            && run.ClassificationDecision == AiSuggestionDecision.Pending;

        List<AiFieldSuggestion> pending =
        [
            .. run.Fields.Where(f => f.Decision == AiSuggestionDecision.Pending && f.WasFound)
        ];

        AiReview.AcceptAllFound(run, RequireUser(), clock.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        // "Accept all" is several decisions, and each is audited on its own — the trail must be
        // able to say which values were accepted, not only that a button was pressed.
        if (classificationPending)
        {
            DocumentType suggested = await LoadTypeAsync(run.SuggestedDocumentTypeId!.Value, cancellationToken);

            await auditService.WriteAsync(
                DocumentAudit(
                    run.Document!,
                    AuditActions.AiClassificationAccepted,
                    $"Classificação sugerida '{suggested.Name}' aceite (aceitar tudo)",
                    before: null,
                    after: new { SuggestedType = suggested.Code, run.ClassificationConfidence, Accepted = true },
                    run),
                cancellationToken);
        }

        DocumentType target = await LoadTypeAsync(run.EffectiveDocumentTypeId, cancellationToken);

        foreach (AiFieldSuggestion suggestion in pending.Where(f => f.Decision == AiSuggestionDecision.Accepted))
        {
            string label = target.MetadataFields.FirstOrDefault(f =>
                string.Equals(f.Code, suggestion.FieldCode, StringComparison.OrdinalIgnoreCase))?.Label ?? suggestion.FieldCode;

            await auditService.WriteAsync(
                DocumentAudit(
                    run.Document!,
                    AuditActions.AiSuggestionAccepted,
                    $"Sugestão de IA para '{label}' aceite (aceitar tudo)",
                    before: new { Field = suggestion.FieldCode, Suggested = suggestion.SuggestedValue, suggestion.Confidence },
                    after: new { Field = suggestion.FieldCode, Final = suggestion.FinalValue, Decision = "Accepted" },
                    run),
                cancellationToken);
        }

        return await BuildRunAsync(run, access, cancellationToken);
    }

    public async Task<AiRunDto> ApplyAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        (AiProcessingRun run, EffectiveAccess access) = await LoadForReviewAsync(runId, cancellationToken);
        Document document = run.Document!;

        AiReview.EnsureCanApply(run);
        DocumentLifecycle.EnsureCanChangeMetadata(document.Status);

        if (document.CurrentVersionId != run.DocumentVersionId)
        {
            throw new DomainException(
                "O documento tem uma versão mais recente do que a analisada. Processe-o novamente.", "ai_run_stale");
        }

        if (document.DocumentTypeId != run.OriginalDocumentTypeId)
        {
            throw new DomainException(
                "O tipo do documento mudou desde a análise. Processe-o novamente.", "ai_run_stale");
        }

        DocumentType originalType = await LoadTypeAsync(run.OriginalDocumentTypeId, cancellationToken);
        DocumentType targetType = await LoadTypeAsync(run.EffectiveDocumentTypeId, cancellationToken);

        HashSet<string> filled = FilledFieldCodes(document, originalType);
        IReadOnlyList<string> missing = AiReview.MissingRequiredFields(
            run, targetType.MetadataFields.Where(f => f.IsRequired).OrderBy(f => f.DisplayOrder).Select(f => f.Code), filled);

        if (missing.Count > 0)
        {
            string labels = string.Join(", ", targetType.MetadataFields
                .OrderBy(f => f.DisplayOrder)
                .Where(f => missing.Contains(f.Code, StringComparer.OrdinalIgnoreCase))
                .Select(f => f.Label));

            throw new DomainException(
                $"Faltam campos obrigatórios de '{targetType.Name}': {labels}. Indique-os antes de aplicar.",
                "ai_required_fields_missing");
        }

        var before = new
        {
            Type = originalType.Code,
            Metadata = MetadataSnapshot(document, originalType)
        };

        bool reclassified = targetType.Id != originalType.Id;

        if (reclassified)
        {
            metadataWriter.ChangeType(document, originalType, targetType);
        }

        IReadOnlyDictionary<string, string> values = AiReview.ValuesToApply(run);

        Dictionary<string, decimal> confidence = run.Fields
            .Where(f => f.DocumentTypeId == targetType.Id && f.Decision == AiSuggestionDecision.Accepted)
            .ToDictionary(f => f.FieldCode, f => f.Confidence, StringComparer.OrdinalIgnoreCase);

        metadataWriter.Apply(
            document,
            targetType,
            values.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase),
            confidence);

        run.Status = AiRunStatus.Applied;
        run.AppliedAtUtc = clock.UtcNow;
        run.AppliedByUserId = RequireUser();

        // Document, values and run change in one transaction: either the document has the reviewed
        // values and the run says so, or neither happened.
        await dbContext.SaveChangesAsync(cancellationToken);

        var after = new
        {
            Type = targetType.Code,
            Metadata = MetadataSnapshot(document, targetType)
        };

        if (reclassified)
        {
            await auditService.WriteAsync(
                DocumentAudit(
                    document,
                    AuditActions.DocumentReclassified,
                    $"Documento reclassificado de '{originalType.Name}' para '{targetType.Name}'",
                    before: new { Type = originalType.Code },
                    after: new { Type = targetType.Code, Classification = document.Classification.ToString() },
                    run),
                cancellationToken);
        }

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.AiRunApplied,
                $"Sugestões de IA aplicadas: {values.Count} campo(s)",
                before,
                after,
                run),
            cancellationToken);

        return await BuildRunAsync(run, access, cancellationToken);
    }

    public async Task<AiRunDto> DiscardAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        (AiProcessingRun run, EffectiveAccess access) = await LoadForReviewAsync(runId, cancellationToken);

        run.Status = AiRunStatus.Discarded;
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(run.Document!, AuditActions.AiRunDiscarded, "Análise de IA rejeitada; nada foi aplicado", null, null, run),
            cancellationToken);

        return await BuildRunAsync(run, access, cancellationToken);
    }

    // -----------------------------------------------------------------------------------------
    // Questions
    // -----------------------------------------------------------------------------------------

    public async Task<DocumentAnswerDto> AskAsync(AskDocumentRequest request, CancellationToken cancellationToken = default)
    {
        Document document = await LoadDocumentAsync(request.DocumentId, cancellationToken);
        await accessResolver.EnsureAsync(document, DocumentAccessLevel.View, clock.UtcNow, cancellationToken);

        DocumentVersion version = document.Versions.Single(v => v.IsCurrent);
        string question = request.Question.Trim();
        AiProviderInfo info = provider.Provider;

        DocumentAnswerDto answer = IsApprovalQuestion(question)
            ? await AnswerFromApprovalsAsync(document, info, cancellationToken)
            : await AnswerFromTextAsync(document, version, question, info, cancellationToken);

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                AuditActions.DocumentQuestionAsked,
                "Pergunta feita ao documento",
                before: null,
                after: new { Question = question.Length > 200 ? question[..200] : question, answer.Answered, answer.Source },
                run: null),
            cancellationToken);

        return answer;
    }

    private async Task<DocumentAnswerDto> AnswerFromTextAsync(
        Document document, DocumentVersion version, string question, AiProviderInfo info, CancellationToken cancellationToken)
    {
        DocumentText text = new(version.ExtractedText);
        DocumentType type = await LoadTypeAsync(document.DocumentTypeId, cancellationToken);
        DocumentTypeProfile? schema = null;

        if (type.Code != DocumentTypeCodes.Unclassified)
        {
            schema = AiDocumentAnalyzer.ToProfile(type);
        }
        else
        {
            // An unclassified document can still be asked about: read it as what it most likely is.
            List<(DocumentType Type, DocumentTypeProfile Profile)> types = await analyzer.LoadTypesAsync(document.TenantId, cancellationToken);
            List<DocumentTypeProfile> candidates = [.. types.Where(t => t.Type.Code != DocumentTypeCodes.Unclassified).Select(t => t.Profile)];

            ClassificationResult classification = await provider.ClassifyAsync(text, candidates, cancellationToken);
            ClassificationCandidate? best = classification.Candidates.FirstOrDefault();

            if (best is not null && best.Confidence >= AiReview.MediumConfidence)
            {
                schema = candidates.First(c => c.Code == best.TypeCode);
            }
        }

        DocumentAnswer raw = await provider.AnswerAsync(text, schema, question, cancellationToken);

        // The citation guard: only citations that match the document's text are shown. An answer
        // whose every citation failed is withheld — an unsupported claim is not an answer.
        List<TextReference> verified = [.. raw.Citations.Where(text.Verifies)];
        int discarded = raw.Citations.Count - verified.Count;
        bool answered = raw.Answered && (verified.Count > 0 || raw.Citations.Count == 0);

        string answerText = answered
            ? raw.Text
            : discarded > 0
                ? "A resposta obtida não pôde ser confirmada no texto do documento e não é apresentada."
                : raw.Text;

        return new DocumentAnswerDto
        {
            Answered = answered,
            Text = answerText,
            Confidence = answered ? raw.Confidence : 0m,
            Source = info.Source,
            ProviderName = info.Name,
            DiscardedCitations = discarded,
            Citations =
            [
                .. verified.Select(c => new AiCitationDto
                {
                    Kind = "document",
                    DocumentTitle = document.Title,
                    FileName = version.FileName,
                    VersionNumber = version.VersionNumber,
                    Page = c.Page,
                    Line = c.Line,
                    Section = c.Section,
                    Excerpt = c.Excerpt
                })
            ]
        };
    }

    /// <summary>
    /// "Who approved this?" is not in the text; it is in the system's own records. Answered from the
    /// completed approval tasks, each cited as the record it came from — not by any provider.
    /// </summary>
    private async Task<DocumentAnswerDto> AnswerFromApprovalsAsync(
        Document document, AiProviderInfo info, CancellationToken cancellationToken)
    {
        var approvals = await dbContext.WorkflowTasks
            .Where(t => t.Instance!.DocumentId == document.Id
                && t.Decision == WorkflowTaskDecision.Approved
                && t.CompletedAtUtc != null)
            .OrderBy(t => t.CompletedAtUtc)
            .Select(t => new { t.Title, t.CompletedAtUtc, t.CompletedByUserId, t.DecisionComment })
            .ToListAsync(cancellationToken);

        if (approvals.Count == 0)
        {
            return new DocumentAnswerDto
            {
                Answered = false,
                Text = "Não há registo de aprovação deste documento.",
                Confidence = 0m,
                Source = info.Source,
                ProviderName = info.Name,
                DiscardedCitations = 0,
                Citations = []
            };
        }

        Dictionary<Guid, string> names = await UserNamesAsync(approvals.Select(a => a.CompletedByUserId), cancellationToken);

        string Who(Guid? id) => id is { } u ? names.GetValueOrDefault(u, "-") : "-";

        string text = approvals.Count == 1
            ? $"Aprovado por {Who(approvals[0].CompletedByUserId)} ({approvals[0].Title})."
            : $"Aprovado por {string.Join(", ", approvals.Select(a => $"{Who(a.CompletedByUserId)} ({a.Title})"))}.";

        return new DocumentAnswerDto
        {
            Answered = true,
            Text = text,
            Confidence = 1m,
            Source = info.Source,
            ProviderName = info.Name,
            DiscardedCitations = 0,
            Citations =
            [
                .. approvals.Select(a => new AiCitationDto
                {
                    Kind = "audit",
                    DocumentTitle = document.Title,
                    Excerpt = a.DecisionComment is { Length: > 0 } comment
                        ? $"{a.Title}, por {Who(a.CompletedByUserId)}: «{comment}»"
                        : $"{a.Title}, por {Who(a.CompletedByUserId)}",
                    OccurredAtUtc = a.CompletedAtUtc
                })
            ]
        };
    }

    // -----------------------------------------------------------------------------------------
    // Internals
    // -----------------------------------------------------------------------------------------

    private async Task<(AiProcessingRun Run, EffectiveAccess Access)> LoadForReviewAsync(
        Guid runId, CancellationToken cancellationToken)
    {
        AiProcessingRun run = await LoadRunAsync(runId, cancellationToken);
        EffectiveAccess access = await accessResolver.EnsureAsync(run.Document!, DocumentAccessLevel.Edit, clock.UtcNow, cancellationToken);
        AiReview.EnsureReviewable(run);

        return (run, access);
    }

    private async Task<IQueryable<AiProcessingRun>> VisibleRunsAsync(CancellationToken cancellationToken)
    {
        AccessSubject subject = await accessResolver.GetSubjectAsync(cancellationToken);
        IQueryable<Guid> visibleDocuments = dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, clock.UtcNow))
            .Select(d => d.Id);

        return dbContext.AiProcessingRuns.Where(r => visibleDocuments.Contains(r.DocumentId));
    }

    private async Task<Document> LoadDocumentAsync(Guid documentId, CancellationToken cancellationToken) =>
        await dbContext.Documents
            .Include(d => d.Versions)
            .Include(d => d.MetadataValues)
            .Include(d => d.Permissions)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(Document), documentId);

    private async Task<AiProcessingRun> LoadRunAsync(Guid runId, CancellationToken cancellationToken) =>
        await dbContext.AiProcessingRuns
            .Include(r => r.Fields)
            .Include(r => r.Document).ThenInclude(d => d!.Versions)
            .Include(r => r.Document).ThenInclude(d => d!.MetadataValues)
            .Include(r => r.Document).ThenInclude(d => d!.Permissions)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(AiProcessingRun), runId);

    private async Task<DocumentType> LoadTypeAsync(Guid typeId, CancellationToken cancellationToken) =>
        await dbContext.DocumentTypes
            .Include(t => t.MetadataFields)
            .FirstOrDefaultAsync(t => t.Id == typeId, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(DocumentType), typeId);

    private async Task<AiRunDto> BuildRunAsync(AiProcessingRun run, EffectiveAccess access, CancellationToken cancellationToken)
    {
        Document document = run.Document!;
        DocumentType original = await LoadTypeAsync(run.OriginalDocumentTypeId, cancellationToken);
        DocumentType? suggested = run.SuggestedDocumentTypeId is { } s ? await LoadTypeAsync(s, cancellationToken) : null;
        DocumentType target = run.EffectiveDocumentTypeId == original.Id ? original : suggested!;

        bool typeUnchanged = target.Id == document.DocumentTypeId;
        int versionNumber = document.Versions.FirstOrDefault(v => v.Id == run.DocumentVersionId)?.VersionNumber ?? 0;
        bool stale = document.CurrentVersionId != run.DocumentVersionId || document.DocumentTypeId != run.OriginalDocumentTypeId;

        Dictionary<Guid, string> names = await UserNamesAsync(
            [run.CreatedByUserId, run.AppliedByUserId, .. run.Fields.Select(f => f.DecidedByUserId)],
            cancellationToken);

        List<AiFieldSuggestionDto> fields =
        [
            .. target.MetadataFields
                .OrderBy(f => f.DisplayOrder)
                .Select(field =>
                {
                    AiFieldSuggestion? suggestion = run.Fields.FirstOrDefault(x =>
                        x.DocumentTypeId == target.Id && string.Equals(x.FieldCode, field.Code, StringComparison.OrdinalIgnoreCase));

                    if (suggestion is null)
                    {
                        return null;
                    }

                    string? current = typeUnchanged
                        ? document.MetadataValues.FirstOrDefault(v => v.FieldId == field.Id)?.TextValue
                        : null;

                    return new AiFieldSuggestionDto
                    {
                        Id = suggestion.Id,
                        FieldCode = field.Code,
                        Label = field.Label,
                        FieldType = field.FieldType,
                        IsRequired = field.IsRequired,
                        Options = AiDocumentAnalyzer.ParseOptions(field.OptionsJson),
                        SuggestedValue = suggestion.SuggestedValue,
                        Confidence = suggestion.Confidence,
                        Band = AiReview.Band(suggestion.Confidence),
                        Evidence = suggestion.EvidenceLine is { } line
                            ? new TextReferenceDto(line, suggestion.EvidencePage ?? 1, null, suggestion.EvidenceText ?? string.Empty)
                            : null,
                        Decision = suggestion.Decision,
                        FinalValue = suggestion.FinalValue,
                        CurrentValue = current,
                        DecidedByName = suggestion.DecidedByUserId is { } u ? names.GetValueOrDefault(u) : null,
                        DecidedAtUtc = suggestion.DecidedAtUtc
                    };
                })
                .Where(f => f is not null)
                .Select(f => f!)
        ];

        IReadOnlyList<string> missingCodes = run.Status == AiRunStatus.AwaitingReview
            ? AiReview.MissingRequiredFields(
                run,
                target.MetadataFields.Where(f => f.IsRequired).OrderBy(f => f.DisplayOrder).Select(f => f.Code),
                FilledFieldCodes(document, original))
            : [];

        List<string> missingLabels =
        [
            .. target.MetadataFields
                .OrderBy(f => f.DisplayOrder)
                .Where(f => missingCodes.Contains(f.Code, StringComparer.OrdinalIgnoreCase))
                .Select(f => f.Label)
        ];

        bool canReview = run.Status == AiRunStatus.AwaitingReview
            && access.Allows(DocumentAccessLevel.Edit)
            && currentUser.HasPermission(Permissions.AiUse)
            && DocumentLifecycle.AllowsMetadataChange(document.Status);

        TextReference? classificationEvidence = AiDocumentAnalyzer.Deserialize(run.ClassificationEvidence);

        return new AiRunDto
        {
            Id = run.Id,
            DocumentId = document.Id,
            DocumentReference = document.Reference,
            DocumentTitle = document.Title,
            VersionNumber = versionNumber,
            Status = run.Status,
            Origin = run.Origin,
            Source = run.Source,
            ProviderName = run.ProviderName,
            CreatedAtUtc = run.CreatedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            AppliedAtUtc = run.AppliedAtUtc,
            RequestedByName = run.CreatedByUserId is { } c ? names.GetValueOrDefault(c) : null,
            AppliedByName = run.AppliedByUserId is { } a ? names.GetValueOrDefault(a) : null,
            FailureReason = run.FailureReason,
            OriginalType = new AiDocumentTypeRef(original.Id, original.Code, original.Name),
            SuggestedType = suggested is null ? null : new AiDocumentTypeRef(suggested.Id, suggested.Code, suggested.Name),
            ClassificationConfidence = run.ClassificationConfidence,
            ClassificationBand = run.ClassificationConfidence is { } cc ? AiReview.Band(cc) : null,
            ClassificationEvidence = classificationEvidence is null
                ? null
                : new TextReferenceDto(classificationEvidence.Line, classificationEvidence.Page, classificationEvidence.Section, classificationEvidence.Excerpt),
            ClassificationDecision = run.ClassificationDecision,
            Fields = fields,
            MissingRequiredFields = missingLabels,
            IsStale = stale,
            CanReview = canReview,
            CanApply = canReview && !stale && missingLabels.Count == 0 && !run.ClassificationUndecided
        };
    }

    private static HashSet<string> FilledFieldCodes(Document document, DocumentType type)
    {
        HashSet<Guid> filled = [.. document.MetadataValues.Select(v => v.FieldId)];
        return new HashSet<string>(
            type.MetadataFields.Where(f => filled.Contains(f.Id)).Select(f => f.Code),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> MetadataSnapshot(Document document, DocumentType type) =>
        type.MetadataFields
            .OrderBy(f => f.DisplayOrder)
            .ToDictionary(
                f => f.Code,
                f => document.MetadataValues.FirstOrDefault(v => v.FieldId == f.Id)?.TextValue);

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        Guid[] wanted = [.. ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct()];

        if (wanted.Length == 0)
        {
            return [];
        }

        return await dbContext.Users
            .Where(u => wanted.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
            .ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken);
    }

    private Guid RequireUser() =>
        currentUser.UserId ?? throw new ForbiddenOperationException("É necessário um utilizador autenticado.");

    private async Task AuditRunAsync(AiProcessingRun run, Document document, CancellationToken cancellationToken)
    {
        bool failed = run.Status == AiRunStatus.Failed;

        await auditService.WriteAsync(
            DocumentAudit(
                document,
                failed ? AuditActions.AiRunFailed : AuditActions.AiRunCreated,
                failed
                    ? "Análise de IA sem resultado"
                    : $"Documento analisado por '{run.ProviderName}': {run.Fields.Count(f => f.WasFound)} valor(es) sugerido(s)",
                before: null,
                after: new
                {
                    Source = run.Source.ToString(),
                    run.ProviderName,
                    run.SuggestedDocumentTypeId,
                    run.ClassificationConfidence,
                    Found = run.Fields.Count(f => f.WasFound),
                    Missing = run.Fields.Count(f => !f.WasFound),
                    run.FailureReason
                },
                run),
            cancellationToken);
    }

    private static bool IsApprovalQuestion(string question)
    {
        string folded = TextValueParser.Fold(question);
        return DemoVocabulary.ApprovalTerms.Any(term => folded.Contains(term, StringComparison.Ordinal));
    }

    private static AuditEntry DocumentAudit(
        Document document, string action, string summary, object? before, object? after, AiProcessingRun? run) =>
        new()
        {
            Action = action,
            EntityType = AuditEntityTypes.Document,
            EntityId = document.Id,
            EntityDisplayName = $"{document.Reference} · {document.Title}",
            Summary = summary,
            Before = before,
            After = after,
            Metadata = run is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["aiRunId"] = run.Id,
                    ["aiSource"] = run.Source.ToString(),
                    ["aiProvider"] = run.ProviderName
                }
        };
}
