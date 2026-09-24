using DocOps.Domain.Common;
using DocOps.Domain.Signatures;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// The signature step: asks a named person to sign the document the process is running on, and
/// parks until they act.
///
/// **A portfolio demonstration with no legal validity.** What is recorded is a person's agreement
/// over a stated content hash, not a qualified electronic signature.
///
/// The step follows the same shape as a human task — create, wait, read the outcome on re-entry —
/// rather than the queue-and-worker shape of the AI and integration steps, because nothing is being
/// called: it is a person who has to act, and people are exactly what the engine already knows how
/// to wait for.
///
/// A refusal is a legitimate outcome, not a failure. It follows the <c>declined</c> branch when the
/// process has one; without such a branch the process stops, because carrying on as if a document
/// had been signed when someone refused to sign it would be the worst possible default.
/// </summary>
public sealed class SignatureNodeExecutor(
    DocOpsDbContext dbContext, TaskAssignmentResolver assignmentResolver) : INodeExecutor
{
    /// <summary>Branch followed when the named signer refused.</summary>
    public const string DeclinedBranch = "declined";

    public WorkflowNodeType NodeType => WorkflowNodeType.SignatureRequest;

    public async Task<NodeResult> ExecuteAsync(
        WorkflowExecutionContext context, WorkflowNode node, CancellationToken cancellationToken)
    {
        if (context.Instance.DocumentId is not { } documentId)
        {
            throw new DomainException(
                $"O passo de assinatura '{node.Key}' precisa de um documento, e este processo não corre sobre nenhum.",
                "signature_node_without_document");
        }

        SignatureRequest? existing = await dbContext.SignatureRequests
            .Where(request => request.WorkflowInstanceId == context.Instance.Id
                && request.WorkflowNodeKey == node.Key)
            .OrderByDescending(request => request.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            SignatureRequest created = await CreateAsync(context, node, documentId, cancellationToken);

            context.AddEvent(
                WorkflowEventTypes.SignatureRequested,
                $"Assinatura pedida a {created.SignerDisplayName} em '{node.Name}'",
                node.Key,
                $"Versão {created.VersionNumber}, conteúdo {Short(created.RequestedContentHash)}");

            return NodeResult.Wait();
        }

        if (existing.Status == SignatureRequestStatus.Pending)
        {
            return NodeResult.Wait();
        }

        context.Variables["signature.status"] = existing.Status.ToString().ToLowerInvariant();
        context.Variables["signature.signer"] = existing.SignerDisplayName;

        if (existing.Status == SignatureRequestStatus.Signed)
        {
            context.AddEvent(
                WorkflowEventTypes.SignatureCompleted,
                $"Assinado por {existing.SignerDisplayName}",
                node.Key,
                $"Conteúdo {Short(existing.SignedContentHash ?? existing.RequestedContentHash)}");

            return NodeResult.Continue();
        }

        // Declined or cancelled: both leave the document unsigned, and the process must say so.
        context.AddEvent(
            WorkflowEventTypes.SignatureCompleted,
            existing.Status == SignatureRequestStatus.Declined
                ? $"{existing.SignerDisplayName} recusou assinar"
                : "Pedido de assinatura retirado",
            node.Key,
            existing.DeclineReason);

        return NodeResult.Continue(DeclinedBranch);
    }

    private async Task<SignatureRequest> CreateAsync(
        WorkflowExecutionContext context, WorkflowNode node, Guid documentId, CancellationToken cancellationToken)
    {
        NodeConfiguration config = NodeConfiguration.Parse(node.ConfigJson);

        // Only a single named person can sign. A department or a role is resolved to one user here,
        // because "the finance department signed this" is not a statement anyone can stand behind.
        ResolvedAssignment assignment = await assignmentResolver.ResolveAsync(
            config.Signer ?? config.Assignment, context.Instance, cancellationToken);

        if (assignment.UserId is not { } signerId)
        {
            throw new DomainException(
                $"O passo de assinatura '{node.Key}' não resolveu um signatário concreto. "
                + "Uma assinatura é de uma pessoa, não de um grupo.",
                "signature_node_without_signer");
        }

        var version = await dbContext.DocumentVersions
            .Where(candidate => candidate.DocumentId == documentId)
            .OrderByDescending(candidate => candidate.VersionNumber)
            .Select(candidate => new { candidate.Id, candidate.VersionNumber, candidate.ContentHash })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(
                "O documento não tem conteúdo para assinar.", "document_has_no_content");

        var signer = await dbContext.Users
            .Where(user => user.Id == signerId)
            .Select(user => new { Name = user.FirstName + " " + user.LastName, user.Email })
            .FirstAsync(cancellationToken);

        SignatureRequest request = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = context.Instance.TenantId,
            DocumentId = documentId,
            DocumentVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            RequestedContentHash = version.ContentHash,
            SignerUserId = signerId,
            SignerDisplayName = signer.Name,
            SignerEmail = signer.Email ?? string.Empty,
            RequestedByUserId = context.ActorUserId ?? signerId,
            RequestedByDisplayName = context.ActorDisplayName,
            RequestedAtUtc = context.Now,
            Reason = config.SignatureReason,
            Status = SignatureRequestStatus.Pending,
            WorkflowInstanceId = context.Instance.Id,
            WorkflowNodeKey = node.Key,
            CreatedAtUtc = context.Now
        };

        dbContext.SignatureRequests.Add(request);

        return request;
    }

    /// <summary>First twelve characters of a hash: enough to compare by eye, short enough to read.</summary>
    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12];
}
