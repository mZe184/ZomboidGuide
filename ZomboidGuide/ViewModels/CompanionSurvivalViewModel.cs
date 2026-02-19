using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly UiLocalizationService? _uiLocalizationService;
    private readonly Func<string>? _languageCodeProvider;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public CompanionSurvivalViewModel(
        LiveStateStore liveStateStore,
        StatsEngine statsEngine,
        Func<string>? gamePathProvider = null,
        UiLocalizationService? uiLocalizationService = null,
        Func<string>? languageCodeProvider = null)
    {
        _liveStateStore = liveStateStore;
        _statsEngine = statsEngine;
        _gamePathProvider = gamePathProvider;
        _uiLocalizationService = uiLocalizationService;
        _languageCodeProvider = languageCodeProvider;
        ApplyLocalization();
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    [ObservableProperty]
    private string title = "Survival Dashboard";

    [ObservableProperty]
    private string combatSectionTitle = "Combat";

    [ObservableProperty]
    private string topIssuesSectionTitle = "Top Issues";

    [ObservableProperty]
    private string activeMoodlesSectionTitle = "Active Moodles";

    [ObservableProperty]
    private string currentVitalsSectionTitle = "Current Vitals";

    [ObservableProperty]
    private string killsTotalText = "0";

    [ObservableProperty]
    private string killsThisSessionText = "0";

    [ObservableProperty]
    private string killsPerHourText = "0.0 / h";

    [ObservableProperty]
    private string timeSurvivedText = "0d 00h 00m";

    [ObservableProperty]
    private string killsTotalLineText = "Total Kills: 0";

    [ObservableProperty]
    private string killsThisSessionLineText = "Kills This Session: 0";

    [ObservableProperty]
    private string killsPerHourLineText = "Kills / hour (played): 0.0 / h";

    [ObservableProperty]
    private string timeSurvivedLineText = "Time survived: 0d 00h 00m";

    [ObservableProperty]
    private string dangerLabelText = "GRAY";

    [ObservableProperty]
    private string dangerDisplayText = "Unknown";

    [ObservableProperty]
    private IBrush dangerDisplayBrush = DangerUnknownBrush;

    [ObservableProperty]
    private int dangerIndex;

    [ObservableProperty]
    private string dangerIndexLineText = "Index: 0/100";

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
    private string fatigueLineText = "Fatigue: 0%";

    [ObservableProperty]
    private string tirednessLineText = "Tiredness: 0%";

    [ObservableProperty]
    private string enduranceLineText = "Endurance: 100%";

    [ObservableProperty]
    private string hungerLineText = "Hunger: 0%";

    [ObservableProperty]
    private string thirstLineText = "Thirst: 0%";

    [ObservableProperty]
    private string painLineText = "Pain: 0%";

    [ObservableProperty]
    private string outOfBreathLineText = "Out of Breath: 0%";

    [ObservableProperty]
    private string queasyLineText = "Queasy: 0%";

    [ObservableProperty]
    private ObservableCollection<CompanionMoodleIconViewModel> activeMoodles = [];

    [ObservableProperty]
    private ObservableCollection<string> topIssues = [];

    public void ApplyLocalization()
    {
        Title = T("Survival Dashboard", "Survival Übersicht");
        CombatSectionTitle = T("Combat", "Kampf");
        TopIssuesSectionTitle = T("Top Issues", "Wichtigste Probleme");
        ActiveMoodlesSectionTitle = T("Active Moodles", "Aktive Moodles");
        CurrentVitalsSectionTitle = T("Current Vitals", "Aktuelle Werte");
        Refresh();
    }

    private void Refresh()
    {
        var latest = _liveStateStore.GetLatest();
        var sessionStats = _liveStateStore.GetSessionStats();
        var history = _liveStateStore.GetHistory(TimeSpan.FromMinutes(60));
        var summary = _statsEngine.BuildSummary(latest, history);

        KillsTotalText = sessionStats.KillsTotal.ToString(CultureInfo.InvariantCulture);
        KillsThisSessionText = sessionStats.KillsThisSession.ToString(CultureInfo.InvariantCulture);
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
        UpdateDisplayLines();

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
            TopIssues.Add(LocalizeIssue(issue));
        }
    }

    private void UpdateDisplayLines()
    {
        KillsTotalLineText = string.Format(
            CultureInfo.CurrentCulture,
            T("Total Kills: {0}", "Kills gesamt: {0}"),
            KillsTotalText);
        KillsThisSessionLineText = string.Format(
            CultureInfo.CurrentCulture,
            T("Kills This Session: {0}", "Kills diese Session: {0}"),
            KillsThisSessionText);
        KillsPerHourLineText = string.Format(
            CultureInfo.CurrentCulture,
            T("Kills / hour (played): {0}", "Kills / Stunde (gespielt): {0}"),
            KillsPerHourText);
        TimeSurvivedLineText = string.Format(
            CultureInfo.CurrentCulture,
            T("Time survived: {0}", "Überlebt seit: {0}"),
            TimeSurvivedText);
        DangerIndexLineText = string.Format(
            CultureInfo.CurrentCulture,
            T("Index: {0}/100", "Index: {0}/100"),
            DangerIndex);
        FatigueLineText = string.Format(CultureInfo.CurrentCulture, T("Fatigue: {0}%", "Müdigkeit: {0}%"), FatigueValue);
        TirednessLineText = string.Format(CultureInfo.CurrentCulture, T("Tiredness: {0}%", "Erschöpfung: {0}%"), TirednessValue);
        EnduranceLineText = string.Format(CultureInfo.CurrentCulture, T("Endurance: {0}%", "Ausdauer: {0}%"), EnduranceValue);
        HungerLineText = string.Format(CultureInfo.CurrentCulture, T("Hunger: {0}%", "Hunger: {0}%"), HungerValue);
        ThirstLineText = string.Format(CultureInfo.CurrentCulture, T("Thirst: {0}%", "Durst: {0}%"), ThirstValue);
        PainLineText = string.Format(CultureInfo.CurrentCulture, T("Pain: {0}%", "Schmerz: {0}%"), PainValue);
        OutOfBreathLineText = string.Format(CultureInfo.CurrentCulture, T("Out of Breath: {0}%", "Außer Atem: {0}%"), OutOfBreathValue);
        QueasyLineText = string.Format(CultureInfo.CurrentCulture, T("Queasy: {0}%", "Übelkeit: {0}%"), QueasyValue);
    }

    private string LocalizeIssue(string issue)
    {
        return issue switch
        {
            "High fatigue/tiredness" => T("High fatigue/tiredness", "Müdigkeit/Erschöpfung ist hoch"),
            "Low endurance" => T("Low endurance", "Ausdauer ist niedrig"),
            "Food/water critical" => T("Food/water critical", "Nahrung/Wasser ist kritisch"),
            "High panic/stress" => T("High panic/stress", "Panik/Stress ist hoch"),
            "Pain is elevated" => T("Pain is elevated", "Schmerz ist erhöht"),
            "Out of breath" => T("Out of breath", "Außer Atem"),
            "Queasy / sickness" => T("Queasy / sickness", "Übelkeit / Krankheit"),
            "No major issues" => T("No major issues", "Keine größeren Probleme"),
            "Weight warning" => T("Weight warning", "Gewichts-Warnung"),
            "Injuries" => T("Injuries", "Verletzungen"),
            _ => issue,
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
                DangerDisplayText = T("Critical", "Kritisch");
                DangerDisplayBrush = DangerCriticalBrush;
                break;
            case "ORANGE":
                DangerDisplayText = T("Risky", "Gefährlich");
                DangerDisplayBrush = DangerRiskyBrush;
                break;
            case "YELLOW":
                DangerDisplayText = T("Caution", "Unsicher");
                DangerDisplayBrush = DangerCautionBrush;
                break;
            case "GREEN":
                DangerDisplayText = T("Safe", "Sicher");
                DangerDisplayBrush = DangerSafeBrush;
                break;
            default:
                DangerDisplayText = T("Unknown", "Unbekannt");
                DangerDisplayBrush = DangerUnknownBrush;
                break;
        }
    }

    private string T(string english, string german)
    {
        var languageCode = _languageCodeProvider?.Invoke();
        return _uiLocalizationService?.Translate(languageCode, english, german) ?? english;
    }
}
