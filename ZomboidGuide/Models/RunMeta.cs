using System;

namespace ZomboidGuide.Models;

public sealed class RunMeta
{
    public RunId RunId { get; set; } = new("run-unknown");

    public string PlayerName { get; set; } = string.Empty;

    public string SourceSavePath { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
