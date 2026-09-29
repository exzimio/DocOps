using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Domain.Documents;
using DocOps.Domain.Tenancy;
using DocOps.Infrastructure.Persistence;
using DocOps.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace DocOps.IntegrationTests;

/// <summary>
/// What the analytics dashboard costs on a catalogue that is not tiny.
///
/// <see href="../../docs/adr/ADR-010-analytics-from-queries.md">ADR-010</see> takes a deliberate
/// debt: every figure is a query executed on the request, with no stored aggregates and no cache,
/// «because a panel that disagrees with the list beside it is the one failure this product cannot
/// have». The cost written down there was a sentence. This turns it into a number.
///
/// <b>Deliberately not a gate.</b> The thresholds below are absurd-only: they catch an algorithmic
/// collapse — a query that went quadratic, an N+1 that crept into the projection — and nothing else.
/// A load test that fails because somebody's laptop is busy would teach the team to ignore it, and
/// an ignored test is worse than no test. The value here is the number printed in the output, and
/// how it moves over time.
///
/// It runs against a container of its own: seeding thousands of documents into the shared database
/// would change the figures every other test reads.
/// </summary>
/// <remarks>
/// Na mesma coleção do resto, embora não use o mesmo contentor. A coleção não é sobre partilhar a
/// base: é sobre <b>não correr ao mesmo tempo</b>. Esta classe levanta um SQL Server só para si, e
/// dois servidores a disputar a mesma máquina fazem falhar testes que nada têm que ver com carga —
/// aconteceu, e os que caíram foram os de isolamento entre organizações, que estavam certos.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ReportingLoadTests(ITestOutputHelper output) : IAsyncLifetime
{
    /// <summary>Two orders of magnitude above the demo catalogue, which has around thirty.</summary>
    private const int DocumentCount = 5_000;

    /// <summary>Absurd-only. A dashboard taking this long is broken, not slow.</summary>
    private static readonly TimeSpan DashboardCeiling = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ListingCeiling = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly DocOpsApiFactory _factory = new();

    public async Task InitializeAsync()
    {
        await ((IAsyncLifetime)_factory).InitializeAsync();
        await SeedBulkDocumentsAsync();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_factory).DisposeAsync();

    [Fact]
    public async Task The_dashboard_and_the_listing_stay_usable_on_a_catalogue_of_thousands()
    {
        HttpClient admin = await _factory.SignInAsync("admin@asteria.example");

        // Before timing anything: prove the endpoints actually see the volume. A tenant filter or a
        // visibility predicate that excluded the seeded rows would produce an empty answer, very
        // fast, and the measurement would be of nothing at all — a test passing by accident, which
        // is the failure mode this suite has had to fix more than once.
        PagedResult<DocumentListItemDto> page =
            (await admin.GetFromJsonAsync<PagedResult<DocumentListItemDto>>(
                "/api/documents?page=1&pageSize=25", Json))!;

        Assert.True(
            page.TotalCount >= DocumentCount,
            $"A listagem só vê {page.TotalCount} documentos; foram semeados {DocumentCount}. "
            + "A medição seguinte estaria a cronometrar uma resposta vazia.");

        // The first call of each pays for the query plans and the connection; measuring it would be
        // measuring the warm-up, which nobody experiences twice.
        await admin.GetAsync("/api/reports/dashboard?days=30");

        TimeSpan dashboard = await MeasureAsync(admin, "/api/reports/dashboard?days=30", runs: 5);
        TimeSpan listing = await MeasureAsync(admin, "/api/documents?page=1&pageSize=25", runs: 5);

        output.WriteLine($"Documentos na base: {DocumentCount + 33}");
        output.WriteLine($"GET /api/reports/dashboard?days=30  mediana: {dashboard.TotalMilliseconds:F0} ms");
        output.WriteLine($"GET /api/documents?page=1&pageSize=25  mediana: {listing.TotalMilliseconds:F0} ms");

        Assert.True(
            dashboard < DashboardCeiling,
            $"O painel demorou {dashboard.TotalMilliseconds:F0} ms com {DocumentCount} documentos. "
            + "Não é lentidão de máquina: é uma consulta que deixou de escalar.");

        Assert.True(
            listing < ListingCeiling,
            $"A listagem demorou {listing.TotalMilliseconds:F0} ms com {DocumentCount} documentos.");
    }

    [Fact]
    public async Task The_listing_costs_the_same_on_the_first_page_and_on_a_deep_one()
    {
        HttpClient admin = await _factory.SignInAsync("admin@asteria.example");

        await admin.GetAsync("/api/documents?page=1&pageSize=25");

        TimeSpan first = await MeasureAsync(admin, "/api/documents?page=1&pageSize=25", runs: 3);
        TimeSpan deep = await MeasureAsync(admin, "/api/documents?page=100&pageSize=25", runs: 3);

        output.WriteLine($"página 1: {first.TotalMilliseconds:F0} ms · página 100: {deep.TotalMilliseconds:F0} ms");

        // Offset paging does get slower with depth — that is how it works. What it must not do is
        // get slower by an order of magnitude, which is what happens when the ordering has no index
        // behind it and the server sorts the whole table to reach row 2500.
        //
        // The floor matters as much as the multiple. Both pages answer in single-digit milliseconds
        // here, and ten times three milliseconds is thirty — a threshold that any noise on a busy
        // machine crosses. This test failed exactly that way when run alongside the rest of the
        // suite, and passed on its own: it was measuring the machine, not the query. Below half a
        // second the difference between the two pages is not a signal about anything.
        TimeSpan allowed = Max(first * 10, TimeSpan.FromMilliseconds(500));

        Assert.True(
            deep < allowed,
            $"A página 100 custou {deep.TotalMilliseconds:F0} ms contra {first.TotalMilliseconds:F0} ms da "
            + "primeira. Paginação por deslocamento piora com a profundidade, mas não assim.");
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static async Task<TimeSpan> MeasureAsync(HttpClient client, string path, int runs)
    {
        List<TimeSpan> times = [];

        for (int run = 0; run < runs; run++)
        {
            long start = Stopwatch.GetTimestamp();

            HttpResponseMessage response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();

            // Read the body: a measurement that stops at the headers is measuring the wrong thing.
            await response.Content.ReadAsStringAsync();

            times.Add(Stopwatch.GetElapsedTime(start));
        }

        // The median, not the average: one unlucky run must not describe the endpoint.
        times.Sort();

        return times[times.Count / 2];
    }

    /// <summary>
    /// Writes the documents straight to the database rather than through the API.
    ///
    /// Five thousand uploads would measure the upload path, take minutes, and fill the disk with
    /// files nobody reads. What is under test here is what the <b>reading</b> costs, and for that
    /// the rows are enough.
    /// </summary>
    private async Task SeedBulkDocumentsAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DocOpsDbContext context = scope.ServiceProvider.GetRequiredService<DocOpsDbContext>();

        Tenant tenant = await context.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == DemoDataSeeder.AsteriaSlug);

        Guid ownerId = await context.Users
            .IgnoreQueryFilters()
            .Where(user => user.TenantId == tenant.Id)
            .Select(user => user.Id)
            .FirstAsync();

        List<Guid> typeIds = await context.DocumentTypes
            .IgnoreQueryFilters()
            .Where(type => type.TenantId == tenant.Id)
            .Select(type => type.Id)
            .ToListAsync();

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Random random = new(20260929);

        context.ChangeTracker.AutoDetectChangesEnabled = false;

        for (int batch = 0; batch < DocumentCount / 500; batch++)
        {
            List<Document> documents = [];

            for (int index = 0; index < 500; index++)
            {
                int sequence = (batch * 500) + index;

                documents.Add(new Document
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    Title = $"Documento de carga {sequence:00000}",
                    Reference = $"LOAD-2026-{sequence:00000}",
                    DocumentTypeId = typeIds[sequence % typeIds.Count],
                    OwnerUserId = ownerId,

                    // Spread across statuses and days so the dashboard's groupings have something to
                    // group: a catalogue where every row is identical measures nothing.
                    Status = (DocumentStatus)(sequence % 8),
                    Classification = (SecurityClassification)(sequence % 4),
                    CreatedAtUtc = now.AddDays(-random.Next(0, 60)).AddMinutes(-sequence)
                });
            }

            context.Documents.AddRange(documents);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
    }
}
