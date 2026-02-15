namespace ZomboidGuide.ViewModels;

public sealed class StatusFilterOptionViewModel
{
    public string Key { get; init; } = "all";

    public string Label { get; init; } = "All";

    public string Display => Label;
}
