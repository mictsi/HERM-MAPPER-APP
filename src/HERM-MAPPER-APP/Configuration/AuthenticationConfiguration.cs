using System.Security.Claims;

namespace HERMMapperApp.Configuration;

public sealed class LocalAuthenticationOptions
{
    public bool Enabled { get; init; } = true;
}

public sealed class OpenIdConnectAuthenticationOptions
{
    public bool Enabled { get; init; }

    public bool EmitTokensAndClaimsToConsole { get; init; }

    public string DisplayName { get; init; } = "OpenID Connect";

    public string Authority { get; init; } = string.Empty;

    public string MetadataAddress { get; init; } = string.Empty;

    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string CallbackPath { get; init; } = "/signin-oidc";

    public string SignedOutCallbackPath { get; init; } = "/signout-callback-oidc";

    public bool RequireHttpsMetadata { get; init; } = true;

    public bool GetClaimsFromUserInfoEndpoint { get; init; }

    public string NameClaimType { get; init; } = "name";

    public string EmailClaimType { get; init; } = "email";

    public string GivenNameClaimType { get; init; } = "given_name";

    public string SurnameClaimType { get; init; } = "family_name";

    public string GroupClaimType { get; init; } = "groups";

    public string SubjectClaimType { get; init; } = "sub";

    public IReadOnlyList<string> Scopes { get; init; } = ["openid", "profile", "email"];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> RoleGroupMappings { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    public string EffectiveNameClaimType =>
        string.IsNullOrWhiteSpace(NameClaimType) ? ClaimTypes.Name : NameClaimType;

    public string EffectiveEmailClaimType =>
        string.IsNullOrWhiteSpace(EmailClaimType) ? ClaimTypes.Email : EmailClaimType;

    public string EffectiveGivenNameClaimType =>
        string.IsNullOrWhiteSpace(GivenNameClaimType) ? ClaimTypes.GivenName : GivenNameClaimType;

    public string EffectiveSurnameClaimType =>
        string.IsNullOrWhiteSpace(SurnameClaimType) ? ClaimTypes.Surname : SurnameClaimType;

    public string EffectiveGroupClaimType =>
        string.IsNullOrWhiteSpace(GroupClaimType) ? "groups" : GroupClaimType;

    public string EffectiveSubjectClaimType =>
        string.IsNullOrWhiteSpace(SubjectClaimType) ? "sub" : SubjectClaimType;
}
