using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Ai;

public sealed class ModelCatalogTests(ApiFactory factory) : ApiTest(factory)
{
    private static readonly Uri Catalog = new("/ai/models", UriKind.Relative);

    [Fact]
    public async Task The_catalog_lists_rated_models_first_and_only_text_models_that_follow_a_schema()
    {
        ForgetCatalog();
        await CreateUserAsync("rafael@exemplo.test", onboarded: false);
        using var browser = await SignedInAsync("rafael@exemplo.test");

        var models = await ReadAsync<List<ModelOption>>(await browser.GetAsync(Catalog, Ct));

        Assert.Equal(["vendor/top-model", "vendor/free-model:free", "vendor/paid-model"], models.Select(m => m.Id));
        Assert.Equal(61.4m, models[0].Intelligence);
        var paid = models.Single(m => m.Id == "vendor/paid-model");
        Assert.Equal(0.3m, paid.PromptPerMillion);
        Assert.Equal(2.5m, paid.CompletionPerMillion);
        Assert.False(paid.Free);
        Assert.Null(paid.ExpiresOn);
        var free = models.Single(m => m.Id == "vendor/free-model:free");
        Assert.True(free.Free);
        Assert.Equal(new DateOnly(2026, 12, 31), free.ExpiresOn);
    }

    [Fact]
    public async Task The_catalog_is_fetched_once_and_then_served_from_memory()
    {
        ForgetCatalog();
        await CreateUserAsync("rafael@exemplo.test");
        using var browser = await SignedInAsync("rafael@exemplo.test");
        var before = Factory.OpenRouter.CatalogCalls;

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(Catalog, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(Catalog, Ct)).StatusCode);

        Assert.Equal(before + 1, Factory.OpenRouter.CatalogCalls);
    }

    [Fact]
    public async Task When_OpenRouter_is_down_the_catalog_answers_503_with_a_code()
    {
        ForgetCatalog();
        await CreateUserAsync("rafael@exemplo.test");
        using var browser = await SignedInAsync("rafael@exemplo.test");
        Factory.OpenRouter.CatalogDown = true;
        try
        {
            var response = await browser.GetAsync(Catalog, Ct);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("ai_catalog_unavailable", await ProblemCodeAsync(response));
        }
        finally
        {
            Factory.OpenRouter.CatalogDown = false;
        }
    }

    [Fact]
    public async Task The_catalog_needs_a_signed_in_person()
    {
        using var anonymous = Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Catalog, Ct)).StatusCode);
    }

    private void ForgetCatalog() => Factory.Services.GetRequiredService<IMemoryCache>().Remove(ModelCatalog.CacheKey);
}
