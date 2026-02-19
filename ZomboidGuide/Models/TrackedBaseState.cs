using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class TrackedBaseState
{
    public string RunKey { get; set; } = string.Empty;

    public string SaveId { get; set; } = string.Empty;

    public string PlayerName { get; set; } = string.Empty;

    public string BaseId { get; set; } = string.Empty;

    public string BaseName { get; set; } = string.Empty;

    public string BuildingId { get; set; } = string.Empty;

    public DateTimeOffset LastSeenUtc { get; set; }

    public List<string> ItemFullTypes { get; set; } = [];

    public List<string> StructureTypes { get; set; } = [];
}
