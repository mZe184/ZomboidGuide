namespace ZomboidGuide.ViewModels;

public sealed class LanguageOptionViewModel
{
    public string Code { get; init; } = "EN";

    public string Name { get; init; } = "English";

    public string Display => $"{Name} ({Code})";
}
