namespace Rolehound.Api.Features.Ai;

/// <summary>The model slots a person chooses (PRD 10.1): one model per group of features.</summary>
public enum AiSlot
{
    /// <summary>Pitch and ATS analysis.</summary>
    PitchAndAts,

    /// <summary>Tailored resume.</summary>
    Resume,

    /// <summary>Score profile generation and Dossier fix.</summary>
    ProfileAndFix,
}

/// <summary>What a call was for, as logged in <see cref="AiUsage"/>.</summary>
public enum AiFeature
{
    ModelTest,
    DossierFix,
    ScoreProfile,
    Pitch,
    Ats,
    Resume,
}

/// <summary>One per person: their OpenRouter key, encrypted, and their models.</summary>
public sealed class AiSettings
{
    public Guid UserId { get; init; }

    /// <summary>Encrypted with ASP.NET Data Protection; never sent to the browser.</summary>
    public string? EncryptedKey { get; set; }

    /// <summary>Last four characters, to recognise the key (R46).</summary>
    public string? KeyHint { get; set; }

    /// <summary>
    /// Optional OpenRouter management key, only to read the account's credit balance.
    /// It can create and delete keys, so it is encrypted apart and used for nothing else.
    /// </summary>
    public string? EncryptedManagementKey { get; set; }

    public string? ManagementKeyHint { get; set; }

    public string? PitchAndAtsModel { get; set; }

    public string? ResumeModel { get; set; }

    public string? ProfileAndFixModel { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool HasKey => EncryptedKey is not null;

    public bool HasModels => PitchAndAtsModel is not null && ResumeModel is not null && ProfileAndFixModel is not null;

    public string? ModelFor(AiSlot slot) => slot switch
    {
        AiSlot.PitchAndAts => PitchAndAtsModel,
        AiSlot.Resume => ResumeModel,
        AiSlot.ProfileAndFix => ProfileAndFixModel,
        _ => null,
    };
}

/// <summary>Token usage per person, feature and model (FR-AI-03). Also feeds the hourly limit.</summary>
public sealed class AiUsage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public AiFeature Feature { get; init; }

    public required string Model { get; init; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public bool Succeeded { get; set; }

    public DateTimeOffset At { get; init; }
}
