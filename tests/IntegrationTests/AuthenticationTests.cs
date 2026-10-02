using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Identity;
using DocOps.Domain.Auditing;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DocOps.Application.Abstractions;

namespace DocOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthenticationTests(DocOpsApiFactory factory)
{
    private const string FinanceApprover = "ricardo.lopes@asteria.example";

    [Fact]
    public async Task Sign_in_returns_the_profile_and_issues_an_http_only_session_cookie()
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(FinanceApprover, DocOpsApiFactory.SeedPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CurrentUserDto? user = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        Assert.NotNull(user);
        Assert.Equal(FinanceApprover, user.Email);
        Assert.Equal("Asteria Group", user.TenantName);
        Assert.Contains(RoleNames.FinanceApprover, user.Roles);
        Assert.Contains(Permissions.DocumentsApprove, user.Permissions);
        Assert.DoesNotContain(Permissions.AdminTenants, user.Permissions);
        Assert.Contains(user.Departments, d => d is { Code: "FIN", IsPrimary: true });

        string setCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("docops.session", StringComparison.Ordinal));

        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DocOpsApiFactory.SeedPassword, setCookie, StringComparison.Ordinal);

        // A outra metade da mesma regra. O cookie ser HttpOnly não vale nada se a resposta trouxer
        // a credencial no corpo, onde qualquer script a lê — e é assim que a regra se perde na
        // prática: alguém acrescenta um token ao corpo para resolver um problema de sessão.
        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DocOpsApiFactory.SeedPassword, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_the_same_answer_as_an_unknown_account()
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(FinanceApprover, "Definitely!Wrong1"));

        HttpResponseMessage unknownUser = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("nobody@asteria.example", "Definitely!Wrong1"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);

        string wrongPasswordCode = await ReadCodeAsync(wrongPassword);
        string unknownUserCode = await ReadCodeAsync(unknownUser);

        Assert.Equal("invalid_credentials", wrongPasswordCode);
        Assert.Equal(wrongPasswordCode, unknownUserCode);
    }

    [Fact]
    public async Task Invalid_payload_is_rejected_by_validation_before_reaching_the_database()
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("not-an-email", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.TryGetProperty("errors", out JsonElement errors));
        Assert.True(errors.TryGetProperty(nameof(LoginRequest.Password), out _));
    }

    [Fact]
    public async Task Session_endpoint_rejects_an_anonymous_caller()
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sign_out_invalidates_the_session()
    {
        HttpClient client = await factory.SignInAsync(FinanceApprover);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        HttpResponseMessage logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task State_changing_request_without_an_allowed_origin_is_blocked()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(FinanceApprover, DocOpsApiFactory.SeedPassword));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("origin_not_allowed", await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Both_a_successful_and_a_failed_sign_in_are_recorded_in_the_audit_trail()
    {
        string email = "ana.ferreira@asteria.example";
        HttpClient client = factory.CreateBrowserClient();

        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wrong!Password9"));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, DocOpsApiFactory.SeedPassword));

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        List<AuditEvent> events = await context.AuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.ActorEmail == email)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync();

        Assert.Contains(events, e => e.Action == AuditActions.UserLoginFailed && e.Severity == AuditSeverity.Security);
        Assert.Contains(events, e => e.Action == AuditActions.UserLoginSucceeded);
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.CorrelationId)));

        // The audit trail must never contain the submitted password.
        Assert.DoesNotContain(events, e =>
            (e.Summary ?? string.Empty).Contains("Wrong!Password9", StringComparison.Ordinal)
            || (e.MetadataJson ?? string.Empty).Contains("Wrong!Password9", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Health_endpoints_report_status_without_leaking_infrastructure_detail()
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage ready = await client.GetAsync("/health/ready");
        string body = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers_and_a_correlation_id()
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.NotEmpty(response.Headers.GetValues("X-Correlation-Id").Single());
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString() ?? string.Empty;
    }
}
