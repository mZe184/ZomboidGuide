using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class GuideItem
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string GermanName { get; init; } = string.Empty;

    public string GermanNameSource { get; init; } = string.Empty;

    public string GermanNameLanguageCode { get; init; } = string.Empty;

    public GuideItemType Type { get; init; }

    public string Detail { get; init; } = string.Empty;

    public int Level { get; init; }

    public string Category { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public IReadOnlyList<string> Recipes { get; init; } = [];

    public IReadOnlyList<string> Aliases { get; init; } = [];
}
