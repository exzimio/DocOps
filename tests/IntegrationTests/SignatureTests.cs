using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Signatures;
using DocOps.Domain.Documents;
using DocOps.Domain.Signatures;

namespace DocOps.IntegrationTests;

/// <summary>
/// Signature requests against a real SQL Server.
///
/// **A portfolio demonstration with no legal validity**, and these tests are about the only claim it
/// does make: that the record says who agreed, when, and over exactly which content — and refuses
/// to be made to say anything else.
/// </summary>
[Collection(ApiCollection.Name)]
public class SignatureTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";
    private const string Employee = "ana.ferreira@asteria.example";
    private const string FinanceApprover = "ricardo.lopes@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_request_pins_the_version_and_its_content()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        DocumentDetailDto document = await SignableDocumentAsync(admin);

        SignatureRequestDto request = await RequestAsync(admin, document.Summary.Id, await AdminIdAsync(admin));

        Assert.Equal(SignatureRequestStatus.Pending, request.Status);
        Assert.Equal(document.Versions.Count, request.VersionNumber);
        Assert.NotEmpty(request.RequestedContentHash);
        Assert.True(request.ContentStillMatches);

        // The document says it is waiting, so it reads the same wherever it is shown.
        DocumentDetailDto after =
            (await admin.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{document.Summary.Id}", Json))!;

        Assert.Equal(DocumentStatus.SignatureRequired, after.Summary.Status);
    }

    [Fact]
    public async Task Only_the_named_signer_can_sign_whatever_else_someone_holds()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient manager = await factory.SignInAsync(Manager);

        DocumentDetailDto document = await SignableDocumentAsync(admin);
        Guid managerId = await UserIdAsync(manager);

        SignatureRequestDto request = await RequestAsync(admin, document.Summary.Id, managerId);

        // The administrator asked for it, holds every permission in the organisation, and still
        // cannot sign it: a signature is one person's statement, not an act of authority.
        HttpResponseMessage refused = await admin.PostAsync($"/api/signatures/{request.Id}/sign", null);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        SignatureRequestDto signed = await PostAsync<SignatureRequestDto>(
            manager, $"/api/signatures/{request.Id}/sign", null);

        Assert.Equal(SignatureRequestStatus.Signed, signed.Status);
        Assert.Equal(signed.RequestedContentHash, signed.SignedContentHash);
        Assert.NotNull(signed.CompletedAtUtc);
    }

    [Fact]
    public async Task Two_open_requests_on_one_document_are_refused()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        DocumentDetailDto document = await SignableDocumentAsync(admin);
        Guid adminId = await AdminIdAsync(admin);

        await RequestAsync(admin, document.Summary.Id, adminId);

        HttpResponseMessage second = await admin.PostAsJsonAsync(
            $"/api/documents/{document.Summary.Id}/signatures",
            new RequestSignatureRequest { SignerUserId = adminId },
            Json);

        // Two open requests would make "is this signed?" ambiguous, and neither signer would know
        // whether theirs was the one that counted.
        await AssertProblemAsync(second, HttpStatusCode.Conflict, "signature_already_open");
    }

    [Fact]
    public async Task Asking_someone_who_could_never_sign_is_refused_at_the_asking()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient employee = await factory.SignInAsync(Employee);

        DocumentDetailDto document = await SignableDocumentAsync(admin);

        // An employee holds no signatures.sign. Creating the request anyway would put something in
        // their list that they would be refused on every time they touched it.
        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/documents/{document.Summary.Id}/signatures",
            new RequestSignatureRequest { SignerUserId = await UserIdAsync(employee) },
            Json);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "signer_cannot_sign");
    }

    [Fact]
    public async Task Asking_someone_who_cannot_see_the_document_is_refused_at_the_asking()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient finance = await factory.SignInAsync(FinanceApprover);

        // A confidential document belonging to a department the signer is not in.
        DocumentDetailDto document = await ConfidentialDocumentOutsideAsync(admin, finance);
        Guid financeId = await UserIdAsync(finance);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/documents/{document.Summary.Id}/signatures",
            new RequestSignatureRequest { SignerUserId = financeId },
            Json);

        // Either refusal is correct here: the finance approver may lack the permission to sign at
        // all, or may hold it and simply not see this document. Both are the same bug being
        // prevented — a request nobody could ever fulfil.
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, body);
        Assert.True(
            body.Contains("signer_cannot_see_document", StringComparison.Ordinal)
            || body.Contains("signer_cannot_sign", StringComparison.Ordinal),
            body);
    }

    [Fact]
    public async Task Declining_needs_a_reason_and_leaves_the_matter_open()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient manager = await factory.SignInAsync(Manager);

        DocumentDetailDto document = await SignableDocumentAsync(admin);
        SignatureRequestDto request = await RequestAsync(admin, document.Summary.Id, await UserIdAsync(manager));

        SignatureRequestDto declined = await PostAsync<SignatureRequestDto>(
            manager,
            $"/api/signatures/{request.Id}/decline",
            new DeclineSignatureRequest { Reason = "Os valores não conferem com a encomenda." });

        Assert.Equal(SignatureRequestStatus.Declined, declined.Status);
        Assert.Contains("encomenda", declined.DeclineReason!, StringComparison.Ordinal);

        // The document stays where it was: a refusal closes the request, not the matter. The
        // organisation may ask someone else, revise the document, or reject it — and deciding that
        // here would be the service making a business decision that is not its own.
        DocumentDetailDto after =
            (await admin.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{document.Summary.Id}", Json))!;

        Assert.Equal(DocumentStatus.SignatureRequired, after.Summary.Status);

        // And with nothing open, another request can be made.
        SignatureRequestDto again = await RequestAsync(admin, document.Summary.Id, await AdminIdAsync(admin));
        Assert.Equal(SignatureRequestStatus.Pending, again.Status);
    }

    [Fact]
    public async Task The_document_history_says_who_signed_and_over_what()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        DocumentDetailDto document = await SignableDocumentAsync(admin);
        Guid adminId = await AdminIdAsync(admin);

        SignatureRequestDto request = await RequestAsync(admin, document.Summary.Id, adminId);
        await PostAsync<SignatureRequestDto>(admin, $"/api/signatures/{request.Id}/sign", null);

        PagedResult<DocumentAuditEntryDto> audit =
            (await admin.GetFromJsonAsync<PagedResult<DocumentAuditEntryDto>>(
                $"/api/documents/{document.Summary.Id}/audit?pageSize=50", Json))!;

        // Anchored to the document, not to the request: the place people look for a document's
        // history is the document, and a trail that showed only the status change would say
        // nothing about who signed or over what content.
        Assert.Contains(audit.Items, entry => entry.Action == "signature.requested");
        Assert.Contains(audit.Items, entry => entry.Action == "signature.signed");
    }

    [Fact]
    public async Task A_signature_belongs_to_one_organisation()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient vertice = await factory.SignInAsync("admin@vertice.example");

        DocumentDetailDto document = await SignableDocumentAsync(admin);
        SignatureRequestDto request = await RequestAsync(admin, document.Summary.Id, await AdminIdAsync(admin));

        HttpResponseMessage reach = await vertice.PostAsync($"/api/signatures/{request.Id}/sign", null);

        Assert.True(
            reach.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"esperado 404 ou 403, veio {reach.StatusCode}");
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A document this test may put into signature: approved, and freshly created so that other
    /// tests in the shared database cannot have left an open request on it.
    /// </summary>
    private static async Task<DocumentDetailDto> SignableDocumentAsync(
        HttpClient client, SecurityClassification classification = SecurityClassification.Internal)
    {
        using MultipartFormDataContent form = [];
        form.Add(new StringContent($"Contrato para assinar {Guid.CreateVersion7()}"), "Title");
        form.Add(new StringContent(await IntakeTypeIdAsync(client)), "DocumentTypeId");

        // Internal rather than the intake type's confidential default: these tests ask colleagues
        // from other departments to sign, and a confidential document with no department is visible
        // to its owner alone — which is a real rule, exercised by its own test below.
        form.Add(new StringContent(((int)classification).ToString()), "Classification");

        ByteArrayContent file = new("CONTRATO\nCláusulas de exemplo.\n"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        form.Add(file, "File", "contrato.txt");

        HttpResponseMessage created = await client.PostAsync("/api/documents", form);
        created.EnsureSuccessStatusCode();

        DocumentDetailDto document = (await created.Content.ReadFromJsonAsync<DocumentDetailDto>(Json))!;

        // Draft → InReview → PendingApproval → Approved, the path the lifecycle allows.
        foreach (DocumentStatus status in new[]
                 {
                     DocumentStatus.InReview, DocumentStatus.PendingApproval, DocumentStatus.Approved
                 })
        {
            HttpResponseMessage moved = await client.PostAsJsonAsync(
                $"/api/documents/{document.Summary.Id}/status",
                new ChangeDocumentStatusRequest(status, "Preparação de teste"),
                Json);

            moved.EnsureSuccessStatusCode();
        }

        return (await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{document.Summary.Id}", Json))!;
    }

    private static async Task<DocumentDetailDto> ConfidentialDocumentOutsideAsync(
        HttpClient owner, HttpClient other)
    {
        DocumentDetailDto document = await SignableDocumentAsync(owner, SecurityClassification.Confidential);
        Guid otherId = await UserIdAsync(other);

        // Sanity: the helper's document must genuinely be out of the other user's reach, otherwise
        // the test it serves would pass for the wrong reason.
        HttpResponseMessage seen = await other.GetAsync($"/api/documents/{document.Summary.Id}");
        Assert.True(
            seen.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"o documento devia estar fora do alcance de {otherId}, mas veio {seen.StatusCode}");

        return document;
    }

    private static async Task<string> IntakeTypeIdAsync(HttpClient client)
    {
        IReadOnlyList<DocumentTypeDto> types =
            (await client.GetFromJsonAsync<IReadOnlyList<DocumentTypeDto>>("/api/document-catalogue/types", Json))!;

        return types.First(type => type.Code == "unclassified" || type.Name.Contains("classificar")).Id.ToString();
    }

    private static async Task<SignatureRequestDto> RequestAsync(HttpClient client, Guid documentId, Guid signerId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/documents/{documentId}/signatures",
            new RequestSignatureRequest { SignerUserId = signerId, Reason = "Pedido de teste." },
            Json);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"pedido falhou: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<SignatureRequestDto>(Json))!;
    }

    private static Task<Guid> AdminIdAsync(HttpClient client) => UserIdAsync(client);

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        CurrentUserProbe me = (await client.GetFromJsonAsync<CurrentUserProbe>("/api/auth/me", Json))!;

        return me.Id;
    }

    private sealed record CurrentUserProbe(Guid Id);

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object? body)
    {
        HttpResponseMessage response = body is null
            ? await client.PostAsync(path, null)
            : await client.PostAsync(path, JsonContent.Create(body, body.GetType(), options: Json));

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {path} falhou: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private sealed record ProblemDocument(string? Code, string? Detail);

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"esperado {status}, veio {response.StatusCode}: {body}");

        ProblemDocument? problem = JsonSerializer.Deserialize<ProblemDocument>(body, Json);
        Assert.Equal(code, problem?.Code);
    }
}
