namespace Rolehound.Api.Features.Accounts.Admin;

internal static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/users").WithTags("Admin").RequireAuthorization(AccountsModule.AdminPolicy);

        admin.MapGet(string.Empty, AdminUsers.List);
        admin.MapPost("/{id:guid}/access-link", AdminUsers.ResendAccessLink);
        admin.MapPost("/{id:guid}/deactivate", AdminUsers.Deactivate);
        admin.MapPost("/{id:guid}/reactivate", AdminUsers.Reactivate);

        // Sensitive actions (FR-ACC-12).
        admin.MapPost(string.Empty, AdminUsers.Create).RequireRecentReauthentication();
        admin.MapPut("/{id:guid}/role", AdminUsers.ChangeRole).RequireRecentReauthentication();

        return endpoints;
    }
}
