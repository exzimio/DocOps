using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Integrations;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DocOps.Application.Abstractions;

namespace DocOps.IntegrationTests;

/// <summary>
/// The integrations centre against a real SQL Server.
///
/// The point most worth proving here is that the screen is not a display case: running the ERP card
/// reconciles the documents this organisation really has, and running the Outlook card is the
/// inbox's own synchronisation — the numbers on a card are numbers from the repository, not a
/// second set kept for show.
/// </summary>
[Collection(ApiCollection.Name)]
public class IntegrationsTests(DocOpsApiFactory factory)
{
    private const string TenantAdmin = "admin@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";
    private const string Employee = "ana.ferreira@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<IReadOnlyList<IntegrationCardDto>> CardsAsync(HttpClient client)
    {
        IReadOnlyList<IntegrationCardDto>? cards =
            await client.GetFromJsonAsync<IReadOnlyList<IntegrationCardDto>>("/api/integrations", Json);

        Assert.NotNull(cards);

        return cards;
    }

    private static async Task<IntegrationCardDto> CardAsync(HttpClient client, IntegrationKind kind) =>
        (await CardsAsync(client)).Single(card => card.Kind == kind);

    // -----------------------------------------------------------------------------------------
    // Catalogue, authorization and isolation
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_card_has_an_adapter_behind_it()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        IReadOnlyList<IntegrationCardDto> cards = await CardsAsync(admin);

