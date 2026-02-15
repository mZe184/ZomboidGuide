using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class AppUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public UpdateCheckResult CheckForUpdate(string updateSource, Version currentVersion)
    {
        if (string.IsNullOrWhiteSpace(updateSource))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = "Kein GitHub-Repository gesetzt.",
            };
        }

        if (!TryParseGitHubRepository(updateSource, out var owner, out var repository))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = "Ungueltiges GitHub-Repository. Format: owner/repo",
            };
        }

        return CheckForGitHubUpdate(owner, repository, currentVersion);
    }

    public bool TryStartUpdate(UpdateCheckResult updateResult, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!updateResult.Success || !updateResult.UpdateAvailable)
        {
            errorMessage = "Kein installierbares Update vorhanden.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(updateResult.DownloadUrl))
        {
            errorMessage = "Kein Download-Link fuer das Update vorhanden.";
            return false;
        }

        if (!TryPrepareDownloadedPackage(updateResult, out var packagePath, out errorMessage))
        {
            return false;
        }

        if (!Directory.Exists(packagePath))
        {
            errorMessage = $"Paketordner existiert nicht: {packagePath}";
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

        var sourceEscaped = EscapeForCmdSet(packagePath);
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

    private static UpdateCheckResult CheckForGitHubUpdate(string owner, string repository, Version currentVersion)
    {
        var latestReleaseUrl = $"https://api.github.com/repos/{owner}/{repository}/releases/latest";
        GitHubRelease release;
        try
        {
            using var response = HttpClient.GetAsync(latestReleaseUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = currentVersion,
                    Message = $"GitHub Release konnte nicht geladen werden ({(int)response.StatusCode}).",
                };
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions) ?? new GitHubRelease();
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = $"GitHub Release-Abfrage fehlgeschlagen: {exception.Message}",
            };
        }

        var rawVersion = string.IsNullOrWhiteSpace(release.TagName) ? release.Name : release.TagName;
        if (!TryParseVersion(rawVersion, out var availableVersion))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = $"GitHub Release-Version ungueltig: {rawVersion}",
            };
        }

        var asset = SelectReleaseAsset(release);
        if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                Message = "Kein passendes ZIP-Asset im GitHub Release gefunden.",
            };
        }

        var hasNewerVersion = availableVersion > currentVersion;
        return new UpdateCheckResult
        {
            Success = true,
            UpdateAvailable = hasNewerVersion,
            CurrentVersion = currentVersion,
            AvailableVersion = availableVersion,
            DownloadUrl = asset.BrowserDownloadUrl,
            DownloadFileName = string.IsNullOrWhiteSpace(asset.Name) ? "update.zip" : asset.Name,
            ExeName = "ZomboidGuide.exe",
            Notes = string.IsNullOrWhiteSpace(release.Body) ? string.Empty : release.Body.Trim(),
            Message = hasNewerVersion
                ? $"Update verfuegbar: {availableVersion} (GitHub)"
                : $"Bereits aktuell ({currentVersion})",
        };
    }

    private static GitHubReleaseAsset? SelectReleaseAsset(GitHubRelease release)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        var zipAssets = release.Assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name) &&
                            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (zipAssets.Count == 0)
        {
            return null;
        }

        var preferred = zipAssets.FirstOrDefault(asset =>
            asset.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) &&
            !asset.Name.Contains("update-feed", StringComparison.OrdinalIgnoreCase));
        if (preferred is not null)
        {
            return preferred;
        }

        var secondChoice = zipAssets.FirstOrDefault(asset =>
            !asset.Name.Contains("update-feed", StringComparison.OrdinalIgnoreCase));
        if (secondChoice is not null)
        {
            return secondChoice;
        }

        return zipAssets.First();
    }

    private static bool TryPrepareDownloadedPackage(
        UpdateCheckResult updateResult,
        out string packagePath,
        out string errorMessage)
    {
        packagePath = string.Empty;
        errorMessage = string.Empty;

        var workingDirectory = Path.Combine(Path.GetTempPath(), "ZomboidGuide", "downloads", Guid.NewGuid().ToString("N"));
        var downloadsDirectory = Path.Combine(workingDirectory, "download");
        var extractDirectory = Path.Combine(workingDirectory, "extract");
        Directory.CreateDirectory(downloadsDirectory);
        Directory.CreateDirectory(extractDirectory);

        var fileName = string.IsNullOrWhiteSpace(updateResult.DownloadFileName)
            ? "update.zip"
            : updateResult.DownloadFileName;
        fileName = Path.GetFileName(fileName);
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"{fileName}.zip";
        }

        var zipPath = Path.Combine(downloadsDirectory, fileName);

        try
        {
            using (var response = HttpClient.GetAsync(updateResult.DownloadUrl).GetAwaiter().GetResult())
            {
                if (!response.IsSuccessStatusCode)
                {
                    errorMessage = $"Download fehlgeschlagen ({(int)response.StatusCode}).";
                    return false;
                }

                using var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var target = File.Create(zipPath);
                source.CopyTo(target);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDirectory, overwriteFiles: true);
        }
        catch (Exception exception)
        {
            errorMessage = $"Update-Paket konnte nicht geladen oder entpackt werden: {exception.Message}";
            return false;
        }

        packagePath = ResolveExtractedPackagePath(extractDirectory, updateResult.ExeName);
        if (!Directory.Exists(packagePath))
        {
            errorMessage = $"Entpackter Paketordner nicht gefunden: {packagePath}";
            return false;
        }

        return true;
    }

    private static string ResolveExtractedPackagePath(string extractDirectory, string exeName)
    {
        var packagedFolder = Path.Combine(extractDirectory, "package");
        if (Directory.Exists(packagedFolder))
        {
            return packagedFolder;
        }

        var normalizedExe = string.IsNullOrWhiteSpace(exeName) ? "ZomboidGuide.exe" : exeName.Trim();
        var directExePath = Path.Combine(extractDirectory, normalizedExe);
        if (File.Exists(directExePath))
        {
            return extractDirectory;
        }

        var subDirectories = Directory.GetDirectories(extractDirectory);
        if (subDirectories.Length == 1)
        {
            var nestedExe = Path.Combine(subDirectories[0], normalizedExe);
            if (File.Exists(nestedExe))
            {
                return subDirectories[0];
            }

            var nestedPackage = Path.Combine(subDirectories[0], "package");
            if (Directory.Exists(nestedPackage))
            {
                return nestedPackage;
            }
        }

        return extractDirectory;
    }

    private static bool TryParseGitHubRepository(string rawValue, out string owner, out string repository)
    {
        owner = string.Empty;
        repository = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var value = rawValue.Trim();
        if (value.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["github:".Length..].Trim();
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                owner = segments[0];
                repository = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                    ? segments[1][..^4]
                    : segments[1];
                return true;
            }
        }

        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            !parts[0].Contains('\\') &&
            !parts[1].Contains('\\'))
        {
            owner = parts[0];
            repository = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? parts[1][..^4]
                : parts[1];
            return true;
        }

        return false;
    }

    private static bool TryParseVersion(string rawVersion, out Version version)
    {
        version = new Version(1, 0, 0);
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return false;
        }

        var normalized = rawVersion.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MietzeMatze-ZomboidGuide-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public System.Collections.Generic.List<GitHubReleaseAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAsset
    {
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
