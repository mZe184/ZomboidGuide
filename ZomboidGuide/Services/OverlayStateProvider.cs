using System;
using System.Linq;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class OverlayStateProvider
{
    private readonly LiveStateStore _liveStateStore;
    private readonly StatsEngine _statsEngine;
    private readonly SleepOptimizer _sleepOptimizer;
    private readonly TodoEngine _todoEngine;
    private readonly UiLocalizationService? _uiLocalizationService;
    private readonly Func<string>? _languageCodeProvider;
    private readonly Func<bool>? _rotateSlidesProvider;

    public OverlayStateProvider(
        LiveStateStore liveStateStore,
        StatsEngine statsEngine,
        SleepOptimizer sleepOptimizer,
        TodoEngine todoEngine,
        UiLocalizationService? uiLocalizationService = null,
        Func<string>? languageCodeProvider = null,
        Func<bool>? rotateSlidesProvider = null)
    {
        _liveStateStore = liveStateStore;
        _statsEngine = statsEngine;
        _sleepOptimizer = sleepOptimizer;
        _todoEngine = todoEngine;
        _uiLocalizationService = uiLocalizationService;
        _languageCodeProvider = languageCodeProvider;
        _rotateSlidesProvider = rotateSlidesProvider;
    }

    public OverlayStatePayload GetState()
    {
        var languageCode = ResolveLanguageCode();
        var latest = _liveStateStore.GetLatest();
        var sessionStats = _liveStateStore.GetSessionStats();
        var history60m = _liveStateStore.GetHistory(TimeSpan.FromMinutes(60));
        var history45m = history60m
            .Where(sample => sample.TimestampUtc >= DateTimeOffset.UtcNow.AddMinutes(-45))
            .ToList();

        var stats = _statsEngine.BuildSummary(latest, history60m);
        var sleep = _sleepOptimizer.BuildRecommendation(latest, history45m);
        var todos = _todoEngine.BuildTopTodos(latest, stats, sleep);

        return new OverlayStatePayload
        {
            LabelKillsTotal = T("Kills Total", "Kills Gesamt", languageCode),
            LabelKillsThisSession = T("Kills This Session", "Kills diese Session", languageCode),
            LabelKillsPerHour = T("Kills / Hour (played)", "Kills / Stunde (gespielt)", languageCode),
            LabelTimeSurvived = T("Time Survived", "Überlebt seit", languageCode),
            LabelDanger = T("Danger Level", "Gefahrstufe", languageCode),
            LabelFatigue = T("Fatigue", "Müdigkeit", languageCode),
            LabelTiredness = T("Tiredness", "Erschöpfung", languageCode),
            LabelEndurance = T("Endurance", "Ausdauer", languageCode),
            LabelHunger = T("Hunger", "Hunger", languageCode),
            LabelThirst = T("Thirst", "Durst", languageCode),
            LabelPain = T("Pain", "Schmerz", languageCode),
            LabelOutOfBreath = T("Out of Breath", "Außer Atem", languageCode),
            LabelQueasy = T("Queasy", "Übelkeit", languageCode),
            LabelMoodles = T("Moodles", "Moodles", languageCode),
            RunId = _liveStateStore.GetRunId(),
            WorldTime = latest is null
                ? "-"
                : latest.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            KillsTotal = sessionStats.KillsTotal,
            KillsThisSession = sessionStats.KillsThisSession,
            KillsPerHour = Math.Round(sessionStats.KillsPerHourReal, 2),
            TimeSurvived = FormatTimeSurvived(sessionStats.TimeSurvived),
            DangerIndex = stats.DangerIndex,
            DangerLabel = stats.DangerLabel,
            DangerLabelText = LocalizeDangerLabel(stats.DangerLabel, languageCode),
            Fatigue = Clamp01(latest?.Fatigue ?? 0.0),
            Tiredness = Clamp01(latest?.Tiredness ?? 0.0),
            Endurance = Clamp01(latest?.Endurance ?? 1.0),
            Hunger = Clamp01(latest?.Hunger ?? 0.0),
            Thirst = Clamp01(latest?.Thirst ?? 0.0),
            Pain = Clamp01(latest?.Pain ?? 0.0),
            OutOfBreath = Clamp01(latest?.OutOfBreath ?? 0.0),
            Queasy = Clamp01(latest?.Queasy ?? 0.0),
            Moodles = latest?.Moodles
                .Where(moodle => !string.IsNullOrWhiteSpace(moodle))
                .ToArray() ?? Array.Empty<string>(),
            RotateSlides = _rotateSlidesProvider?.Invoke() ?? true,
            SleepAction = ToApiAction(sleep.Action),
            SleepConfidence = Clamp01(sleep.Confidence),
            TopTodos = todos.Select(todo => todo.Title).ToList(),
        };
    }

    private string ResolveLanguageCode()
    {
        var languageCode = _languageCodeProvider?.Invoke();
        return string.IsNullOrWhiteSpace(languageCode)
            ? "EN"
            : languageCode;
    }

    private string T(string english, string german, string languageCode)
    {
        return _uiLocalizationService?.Translate(languageCode, english, german) ?? english;
    }

    private string LocalizeDangerLabel(string dangerLabel, string languageCode)
    {
        var normalized = (dangerLabel ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "GREEN" => T("Safe", "Sicher", languageCode),
            "YELLOW" => T("Caution", "Unsicher", languageCode),
            "ORANGE" => T("Risky", "Gefährlich", languageCode),
            "RED" => T("Critical", "Kritisch", languageCode),
            _ => T("Unknown", "Unbekannt", languageCode),
        };
    }

    private static string FormatTimeSurvived(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return $"{value.Days}d {value.Hours:00}h {value.Minutes:00}m";
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0.0, 1.0);
    }

    private static string ToApiAction(SleepAction action)
    {
        return action switch
        {
            SleepAction.SleepNow => "SLEEP_NOW",
            SleepAction.SleepSoon => "SLEEP_SOON",
            SleepAction.Rest => "REST",
            SleepAction.EatDrinkFirst => "EAT_DRINK_FIRST",
            SleepAction.SecureAreaFirst => "SECURE_AREA_FIRST",
            _ => "KEEP_GOING",
        };
    }
}


