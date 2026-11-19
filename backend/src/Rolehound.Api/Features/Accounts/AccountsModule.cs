using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Features.Accounts.Admin;
using Rolehound.Api.Features.Accounts.Identity;
using Rolehound.Api.Features.Accounts.Profile;
using Rolehound.Api.Features.Accounts.SignIn;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts;

public static class AccountsModule
{
    public const string AdminPolicy = "admin";

    public static IServiceCollection AddAccounts(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyz0123456789._-";

                // FR-ACC-04: 8 to 128 characters, an uppercase letter, a number and a special character.
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireLowercase = false;
                options.Password.RequiredUniqueChars = 1;

                // FR-ACC-15: five failures lock the account for fifteen minutes.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.SignIn.RequireConfirmedEmail = false;
                options.Tokens.ProviderMap[EmailCodeTokenProvider.Name] = new TokenProviderDescriptor(typeof(EmailCodeTokenProvider));
            })
            .AddEntityFrameworkStores<RolehoundDbContext>()
            .AddSignInManager<RolehoundSignInManager>()
            .AddClaimsPrincipalFactory<RolehoundClaimsFactory>()
            .AddErrorDescriber<PortugueseIdentityErrors>()
            .AddPasswordValidator<MaxLengthPasswordValidator>()
            .AddPasswordValidator<BreachedPasswordValidator>()
            .AddDefaultTokenProviders();

        services.AddScoped<EmailCodeTokenProvider>();

        // OWASP password storage guidance for PBKDF2 with HMAC-SHA512 (Identity's algorithm).
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);

        services.AddHttpClient(BreachedPasswordValidator.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            client.Timeout = TimeSpan.FromSeconds(3);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("rolehound");
            client.DefaultRequestHeaders.Add("Add-Padding", "true");
        });

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies(cookies =>
            {
                cookies.ApplicationCookie?.Configure(options =>
                {
                    ConfigureCookie(options, environment, "rh_session");
                    // FR-ACC-11: seven days, renewed with use, no inactivity logout.
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                });
                cookies.TwoFactorUserIdCookie?.Configure(options =>
                {
                    ConfigureCookie(options, environment, "rh_2fa");
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                });
            });

        // Every request checks the security stamp, so deactivation, password and
        // 2FA changes and "sign out everywhere" end other sessions at once.
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
            options.OnRefreshingPrincipal = context =>
            {
                var reauthenticated = context.CurrentPrincipal?.FindFirst(Reauthentication.ClaimType);
                if (reauthenticated is not null && context.NewPrincipal?.Identity is System.Security.Claims.ClaimsIdentity identity)
                {
                    identity.AddClaim(reauthenticated);
                }

                return Task.CompletedTask;
            };
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(nameof(UserRole.Admin)));

        services.AddScoped<AccountTokens>();
        services.AddScoped<AccountEmails>();
        services.AddScoped<SecurityLog>();
        services.AddScoped<AdminSeeder>();

        return services;
    }

    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSignInEndpoints();
        endpoints.MapProfileEndpoints();
        endpoints.MapAdminUserEndpoints();
        return endpoints;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, IHostEnvironment environment, string name)
    {
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        // An API answers with status codes, never with redirects to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}
