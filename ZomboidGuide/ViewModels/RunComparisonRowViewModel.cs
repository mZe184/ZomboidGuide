namespace ZomboidGuide.ViewModels;

public sealed class RunComparisonRowViewModel
{
    public required string MetricText { get; init; }

    public required string RunAText { get; init; }

    public required string RunBText { get; init; }

    public required string DeltaText { get; init; }
}
