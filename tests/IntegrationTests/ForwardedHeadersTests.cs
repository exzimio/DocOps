using System.Net;
using System.Net.Http.Json;
using DocOps.Application.Abstractions;
using DocOps.Application.Identity;
using DocOps.Domain.Auditing;
using DocOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DocOps.IntegrationTests;

/// <summary>
/// The API behind nginx. The test server has no network, so each host here is given the address the
/// request "arrives from": the proxy's, inside the trusted network, or someone else's.
/// </summary>
[Collection(ApiCollection.Name)]
public class ForwardedHeadersTests(DocOpsApiFactory factory)
{
    private const string FinanceApprover = "ricardo.lopes@asteria.example";
    private const string TrustedNetwork = "10.89.0.0/24";
    private const string ProxyAddress = "10.89.0.10";
    private const string UntrustedAddress = "198.51.100.20";

    [Fact]
    public async Task The_audit_records_the_client_address_a_trusted_proxy_forwards()
    {
        using WebApplicationFactory<Program> api = Behind(ProxyAddress);

        HttpResponseMessage response = await SignInAsync(api, forwardedFor: "203.0.113.7");

        Assert.Equal("203.0.113.7", await LoginAuditAddressAsync(response));
    }

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_source_is_ignored()
    {
        using WebApplicationFactory<Program> api = Behind(UntrustedAddress);

        HttpResponseMessage response = await SignInAsync(api, forwardedFor: "203.0.113.8");

        Assert.Equal(UntrustedAddress, await LoginAuditAddressAsync(response));
    }

    [Fact]
    public async Task The_sign_in_limiter_counts_each_client_and_not_the_proxy()
    {
        using WebApplicationFactory<Program> api = Behind(ProxyAddress, authenticationPermitLimit: 3);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SignInAsync(api, forwardedFor: "203.0.113.20")).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await SignInAsync(api, forwardedFor: "203.0.113.20")).StatusCode);

        // Same proxy, another client: before the forwarded address was read, this was refused too.
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(api, forwardedFor: "203.0.113.21")).StatusCode);

        // A client cannot pick a fresh partition by writing its own entry first: only the address
        // nginx appended counts.
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await SignInAsync(api, forwardedFor: "192.0.2.99, 203.0.113.20")).StatusCode);
    }

    [Fact]
    public async Task The_session_cookie_is_secure_outside_development_even_over_plain_http()
    {
        using WebApplicationFactory<Program> api = Behind(ProxyAddress, environment: Environments.Production);

        HttpResponseMessage response = await SignInAsync(api, forwardedFor: "203.0.113.30");

        string setCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("docops.session", StringComparison.Ordinal));

        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Program.cs reads these settings while the builder is still being assembled, before any
    /// configuration a test factory adds becomes visible, which is why the shared factory uses
    /// environment variables. Here they are set only while this host starts, then put back.
    /// </summary>
    private WebApplicationFactory<Program> Behind(
        string remoteAddress, int? authenticationPermitLimit = null, string? environment = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["ForwardedHeaders__KnownNetworks__0"] = TrustedNetwork
        };

        if (authenticationPermitLimit is { } limit)
        {
            settings["RateLimiting__AuthenticationPermitLimit"] = limit.ToString();
        }

        WebApplicationFactory<Program> api = factory.WithWebHostBuilder(builder =>
        {
            if (environment is not null)
            {
                builder.UseEnvironment(environment);
            }

            foreach ((string name, string? value) in settings)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(IPAddress.Parse(remoteAddress))));
        });

        Dictionary<string, string?> previous = settings.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);

        try
        {
            _ = api.Server;
        }
        finally
        {
            foreach ((string name, string? value) in previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        return api;
    }

    private static async Task<HttpResponseMessage> SignInAsync(WebApplicationFactory<Program> api, string forwardedFor)
    {
        HttpClient client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri(DocOpsApiFactory.TestOrigin)
        });

        client.DefaultRequestHeaders.Add("Origin", DocOpsApiFactory.TestOrigin);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);

        return await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(FinanceApprover, DocOpsApiFactory.SeedPassword));
    }

    private async Task<string?> LoginAuditAddressAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string correlationId = response.Headers.GetValues("X-Correlation-Id").Single();

        using ISystemScope scope = factory.CreateServiceScope();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        return await context.AuditEvents
            .IgnoreQueryFilters()
            .Where(e => e.CorrelationId == correlationId && e.Action == AuditActions.UserLoginSucceeded)
            .Select(e => e.IpAddress)
            .SingleAsync();
    }

    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });

            next(app);
        };
    }
}
