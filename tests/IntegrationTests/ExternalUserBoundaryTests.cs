using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DocOps.Application.Portal;

namespace DocOps.IntegrationTests;

/// <summary>
/// An external partner reaches the portal and its own session, and nothing else. Its role keeps the
/// document permissions the portal uses on the server, so the boundary cannot rest on permissions:
/// it is a rule about the kind of account, refused by default.
/// </summary>
[Collection(ApiCollection.Name)]
public class ExternalUserBoundaryTests(DocOpsApiFactory factory)
{
    private const string Supplier = "contacto@northstar.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_partner_reaches_the_portal_and_its_session()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);
        PortalSubmissionDto submission = await SubmitAsync(supplier, "Fatura submetida pelo portal");

        foreach (string path in new[]
                 {
                     "/api/auth/me",
                     "/api/portal/submissions",
                     $"/api/portal/submissions/{submission.Id}",
                     $"/api/portal/submissions/{submission.Id}/content"
                 })
        {
            Assert.True((await supplier.GetAsync(path)).StatusCode == HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task A_partner_is_refused_every_internal_endpoint_even_for_its_own_document()
    {
        HttpClient supplier = await factory.SignInAsync(Supplier);

        // The supplier owns this document: whatever refuses the calls below is the boundary, not
        // the document's own access rules.
        Guid own = (await SubmitAsync(supplier, "Fatura do próprio fornecedor")).Id;

        foreach ((HttpMethod method, string path) in new[]
                 {
                     (HttpMethod.Get, "/api/documents"),
                     (HttpMethod.Get, $"/api/documents/{own}"),
                     (HttpMethod.Post, "/api/documents"),
                     (HttpMethod.Get, $"/api/documents/{own}/content"),
                     (HttpMethod.Get, $"/api/documents/{own}/text"),
                     (HttpMethod.Get, $"/api/documents/{own}/audit"),
                     (HttpMethod.Get, $"/api/documents/{own}/permissions"),
                     (HttpMethod.Get, $"/api/documents/{own}/effective-access"),
                     (HttpMethod.Post, $"/api/documents/{own}/status"),
                     (HttpMethod.Get, $"/api/documents/{own}/signatures"),
                     (HttpMethod.Get, "/api/document-catalogue/types"),
                     (HttpMethod.Get, "/api/document-catalogue/tags"),
                     (HttpMethod.Get, "/api/tenants/current"),
                     (HttpMethod.Get, "/api/tenants/current/departments"),
                     (HttpMethod.Get, "/api/tenants/current/users"),
                     (HttpMethod.Get, "/api/tenants/current/roles"),
                     (HttpMethod.Get, "/api/notifications"),
                     (HttpMethod.Get, "/api/notifications/unread-count"),
                     (HttpMethod.Post, $"/api/notifications/{Guid.CreateVersion7()}/read"),
                     (HttpMethod.Post, "/api/notifications/read-all")
                 })
        {
            using HttpRequestMessage request = new(method, path);

            if (method == HttpMethod.Post)
            {
                request.Content = path.EndsWith("/status", StringComparison.Ordinal)
                    ? JsonContent.Create(new { status = 1 })
                    : new MultipartFormDataContent();
            }

            HttpResponseMessage response = await supplier.SendAsync(request);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path}: {response.StatusCode}");
        }
    }

    private static async Task<PortalSubmissionDto> SubmitAsync(HttpClient client, string title)
    {
        using MultipartFormDataContent form = [];
        form.Add(new StringContent(title), "Title");
        form.Add(new StringContent("Submetida por um teste de integração."), "Note");

        ByteArrayContent file = new(Encoding.UTF8.GetBytes("FATURA de teste\nTotal: 100,00 EUR"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "teste.txt");

        HttpResponseMessage response = await client.PostAsync("/api/portal/submissions", form);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<PortalSubmissionDto>(Json))!;
    }
}
