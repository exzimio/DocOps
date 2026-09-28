using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Common;
using DocOps.Application.Documents;
using DocOps.Application.Tenancy;

namespace DocOps.IntegrationTests;

/// <summary>
/// The permission editor against a real SQL Server.
///
/// What is worth proving here is not that a grant can be created — it is that the explanation the
/// screen shows is the <b>same evaluation</b> the server uses when it actually decides. An
/// authorization panel that computed its own answer would eventually disagree with the rule it
/// claims to explain, and the disagreement would be invisible until someone reached something they
/// should not have.
/// </summary>
[Collection(ApiCollection.Name)]
public class PermissionEditorTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Employee = "ana.ferreira@asteria.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_roles_offered_for_sharing_never_include_platform_administration()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        IReadOnlyList<TenantRoleDto> roles =
            (await admin.GetFromJsonAsync<IReadOnlyList<TenantRoleDto>>("/api/tenants/current/roles", Json))!;

        Assert.NotEmpty(roles);

        // Platform administration is held above the organisation: sharing a document with it would
        // grant access to an account the organisation does not manage.
        Assert.DoesNotContain(roles, role => role.Name == "SystemAdmin");
        Assert.Contains(roles, role => role.Name == "Manager");
    }

    [Fact]
    public async Task Explaining_another_persons_access_needs_the_right_to_share()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        HttpClient employee = await factory.SignInAsync(Employee);

        Guid documentId = await FirstVisibleDocumentAsync(admin);
        Guid otherUserId = await SomeoneElseAsync(admin);

        HttpResponseMessage allowed = await admin.GetAsync(
            $"/api/documents/{documentId}/effective-access?userId={otherUserId}");

        allowed.EnsureSuccessStatusCode();

        // Asking «could this person do this?» is legitimate for whoever decides to grant it, and
        // for nobody else: the answer describes someone else's reach.
        HttpResponseMessage refused = await employee.GetAsync(
            $"/api/documents/{documentId}/effective-access?userId={otherUserId}");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task The_explanation_matches_the_access_it_explains()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        Guid documentId = await FirstVisibleDocumentAsync(admin);
        Guid otherUserId = await SomeoneElseAsync(admin);

        EffectiveAccessDto before = await ExplainAsync(admin, documentId, otherUserId);

        await admin.PostAsJsonAsync(
            $"/api/documents/{documentId}/permissions",
            new { subjectType = 0, subjectId = otherUserId, accessLevels = new[] { "view", "download" } },
            Json);

        EffectiveAccessDto after = await ExplainAsync(admin, documentId, otherUserId);

        // The grant has to show up both in what the person may do and in why they may do it. A panel
        // that listed levels without reasons would be an assertion nobody could check.
        Assert.Contains("view", after.Levels);
        Assert.NotEmpty(after.Reasons);
        Assert.True(after.Levels.Count > before.Levels.Count);
    }

    [Fact]
    public async Task A_grant_can_be_given_an_expiry_but_not_one_in_the_past()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        Guid documentId = await FirstVisibleDocumentAsync(admin);
        Guid otherUserId = await SomeoneElseAsync(admin);

        HttpResponseMessage future = await admin.PostAsJsonAsync(
            $"/api/documents/{documentId}/permissions",
            new
            {
                subjectType = 0,
                subjectId = otherUserId,
                accessLevels = new[] { "view" },
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
            },
            Json);

        future.EnsureSuccessStatusCode();

        DocumentPermissionDto granted = (await future.Content.ReadFromJsonAsync<DocumentPermissionDto>(Json))!;

        Assert.NotNull(granted.ExpiresAtUtc);

        // An access window that closed before it opened is not a window.
        HttpResponseMessage past = await admin.PostAsJsonAsync(
            $"/api/documents/{documentId}/permissions",
            new
            {
                subjectType = 0,
                subjectId = otherUserId,
                accessLevels = new[] { "view" },
                expiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
            },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
    }

    private static async Task<EffectiveAccessDto> ExplainAsync(HttpClient client, Guid documentId, Guid userId) =>
        (await client.GetFromJsonAsync<EffectiveAccessDto>(
            $"/api/documents/{documentId}/effective-access?userId={userId}", Json))!;

    private static async Task<Guid> FirstVisibleDocumentAsync(HttpClient client)
    {
        PagedResult<DocumentListItemDto> page =
            (await client.GetFromJsonAsync<PagedResult<DocumentListItemDto>>(
                "/api/documents?page=1&pageSize=1", Json))!;

        return page.Items[0].Id;
    }

    private static async Task<Guid> SomeoneElseAsync(HttpClient client)
    {
        IReadOnlyList<TenantMemberDto> members =
            (await client.GetFromJsonAsync<IReadOnlyList<TenantMemberDto>>("/api/tenants/current/users", Json))!;

        return members.First(member => member.DisplayName.Contains("Ana", StringComparison.Ordinal)).Id;
    }
}
