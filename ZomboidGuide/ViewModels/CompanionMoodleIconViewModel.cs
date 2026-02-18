using Avalonia.Media.Imaging;

namespace ZomboidGuide.ViewModels;

public sealed class CompanionMoodleIconViewModel
{
    public required string Label { get; init; }

    public Bitmap? Icon { get; init; }
}
