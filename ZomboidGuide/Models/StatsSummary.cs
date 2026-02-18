using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed record StatsSummary
{
    public int KillsTotal { get; init; }

    public double KillsPerHour { get; init; }

    public int DangerIndex { get; init; }

    public string DangerLabel { get; init; } = "GRAY";

    public IReadOnlyList<double> FatigueTrend { get; init; } = Array.Empty<double>();

    public IReadOnlyList<double> TirednessTrend { get; init; } = Array.Empty<double>();

    public IReadOnlyList<double> EnduranceTrend { get; init; } = Array.Empty<double>();

    public IReadOnlyList<string> TopIssues { get; init; } = Array.Empty<string>();
}
