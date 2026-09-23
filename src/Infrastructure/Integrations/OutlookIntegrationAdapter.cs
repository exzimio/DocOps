using DocOps.Application.Inbox;
using DocOps.Application.Integrations;
using DocOps.Domain.Inbox;
using DocOps.Domain.Integrations;

namespace DocOps.Infrastructure.Integrations;

/// <summary>
/// The mail integration, which is the inbox's own synchronisation seen from the integrations screen.
///
/// It does not reimplement anything: it calls <see cref="IInboxService.SyncAsync(Guid, CancellationToken)"/>
/// and reports what came back. That is the whole point of putting it here — the numbers on the card
/// are the numbers of the mail that really arrived, not a second set kept for display. A card that
/// counted its own imaginary records while the inbox filled up beside it would be a decoration.
///
/// What it is honest about is inherited: the connector behind the inbox says whether it is a
/// demonstration mailbox or a real one, and this adapter passes that through rather than deciding
/// for itself.
/// </summary>
public sealed class OutlookIntegrationAdapter(
    IInboxService inboxService,
    IMailConnector connector) : IIntegrationAdapter
{
    public IntegrationAdapterInfo Info { get; } = new(
        IntegrationKind.MicrosoftOutlook,
        "Microsoft Outlook",
        "Recolhe as mensagens das caixas monitorizadas e coloca-as na caixa de entrada documental.",
        RequiresCredentials: connector.Info.RequiresCredentials,
        IsSimulated: connector.Info.Source == MailSource.Demo,
        SimulationNotice: connector.Info.Source == MailSource.Demo
            ? "As mensagens são geradas localmente por um catálogo fixo. Não há ligação a nenhum "
              + "servidor de correio: o percurso de entrada é real, a origem das mensagens não."
            : null);

    public async Task<IntegrationRunResult> RunAsync(
        IntegrationRunContext context, CancellationToken cancellationToken = default)
    {
        MailSyncResultDto sync = await inboxService.SyncAsync(context.TenantId, cancellationToken);

        List<IntegrationLogEntry> entries =
        [
            new(
                IntegrationLogLevel.Information,
                "sync",
                $"{sync.Added} mensagem(ns) nova(s) de {sync.Fetched} obtida(s).",
                $"Caixas: {string.Join(", ", sync.Mailboxes)}. Já conhecidas: {sync.AlreadyKnown}.",
                sync.Added)
        ];

        return new IntegrationRunResult
        {
            // Nothing new is a perfectly good outcome for a mailbox, not a failure. It is reported
            // as "skipped" so the card's "last activity" only moves when something actually arrived.
            Outcome = sync.Added > 0 ? IntegrationRunOutcome.Succeeded : IntegrationRunOutcome.Skipped,
            RecordsProcessed = sync.Added,
            Summary = sync.Added > 0
                ? $"{sync.Added} mensagem(ns) nova(s) em {sync.Mailboxes.Count} caixa(s)."
                : $"Sem mensagens novas em {sync.Mailboxes.Count} caixa(s).",
            Entries = entries,
            Status = IntegrationStatus.Connected
        };
    }
}
