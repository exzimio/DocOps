using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Administration;

namespace DocOps.IntegrationTests;

/// <summary>
/// Tags and retention policies against a real SQL Server.
///
/// The interesting part is where these two differ from the metadata schema, and from each other.
/// A metadata value is what a document <b>asserts</b>, so it is never destroyed by a configuration
/// change. A tag is an annotation <b>placed on</b> a document, so removing one is allowed — and
/// reversible, which is what makes it honest. A retention rule is neither: changing it is not a
/// falsification but an obligation that genuinely moved, so it is allowed and audited.
/// </summary>
[Collection(ApiCollection.Name)]
public class GovernanceAdminTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task One_organisation_never_sees_the_tags_of_another()
    {
        HttpClient asteria = await factory.SignInAsync(Administrator);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        IReadOnlyList<AdminTagDto> theirs = await TagsAsync(asteria, includeDeleted: true);
        IReadOnlyList<AdminTagDto> ours = await TagsAsync(vertice, includeDeleted: true);

        // This is not hypothetical: the first version of the listing counted documents with
        // `IgnoreQueryFilters()` inside the projection, which disables the filters of the entire
        // query — the organisation one included — and Vértice could read Asteria's tags. Identity,
        // not counts: both organisations grow their own labels.
        Assert.NotEmpty(theirs);
        Assert.DoesNotContain(ours, tag => theirs.Any(other => other.Id == tag.Id));
    }

    [Fact]
    public async Task Removing_a_tag_hides_it_without_losing_what_it_was_on()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminTagDto used = (await TagsAsync(admin)).First(tag => tag.DocumentCount > 0);
        int before = used.DocumentCount;

        (await admin.DeleteAsync($"/api/admin/tags/{used.Id}")).EnsureSuccessStatusCode();

        Assert.DoesNotContain(await TagsAsync(admin), tag => tag.Id == used.Id);

        AdminTagDto removed = (await TagsAsync(admin, includeDeleted: true)).Single(tag => tag.Id == used.Id);

        Assert.True(removed.IsDeleted);

        HttpResponseMessage restored = await admin.PostAsync($"/api/admin/tags/{used.Id}/restore", null);

        restored.EnsureSuccessStatusCode();

        AdminTagDto back = (await restored.Content.ReadFromJsonAsync<AdminTagDto>(Json))!;

        // The links were never touched, which is the whole claim behind allowing the removal.
        Assert.Equal(before, back.DocumentCount);
    }

    [Fact]
    public async Task A_colour_that_is_not_a_colour_is_refused_rather_than_dropped()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/admin/tags", new { name = Unique("cor"), colour = "verde-escuro" }, Json);

        // Silently dropping it would leave someone looking for a colour they chose and never saw.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_tag_name_freed_by_a_removal_can_be_used_again()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        string name = Unique("livre");

        AdminTagDto first = await CreateTagAsync(admin, name);

        HttpResponseMessage duplicate = await admin.PostAsJsonAsync("/api/admin/tags", new { name }, Json);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        (await admin.DeleteAsync($"/api/admin/tags/{first.Id}")).EnsureSuccessStatusCode();

        // Uniqueness is about what is in circulation, not about what ever existed.
        HttpResponseMessage again = await admin.PostAsJsonAsync("/api/admin/tags", new { name }, Json);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task A_policy_adopted_by_a_type_cannot_be_withdrawn()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminRetentionPolicyDto adopted = (await PoliciesAsync(admin)).First(policy => policy.TypeCount > 0);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/admin/retention-policies/{adopted.Id}/active", new { isActive = false }, Json);

        // Those types would go on applying a rule the organisation decided to stop applying, and
        // nothing on screen would say so.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Changing_how_long_documents_are_kept_is_allowed_and_recorded()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        AdminRetentionPolicyDto policy = await CreatePolicyAsync(admin, months: 24);

        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"/api/admin/retention-policies/{policy.Id}",
            new { code = policy.Code, name = policy.Name, retentionMonths = 60, trigger = 2 },
            Json);

        response.EnsureSuccessStatusCode();

        AdminRetentionPolicyDto updated = (await response.Content.ReadFromJsonAsync<AdminRetentionPolicyDto>(Json))!;

        // Unlike a metadata field, this is not a falsification: obligations do change, and a
        // catalogue unable to follow them would be the lie.
        Assert.Equal(60, updated.RetentionMonths);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1201)]
    public async Task A_duration_that_is_not_a_period_is_refused(int months)
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/admin/retention-policies",
            new { code = Unique("D").ToUpperInvariant(), name = "Duração", retentionMonths = months },
            Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Reading_the_catalogue_does_not_grant_changing_how_long_documents_are_kept()
    {
        HttpClient manager = await factory.SignInAsync(Manager);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/tags")).StatusCode);

        HttpResponseMessage created = await manager.PostAsJsonAsync(
            "/api/admin/retention-policies",
            new { code = "XPTO", name = "Qualquer", retentionMonths = 12 },
            Json);

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12];

    private static async Task<IReadOnlyList<AdminTagDto>> TagsAsync(
        HttpClient client, bool includeDeleted = false) =>
        (await client.GetFromJsonAsync<IReadOnlyList<AdminTagDto>>(
            $"/api/admin/tags?includeDeleted={includeDeleted}", Json))!;

    private static async Task<IReadOnlyList<AdminRetentionPolicyDto>> PoliciesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<IReadOnlyList<AdminRetentionPolicyDto>>(
            "/api/admin/retention-policies", Json))!;

    private static async Task<AdminTagDto> CreateTagAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/admin/tags", new { name }, Json);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AdminTagDto>(Json))!;
    }

    private static async Task<AdminRetentionPolicyDto> CreatePolicyAsync(HttpClient client, int months)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/admin/retention-policies",
            new
            {
                code = Unique("P")[..8].ToUpperInvariant(),
                name = "Política de integração",
                retentionMonths = months,
                trigger = 2
            },
            Json);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"criação falhou: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<AdminRetentionPolicyDto>(Json))!;
    }
}
