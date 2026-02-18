using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed record GameSnapshot
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public int? ZombieKillsTotal { get; init; }

    public int DangerIndex { get; init; }

    public SessionRiskLevel RiskLevel { get; init; } = SessionRiskLevel.Unknown;

    public double Fatigue { get; init; }

    public double Tiredness { get; init; }

    public double Endurance { get; init; } = 1.0;

    public double Hunger { get; init; }

    public double Thirst { get; init; }

    public double Pain { get; init; }

    public double OutOfBreath { get; init; }

    public double Queasy { get; init; }

    public double Panic { get; init; }

    public double Stress { get; init; }

    public double? InGameSurvivedHours { get; init; }

    public double? RealPlayedHours { get; init; }

    public IReadOnlyList<string> Moodles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}
