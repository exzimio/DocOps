using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Integrations;
using DocOps.Domain.Auditing;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocOps.Infrastructure.Integrations;

/// <summary>The non-secret settings this integration keeps. A signing secret would not live here.</summary>
public sealed record WebhookSettings
{
    /// <summary>Where to deliver. Must be HTTPS and must not point inside the network (see <see cref="WebhookEndpoint"/>).</summary>
    public string? Endpoint { get; init; }

    /// <summary>Everything up to here has been delivered. Advances only on a successful delivery.</summary>
    public DateTimeOffset? DeliveredThroughUtc { get; init; }
}

/// <summary>
/// Decides whether an address is one this application is willing to call.
///
/// An administrator typing an address that the server then fetches is server-side request forgery
/// waiting to happen: <c>http://169.254.169.254/</c> is a cloud metadata service, and
/// <c>http://localhost:5180/</c> is this application talking to itself with its own network
/// position. Neither is a webhook. The rule is therefore a small allow-list of shapes — HTTPS, a
/// real host name, a public address — rather than a list of things to block.
/// </summary>
public static class WebhookEndpoint
{
    public static bool IsAcceptable(string? candidate, out string reason)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            reason = "Nenhum destino configurado.";
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
        {
            reason = "O destino não é um endereço absoluto.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = "O destino tem de usar HTTPS.";
            return false;
        }

        if (uri.IsLoopback)
        {
            reason = "O destino aponta para a própria máquina.";
            return false;
        }

        // A literal address is checked directly; a host name is only accepted as a name, because
        // resolving it here and trusting the answer later is its own race.
        if (IPAddress.TryParse(uri.Host, out IPAddress? address) && IsInternal(address))
        {
            reason = "O destino aponta para um endereço interno.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsInternal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();

        return bytes.Length == 4
            && (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254));
    }
}

/// <summary>
/// Delivers the events that matter to an address the organisation configured.
///
/// Nothing is simulated here — it really posts, over HTTPS, to wherever it is pointed. What it
/// delivers is a change feed over the audit trail: the actions worth telling another system about,
/// in order, with a cursor so that a delivery is attempted once. Without an address it reports
/// itself as not configured, which is true, instead of quietly succeeding at doing nothing.
///
/// Only the fact of an event is sent — action, entity, reference, when. Never the document, never
/// its contents, never who may see it. A webhook is a notification, not an export.
/// </summary>
public sealed class WebhookIntegrationAdapter(
    HttpClient httpClient,
    DocOpsDbContext dbContext) : IIntegrationAdapter
{
    /// <summary>How many events one run will deliver, so a long backlog is drained in slices.</summary>
    private const int BatchSize = 25;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The actions another system plausibly cares about. A webhook that fired on every audit line —
    /// including every document anyone merely opened — would be noise, and would leak who reads what.
    /// </summary>
    private static readonly IReadOnlySet<string> Delivered = new HashSet<string>(StringComparer.Ordinal)
    {
        AuditActions.DocumentCreated,
        AuditActions.DocumentStatusChanged,
        AuditActions.DocumentArchived,
        AuditActions.WorkflowInstanceStarted,
        AuditActions.WorkflowInstanceCompleted,
        AuditActions.WorkflowInstanceCancelled,
        AuditActions.WorkflowTaskCompleted
    };

    public IntegrationAdapterInfo Info { get; } = new(
        IntegrationKind.Webhooks,
        "Webhooks",
        "Entrega os acontecimentos relevantes, como documentos criados, estados alterados e processos "
        + "concluídos, a um endereço HTTPS configurado pela organização.",
        RequiresCredentials: false,
        IsSimulated: false);

    public async Task<IntegrationRunResult> RunAsync(
        IntegrationRunContext context, CancellationToken cancellationToken = default)
    {
        WebhookSettings settings = Read(context.Settings);

        if (!WebhookEndpoint.IsAcceptable(settings.Endpoint, out string reason))
        {
            return IntegrationRunResult.Skipped(reason, IntegrationStatus.NotConfigured);
        }

        DateTimeOffset since = settings.DeliveredThroughUtc ?? context.StartedAtUtc.AddDays(-7);

        var pending = await dbContext.AuditEvents
            .IgnoreQueryFilters()
            .Where(entry =>
                entry.TenantId == context.TenantId
                && entry.OccurredAtUtc > since
                && Delivered.Contains(entry.Action))
            .OrderBy(entry => entry.OccurredAtUtc)
            .Take(BatchSize)
            .Select(entry => new
            {
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.EntityDisplayName,
                entry.OccurredAtUtc
            })
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return IntegrationRunResult.Skipped("Sem acontecimentos novos para entregar.", IntegrationStatus.Connected);
        }

        List<IntegrationLogEntry> entries = [];
        int delivered = 0;
        DateTimeOffset cursor = since;

        foreach (var evt in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                    settings.Endpoint!,
                    new
                    {
                        evt.Action,
                        evt.EntityType,
                        evt.EntityId,
                        Reference = evt.EntityDisplayName,
                        OccurredAt = evt.OccurredAtUtc
                    },
                    Json,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // Stop at the first refusal: delivering the rest would put them out of order,
                    // and the cursor must not pass something that never arrived.
                    entries.Add(new IntegrationLogEntry(
                        IntegrationLogLevel.Error,
                        "deliver",
                        $"O destino respondeu {(int)response.StatusCode} a «{evt.Action}».",
                        "As entregas seguintes ficam para a próxima execução, para não saírem por ordem."));
                    break;
                }

                delivered++;
                cursor = evt.OccurredAtUtc;
            }
            catch (HttpRequestException exception)
            {
                entries.Add(new IntegrationLogEntry(
                    IntegrationLogLevel.Error,
                    "deliver",
                    $"Não foi possível contactar o destino para «{evt.Action}».",
                    exception.Message));
                break;
            }
        }

        if (delivered > 0)
        {
            entries.Insert(0, new IntegrationLogEntry(
                IntegrationLogLevel.Information,
                "deliver",
                $"{delivered} acontecimento(s) entregue(s).",
                RecordsProcessed: delivered));
        }

        bool anyError = entries.Any(entry => entry.Level == IntegrationLogLevel.Error);

        return new IntegrationRunResult
        {
            Outcome = anyError
                ? delivered > 0 ? IntegrationRunOutcome.PartiallyFailed : IntegrationRunOutcome.Failed
                : IntegrationRunOutcome.Succeeded,
            RecordsProcessed = delivered,
            Summary = anyError
                ? $"{delivered} entregue(s); a entrega parou num erro do destino."
                : $"{delivered} acontecimento(s) entregue(s).",
            Entries = entries,

            // The cursor lives in the settings, so a delivery is attempted once even across restarts.
            Status = anyError ? IntegrationStatus.Degraded : IntegrationStatus.Connected,
            UpdatedSettings = delivered > 0
                ? JsonSerializer.Serialize(settings with { DeliveredThroughUtc = cursor }, Json)
                : null
        };
    }

    private static WebhookSettings Read(string? settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return new WebhookSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<WebhookSettings>(settings, Json) ?? new WebhookSettings();
        }
        catch (JsonException)
        {
            return new WebhookSettings();
        }
    }
}
