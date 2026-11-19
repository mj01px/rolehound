using Rolehound.Api.Features.Onboarding;

namespace Rolehound.Api.Features.Accounts.Profile;

internal static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var account = endpoints.MapGroup("/account").WithTags("Account").AllowBeforeOnboarding();

        account.MapPost("/reauthenticate", Reauthenticate.Handle);
        account.MapPost("/reauthenticate/code", Reauthenticate.SendCode);
        account.MapGet("/security", AccountSecurity.Get);
        account.MapPost("/sessions/revoke", AccountSecurity.RevokeSessions);

        // Sensitive actions (FR-ACC-12).
        var sensitive = account.MapGroup(string.Empty).RequireRecentReauthentication();
        sensitive.MapPost("/password", AccountSecurity.ChangePassword);
        sensitive.MapPost("/email", AccountSecurity.ChangeEmail);
        sensitive.MapPost("/two-factor/totp", TwoFactor.StartTotp);
        sensitive.MapPost("/two-factor/totp/enable", TwoFactor.EnableTotp);
        sensitive.MapPost("/two-factor/email", TwoFactor.StartEmail);
        sensitive.MapPost("/two-factor/email/enable", TwoFactor.EnableEmail);
        sensitive.MapPost("/two-factor/disable", TwoFactor.Disable);
        sensitive.MapPost("/two-factor/recovery-codes", TwoFactor.RegenerateRecoveryCodes);

        return endpoints;
    }
}
