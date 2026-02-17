using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class SessionSyncResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string SavePath { get; init; } = string.Empty;

    public string PlayerName { get; init; } = string.Empty;

    public string? ProfessionItemId { get; init; }

    public IReadOnlyCollection<string> CheckedBookItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ReadBookItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ObsoleteBookItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> CheckedMagazineItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> ReadMagazineItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> LearnedRecipeItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<SessionSkillLevel> SkillLevels { get; init; } = Array.Empty<SessionSkillLevel>();

    public SessionRiskLevel RiskLevel { get; init; } = SessionRiskLevel.Unknown;

    public int RiskScore { get; init; }

    public int InjuryRiskScore { get; init; }

    public int ExhaustionRiskScore { get; init; }

    public int FoodRiskScore { get; init; }

    public int MoodleRiskScore { get; init; }

    public int WeightRiskScore { get; init; }

    public string RiskNotes { get; init; } = string.Empty;
}
