using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class AppUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public UpdateCheckResult CheckForUpdate(string updateFeedPath, Version currentVersion)
    {
        if (string.IsNullOrWhiteSpace(updateFeedPath))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = "Kein Update-Pfad gesetzt.",
            };
        }

        var manifestPath = ResolveManifestPath(updateFeedPath);
        if (!File.Exists(manifestPath))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = $"Update-Manifest nicht gefunden: {manifestPath}",
            };
        }

        UpdateManifest manifest;
        try
        {
            var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson, JsonOptions) ?? new UpdateManifest();
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = $"Manifest konnte nicht gelesen werden: {exception.Message}",
            };
        }

        if (!TryParseVersion(manifest.Version, out var availableVersion))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = $"Ungueltige Manifest-Version: {manifest.Version}",
            };
        }

        var manifestDirectory = Path.GetDirectoryName(manifestPath) ?? updateFeedPath;
        var packagePath = manifest.PackagePath;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            packagePath = "package";
        }

        if (!Path.IsPathRooted(packagePath))
        {
            packagePath = Path.GetFullPath(Path.Combine(manifestDirectory, packagePath));
        }

        if (!Directory.Exists(packagePath))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = $"Update-Paketordner nicht gefunden: {packagePath}",
            };
        }

        var exeName = string.IsNullOrWhiteSpace(manifest.ExeName) ? "ZomboidGuide.exe" : manifest.ExeName.Trim();
        var hasNewerVersion = availableVersion > currentVersion;

        return new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = hasNewerVersion,
            CurrentVersion = currentVersion,
            AvailableVersion = availableVersion,
            PackagePath = packagePath,
            ExeName = exeName,
            Notes = manifest.Notes ?? string.Empty,
            Message = hasNewerVersion
                ? $"Update verfuegbar: {availableVersion}"
                : $"Bereits aktuell ({currentVersion})",
        };
    }

    public bool TryStartUpdate(UpdateCheckResult updateResult, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!updateResult.Success || !updateResult.UpdateAvailable)
        {
            errorMessage = "Kein installierbares Update vorhanden.";
            return false;
        }

        if (!Directory.Exists(updateResult.PackagePath))
        {
            errorMessage = $"Paketordner existiert nicht: {updateResult.PackagePath}";
            return false;
        }

        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var processName = Path.GetFileName(updateResult.ExeName);
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = "ZomboidGuide.exe";
        }

        var scriptDirectory = Path.Combine(Path.GetTempPath(), "ZomboidGuide", "updater");
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, $"apply_update_{Guid.NewGuid():N}.cmd");

        var sourceEscaped = EscapeForCmdSet(updateResult.PackagePath);
        var targetEscaped = EscapeForCmdSet(targetDirectory);
        var exeEscaped = EscapeForCmdSet(processName);

        var script = $"""
                      @echo off
                      setlocal
                      set "SOURCE={sourceEscaped}"
                      set "TARGET={targetEscaped}"
                      set "EXE={exeEscaped}"
                      set /a "elapsed=0"
                      :waitloop
                      tasklist /FI "IMAGENAME eq %EXE%" | find /I "%EXE%" >nul
                      if errorlevel 1 goto copyfiles
                      if %elapsed% GEQ 120 goto copyfiles
                      timeout /t 1 /nobreak >nul
                      set /a "elapsed+=1"
                      goto waitloop
                      :copyfiles
                      robocopy "%SOURCE%" "%TARGET%" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP >nul
                      start "" "%TARGET%\%EXE%"
                      del "%~f0"
                      endlocal
                      """;

        try
        {
            File.WriteAllText(scriptPath, script, Encoding.ASCII);
            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = $"Updater konnte nicht gestartet werden: {exception.Message}";
            return false;
        }
    }

    private static string ResolveManifestPath(string updateFeedPath)
    {
        if (updateFeedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(updateFeedPath);
        }

        return Path.GetFullPath(Path.Combine(updateFeedPath, "manifest.json"));
    }

    private static bool TryParseVersion(string rawVersion, out Version version)
    {
        version = new Version(1, 0, 0);
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var normalized = rawVersion.Trim();
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        if (!Version.TryParse(normalized, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static string EscapeForCmdSet(string value)
    {
        return value.Replace("^", "^^").Replace("%", "%%");
    }
}
