using System.Net;
using System.Net.Http.Json;
using DocOps.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
}
