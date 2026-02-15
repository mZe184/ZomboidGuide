namespace ZomboidGuide.Models;

public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;

    public string PackagePath { get; set; } = "package";

    public string ExeName { get; set; } = "ZomboidGuide.exe";

    public string Notes { get; set; } = string.Empty;
}
