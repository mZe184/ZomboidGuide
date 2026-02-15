namespace ZomboidGuide.ViewModels;

public sealed class SessionSkillLevelViewModel : ViewModelBase
{
    public string Name { get; init; } = string.Empty;

    public int Level { get; init; }

    public string Display => $"Level {Level}";
}
