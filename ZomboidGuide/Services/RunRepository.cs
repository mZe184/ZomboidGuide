using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class RunRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _sync = new();
    private readonly SessionSyncService _sessionSyncService = new();
    private readonly string _runsDirectoryPath;
    private readonly string _appDataDirectoryPath;
    private readonly Dictionary<string, RunAggregate> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastSaveImportScanUtc = DateTimeOffset.MinValue;

    public RunRepository(string? runsDirectoryPath = null)
    {
        _runsDirectoryPath = string.IsNullOrWhiteSpace(runsDirectoryPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZomboidGuide", "runs")
            : runsDirectoryPath;
        _appDataDirectoryPath = Path.GetDirectoryName(_runsDirectoryPath)
            ?? _runsDirectoryPath;
    }

    public void UpsertSnapshot(RunId runId, GameSnapshot snapshot, string? playerName)
    {
        if (runId.IsUnknown || snapshot is null)
        {
            return;
        }

        lock (_sync)
        {
            var aggregate = LoadOrCreate(runId, playerName, snapshot.TimestampUtc);
            ApplySnapshot(aggregate, snapshot, playerName);
            SaveAggregate(aggregate);
        }
    }

    public IReadOnlyList<RunMeta> LoadRunMetas()
    {
        lock (_sync)
        {
            LoadAllDiscoveredRuns();

            return _cache.Values
                .Select(entry => entry.Meta)
                .OrderByDescending(meta => meta.UpdatedUtc)
                .ToList();
        }
    }

    public RunAggregate? LoadAggregate(RunId runId)
    {
        if (runId.IsUnknown)
        {
            return null;
        }

        lock (_sync)
        {
            if (_cache.TryGetValue(runId.Value, out var cached))
            {
                return cached;
            }

            LoadAllDiscoveredRuns();
            if (_cache.TryGetValue(runId.Value, out cached))
            {
                return cached;
            }

            var loaded = TryLoadFromFile(runId);
            if (loaded is not null)
            {
                _cache[runId.Value] = loaded;
            }

            return loaded;
        }
    }

    private RunAggregate LoadOrCreate(RunId runId, string? playerName, DateTimeOffset timestampUtc)
    {
        if (_cache.TryGetValue(runId.Value, out var cached))
        {
            return cached;
        }

        var loaded = TryLoadFromFile(runId);
        if (loaded is not null)
        {
            _cache[runId.Value] = loaded;
            return loaded;
        }

        var now = timestampUtc == default ? DateTimeOffset.UtcNow : timestampUtc;
        var created = new RunAggregate
        {
            Meta = new RunMeta
            {
                RunId = runId,
                PlayerName = playerName ?? string.Empty,
                CreatedUtc = now,
                UpdatedUtc = now,
            },
            FirstSnapshotUtc = now,
            LastSnapshotUtc = now,
            LastObservedTimestampUtc = null,
        };

        _cache[runId.Value] = created;
        return created;
    }

    private RunAggregate? TryLoadFromFile(RunId runId)
    {
        try
        {
            EnsureDirectory();
            var path = BuildRunFilePath(runId);
            return TryLoadFromPath(path, runId);
        }
        catch
        {
            return null;
        }
    }

    private void SaveAggregate(RunAggregate aggregate)
    {
        EnsureDirectory();
        var path = BuildRunFilePath(aggregate.Meta.RunId);
        var json = JsonSerializer.Serialize(aggregate, JsonOptions);
        File.WriteAllText(path, json);
    }

    private void LoadAllDiscoveredRuns()
    {
        foreach (var filePath in EnumerateCandidateRunFiles())
        {
            var fallbackRunId = new RunId(Path.GetFileNameWithoutExtension(filePath) ?? string.Empty);
            var loaded = TryLoadFromPath(filePath, fallbackRunId);
            if (loaded is null || loaded.Meta.RunId.IsUnknown)
            {
                continue;
            }

            var runId = loaded.Meta.RunId.Value;
            if (_cache.TryGetValue(runId, out var existing))
            {
                if (!ShouldReplace(existing, loaded))
                {
                    continue;
                }
            }

            _cache[runId] = loaded;

            var canonicalPath = BuildRunFilePath(loaded.Meta.RunId);
            if (!filePath.Equals(canonicalPath, StringComparison.OrdinalIgnoreCase))
            {
                SaveAggregate(loaded);
            }
        }

        ImportSavegameRunsIfDue();
    }

    private void ImportSavegameRunsIfDue()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSaveImportScanUtc < TimeSpan.FromSeconds(30))
        {
            return;
        }

        _lastSaveImportScanUtc = now;
        IReadOnlyList<RunAggregate> imported;
        try
        {
            imported = _sessionSyncService.ImportRunAggregatesFromAllSaves(includeDeadCharacters: true);
        }
        catch
        {
            return;
        }

        foreach (var candidate in imported)
        {
            if (candidate.Meta.RunId.IsUnknown)
            {
                continue;
            }

            var runId = candidate.Meta.RunId.Value;
            if (_cache.TryGetValue(runId, out var existing) && !ShouldReplace(existing, candidate))
            {
                continue;
            }

            _cache[runId] = candidate;
            SaveAggregate(candidate);
        }
    }

    private IEnumerable<string> EnumerateCandidateRunFiles()
    {
        EnsureDirectory();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in SafeEnumerateFiles(_runsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            if (seen.Add(filePath))
            {
                yield return filePath;
            }
        }

        foreach (var filePath in SafeEnumerateFiles(_appDataDirectoryPath, "run-*.json", SearchOption.AllDirectories))
        {
            if (seen.Add(filePath))
            {
                yield return filePath;
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern, SearchOption option)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateFiles(directory, pattern, option);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private RunAggregate? TryLoadFromPath(string path, RunId fallbackRunId)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var aggregate = JsonSerializer.Deserialize<RunAggregate>(json, JsonOptions);
            if (aggregate is null)
            {
                return null;
            }

            if (aggregate.Meta is null)
            {
                aggregate.Meta = new RunMeta();
            }

            if (aggregate.Meta.RunId.IsUnknown)
            {
                aggregate.Meta.RunId = fallbackRunId;
            }

            if (aggregate.Meta.CreatedUtc == default)
            {
                aggregate.Meta.CreatedUtc = aggregate.FirstSnapshotUtc == default
                    ? DateTimeOffset.UtcNow
                    : aggregate.FirstSnapshotUtc;
            }

            if (aggregate.Meta.UpdatedUtc == default)
            {
                aggregate.Meta.UpdatedUtc = aggregate.LastSnapshotUtc == default
                    ? DateTimeOffset.UtcNow
                    : aggregate.LastSnapshotUtc;
            }

            return aggregate;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldReplace(RunAggregate existing, RunAggregate candidate)
    {
        if (candidate.Meta.UpdatedUtc > existing.Meta.UpdatedUtc)
        {
            return true;
        }

        if (candidate.Meta.UpdatedUtc < existing.Meta.UpdatedUtc)
        {
            return false;
        }

        return candidate.SampleCount > existing.SampleCount;
    }

    private void ApplySnapshot(RunAggregate aggregate, GameSnapshot snapshot, string? playerName)
    {
        var timestampUtc = snapshot.TimestampUtc == default
            ? DateTimeOffset.UtcNow
            : snapshot.TimestampUtc;

        if (aggregate.LastObservedTimestampUtc.HasValue &&
            timestampUtc <= aggregate.LastObservedTimestampUtc.Value &&
            aggregate.LastObservedKillsTotal == snapshot.ZombieKillsTotal &&
            aggregate.LastObservedInGameSurvivedHours == snapshot.InGameSurvivedHours)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(playerName))
        {
            aggregate.Meta.PlayerName = playerName;
        }

        if (aggregate.SampleCount == 0)
        {
            aggregate.FirstSnapshotUtc = timestampUtc;
            aggregate.FirstKillsTotal = snapshot.ZombieKillsTotal;
            aggregate.FirstInGameSurvivedHours = snapshot.InGameSurvivedHours;
        }

        aggregate.SampleCount += 1;
        aggregate.LastSnapshotUtc = timestampUtc;
        aggregate.Meta.UpdatedUtc = timestampUtc;
        aggregate.DangerSum += Math.Clamp(snapshot.DangerIndex, 0, 100);
        aggregate.FatigueSum += Math.Clamp(snapshot.Fatigue, 0.0, 1.0);
        aggregate.TirednessSum += Math.Clamp(snapshot.Tiredness, 0.0, 1.0);

        if (snapshot.ZombieKillsTotal.HasValue)
        {
            aggregate.FirstKillsTotal ??= snapshot.ZombieKillsTotal.Value;
            aggregate.LastKillsTotal = snapshot.ZombieKillsTotal.Value;
        }

        if (snapshot.InGameSurvivedHours.HasValue)
        {
            aggregate.FirstInGameSurvivedHours ??= snapshot.InGameSurvivedHours.Value;
            aggregate.LastInGameSurvivedHours = snapshot.InGameSurvivedHours.Value;
        }

        if (snapshot.InGameSurvivedHours.HasValue && snapshot.RealPlayedHours.HasValue && snapshot.RealPlayedHours.Value > 0.0)
        {
            aggregate.InGameHoursPerRealHour = Math.Max(0.1, snapshot.InGameSurvivedHours.Value / snapshot.RealPlayedHours.Value);
        }

        AddEstimatedSleepHours(aggregate, snapshot, timestampUtc);
        UpdateDailyStats(aggregate, snapshot, timestampUtc);

        aggregate.LastObservedTimestampUtc = timestampUtc;
        aggregate.LastObservedKillsTotal = snapshot.ZombieKillsTotal;
        aggregate.LastObservedInGameSurvivedHours = snapshot.InGameSurvivedHours;
    }

    private static void AddEstimatedSleepHours(RunAggregate aggregate, GameSnapshot snapshot, DateTimeOffset timestampUtc)
    {
        if (!aggregate.LastObservedTimestampUtc.HasValue ||
            !aggregate.LastObservedInGameSurvivedHours.HasValue ||
            !snapshot.InGameSurvivedHours.HasValue)
        {
            return;
        }

        var wallDeltaHours = (timestampUtc - aggregate.LastObservedTimestampUtc.Value).TotalHours;
        if (wallDeltaHours <= 0.0)
        {
            return;
        }

        var inGameDelta = snapshot.InGameSurvivedHours.Value - aggregate.LastObservedInGameSurvivedHours.Value;
        if (inGameDelta <= 0.0)
        {
            return;
        }

        var expectedInGameHours = wallDeltaHours * Math.Max(0.1, aggregate.InGameHoursPerRealHour);
        var sleepDelta = Math.Max(0.0, inGameDelta - expectedInGameHours);
        aggregate.EstimatedSleepHours += sleepDelta;
    }

    private static void UpdateDailyStats(RunAggregate aggregate, GameSnapshot snapshot, DateTimeOffset timestampUtc)
    {
        var dayIndex = ResolveDayIndex(aggregate, snapshot, timestampUtc);
        var day = aggregate.DailyStats.FirstOrDefault(entry => entry.DayIndex == dayIndex);
        if (day is null)
        {
            day = new RunDailyStats
            {
                DayIndex = dayIndex,
                FirstSnapshotUtc = timestampUtc,
                LastSnapshotUtc = timestampUtc,
                FirstKillsTotal = snapshot.ZombieKillsTotal,
                LastKillsTotal = snapshot.ZombieKillsTotal,
            };
            aggregate.DailyStats.Add(day);
        }

        day.SampleCount += 1;
        day.LastSnapshotUtc = timestampUtc;
        day.DangerSum += Math.Clamp(snapshot.DangerIndex, 0, 100);
        day.FatigueSum += Math.Clamp(snapshot.Fatigue, 0.0, 1.0);
        day.TirednessSum += Math.Clamp(snapshot.Tiredness, 0.0, 1.0);
        day.FirstKillsTotal ??= snapshot.ZombieKillsTotal;
        if (snapshot.ZombieKillsTotal.HasValue)
        {
            day.LastKillsTotal = snapshot.ZombieKillsTotal.Value;
        }

        if (aggregate.LastObservedTimestampUtc.HasValue &&
            aggregate.LastObservedInGameSurvivedHours.HasValue &&
            snapshot.InGameSurvivedHours.HasValue)
        {
            var wallDeltaHours = (timestampUtc - aggregate.LastObservedTimestampUtc.Value).TotalHours;
            var inGameDelta = snapshot.InGameSurvivedHours.Value - aggregate.LastObservedInGameSurvivedHours.Value;
            if (wallDeltaHours > 0.0 && inGameDelta > 0.0)
            {
                var expectedInGameHours = wallDeltaHours * Math.Max(0.1, aggregate.InGameHoursPerRealHour);
                day.SleepHours += Math.Max(0.0, inGameDelta - expectedInGameHours);
            }
        }
    }

    private static int ResolveDayIndex(RunAggregate aggregate, GameSnapshot snapshot, DateTimeOffset timestampUtc)
    {
        if (snapshot.InGameSurvivedHours.HasValue)
        {
            return Math.Max(0, (int)Math.Floor(snapshot.InGameSurvivedHours.Value / 24.0));
        }

        if (aggregate.FirstSnapshotUtc == default)
        {
            return 0;
        }

        var days = (timestampUtc - aggregate.FirstSnapshotUtc).TotalDays;
        return Math.Max(0, (int)Math.Floor(days));
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_runsDirectoryPath);
    }

    private string BuildRunFilePath(RunId runId)
    {
        return Path.Combine(_runsDirectoryPath, $"{runId.Value}.json");
    }
}
