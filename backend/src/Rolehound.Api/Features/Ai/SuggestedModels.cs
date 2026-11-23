using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Ai;

/// <summary>
/// The admin's model suggestions, one row. Until the admin saves one, the
/// suggestions come from configuration (<c>Ai:Suggested</c>).
/// </summary>
public sealed class SuggestedModelsRow
{
    public int Id { get; init; } = 1;

    public required string PitchAndAts { get; set; }

    public required string Resume { get; set; }

    public required string ProfileAndFix { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class SuggestedModelsConfiguration : IEntityTypeConfiguration<SuggestedModelsRow>
{
    public void Configure(EntityTypeBuilder<SuggestedModelsRow> builder)
    {
        builder.ToTable("suggested_models", t => t.HasCheckConstraint("ck_suggested_models_single_row", "id = 1"));
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.PitchAndAts).HasMaxLength(200);
        builder.Property(s => s.Resume).HasMaxLength(200);
        builder.Property(s => s.ProfileAndFix).HasMaxLength(200);
    }
}

internal static partial class SuggestedModels
{
    public static async Task<ModelsDto> CurrentAsync(RolehoundDbContext db, IOptions<AiOptions> options, CancellationToken ct)
    {
        var row = await db.SuggestedModels.AsNoTracking().SingleOrDefaultAsync(ct);
        var fallback = options.Value.Suggested;
        return row is null ? new ModelsDto(fallback.PitchAndAts, fallback.Resume, fallback.ProfileAndFix) : new ModelsDto(row.PitchAndAts, row.Resume, row.ProfileAndFix);
    }

    public static IEndpointRouteBuilder MapSuggestedModelsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin/ai/suggested").WithTags("Admin").RequireAuthorization(AccountsModule.AdminPolicy);
        admin.MapGet(string.Empty, async (RolehoundDbContext db, IOptions<AiOptions> options, CancellationToken ct) => TypedResults.Ok(await CurrentAsync(db, options, ct)));
        admin.MapPut(string.Empty, Save);
        return endpoints;
    }

    /// <summary>
    /// Saves new suggestions. Only the format is checked: testing a model costs
    /// tokens on someone's key, and each person's own save already tests theirs.
    /// </summary>
    private static async Task<Results<NoContent, ValidationProblem>> Save(ModelsDto request, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var (field, value) in new[] { ("pitchAndAts", request.PitchAndAts), ("resume", request.Resume), ("profileAndFix", request.ProfileAndFix) })
        {
            if (string.IsNullOrWhiteSpace(value) || !ModelId().IsMatch(value.Trim()))
            {
                errors[field] = ["Use o identificador do OpenRouter, como deepseek/deepseek-chat."];
            }
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var row = await db.SuggestedModels.SingleOrDefaultAsync(ct);
        if (row is null)
        {
            row = new SuggestedModelsRow { PitchAndAts = string.Empty, Resume = string.Empty, ProfileAndFix = string.Empty };
            db.SuggestedModels.Add(row);
        }

        row.PitchAndAts = request.PitchAndAts.Trim();
        row.Resume = request.Resume.Trim();
        row.ProfileAndFix = request.ProfileAndFix.Trim();
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*/[A-Za-z0-9._:-]+$")]
    private static partial Regex ModelId();
}
