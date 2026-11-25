using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Onboarding;

/// <summary>Marks endpoints a person may use before finishing onboarding.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowBeforeOnboardingAttribute : Attribute;

/// <summary>
/// FR-ONB-01: until onboarding is finished and the current terms are accepted, the
/// API refuses every endpoint except onboarding, profile and session ones. Closed by
/// default: an endpoint is blocked unless it opts in with <see cref="AllowBeforeOnboarding{TBuilder}"/>.
/// </summary>
public static class OnboardingGate
{
    public static TBuilder AllowBeforeOnboarding<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowBeforeOnboardingAttribute());

    public static WebApplication UseOnboardingGate(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var endpoint = context.GetEndpoint();
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (endpoint is null || userId is null || endpoint.Metadata.GetMetadata<AllowBeforeOnboardingAttribute>() is not null)
            {
                await next();
                return;
            }

            var db = context.RequestServices.GetRequiredService<RolehoundDbContext>();
            var id = Guid.Parse(userId);
            var state = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.OnboardingCompletedAt, u.TermsVersion }).SingleOrDefaultAsync(context.RequestAborted);
            if (state?.OnboardingCompletedAt is null)
            {
                await Problems.Forbidden("onboarding_required", "Termine a configuração inicial para usar o rolehound.").ExecuteAsync(context);
                return;
            }

            if (state.TermsVersion != Accounts.Profile.Terms.CurrentVersion)
            {
                await Problems.Forbidden("terms_required", "Aceite os termos de uso e a política de privacidade para continuar.").ExecuteAsync(context);
                return;
            }

            await next();
        });

        return app;
    }
}
