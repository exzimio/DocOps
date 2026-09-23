using System.Globalization;
using DocOps.Domain.Common;
using DocOps.Domain.Integrations;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// The integration step.
///
/// It does not call the far system: doing that here would hold the engine's transaction open for as
/// long as the other end takes to answer, which is the same reason the AI steps queue instead of
/// calling. The step records a request and parks the instance; the worker runs the integration and
/// resumes the instance, and on the second visit the step reads the result into
/// <c>integration.*</c> variables and moves on.
///
/// It fails loudly rather than quietly skipping. A step pointing at an integration this organisation
/// does not have, or at one an administrator switched off, stops the process with a reason — a
/// process that silently carried on as if it had notified the ERP would be worse than one that
/// stopped.
/// </summary>
public sealed class IntegrationNodeExecutor(DocOpsDbContext dbContext) : INodeExecutor
{
    /// <summary>Branch followed when the integration ran and failed.</summary>
    public const string FailedBranch = "failed";

    public WorkflowNodeType NodeType => WorkflowNodeType.Integration;

    public async Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);

        if (config.IntegrationKind is not { } kind)
        {
            throw new DomainException(
                $"O passo de integração '{node.Key}' não indica qual a integração a executar.",
                "integration_node_not_configured");
        }

        IntegrationRunRequest? request = await dbContext.IntegrationRunRequests
            .Where(candidate =>
                candidate.WorkflowInstanceId == context.Instance.Id
                && candidate.WorkflowNodeKey == node.Key)
            .OrderByDescending(candidate => candidate.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            Integration integration = await dbContext.Integrations
                .FirstOrDefaultAsync(
                    candidate => candidate.TenantId == context.Instance.TenantId && candidate.Kind == kind,
                    cancellationToken)
                ?? throw new DomainException(
                    $"A integração '{kind}' não existe nesta organização.", "integration_node_unknown");

            if (!integration.IsEnabled)
            {
                throw new DomainException(
                    $"A integração '{integration.Name}' está desativada.", "integration_node_disabled");
            }

            dbContext.IntegrationRunRequests.Add(new IntegrationRunRequest
            {
                TenantId = context.Instance.TenantId,
                IntegrationId = integration.Id,
                WorkflowInstanceId = context.Instance.Id,
                WorkflowNodeKey = node.Key,
                Status = IntegrationRequestStatus.Queued,
                RequestedAtUtc = context.Now,
                CreatedAtUtc = context.Now
            });

            context.AddEvent(
                WorkflowEventTypes.IntegrationRequested,
                $"Integração '{integration.Name}' pedida em '{node.Name}'",
                node.Key);

            return NodeResult.Wait();
        }

        if (request.Status == IntegrationRequestStatus.Queued)
        {
            return NodeResult.Wait();
        }

        context.Variables["integration.kind"] = kind.ToString();
        context.Variables["integration.records"] =
            request.RecordsProcessed.ToString(CultureInfo.InvariantCulture);

        if (request.Status == IntegrationRequestStatus.Failed)
        {
            context.Variables["integration.status"] = "failed";

            context.AddEvent(
                WorkflowEventTypes.IntegrationFailed,
                $"Integração falhou em '{node.Name}'",
                node.Key,
                request.FailureReason);

            return NodeResult.Continue(FailedBranch);
        }

        context.Variables["integration.status"] = "completed";

        context.AddEvent(
            WorkflowEventTypes.IntegrationCompleted,
            $"Integração concluída em '{node.Name}'",
            node.Key,
            request.ResultSummary);

        return NodeResult.Continue();
    }
}
