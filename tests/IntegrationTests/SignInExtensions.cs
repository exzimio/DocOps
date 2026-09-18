using System.Net;
using System.Net.Http.Json;
using DocOps.Application.Identity;

namespace DocOps.IntegrationTests;

public static class SignInExtensions
{
    /// <summary>Returns a cookie-carrying client already signed in as the given seeded account.</summary>
    public static async Task<HttpClient> SignInAsync(
        this DocOpsApiFactory factory, string email, string? password = null)
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password ?? DocOpsApiFactory.SeedPassword));

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Test sign-in failed for '{email}': {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return client;
    }
}
