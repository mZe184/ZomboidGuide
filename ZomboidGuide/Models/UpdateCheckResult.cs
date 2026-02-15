using System;

namespace ZomboidGuide.Models;

public sealed class UpdateCheckResult
{
    public bool Success { get; init; }

    public bool UpdateAvailable { get; init; }

    public string Message { get; init; } = string.Empty;

    public Version CurrentVersion { get; init; } = new(1, 0, 0);

    public Version? AvailableVersion { get; init; }

    public string PackagePath { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    public string DownloadFileName { get; init; } = string.Empty;

    public string ExeName { get; init; } = "ZomboidGuide.exe";

    public string Notes { get; init; } = string.Empty;
}
