using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class RunComparisonResult
{
    public RunMeta RunA { get; init; } = new();

    public RunMeta RunB { get; init; } = new();

    public DateTimeOffset ComparedUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<RunComparisonMetric> Metrics { get; init; } = Array.Empty<RunComparisonMetric>();
}
