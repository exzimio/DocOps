using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocOps.Application.Administration;

namespace DocOps.IntegrationTests;

/// <summary>
/// Administration against a real SQL Server.
///
/// The tests that matter here are about the changes the screen **refuses** to make. An
/// administration surface is the one place where a mistake can be unrecoverable from inside the
/// product: an administrator who removes their own role, or deactivates the account that supervises
/// them, leaves nobody able to undo it.
/// </summary>
[Collection(ApiCollection.Name)]
public class AdministrationTests(DocOpsApiFactory factory)
{
    private const string Administrator = "admin@asteria.example";
    private const string Manager = "sofia.martins@asteria.example";
    private const string VerticeAdmin = "admin@vertice.example";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_administrator_cannot_change_their_own_account()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        AdminUserDto self = (await UsersAsync(admin)).Single(user => user.Email == Administrator);

        // The screen says so, and the server refuses anyway: removing your own administration role
        // can leave an organisation with nobody able to give it back.
        Assert.False(self.CanEdit);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/admin/users/{self.Id}/active", new { isActive = false }, Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_platform_account_is_not_managed_from_inside_an_organisation()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        IReadOnlyList<AdminUserDto> users = await UsersAsync(admin);

        AdminUserDto platform = users.Single(user => user.Email == "admin@docops.example");

        // Deactivating it would cut off the account that supervises this organisation, and saving an
        // edit would silently demote it, since SystemAdmin is not among the roles this screen offers.
        Assert.False(platform.CanEdit);

        // Refusing is not enough: the caller's own account is also not editable, and a screen that
        // could not tell the two apart would explain this one with the wrong reason.
        Assert.True(platform.IsPlatformAccount);
        Assert.False(users.Single(user => user.Email == Administrator).IsPlatformAccount);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            $"/api/admin/users/{platform.Id}/active", new { isActive = false }, Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task System_administration_is_not_a_role_an_organisation_can_hand_out()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        IReadOnlyList<AdminRoleDto> roles =
            (await admin.GetFromJsonAsync<IReadOnlyList<AdminRoleDto>>("/api/admin/roles", Json))!;

        Assert.DoesNotContain(roles, role => role.Name == "SystemAdmin");

        AdminUserDto target = (await UsersAsync(admin)).First(user => user.CanEdit);

        HttpResponseMessage response = await admin.PutAsJsonAsync(
            $"/api/admin/users/{target.Id}",
            new { firstName = "Teste", lastName = "Perfil", roles = new[] { "SystemAdmin" } },
            Json);

        // Otherwise an organisation could promote itself out of its own boundary.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_created_account_can_sign_in_and_a_deactivated_one_cannot()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);
        string email = $"teste.{Guid.CreateVersion7():N}"[..16] + "@asteria.example";

        CreatedUserDto created = await CreateAsync(admin, email);

        Assert.NotEmpty(created.TemporaryPassword);

        // The password is only useful if it works — the one claim this feature makes that a unit
        // test cannot check.
        Assert.True(await CanSignInAsync(created.User.Email, created.TemporaryPassword));

        await admin.PostAsJsonAsync(
            $"/api/admin/users/{created.User.Id}/active", new { isActive = false }, Json);

        Assert.False(await CanSignInAsync(created.User.Email, created.TemporaryPassword));

        // Deactivated, not deleted: the audit trail and the documents refer to this account.
        IReadOnlyList<AdminUserDto> active = await UsersAsync(admin);
        IReadOnlyList<AdminUserDto> all = await UsersAsync(admin, includeInactive: true);

        Assert.DoesNotContain(active, user => user.Id == created.User.Id);
        Assert.Contains(all, user => user.Id == created.User.Id);
    }

    [Fact]
    public async Task An_account_with_no_role_is_refused()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        HttpResponseMessage response = await admin.PostAsJsonAsync(
            "/api/admin/users",
            new
            {
                email = $"vazio.{Guid.CreateVersion7():N}"[..16] + "@asteria.example",
                firstName = "Sem",
                lastName = "Perfil",
                roles = Array.Empty<string>()
            },
            Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task One_organisation_never_sees_the_accounts_of_another()
    {
        HttpClient asteria = await factory.SignInAsync(Administrator);
        HttpClient vertice = await factory.SignInAsync(VerticeAdmin);

        IReadOnlyList<AdminUserDto> theirs = await UsersAsync(asteria, includeInactive: true);
        IReadOnlyList<AdminUserDto> ours = await UsersAsync(vertice, includeInactive: true);

        // Identity, not counts: both organisations grow their own people over time.
        Assert.NotEmpty(theirs);
        Assert.NotEmpty(ours);
        Assert.DoesNotContain(ours, user => theirs.Any(other => other.Id == user.Id));
    }

    [Fact]
    public async Task Administration_is_not_something_a_manager_can_reach()
    {
        HttpClient manager = await factory.SignInAsync(Manager);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/admin/departments")).StatusCode);
    }

    [Fact]
    public async Task A_department_code_is_unique_within_the_organisation()
    {
        HttpClient admin = await factory.SignInAsync(Administrator);

        string code = $"T{Guid.CreateVersion7():N}"[..6].ToUpperInvariant();

        HttpResponseMessage first = await admin.PostAsJsonAsync(
            "/api/admin/departments", new { name = "Teste", code }, Json);

        first.EnsureSuccessStatusCode();

        HttpResponseMessage duplicate = await admin.PostAsJsonAsync(
            "/api/admin/departments", new { name = "Outro", code }, Json);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static async Task<IReadOnlyList<AdminUserDto>> UsersAsync(
        HttpClient client, bool includeInactive = false) =>
        (await client.GetFromJsonAsync<IReadOnlyList<AdminUserDto>>(
            $"/api/admin/users?includeInactive={includeInactive}", Json))!;

    private static async Task<CreatedUserDto> CreateAsync(HttpClient client, string email)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/admin/users",
            new { email, firstName = "Teste", lastName = "Integração", roles = new[] { "Employee" } },
            Json);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"criação falhou: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<CreatedUserDto>(Json))!;
    }

    /// <summary>
    /// A browser-shaped client, like the sign-in helper uses: the login endpoint checks the request
    /// origin, so a bare client is refused before the credentials are even looked at.
    /// </summary>
    private async Task<bool> CanSignInAsync(string email, string password)
    {
        HttpClient client = factory.CreateBrowserClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email, password }, Json);

        return response.IsSuccessStatusCode;
    }
}
