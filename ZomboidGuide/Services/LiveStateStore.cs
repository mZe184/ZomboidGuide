using System;
using System.Collections.Generic;
using System.Linq;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class LiveStateStore
{
    private const int MaxSamples = 7200;
    private readonly object _sync = new();
    private readonly Queue<GameSnapshot> _buffer = new();
    private GameSnapshot? _latest;
    private string _runId = BuildRunId(DateTimeOffset.UtcNow);
    private readonly DateTimeOffset _appSessionStartedUtc = DateTimeOffset.UtcNow;
    private int? _appSessionBaselineKills;
    private DateTimeOffset _appSessionBaselineUtc = DateTimeOffset.UtcNow;
    private double? _appSessionBaselineRealPlayedHours;

    private LiveStateStore()
    {
    }

    public static LiveStateStore Instance { get; } = new();

    public void Update(GameSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        var normalizedTimestamp = snapshot.TimestampUtc == default
            ? snapshot with { TimestampUtc = DateTimeOffset.UtcNow }
            : snapshot;

        lock (_sync)
        {
            if (ShouldRotateRun(_latest, normalizedTimestamp))
            {
                _runId = BuildRunId(normalizedTimestamp.TimestampUtc);
                _buffer.Clear();
                ResetAppSessionBaseline(normalizedTimestamp);
            }

            if (!_appSessionBaselineKills.HasValue)
            {
                ResetAppSessionBaseline(normalizedTimestamp);
            }

            if (!_appSessionBaselineRealPlayedHours.HasValue && normalizedTimestamp.RealPlayedHours.HasValue)
            {
                _appSessionBaselineRealPlayedHours = normalizedTimestamp.RealPlayedHours.Value;
            }

            _latest = normalizedTimestamp;
            _buffer.Enqueue(normalizedTimestamp);

            while (_buffer.Count > MaxSamples)
            {
                _buffer.Dequeue();
            }
        }
    }

    public GameSnapshot? GetLatest()
    {
        lock (_sync)
        {
            return _latest;
        }
    }

    public string GetRunId()
    {
        lock (_sync)
        {
            return _runId;
        }
    }

    public IReadOnlyList<GameSnapshot> GetHistory(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        lock (_sync)
        {
            return _buffer
                .Where(snapshot => snapshot.TimestampUtc >= cutoff)
                .ToList();
        }
    }

    public LiveSessionStats GetSessionStats()
    {
        lock (_sync)
        {
            var latestKills = _latest?.ZombieKillsTotal ?? _appSessionBaselineKills ?? 0;
            var baselineKills = _appSessionBaselineKills ?? latestKills;
            var killsThisSession = Math.Max(0, latestKills - baselineKills);
            var nowUtc = DateTimeOffset.UtcNow;
            var survivedSince = _appSessionBaselineUtc == default ? _appSessionStartedUtc : _appSessionBaselineUtc;
            var realSessionDuration = nowUtc - survivedSince;
            if (realSessionDuration < TimeSpan.Zero)
            {
                realSessionDuration = TimeSpan.Zero;
            }

            var inGameSurvivedHours = _latest?.InGameSurvivedHours;
            var timeSurvived = inGameSurvivedHours.HasValue && inGameSurvivedHours.Value >= 0.0
                ? TimeSpan.FromHours(inGameSurvivedHours.Value)
                : realSessionDuration;

            var playedHoursNow = _latest?.RealPlayedHours;
            var playedHoursBaseline = _appSessionBaselineRealPlayedHours;
            var playedHoursDelta = playedHoursNow.HasValue && playedHoursBaseline.HasValue
                ? Math.Max(0.0, playedHoursNow.Value - playedHoursBaseline.Value)
                : 0.0;
            var effectiveSessionDuration = playedHoursDelta > 0.0
                ? TimeSpan.FromHours(playedHoursDelta)
                : realSessionDuration;

            var hours = effectiveSessionDuration.TotalHours;
            var killsPerHourReal = hours <= 0.0
                ? 0.0
                : killsThisSession / hours;

            return new LiveSessionStats
            {
                KillsTotal = latestKills,
                KillsThisSession = killsThisSession,
                KillsPerHourReal = killsPerHourReal,
                TimeSurvived = timeSurvived,
            };
        }
    }

    private static bool ShouldRotateRun(GameSnapshot? previous, GameSnapshot current)
    {
        if (previous is null)
        {
            return false;
        }

        if (previous.ZombieKillsTotal.HasValue &&
            current.ZombieKillsTotal.HasValue &&
            current.ZombieKillsTotal.Value < previous.ZombieKillsTotal.Value)
        {
            return true;
        }

        if (previous.InGameSurvivedHours.HasValue &&
            current.InGameSurvivedHours.HasValue &&
            current.InGameSurvivedHours.Value + 0.01 < previous.InGameSurvivedHours.Value)
        {
            return true;
        }

        return false;
    }

    private static string BuildRunId(DateTimeOffset timestampUtc)
    {
        return $"run-{timestampUtc:yyyyMMdd-HHmmss}";
    }

    private void ResetAppSessionBaseline(GameSnapshot snapshot)
    {
        _appSessionBaselineKills = snapshot.ZombieKillsTotal ?? 0;
        _appSessionBaselineUtc = DateTimeOffset.UtcNow;
        _appSessionBaselineRealPlayedHours = snapshot.RealPlayedHours;
    }
}
