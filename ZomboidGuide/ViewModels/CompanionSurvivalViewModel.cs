using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class CompanionSurvivalViewModel : ViewModelBase
{
    private static readonly IBrush DangerSafeBrush = Brush.Parse("#87C27B");
    private static readonly IBrush DangerCautionBrush = Brush.Parse("#E2C06B");
    private static readonly IBrush DangerRiskyBrush = Brush.Parse("#E59A4A");
    private static readonly IBrush DangerCriticalBrush = Brush.Parse("#E36B6B");
    private static readonly IBrush DangerUnknownBrush = Brush.Parse("#B4BAA4");
    private static readonly object IconCacheSync = new();
    private static readonly Dictionary<string, Bitmap> MoodleIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LiveStateStore _liveStateStore;
    private readonly StatsEngine _statsEngine;
    private readonly Func<string>? _gamePathProvider;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public CompanionSurvivalViewModel(
        LiveStateStore liveStateStore,
        StatsEngine statsEngine,
        Func<string>? gamePathProvider = null)
    {
        _liveStateStore = liveStateStore;
        _statsEngine = statsEngine;
        _gamePathProvider = gamePathProvider;
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    [ObservableProperty]
    private string title = "Survival Dashboard";

    [ObservableProperty]
    private string killsTotalText = "0";

    [ObservableProperty]
    private string killsThisSessionText = "0";

    [ObservableProperty]
    private string killsPerHourText = "0.0 / h";

    [ObservableProperty]
    private string timeSurvivedText = "0d 00h 00m";

    [ObservableProperty]
    private string dangerLabelText = "GRAY";

    [ObservableProperty]
    private string dangerDisplayText = "Unbekannt";

    [ObservableProperty]
    private IBrush dangerDisplayBrush = DangerUnknownBrush;

    [ObservableProperty]
    private int dangerIndex;

    [ObservableProperty]
    private int fatigueValue;

    [ObservableProperty]
    private int tirednessValue;

    [ObservableProperty]
    private int enduranceValue = 100;

    [ObservableProperty]
    private int hungerValue;

    [ObservableProperty]
    private int thirstValue;

    [ObservableProperty]
    private int painValue;

    [ObservableProperty]
    private int outOfBreathValue;

    [ObservableProperty]
    private int queasyValue;

    [ObservableProperty]
    private ObservableCollection<CompanionMoodleIconViewModel> activeMoodles = [];

    [ObservableProperty]
    private ObservableCollection<string> topIssues = [];

    private void Refresh()
    {
        var latest = _liveStateStore.GetLatest();
        var sessionStats = _liveStateStore.GetSessionStats();
        var history = _liveStateStore.GetHistory(TimeSpan.FromMinutes(60));
        var summary = _statsEngine.BuildSummary(latest, history);

        KillsTotalText = sessionStats.KillsTotal.ToString();
        KillsThisSessionText = sessionStats.KillsThisSession.ToString();
        KillsPerHourText = $"{sessionStats.KillsPerHourReal:F1} / h";
        TimeSurvivedText = FormatTimeSurvived(sessionStats.TimeSurvived);
        DangerIndex = summary.DangerIndex;
        DangerLabelText = summary.DangerLabel;
        ApplyDangerDisplay(summary.DangerLabel);
        FatigueValue = latest is null ? 0 : (int)Math.Round(Math.Clamp(latest.Fatigue, 0.0, 1.0) * 100.0);
        TirednessValue = latest is null ? 0 : (int)Math.Round(Math.Clamp(latest.Tiredness, 0.0, 1.0) * 100.0);
        EnduranceValue = latest is null ? 100 : (int)Math.Round(Math.Clamp(latest.Endurance, 0.0, 1.0) * 100.0);
        HungerValue = latest is null ? 0 : (int)Math.Round(Math.Clamp(latest.Hunger, 0.0, 1.0) * 100.0);
        ThirstValue = latest is null ? 0 : (int)Math.Round(Math.Clamp(latest.Thirst, 0.0, 1.0) * 100.0);
        PainValue = latest is null ? 0 : (int)Math.Round(Math.Clamp(latest.Pain, 0.0, 1.0) * 100.0);
        OutOfBreathValue = latest is null ? 0 : (int)Math.Round(Math.Clamp(latest.OutOfBreath, 0.0, 1.0) * 100.0);
        QueasyValue = latest is null ? 0 : (int)Math.Round(Math.Clamp(latest.Queasy, 0.0, 1.0) * 100.0);

        ActiveMoodles.Clear();
        if (latest?.Moodles is { Count: > 0 })
        {
            var gamePath = _gamePathProvider?.Invoke();
            foreach (var moodle in latest.Moodles)
            {
                if (!string.IsNullOrWhiteSpace(moodle))
                {
                    var fileName = MoodleIconResolver.ResolveIconFileName(moodle);
                    var iconPath = MoodleIconResolver.TryResolveIconPath(gamePath, fileName, out var resolvedPath)
                        ? resolvedPath
                        : string.Empty;
                    ActiveMoodles.Add(new CompanionMoodleIconViewModel
                    {
                        Label = moodle,
                        Icon = LoadMoodleIcon(iconPath),
                    });
                }
            }
        }

        TopIssues.Clear();
        foreach (var issue in summary.TopIssues)
        {
            TopIssues.Add(issue);
        }
    }

    private static string FormatTimeSurvived(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return $"{value.Days}d {value.Hours:00}h {value.Minutes:00}m";
    }

    private static Bitmap? LoadMoodleIcon(string iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        lock (IconCacheSync)
        {
            if (MoodleIconCache.TryGetValue(iconPath, out var cached))
            {
                return cached;
            }

            try
            {
                var bitmap = new Bitmap(iconPath);
                MoodleIconCache[iconPath] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    private void ApplyDangerDisplay(string label)
    {
        switch ((label ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "RED":
                DangerDisplayText = "Kritisch";
                DangerDisplayBrush = DangerCriticalBrush;
                break;
            case "ORANGE":
                DangerDisplayText = "Gefährlich";
                DangerDisplayBrush = DangerRiskyBrush;
                break;
            case "YELLOW":
                DangerDisplayText = "Unsicher";
                DangerDisplayBrush = DangerCautionBrush;
                break;
            case "GREEN":
                DangerDisplayText = "Sicher";
                DangerDisplayBrush = DangerSafeBrush;
                break;
            default:
                DangerDisplayText = "Unbekannt";
                DangerDisplayBrush = DangerUnknownBrush;
                break;
        }
    }
}
