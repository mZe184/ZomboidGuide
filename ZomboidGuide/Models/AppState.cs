using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class AppState 
{
    public string? GamePath { get; set; }

    public bool IncludeMods { get; set; } = true;

    public string LanguageCode { get; set; } = string.Empty;

    public bool AutoSessionSync { get; set; } = true;

    public bool AutoUpdateCheck { get; set; } = true;

    public bool RiskIndicatorEnabled { get; set; } = true;

    public bool RiskAlertSoundsEnabled { get; set; } = true;

    public bool OverlayAutoStart { get; set; }

    public int OverlayPort { get; set; } = 8765;

    public bool OverlayRotateSlides { get; set; } = true;

    public string BookStatusFilterKey { get; set; } = "all";

    public string MagazineStatusFilterKey { get; set; } = "all";

    public string RecipeStatusFilterKey { get; set; } = "all";

    public DateTimeOffset? LastSyncAt { get; set; }

    public DateTimeOffset? LastSessionSyncAt { get; set; }

    public DateTimeOffset? LastUpdateCheckAt { get; set; }

    public string LastKnownReleaseVersion { get; set; } = string.Empty;

    public Dictionary<string, bool> CheckedItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, bool> TodoManualChecks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> SeenInInventoryItemIds { get; set; } = [];

    public List<string> CurrentInventoryItemIds { get; set; } = []; 

    public List<string> KnownCatalogItemIds { get; set; } = [];

    public int InventoryDetectionVersion { get; set; }

    public List<TrackedBaseState> TrackedBases { get; set; } = [];

    public Dictionary<string, List<string>> MultiBaseInventoryFullTypesByRun { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<string> MultiBaseInventoryFullTypes { get; set; } = [];

    public string MultiBaseActiveRunKey { get; set; } = string.Empty;

    public DateTimeOffset? LastMultiBaseSnapshotAt { get; set; }
}
