using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using DocOps.Application.Abstractions;
using DocOps.Application.Common;
using DocOps.Application.Integrations;
using DocOps.Application.Tenancy;
using DocOps.Domain.Auditing;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.IntegrationTests;

/// <summary>Answers each host name from a fixed table, or fails the lookup as DNS would.</summary>
file sealed class TableResolver(IReadOnlyDictionary<string, string[]> table) : IWebhookHostResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        table.TryGetValue(host, out string[]? answers)
            ? Task.FromResult(answers.Select(IPAddress.Parse).ToArray())
            : throw new SocketException((int)SocketError.HostNotFound);
}

/// <summary>
/// Webhook delivery through the API, with the host names answered by the test. Saving checks the
/// address as written; these prove the check that matters, the one made when the server connects.
/// </summary>
[Collection(ApiCollection.Name)]
public class WebhookDeliveryTests(DocOpsApiFactory factory)
{
    private const string TenantAdmin = "admin@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, string[]> Dns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["metadata.example"] = ["169.254.169.254"],
        ["intranet.example"] = ["fd00::5"],
        ["mapped.example"] = ["::ffff:10.0.0.5"],
        ["carrier-nat.example"] = ["100.64.0.9"],
        ["half-public.example"] = ["93.184.216.34", "10.0.0.5"]
    };

    [Theory]
    [InlineData("metadata.example")]
    [InlineData("intranet.example")]
    [InlineData("mapped.example")]
    [InlineData("carrier-nat.example")]
    [InlineData("half-public.example")]
    public async Task A_name_that_resolves_inside_the_network_is_refused_at_delivery(string host)
    {
        IntegrationLogEntryDto entry = await DeliverOnceAsync($"https://{host}/hook");

        Assert.Equal(IntegrationLogLevel.Error, entry.Level);
        Assert.Contains("endereço interno", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_connection_is_logged_without_the_exception_text()
    {
        IntegrationLogEntryDto entry = await DeliverOnceAsync("https://unknown-host.example/hook");

        Assert.Equal(IntegrationLogLevel.Error, entry.Level);
        Assert.StartsWith("Não foi possível contactar o destino", entry.Summary, StringComparison.Ordinal);

        // The socket's message names the host and port and says how the attempt failed. Written
        // to a screen administrators read, it turns the webhook into a probe of the server's network.
        Assert.Null(entry.Detail);
        Assert.DoesNotContain("unknown-host.example", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_webhook_client_neither_follows_redirects_nor_goes_through_a_proxy()
    {
        using IServiceScope scope = factory.Services.CreateScope();

        HttpMessageHandler handler = scope.ServiceProvider
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(nameof(WebhookIntegrationAdapter));

        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        SocketsHttpHandler primary = Assert.IsType<SocketsHttpHandler>(handler);

        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseProxy);
        Assert.NotNull(primary.ConnectCallback);
    }

    /// <summary>
    /// Points the webhook at <paramref name="endpoint"/>, makes sure there is one event to deliver,
    /// runs the integration and returns the newest log line. The address is cleared afterwards, as
    /// the card was found.
    /// </summary>
    private async Task<IntegrationLogEntryDto> DeliverOnceAsync(string endpoint)
    {
        using WebApplicationFactory<Program> api = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IWebhookHostResolver>(new TableResolver(Dns))));

        HttpClient admin = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri(DocOpsApiFactory.TestOrigin)
        });

        admin.DefaultRequestHeaders.Add("Origin", DocOpsApiFactory.TestOrigin);

        (await admin.PostAsJsonAsync(
            "/api/auth/login",
            new DocOps.Application.Identity.LoginRequest(TenantAdmin, DocOpsApiFactory.SeedPassword)))
            .EnsureSuccessStatusCode();

        IntegrationCardDto card = (await admin.GetFromJsonAsync<IReadOnlyList<IntegrationCardDto>>(
            "/api/integrations", Json))!.Single(c => c.Kind == IntegrationKind.Webhooks);

        try
        {
            (await admin.PutAsJsonAsync(
                $"/api/integrations/{card.Id}",
                new { isEnabled = true, settingsJson = JsonSerializer.Serialize(new { endpoint }) }))
                .EnsureSuccessStatusCode();

            await RecordDeliverableEventAsync(admin);

            HttpResponseMessage ran = await admin.PostAsync($"/api/integrations/{card.Id}/run", null);
            IntegrationRunDto run = (await ran.Content.ReadFromJsonAsync<IntegrationRunDto>(Json))!;

            // A skipped run writes no log line, so it would read as "nothing logged" further down.
            Assert.True(run.Outcome != IntegrationRunOutcome.Skipped, run.Summary);

            PagedResult<IntegrationLogEntryDto> log = (await admin.GetFromJsonAsync<PagedResult<IntegrationLogEntryDto>>(
                $"/api/integrations/{card.Id}/log?pageSize=5", Json))!;

            return log.Items.OrderByDescending(e => e.OccurredAtUtc).First(e => e.Operation == "deliver");
        }
        finally
        {
            await admin.PutAsJsonAsync(
                $"/api/integrations/{card.Id}",
                new { isEnabled = true, settingsJson = JsonSerializer.Serialize(new { endpoint = (string?)null }) });
        }
    }

    private async Task RecordDeliverableEventAsync(HttpClient admin)
    {
        TenantProfileDto tenant = (await admin.GetFromJsonAsync<TenantProfileDto>("/api/tenants/current", Json))!;

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        context.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenant.Id,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ActorDisplayName = "Teste de webhooks",
            Action = AuditActions.DocumentCreated,
            EntityType = AuditEntityTypes.Document,
            EntityId = Guid.CreateVersion7(),
            EntityDisplayName = "FAT-TESTE · Acontecimento para entregar",
            CorrelationId = Guid.NewGuid().ToString("N")
        });

        await context.SaveChangesAsync();
    }
}
