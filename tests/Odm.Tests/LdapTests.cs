using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Odm.Modules.Identity.Application;
using Odm.Modules.Identity.Domain;
using Odm.Modules.Identity.Infrastructure.Ldap;

namespace Odm.Tests;

public class LdapTests
{
    private const string AdminGroup = "CN=odm-admins,OU=Groups,DC=example,DC=org";
    private const string OperatorGroup = "CN=odm-operators,OU=Groups,DC=example,DC=org";
    private const string ReadGroup = "CN=odm-readers,OU=Groups,DC=example,DC=org";

    private static LdapOptions Options() => new()
    {
        Enabled = true,
        Host = "ldap.example.org",
        BaseDn = "DC=example,DC=org",
        AdminGroup = AdminGroup,
        OperatorGroup = OperatorGroup,
        ReadOnlyGroup = ReadGroup
    };

    private sealed class FakeDirectory(LdapUser? user, string password = "secret") : ILdapDirectory
    {
        public int VerifyCalls { get; private set; }

        public Task<LdapUser?> FindUserAsync(string userName, CancellationToken ct) => Task.FromResult(user);

        public Task<bool> VerifyPasswordAsync(string userDn, string pw, CancellationToken ct)
        {
            VerifyCalls++;
            return Task.FromResult(pw == password);
        }
    }

    private static LdapAuthenticationProvider Provider(ILdapDirectory directory) =>
        new(directory, Microsoft.Extensions.Options.Options.Create(Options()), NullLogger<LdapAuthenticationProvider>.Instance);

    private static LdapUser UserIn(params string[] groups) =>
        new("CN=Anna,OU=Users,DC=example,DC=org", "Anna Muster", groups);

    [Theory]
    [InlineData("anna", "anna")]
    [InlineData("a*", "a\\2a")]
    [InlineData("a)(uid=*", "a\\29\\28uid=\\2a")]
    [InlineData("back\\slash", "back\\5cslash")]
    [InlineData("nul\0", "nul\\00")]
    public void Filter_values_are_escaped(string input, string expected) =>
        Assert.Equal(expected, LdapFilter.Escape(input));

    [Fact]
    public void Highest_role_wins()
    {
        Assert.Equal(UserRole.Admin, LdapRoleMapper.Map([ReadGroup, AdminGroup], Options()));
        Assert.Equal(UserRole.Operator, LdapRoleMapper.Map([ReadGroup, OperatorGroup], Options()));
        Assert.Equal(UserRole.ReadOnly, LdapRoleMapper.Map([ReadGroup], Options()));
        Assert.Null(LdapRoleMapper.Map(["CN=other,DC=example,DC=org"], Options()));
        Assert.Null(LdapRoleMapper.Map([], Options()));
    }

    [Fact]
    public void Group_dn_comparison_ignores_case_and_spacing()
    {
        Assert.Equal(UserRole.Admin,
            LdapRoleMapper.Map(["cn=ODM-Admins, ou=Groups , dc=example, dc=org"], Options()));
    }

    [Fact]
    public void Unset_groups_never_match()
    {
        var options = Options();
        options.OperatorGroup = "";
        Assert.Null(LdapRoleMapper.Map([""], options));
    }