        // The catalogue is built from the registered adapters, so a card without one would mean a
        // screen offering a button that could not do anything.
        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.True(card.HasAdapter));
        Assert.Equal(Enum.GetValues<IntegrationKind>().Length, cards.Count);
    }

    /// <summary>
    /// The honesty rule, checked rather than trusted: whatever is simulated says so, and says what.
    /// </summary>
    [Fact]
    public async Task Anything_simulated_says_so_and_says_what_is_simulated()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        IReadOnlyList<IntegrationCardDto> cards = await CardsAsync(admin);

        Assert.All(
            cards.Where(card => card.IsSimulated),
            card => Assert.False(string.IsNullOrWhiteSpace(card.SimulationNotice)));

        // And the ones that are not simulated do not carry a notice that would confuse the matter.
        Assert.All(
            cards.Where(card => !card.IsSimulated),
            card => Assert.True(card.SimulationNotice is null));
    }

    [Fact]
    public async Task Starts_as_not_configured_rather_than_claiming_to_be_connected()
    {
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        // Vertice has never run anything, so nothing may claim otherwise.
        IReadOnlyList<IntegrationCardDto> cards = await CardsAsync(vertice);

        Assert.All(cards, card => Assert.Equal(IntegrationStatus.NotConfigured, card.Status));
        Assert.All(cards, card => Assert.Null(card.LastRunAtUtc));
    }

    [Fact]
    public async Task Refuses_a_caller_without_the_view_permission()
    {
        HttpClient employee = await factory.SignInAsync(Employee);

        HttpResponseMessage response = await employee.GetAsync("/api/integrations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Watching and changing are different permissions, and the split is enforced server-side.</summary>
    [Fact]
    public async Task A_caller_who_may_watch_may_not_run_or_configure()
    {
        HttpClient manager = await factory.SignInAsync(Manager);
        IntegrationCardDto card = await CardAsync(manager, IntegrationKind.Erp);

        HttpResponseMessage run = await manager.PostAsync($"/api/integrations/{card.Id}/run", content: null);
        HttpResponseMessage update = await manager.PutAsJsonAsync(
            $"/api/integrations/{card.Id}", new { isEnabled = false });

        Assert.Equal(HttpStatusCode.Forbidden, run.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task One_organisation_cannot_see_or_touch_another_ones_integrations()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        IntegrationCardDto asteriaCard = await CardAsync(admin, IntegrationKind.Erp);
        IReadOnlyList<IntegrationCardDto> verticeCards = await CardsAsync(vertice);

        Assert.DoesNotContain(verticeCards, card => card.Id == asteriaCard.Id);

        HttpResponseMessage read = await vertice.GetAsync($"/api/integrations/{asteriaCard.Id}");
        HttpResponseMessage log = await vertice.GetAsync($"/api/integrations/{asteriaCard.Id}/log");
        HttpResponseMessage run = await vertice.PostAsync($"/api/integrations/{asteriaCard.Id}/run", content: null);

        // Not "forbidden": from the other organisation's side it does not exist at all.
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, log.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // Running
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_erp_card_reconciles_the_documents_that_really_exist()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        IntegrationCardDto card = await CardAsync(admin, IntegrationKind.Erp);

        HttpResponseMessage response = await admin.PostAsync($"/api/integrations/{card.Id}/run", content: null);
        response.EnsureSuccessStatusCode();
        IntegrationRunDto? run = await response.Content.ReadFromJsonAsync<IntegrationRunDto>(Json);

        Assert.NotNull(run);
        Assert.Equal(IntegrationRunOutcome.Succeeded, run.Outcome);

        // The count is the number of supplier values in this repository, not a number the adapter
        // made up: it matches what the database holds.
        using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext dbContext = scope.Services.GetRequiredService<DocOpsDbContext>();

        int suppliers = await dbContext.DocumentMetadataValues
            .IgnoreQueryFilters()
            .CountAsync(value =>
                value.TextValue != null
                && !value.Document!.IsDeleted
                && value.Field!.Code == "supplier");

        Assert.Equal(suppliers, run.RecordsProcessed);

        // And the discrepancy it found is a real one: a supplier the simulated ERP does not hold.
        PagedResult<IntegrationLogEntryDto>? log =
            await admin.GetFromJsonAsync<PagedResult<IntegrationLogEntryDto>>(
                $"/api/integrations/{card.Id}/log?levels={IntegrationLogLevel.Warning}", Json);

        Assert.NotNull(log);
        Assert.NotEmpty(log.Items);
    }

    [Fact]
    public async Task A_run_moves_the_card_off_not_configured_and_writes_its_own_record()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        IntegrationCardDto before = await CardAsync(admin, IntegrationKind.Crm);

        await admin.PostAsync($"/api/integrations/{before.Id}/run", content: null);

        IntegrationCardDto after = await CardAsync(admin, IntegrationKind.Crm);

        Assert.Equal(IntegrationStatus.Connected, after.Status);
        Assert.NotNull(after.LastRunAtUtc);
        Assert.True(after.RecordsProcessed > before.RecordsProcessed);
        Assert.False(string.IsNullOrWhiteSpace(after.LastRunSummary));

        PagedResult<IntegrationLogEntryDto>? log =
            await admin.GetFromJsonAsync<PagedResult<IntegrationLogEntryDto>>(
                $"/api/integrations/{before.Id}/log", Json);

        Assert.NotNull(log);
        Assert.NotEmpty(log.Items);
        Assert.All(log.Items, entry => Assert.True(entry.Manual));
    }

    /// <summary>
    /// The mail card is the inbox's own synchronisation, so running it twice adds nothing the second
    /// time — the same idempotence the inbox has, seen from the other screen.
    /// </summary>
    [Fact]
    public async Task The_mail_card_is_the_inbox_synchronisation_and_stays_idempotent()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        IntegrationCardDto card = await CardAsync(admin, IntegrationKind.MicrosoftOutlook);

        await admin.PostAsync($"/api/integrations/{card.Id}/run", content: null);
        IntegrationCardDto afterFirst = await CardAsync(admin, IntegrationKind.MicrosoftOutlook);

        await admin.PostAsync($"/api/integrations/{card.Id}/run", content: null);
        IntegrationCardDto afterSecond = await CardAsync(admin, IntegrationKind.MicrosoftOutlook);

        Assert.Equal(afterFirst.RecordsProcessed, afterSecond.RecordsProcessed);
    }

    [Fact]
    public async Task A_disabled_integration_refuses_to_run()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        IntegrationCardDto card = await CardAsync(admin, IntegrationKind.FileImport);

        HttpResponseMessage disabled = await admin.PutAsJsonAsync(
            $"/api/integrations/{card.Id}", new { isEnabled = false });
        disabled.EnsureSuccessStatusCode();
        IntegrationCardDto? off = await disabled.Content.ReadFromJsonAsync<IntegrationCardDto>(Json);

        HttpResponseMessage run = await admin.PostAsync($"/api/integrations/{card.Id}/run", content: null);

        Assert.NotNull(off);
        Assert.Equal(IntegrationStatus.Disabled, off.Status);
        Assert.Equal(HttpStatusCode.Conflict, run.StatusCode);

        // Left as it was found, so this test can run again against the same database.
        await admin.PutAsJsonAsync($"/api/integrations/{card.Id}", new { isEnabled = true });
    }

    // -----------------------------------------------------------------------------------------
    // Webhook destinations
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// An administrator typing an address that this server then calls is server-side request forgery
    /// unless somebody checks it. This is that check, and it is on the server.
    /// </summary>
    [Theory]
    [InlineData("http://example.example/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.0.0.5/hook")]
    [InlineData("https://192.168.1.10/hook")]
    [InlineData("https://100.64.0.1/hook")]
    [InlineData("https://0.0.0.0/hook")]
    [InlineData("https://[fd00::1]/hook")]
    [InlineData("https://[fe80::1]/hook")]
    [InlineData("https://[::ffff:169.254.169.254]/hook")]
    [InlineData("not-a-url")]
    public async Task Refuses_a_webhook_destination_that_should_not_be_called(string endpoint)
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        IntegrationCardDto card = await CardAsync(admin, IntegrationKind.Webhooks);

        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"/api/integrations/{card.Id}",
            new { isEnabled = true, settingsJson = JsonSerializer.Serialize(new { endpoint }) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Accepts_an_https_destination_and_lets_it_be_cleared_again()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);
        IntegrationCardDto card = await CardAsync(admin, IntegrationKind.Webhooks);

        HttpResponseMessage set = await admin.PutAsJsonAsync(
            $"/api/integrations/{card.Id}",
            new
            {
                isEnabled = true,
                settingsJson = JsonSerializer.Serialize(new { endpoint = "https://hooks.example/docops" })
            });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        // Clearing it is how delivery is turned off again, so an empty address is accepted.
        HttpResponseMessage cleared = await admin.PutAsJsonAsync(
            $"/api/integrations/{card.Id}",
            new { isEnabled = true, settingsJson = JsonSerializer.Serialize(new { endpoint = (string?)null }) });

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
    }

    // -----------------------------------------------------------------------------------------
    // The workflow step
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The integration step end to end: it parks the process rather than calling the far system
    /// inside the engine's transaction, the worker runs the request, and the resumed process reads
    /// the result into its own variables.
    /// </summary>
    [Fact]
    public async Task A_workflow_integration_step_parks_the_process_and_the_worker_lets_it_go()
    {
        HttpClient admin = await factory.SignInAsync(TenantAdmin);

        using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext dbContext = scope.Services.GetRequiredService<DocOpsDbContext>();

        Guid tenantId = await dbContext.Tenants
            .IgnoreQueryFilters()
            .Where(tenant => tenant.Slug == "asteria-group")
            .Select(tenant => tenant.Id)
            .FirstAsync();

        Guid integrationId = await dbContext.Integrations
            .IgnoreQueryFilters()
            .Where(integration => integration.TenantId == tenantId && integration.Kind == IntegrationKind.Crm)
            .Select(integration => integration.Id)
            .FirstAsync();

        Guid instanceId = await dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Where(instance => instance.TenantId == tenantId)
            .Select(instance => instance.Id)
            .FirstAsync();

        // The request a step would have queued. Writing it directly keeps this test about the queue
        // and the worker, rather than about designing a workflow through the API.
        dbContext.IntegrationRunRequests.Add(new IntegrationRunRequest
        {
            TenantId = tenantId,
            IntegrationId = integrationId,
            WorkflowInstanceId = instanceId,
            WorkflowNodeKey = $"test-{Guid.NewGuid():N}",
            Status = IntegrationRequestStatus.Queued,
            RequestedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        // O processador da fila e componente de worker: corre acima de todas as organizacoes, e
        // com a politica a negar por omissao tem de o declarar.
        using ISystemScope workerScope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        IntegrationQueueProcessor processor =
            workerScope.Services.GetRequiredService<IntegrationQueueProcessor>();

        int processed = await processor.ProcessQueuedAsync(default);

        Assert.True(processed > 0);

        using ISystemScope readScope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext reader = readScope.Services.GetRequiredService<DocOpsDbContext>();

        List<IntegrationRunRequest> requests = await reader.IntegrationRunRequests
            .IgnoreQueryFilters()
            .Where(request => request.WorkflowInstanceId == instanceId)
            .ToListAsync();

        // Nothing is left queued: a process parked for ever on a step nobody can see is worse than
        // one that took its failure branch.
        Assert.All(requests, request => Assert.NotEqual(IntegrationRequestStatus.Queued, request.Status));
        Assert.All(requests, request => Assert.NotNull(request.CompletedAtUtc));
    }

    [Fact]
    public async Task The_scheduled_runner_serves_every_organisation_without_a_user()
    {
        using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        IIntegrationRunner runner = scope.Services.GetRequiredService<IIntegrationRunner>();

        int ran = await runner.RunDueAsync(default);

        // Both seeded organisations have the full catalogue enabled, so the sweep reaches both.
        Assert.True(ran >= Enum.GetValues<IntegrationKind>().Length);
    }
}
