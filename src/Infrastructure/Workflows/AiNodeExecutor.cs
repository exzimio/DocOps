using System.Globalization;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Intelligence;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// The AI classification and AI extraction steps. Neither calls a provider: calling one inside the
/// engine's transaction would hold a database transaction open for as long as a model takes to
/// answer. The step queues an analysis and parks the instance; the worker runs the analysis and
/// resumes the instance, and on that second visit the step reads the result into variables.
///
/// The step never applies anything to the document. The analysis waits for a person exactly like
/// one requested from the document page — "the AI suggests, the person decides" holds inside a
/// process too. A process that should continue on the extracted values uses a condition on the
/// <c>ai.*</c> variables, and a human task to validate them.
/// </summary>
public sealed class AiNodeExecutor(DocOpsDbContext dbContext, WorkflowNodeType nodeType) : INodeExecutor
{
    /// <summary>Branch followed when the analysis could not be made, e.g. a scanned file with no text.</summary>
    public const string FailedBranch = "failed";

    public WorkflowNodeType NodeType { get; } = nodeType;

    public async Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        if (context.Instance.DocumentId is not { } documentId)
        {
            throw new DomainException("Um passo de IA precisa de um documento associado ao processo.", "ai_node_requires_document");
        }

        Document document = await dbContext.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            ?? throw new DomainException("O documento do processo já não existe.", "ai_node_document_missing");

        // One analysis per document version per instance: an extraction step after a classification
        // step reads the same analysis instead of asking for a second one.
        AiProcessingRun? run = await dbContext.AiProcessingRuns
            .Include(r => r.Fields)
            .Where(r => r.WorkflowInstanceId == context.Instance.Id && r.DocumentVersionId == document.CurrentVersionId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            dbContext.AiProcessingRuns.Add(new AiProcessingRun
            {
                TenantId = context.Instance.TenantId,
                DocumentId = document.Id,
                DocumentVersionId = document.CurrentVersionId
                    ?? throw new DomainException("O documento do processo não tem conteúdo.", "ai_node_document_missing"),
                OriginalDocumentTypeId = document.DocumentTypeId,
                Origin = AiRunOrigin.Workflow,
                Status = AiRunStatus.Queued,
                WorkflowInstanceId = context.Instance.Id,
                WorkflowNodeKey = node.Key,
                CreatedAtUtc = context.Now
            });

            context.AddEvent(WorkflowEventTypes.AiRequested, $"Análise de IA pedida em '{node.Name}'", node.Key);

            return NodeResult.Wait();
        }

        if (run.Status == AiRunStatus.Queued)
        {
            return NodeResult.Wait();
        }

        if (run.Status == AiRunStatus.Failed)
        {
            context.Variables["ai.status"] = "failed";
            context.AddEvent(WorkflowEventTypes.AiFailed, $"Análise de IA sem resultado em '{node.Name}'", node.Key, run.FailureReason);

            return NodeResult.Continue(FailedBranch);
        }

        context.Variables["ai.status"] = "completed";
        context.Variables["ai.runId"] = run.Id.ToString();

        if (NodeType == WorkflowNodeType.AiClassification)
        {
            await WriteClassificationAsync(context, run, cancellationToken);
        }
        else
        {
            await WriteExtractionAsync(context, run, cancellationToken);
        }

        context.AddEvent(
            WorkflowEventTypes.AiCompleted,
            $"Análise de IA concluída em '{node.Name}'",
            node.Key,
            $"Fonte: {run.ProviderName}. As sugestões aguardam revisão no documento.");

        return NodeResult.Continue();
    }

    private async Task WriteClassificationAsync(
        WorkflowExecutionContext context, AiProcessingRun run, CancellationToken cancellationToken)
    {
        string? code = run.SuggestedDocumentTypeId is { } typeId
            ? await dbContext.DocumentTypes.Where(t => t.Id == typeId).Select(t => t.Code).FirstOrDefaultAsync(cancellationToken)
            : null;

        context.Variables["ai.suggestedType"] = code;
        context.Variables["ai.classificationConfidence"] =
            run.ClassificationConfidence?.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private async Task WriteExtractionAsync(
        WorkflowExecutionContext context, AiProcessingRun run, CancellationToken cancellationToken)
    {
        Guid target = run.EffectiveDocumentTypeId;

        List<DocumentMetadataField> fields = await dbContext.DocumentMetadataFields
            .Where(f => f.DocumentTypeId == target)
            .ToListAsync(cancellationToken);

        List<AiFieldSuggestion> suggestions = [.. run.Fields.Where(f => f.DocumentTypeId == target)];

        // Suggested values, not accepted ones: conditions may route on them (a large amount goes to a
        // senior approver) but nothing is written to the document here.
        foreach (AiFieldSuggestion suggestion in suggestions.Where(s => s.WasFound))
        {
            context.Variables[$"ai.{suggestion.FieldCode}"] = suggestion.SuggestedValue;
        }

        int requiredMissing = fields.Count(f =>
            f.IsRequired && !suggestions.Any(s => s.WasFound && string.Equals(s.FieldCode, f.Code, StringComparison.OrdinalIgnoreCase)));

        context.Variables["ai.fieldsFound"] = suggestions.Count(s => s.WasFound).ToString(CultureInfo.InvariantCulture);
        context.Variables["ai.requiredMissing"] = requiredMissing.ToString(CultureInfo.InvariantCulture);
    }
}
