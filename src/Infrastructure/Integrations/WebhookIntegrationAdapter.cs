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
/// position. Neither is a webhook. The rule is therefore a small allow-list of shapes (HTTPS, a real
/// host name, a public address) rather than a list of things to block.
///
/// Saving checks the address as written. A host name is checked again on every delivery, by
/// <see cref="WebhookConnector"/>, against the addresses it resolves to at that moment.
/// </summary>
public static class WebhookEndpoint
{
    private static readonly System.Net.IPNetwork[] InternalNetworks =
    [
        System.Net.IPNetwork.Parse("0.0.0.0/8"),
        System.Net.IPNetwork.Parse("10.0.0.0/8"),
        System.Net.IPNetwork.Parse("100.64.0.0/10"),
        System.Net.IPNetwork.Parse("127.0.0.0/8"),
        System.Net.IPNetwork.Parse("169.254.0.0/16"),
        System.Net.IPNetwork.Parse("172.16.0.0/12"),
        System.Net.IPNetwork.Parse("192.0.0.0/24"),
        System.Net.IPNetwork.Parse("192.168.0.0/16"),
        System.Net.IPNetwork.Parse("198.18.0.0/15"),
        System.Net.IPNetwork.Parse("224.0.0.0/3"),

        System.Net.IPNetwork.Parse("::/96"),
        System.Net.IPNetwork.Parse("64:ff9b::/96"),
        System.Net.IPNetwork.Parse("64:ff9b:1::/48"),
        System.Net.IPNetwork.Parse("2001::/32"),
        System.Net.IPNetwork.Parse("2002::/16"),
        System.Net.IPNetwork.Parse("fc00::/7"),
        System.Net.IPNetwork.Parse("fe80::/10"),
        System.Net.IPNetwork.Parse("fec0::/10"),
        System.Net.IPNetwork.Parse("ff00::/8")
    ];

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

        if (IPAddress.TryParse(uri.IdnHost, out IPAddress? address) && IsInternal(address))
        {
            reason = "O destino aponta para um endereço interno.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// True for every address that is not plainly on the public internet. IPv4-mapped IPv6 is
    /// unwrapped first, and the IPv6 transition ranges (NAT64, 6to4, Teredo, IPv4-compatible) are
    /// refused whole, because each of them can carry an internal IPv4 address inside.
    /// </summary>
    public static bool IsInternal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        foreach (System.Net.IPNetwork network in InternalNetworks)
        {
            if (network.Contains(address))
            {
                return true;
            }
        }

        return false;
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
    /// An array because EF Core translates <c>Contains</c> on an array into SQL, and on a set it
    /// throws when the query runs.
    /// </summary>
    private static readonly string[] Delivered =
    [
        AuditActions.DocumentCreated,
        AuditActions.DocumentStatusChanged,
        AuditActions.DocumentArchived,
        AuditActions.WorkflowInstanceStarted,
        AuditActions.WorkflowInstanceCompleted,
        AuditActions.WorkflowInstanceCancelled,
        AuditActions.WorkflowTaskCompleted
    ];

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
            catch (HttpRequestException exception) when (WebhookConnector.Refusal(exception) is { } refusal)
            {
                entries.Add(new IntegrationLogEntry(
                    IntegrationLogLevel.Error,
                    "deliver",
                    refusal.Message,
                    "Nada foi enviado. O destino só é contactado se todos os endereços do nome forem públicos."));
                break;
            }
            catch (HttpRequestException)
            {
                // The exception's own message is left out of this log on purpose. It names the
                // address and says whether the port refused or timed out, and the administrators
                // who read this screen could use that to map what the server can reach.
                entries.Add(new IntegrationLogEntry(
                    IntegrationLogLevel.Error,
                    "deliver",
                    $"Não foi possível contactar o destino para «{evt.Action}»."));
                break;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                entries.Add(new IntegrationLogEntry(
                    IntegrationLogLevel.Error,
                    "deliver",
                    $"O destino não respondeu a tempo a «{evt.Action}»."));
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
