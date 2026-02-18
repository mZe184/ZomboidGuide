using System;

namespace ZomboidGuide.Models;

public sealed record TodoItem
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public TodoPriority Priority { get; init; } = TodoPriority.LOW;

    public string Category { get; init; } = string.Empty;

    public bool IsPinned { get; init; }

    public bool IsDone { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
