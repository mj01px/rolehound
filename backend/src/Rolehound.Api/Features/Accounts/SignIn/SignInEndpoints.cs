namespace Rolehound.Api.Features.Accounts.SignIn;

internal static class SignInEndpoints
{
    public static IEndpointRouteBuilder MapSignInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/auth").WithTags("Sign in");

        auth.MapPost("/login", Login.Handle).AllowAnonymous();
        auth.MapPost("/login/two-factor", LoginTwoFactor.Handle).AllowAnonymous();
        auth.MapPost("/login/two-factor/code", LoginTwoFactor.ResendCode).AllowAnonymous();
        auth.MapPost("/logout", Session.Logout).AllowAnonymous();
        auth.MapGet("/me", Session.Me);

        auth.MapPost("/access/inspect", FirstAccess.Inspect).AllowAnonymous();
        auth.MapPost("/access/accept", FirstAccess.Accept).AllowAnonymous();

        auth.MapPost("/password/forgot", PasswordReset.Forgot).AllowAnonymous();
        auth.MapPost("/password/reset/inspect", PasswordReset.Inspect).AllowAnonymous();
        auth.MapPost("/password/reset", PasswordReset.Reset).AllowAnonymous();

        auth.MapPost("/email/confirm", ConfirmEmailChange.Handle).AllowAnonymous();

        return endpoints;
    }
}
