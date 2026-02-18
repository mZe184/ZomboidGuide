namespace ZomboidGuide.Models;

public sealed record TodoItemState
{
    public bool IsPinned { get; init; }

    public bool IsDone { get; init; }

    public bool IsDismissed { get; init; }
}
