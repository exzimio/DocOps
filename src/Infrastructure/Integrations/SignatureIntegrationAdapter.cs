using DocOps.Application.Integrations;
using DocOps.Domain.Documents;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Integrations;

/// <summary>
/// Reports on the documents waiting to be signed.
///
/// There is no signature provider configured, and this says so rather than pretending — which is the
/// whole difference between a card and a placeholder. What it is **not** is empty: it counts the
/// documents that really are sitting in "aguarda assinatura" right now and reports that number, so
/// the card answers a question an operator actually has. When the signature phase adds a provider,
/// this adapter starts sending those same documents instead of only counting them; the card does not
/// change shape.
///
/// It never claims a signature has legal validity. That claim is the one this project must never
/// make, and no status here implies it.
/// </summary>
public sealed class SignatureIntegrationAdapter(DocOpsDbContext dbContext) : IIntegrationAdapter
{
    public IntegrationAdapterInfo Info { get; } = new(
        IntegrationKind.ElectronicSignature,
        "Assinatura eletrónica",
        "Acompanha os documentos que aguardam assinatura e entrega-os a um fornecedor de assinatura.",
        RequiresCredentials: true,
        IsSimulated: false);

    public async Task<IntegrationRunResult> RunAsync(
        IntegrationRunContext context, CancellationToken cancellationToken = default)
    {
        int waiting = await dbContext.Documents
            .IgnoreQueryFilters()
            .CountAsync(
                document =>
                    document.TenantId == context.TenantId
                    && !document.IsDeleted
                    && document.Status == DocumentStatus.SignatureRequired,
                cancellationToken);

        // No provider is configured, so nothing can be sent. The count is still worth reporting: it
        // is the real size of the queue that a provider would receive.
        return new IntegrationRunResult
        {
            Outcome = IntegrationRunOutcome.Skipped,
            RecordsProcessed = 0,
            Summary = waiting == 0
                ? "Nenhum documento aguarda assinatura. Sem fornecedor de assinatura configurado."
                : $"{waiting} documento(s) aguardam assinatura. Sem fornecedor de assinatura configurado, "
                  + "pelo que nenhum foi enviado.",
            Status = IntegrationStatus.NotConfigured,
            Entries =
            [
                new IntegrationLogEntry(
                    IntegrationLogLevel.Information,
                    "poll",
                    $"{waiting} documento(s) em «aguarda assinatura».",
                    "Não há fornecedor de assinatura configurado nesta instalação, por isso os "
                    + "documentos ficam à espera em vez de serem enviados.")
            ]
        };
    }
}
