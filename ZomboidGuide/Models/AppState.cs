using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class AppState
{
    public string? GamePath { get; set; }

    public bool IncludeMods { get; set; } = true;

    public string LanguageCode { get; set; } = "EN";

    public bool AutoSessionSync { get; set; } = true;

    public bool AutoUpdateCheck { get; set; } = true;

    public string BookStatusFilterKey { get; set; } = "all";

    public string MagazineStatusFilterKey { get; set; } = "all";

    public string RecipeStatusFilterKey { get; set; } = "all";

    public DateTimeOffset? LastSyncAt { get; set; }

    public DateTimeOffset? LastSessionSyncAt { get; set; }

    public DateTimeOffset? LastUpdateCheckAt { get; set; }

    public string LastKnownReleaseVersion { get; set; } = string.Empty;

    public Dictionary<string, bool> CheckedItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> SeenInInventoryItemIds { get; set; } = [];

    public List<string> CurrentInventoryItemIds { get; set; } = [];

    public List<string> KnownCatalogItemIds { get; set; } = [];

    public int InventoryDetectionVersion { get; set; }
}
