namespace ZomboidGuide.Models;

public sealed class RunComparisonMetric
{
    public string Key { get; init; } = string.Empty;

    public double RunAValue { get; init; }

    public double RunBValue { get; init; }

    public double Delta { get; init; }

    public bool IsRunABetter { get; init; }

    public bool IsTie { get; init; }

    public bool IsRunBBetter => !IsTie && !IsRunABetter;
}
