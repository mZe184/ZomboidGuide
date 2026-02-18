using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed record SleepRecommendation
{
    public SleepAction Action { get; init; } = SleepAction.KeepGoing;

    public double Confidence { get; init; }

    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
}