    [Fact]
    public async Task Valid_user_gets_role_name_and_stable_id()
    {
        var provider = Provider(new FakeDirectory(UserIn(OperatorGroup)));

        var user = await provider.AuthenticateAsync("Anna", "secret", CancellationToken.None);
        var again = await provider.AuthenticateAsync("ANNA", "secret", CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal(UserRole.Operator, user!.Role);
        Assert.Equal("anna", user.UserName);
        Assert.Equal("Anna Muster", user.DisplayName);
        Assert.Equal(user.Id, again!.Id);
    }

    [Fact]
    public async Task Empty_password_is_rejected_without_asking_the_directory()
    {
        var directory = new FakeDirectory(UserIn(AdminGroup), password: "");
        var provider = Provider(directory);

        Assert.Null(await provider.AuthenticateAsync("anna", "", CancellationToken.None));
        Assert.Equal(0, directory.VerifyCalls);
    }

    [Fact]
    public async Task Wrong_password_unknown_user_and_missing_group_are_rejected()
    {
        Assert.Null(await Provider(new FakeDirectory(UserIn(AdminGroup))).AuthenticateAsync("anna", "wrong", CancellationToken.None));
        Assert.Null(await Provider(new FakeDirectory(null)).AuthenticateAsync("anna", "secret", CancellationToken.None));
        Assert.Null(await Provider(new FakeDirectory(UserIn())).AuthenticateAsync("anna", "secret", CancellationToken.None));
    }

    private sealed class ThrowingDirectory : ILdapDirectory
    {
        public Task<LdapUser?> FindUserAsync(string userName, CancellationToken ct) =>
            throw new InvalidOperationException("Verzeichnis nicht erreichbar");

        public Task<bool> VerifyPasswordAsync(string userDn, string password, CancellationToken ct) =>
            Task.FromResult(true);
    }

    [Fact]
    public async Task Directory_errors_fail_closed()
    {
        Assert.Null(await Provider(new ThrowingDirectory()).AuthenticateAsync("anna", "secret", CancellationToken.None));
    }

    [Fact]
    public void Ids_differ_per_provider_and_user()
    {
        Assert.Equal(ExternalUserIds.Create("ldap", "anna"), ExternalUserIds.Create("ldap", "anna"));
        Assert.NotEqual(ExternalUserIds.Create("ldap", "anna"), ExternalUserIds.Create("ldap", "bob"));
        Assert.NotEqual(ExternalUserIds.Create("ldap", "anna"), ExternalUserIds.Create("odata", "anna"));
    }

    [Fact]
    public void Options_report_the_first_problem()
    {
        Assert.Null(Options().Problem());
        Assert.NotNull(new LdapOptions().Problem());

        var noPlaceholder = Options();
        noPlaceholder.UserFilter = "(uid=fest)";
        Assert.NotNull(noPlaceholder.Problem());

        var noGroups = Options();
        noGroups.AdminGroup = noGroups.OperatorGroup = noGroups.ReadOnlyGroup = "";
        Assert.NotNull(noGroups.Problem());

        var bindWithoutPassword = Options();
        bindWithoutPassword.BindDn = "CN=svc,DC=example,DC=org";
        Assert.NotNull(bindWithoutPassword.Problem());
    }

    // ---- Auswahl des Anmeldeverfahrens ----

    private sealed class FakeProvider(string name, bool owns, AuthenticatedUser? result) : IAuthenticationProvider
    {
        public string Name => name;
        public int Calls { get; private set; }

        public Task<bool> OwnsUserAsync(string userName, CancellationToken ct) => Task.FromResult(owns);

        public Task<AuthenticatedUser?> AuthenticateAsync(string userName, string password, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeTokens : ITokenService
    {
        public (string Token, DateTimeOffset ExpiresAt) Create(AuthenticatedUser user) =>
            ("token-" + user.UserName, DateTimeOffset.UtcNow.AddHours(1));
    }

    private static readonly AuthenticatedUser External = new(Guid.NewGuid(), "anna", "Anna", UserRole.Admin);

    [Fact]
    public async Task Owner_decides_alone_so_a_directory_account_cannot_rescue_a_local_one()
    {
        var local = new FakeProvider("local", owns: true, result: null);
        var ldap = new FakeProvider("ldap", owns: false, result: External);
        var auth = new AuthService([local, ldap], new FakeTokens());

        Assert.Null(await auth.LoginAsync("anna", "wrong", CancellationToken.None));
        Assert.Equal(0, ldap.Calls);
    }

    [Fact]
    public async Task Unowned_names_fall_through_to_the_next_provider()
    {
        var local = new FakeProvider("local", owns: false, result: null);
        var ldap = new FakeProvider("ldap", owns: false, result: External);
        var auth = new AuthService([local, ldap], new FakeTokens());

        var response = await auth.LoginAsync(" anna ", "secret", CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("token-anna", response!.Token);
        Assert.Equal(1, local.Calls);
        Assert.Equal(1, ldap.Calls);
    }
}
