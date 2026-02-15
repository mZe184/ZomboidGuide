using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class GuideCatalog
{
    public IReadOnlyList<GuideItem> Items { get; init; } = [];

    public bool LoadedFromGameFiles { get; init; }

    public IReadOnlyList<string> SourcesScanned { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
