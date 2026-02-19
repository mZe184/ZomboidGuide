using System;
using System.Globalization;

namespace ZomboidGuide.ViewModels;

public sealed class TrackedBaseOptionViewModel
{
    public string BaseId { get; init; } = string.Empty;

    public string BaseName { get; init; } = string.Empty;

    public string BuildingId { get; init; } = string.Empty;

    public DateTimeOffset LastSeenUtc { get; init; } = DateTimeOffset.MinValue;

    public int ItemCount { get; init; }

    public int StructureCount { get; init; }

    public string Display => string.Format(
        CultureInfo.InvariantCulture,
        "{0} | items {1} | structures {2} | {3}",
        BaseName,
        ItemCount,
        StructureCount,
        LastSeenUtc == DateTimeOffset.MinValue
            ? "last seen n/a"
            : $"last seen {LastSeenUtc:yyyy-MM-dd HH:mm:ss} UTC");
}
