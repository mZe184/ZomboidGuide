using System;

namespace ZomboidGuide.Models;

public sealed record LiveSessionStats
{
    public int KillsTotal { get; init; }

    public int KillsThisSession { get; init; }

    public double KillsPerHourReal { get; init; }

    public TimeSpan TimeSurvived { get; init; }
}
