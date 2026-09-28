using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Administration;

namespace DocOps.IntegrationTests;

/// <summary>
/// The document catalogue against a real SQL Server.
///
/// What is worth testing here is not that a type can be created — it is what the service refuses,
/// because every refusal exists to stop a change that would make something already written false.
/// A field that changes kind leaves its values in the wrong column; a field that becomes required
/// retroactively invalidates documents nobody is going to revisit.
/// </summary>
[Collection(ApiCollection.Name)]
public class DocumentCatalogueAdminTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_intake_type_cannot_be_deactivated()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminDocumentTypeDto intake = (await TypesAsync(admin)).Single(type => type.IsIntakeType);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/admin/document-types/{intake.Id}/active", new { isActive = false }, Json);

        // It is the door every file comes through before anyone has read it — including the ones
        // that arrive by e-mail and through the portal, where nobody is watching.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_intake_type_refuses_a_required_field()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminDocumentTypeDto intake = (await TypesAsync(admin)).Single(type => type.IsIntakeType);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/admin/document-types/{intake.Id}/fields",
            new { code = Unique("req"), label = "Obrigatório", fieldType = 0, isRequired = true },
            Json);

        // A required field there would mean refusing the upload until someone answers a question
        // that only reading the document can answer.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_field_with_values_neither_changes_kind_nor_is_deleted()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminDocumentTypeDto type = (await TypesAsync(admin)).First(t => t.DocumentCount > 0 && !t.IsIntakeType);
        IReadOnlyList<AdminMetadataFieldDto> fields = await FieldsAsync(admin, type.Id);

        AdminMetadataFieldDto used = fields.First(field => field.ValueCount > 0);

        Assert.False(used.CanChangeType);
        Assert.False(used.CanDelete);

        int otherKind = (int)used.FieldType == 0 ? 1 : 0;

        HttpResponseMessage changed = await admin.PutAsJsonAsync(
            $"/api/admin/document-types/{type.Id}/fields/{used.Id}",
            new { code = used.Code, label = used.Label, fieldType = otherKind, isRequired = used.IsRequired },
            Json);

        // The value sits in the typed column of its kind. Changing the kind does not move it: the
        // document goes silently blank, or shows something never written there.
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        HttpResponseMessage deleted = await admin.DeleteAsync(
            $"/api/admin/document-types/{type.Id}/fields/{used.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
    }

    [Fact]
    public async Task A_field_becomes_required_only_when_nothing_is_left_invalid()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminDocumentTypeDto withDocuments =
            (await TypesAsync(admin)).First(type => type.DocumentCount > 0 && !type.IsIntakeType);

        HttpResponseMessage refused = await admin.PostAsJsonAsync(
            $"/api/admin/document-types/{withDocuments.Id}/fields",
            new { code = Unique("new"), label = "Novo obrigatório", fieldType = 0, isRequired = true },
            Json);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // The same change on a type with no documents is simply true, so it is allowed.
        AdminDocumentTypeDto empty = await CreateTypeAsync(admin);

        HttpResponseMessage allowed = await admin.PostAsJsonAsync(
            $"/api/admin/document-types/{empty.Id}/fields",
            new { code = "mandatory", label = "Obrigatório", fieldType = 0, isRequired = true },
            Json);

        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task Two_types_never_share_a_reference_prefix()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminDocumentTypeDto first = await CreateTypeAsync(admin);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/admin/document-types",
            new { code = Unique("Other"), name = "Outro", referencePrefix = first.ReferencePrefix },
            Json);

        // The reference is what people quote to each other; two types sharing a prefix produce two
        // documents with the same one.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_select_field_needs_something_to_choose_from()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        AdminDocumentTypeDto type = await CreateTypeAsync(admin);

        HttpResponseMessage empty = await admin.PostAsJsonAsync(
            $"/api/admin/document-types/{type.Id}/fields",
            new { code = "choice", label = "Escolha", fieldType = 5, options = Array.Empty<string>() },
            Json);

        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);

        HttpResponseMessage filled = await admin.PostAsJsonAsync(
            $"/api/admin/document-types/{type.Id}/fields",
            new { code = "choice", label = "Escolha", fieldType = 5, options = new[] { "Sim", " ", "Sim", "Não" } },
            Json);

        filled.EnsureSuccessStatusCode();

        AdminMetadataFieldDto field = (await filled.Content.ReadFromJsonAsync<AdminMetadataFieldDto>(Json))!;

        // Blanks and repeats are dropped rather than refused: the intent is unambiguous.
        Assert.Equal(["Sim", "Não"], field.Options);
    }

    [Fact]
    public async Task A_deactivated_type_leaves_the_working_list_without_leaving_the_database()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        AdminDocumentTypeDto type = await CreateTypeAsync(admin);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/admin/document-types/{type.Id}/active", new { isActive = false }, Json);

        response.EnsureSuccessStatusCode();

        // Never deleted: the documents classified under it refer to it, and a type that disappears
        // takes their classification with it.
        Assert.DoesNotContain(await TypesAsync(admin), t => t.Id == type.Id);
        Assert.Contains(await TypesAsync(admin, includeInactive: true), t => t.Id == type.Id);
    }

    [Fact]
    public async Task One_organisation_never_reaches_the_catalogue_of_another()
    {
        HttpClient asteria = await factory.SignInAsync(Administrator);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        AdminDocumentTypeDto theirs = await CreateTypeAsync(asteria);

        // Not «forbidden» but «not found»: admitting that the identifier exists is already an answer.
        HttpResponseMessage response = await vertice.GetAsync($"/api/admin/document-types/{theirs.Id}/fields");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Managing_people_does_not_grant_managing_the_catalogue()
    {
        HttpClient manager = await factory.SignInAsync(Manager);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await manager.GetAsync("/api/admin/document-types")).StatusCode);

        HttpResponseMessage created = await manager.PostAsJsonAsync(
            "/api/admin/document-types",
            new { code = Unique("X"), name = "X", referencePrefix = "XXX" },
            Json);

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
    }

    /// <summary>
    /// Deliberately <see cref="Guid.NewGuid"/> and not <c>CreateVersion7</c>, which the rest of the
    /// product uses: a v7 identifier opens with its timestamp, so two of them minted in the same
    /// run share their first characters — and these names are made by cutting exactly there.
    /// </summary>
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12];

    private static async Task<IReadOnlyList<AdminDocumentTypeDto>> TypesAsync(
        HttpClient client, bool includeInactive = false) =>
        (await client.GetFromJsonAsync<IReadOnlyList<AdminDocumentTypeDto>>(
            $"/api/admin/document-types?includeInactive={includeInactive}", Json))!;

    private static async Task<IReadOnlyList<AdminMetadataFieldDto>> FieldsAsync(HttpClient client, Guid typeId) =>
        (await client.GetFromJsonAsync<IReadOnlyList<AdminMetadataFieldDto>>(
            $"/api/admin/document-types/{typeId}/fields", Json))!;

    private static async Task<AdminDocumentTypeDto> CreateTypeAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/admin/document-types",
            new
            {
                code = Unique("Type"),
                name = "Tipo de integração",
                referencePrefix = Unique("P")[..5].ToUpperInvariant()
            },
            Json);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"criação falhou: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<AdminDocumentTypeDto>(Json))!;
    }
}
