using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Abstractions;
using DocOps.Application.Administration;
using DocOps.Application.Identity;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocOps.IntegrationTests;

/// <summary>
/// What happens to a session that is already open when its account stops being allowed in. The
/// application re-checks a session every five minutes; here the interval is zero, so every request
/// re-checks and the test does not have to wait.
/// </summary>
[Collection(ApiCollection.Name)]
public class SessionRevalidationTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_session_survives_its_re_check_while_the_account_is_active()
    {
        using WebApplicationFactory<Program> api = RevalidatingOnEveryRequest();

        // The re-check reads the account under row-level security. Run before the request knew its
        // organisation, it found nothing and signed everyone out at the first check.
        HttpClient admin = await SignInAsync(api, Administrator, DocOpsApiFactory.SeedPassword);

        for (int request = 0; request < 3; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/documents?pageSize=1")).StatusCode);
        }
    }

    [Fact]
    public async Task Deactivating_an_account_ends_the_sessions_it_already_has()
    {
        using WebApplicationFactory<Program> api = RevalidatingOnEveryRequest();

        HttpClient admin = await SignInAsync(api, Administrator, DocOpsApiFactory.SeedPassword);
        CreatedUserDto created = await CreateUserAsync(admin);
        HttpClient person = await SignInAsync(api, created.User.Email, created.TemporaryPassword);

        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/api/documents?pageSize=1")).StatusCode);

        (await admin.PostAsJsonAsync($"/api/admin/users/{created.User.Id}/active", new { isActive = false }, Json))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await person.GetAsync("/api/documents?pageSize=1")).StatusCode);
    }

    [Fact]
    public async Task An_account_switched_off_outside_the_application_is_refused_at_the_next_check()
    {
        using WebApplicationFactory<Program> api = RevalidatingOnEveryRequest();

        HttpClient admin = await SignInAsync(api, Administrator, DocOpsApiFactory.SeedPassword);
        CreatedUserDto created = await CreateUserAsync(admin);
        HttpClient person = await SignInAsync(api, created.User.Email, created.TemporaryPassword);

        // Straight in the database, as a script or an operator would, so the security stamp does
        // not change: the session has to be refused for being inactive, not for a stale stamp.
        using (ISystemScope scope = factory.CreateServiceScope())
        {
            DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();
            ApplicationUser user = await context.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == created.User.Id);
            user.IsActive = false;
            await context.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await person.GetAsync("/api/documents?pageSize=1")).StatusCode);
    }

    private WebApplicationFactory<Program> RevalidatingOnEveryRequest() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero)));

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> api, string email, string password)
    {
        HttpClient client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            BaseAddress = new Uri(DocOpsApiFactory.TestOrigin)
        });

        client.DefaultRequestHeaders.Add("Origin", DocOpsApiFactory.TestOrigin);

        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password))).EnsureSuccessStatusCode();

        return client;
    }

    private static async Task<CreatedUserDto> CreateUserAsync(HttpClient admin)
    {
        string email = $"sessao.{Guid.CreateVersion7():N}"[..18] + "@asteria.example";

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/admin/users",
            new { email, firstName = "Sessão", lastName = "Aberta", roles = new[] { "Employee" } },
            Json);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CreatedUserDto>(Json))!;
    }
}
