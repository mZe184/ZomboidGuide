using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZomboidGuide.Models;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class CompanionRunsViewModel : ViewModelBase
{
    private readonly RunRepository _runRepository;
    private readonly RunComparisonService _runComparisonService;
    private readonly UiLocalizationService? _uiLocalizationService;
    private readonly Func<string>? _languageCodeProvider;
    private readonly Func<string>? _currentRunIdProvider;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string _runAHeaderBaseText = "Run A";
    private string _runBHeaderBaseText = "Run B";

    public CompanionRunsViewModel(
        RunRepository runRepository,
        RunComparisonService runComparisonService,
        UiLocalizationService? uiLocalizationService = null,
        Func<string>? languageCodeProvider = null,
        Func<string>? currentRunIdProvider = null)
    {
        _runRepository = runRepository;
        _runComparisonService = runComparisonService;
        _uiLocalizationService = uiLocalizationService;
        _languageCodeProvider = languageCodeProvider;
        _currentRunIdProvider = currentRunIdProvider;
        ApplyLocalization();
        _refreshTimer.Tick += (_, _) => RefreshRuns(keepSelection: true);
        _refreshTimer.Start();
        RefreshRuns(keepSelection: false);
    }

    [ObservableProperty]
    private string title = "Runs";

    [ObservableProperty]
    private string runALabelText = "Run A";

    [ObservableProperty]
    private string runBLabelText = "Run B";

    [ObservableProperty]
    private string metricHeaderText = "Metric";

    [ObservableProperty]
    private string runAHeaderText = "Run A";

    [ObservableProperty]
    private string runBHeaderText = "Run B";

    [ObservableProperty]
    private string deltaHeaderText = "Delta";

    [ObservableProperty]
    private string refreshButtonText = "Refresh";

    [ObservableProperty]
    private string noDataText = "No run comparison data yet.";

    [ObservableProperty]
    private string saveMetricText = "Save";

    [ObservableProperty]
    private string runASaveName = "-";

    [ObservableProperty]
    private string runBSaveName = "-";

    [ObservableProperty]
    private string saveDeltaText = "-";

    [ObservableProperty]
    private ObservableCollection<RunOptionViewModel> availableRuns = [];

    [ObservableProperty]
    private RunOptionViewModel? selectedRunA;

    [ObservableProperty]
    private RunOptionViewModel? selectedRunB;

    [ObservableProperty]
    private ObservableCollection<RunComparisonRowViewModel> comparisonRows = [];

    public bool ShowNoData => ComparisonRows.Count == 0;

    partial void OnSelectedRunAChanged(RunOptionViewModel? value)
    {
        UpdateHeaderTexts();
        BuildComparison();
    }

    partial void OnSelectedRunBChanged(RunOptionViewModel? value)
    {
        UpdateHeaderTexts();
        BuildComparison();
    }

    [RelayCommand]
    private void RefreshNow()
    {
        RefreshRuns(keepSelection: true);
    }

    public void ApplyLocalization()
    {
        Title = T("Runs", "Runs");
        RunALabelText = T("Run A", "Run A");
        RunBLabelText = T("Run B", "Run B");
        MetricHeaderText = T("Metric", "Metrik");
        _runAHeaderBaseText = T("Run A", "Run A");
        _runBHeaderBaseText = T("Run B", "Run B");
        DeltaHeaderText = T("Delta", "Delta");
        SaveMetricText = T("Save", "Spielstand");
        SaveDeltaText = "-";
        RefreshButtonText = T("Refresh", "Aktualisieren");
        NoDataText = T("No run comparison data yet.", "Noch keine Run-Vergleichsdaten.");
        UpdateHeaderTexts();
        BuildComparison();
    }

    private void RefreshRuns(bool keepSelection)
    {
        var previousA = keepSelection ? SelectedRunA?.Id : null;
        var preferredCurrentRunId = (_currentRunIdProvider?.Invoke() ?? string.Empty).Trim();

        var metas = _runRepository.LoadRunMetas()
            .Where(meta => !IsExcludedRunMeta(meta))
            .ToList();

        var runEntries = metas
            .Select(meta =>
            {
                var aggregate = _runRepository.LoadAggregate(meta.RunId);
                var lifetimeHours = ResolveCharacterLifetimeHours(aggregate) ?? -1.0;
                return new
                {
                    Meta = meta,
                    Aggregate = aggregate,
                    LifetimeHours = lifetimeHours,
                };
            })
            .OrderByDescending(entry => entry.LifetimeHours)
            .ThenByDescending(entry => entry.Meta.UpdatedUtc)
            .ThenBy(entry => entry.Meta.RunId.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var options = runEntries
            .Select(entry => ToOption(entry.Meta, entry.Aggregate))
            .ToList();

        AvailableRuns.Clear();
        foreach (var option in options)
        {
            AvailableRuns.Add(option);
        }

        if (AvailableRuns.Count == 0)
        {
            SelectedRunA = null;
            SelectedRunB = null;
            UpdateHeaderTexts();
            BuildComparison();
            return;
        }

        SelectedRunA = ResolveSelection(options, preferredCurrentRunId)
                       ?? ResolveSelection(options, previousA)
                       ?? options[0];

        // Run B should default to the longest surviving run that is not Run A.
        SelectedRunB = options.FirstOrDefault(option => option.Id != SelectedRunA?.Id) ?? options[0];
        var selectedAId = SelectedRunA?.Id ?? string.Empty;
        if (selectedAId == SelectedRunB?.Id && options.Count > 1)
        {
            SelectedRunB = options.First(option => option.Id != selectedAId);
        }

        UpdateHeaderTexts();
        BuildComparison();
    }

    private void BuildComparison()
    {
        ComparisonRows.Clear();
        if (SelectedRunA is null || SelectedRunB is null || SelectedRunA.Id == SelectedRunB.Id)
        {
            OnPropertyChanged(nameof(ShowNoData));
            return;
        }

        var aggregateA = _runRepository.LoadAggregate(new RunId(SelectedRunA.Id));
        var aggregateB = _runRepository.LoadAggregate(new RunId(SelectedRunB.Id));
        if (aggregateA is null || aggregateB is null)
        {
            OnPropertyChanged(nameof(ShowNoData));
            return;
        }

        var result = _runComparisonService.Compare(aggregateA, aggregateB);
        foreach (var metric in result.Metrics)
        {
            ComparisonRows.Add(new RunComparisonRowViewModel
            {
                MetricText = LocalizeMetric(metric.Key),
                RunAText = FormatMetric(metric.Key, metric.RunAValue),
                RunBText = FormatMetric(metric.Key, metric.RunBValue),
                DeltaText = FormatDelta(metric.Key, metric.Delta),
            });
        }

        OnPropertyChanged(nameof(ShowNoData));
    }

    private RunOptionViewModel ToOption(RunMeta meta, RunAggregate? aggregate)
    {
        var playerName = string.IsNullOrWhiteSpace(meta.PlayerName)
            ? T("Unknown", "Unbekannt")
            : NormalizeDisplayPlayerName(meta);
        var saveName = FormatSaveName(meta.SourceSavePath);
        var lifetimeText = FormatLifetime(aggregate);
        var display = string.Format(
            CultureInfo.CurrentCulture,
            "{0} ({1}, {2:dd.MM.yyyy HH:mm})",
            playerName,
            lifetimeText,
            meta.UpdatedUtc.ToLocalTime());

        return new RunOptionViewModel
        {
            Id = meta.RunId.Value,
            DisplayName = display,
            HeaderText = $"{meta.RunId.Value} | {lifetimeText}",
            SaveName = saveName,
        };
    }

    private string FormatLifetime(RunAggregate? aggregate)
    {
        var hours = ResolveCharacterLifetimeHours(aggregate);
        if (!hours.HasValue)
        {
            return T("lived: -", "gelebt: -");
        }

        var totalMinutes = (long)Math.Max(0.0, Math.Round(hours.Value * 60.0));
        var totalDays = totalMinutes / (24 * 60);
        var years = totalDays / 365;
        var daysAfterYears = totalDays % 365;
        var months = daysAfterYears / 30;
        var days = daysAfterYears % 30;
        var hoursRemainder = (totalMinutes / 60) % 24;
        var minutesRemainder = totalMinutes % 60;

        if (years > 0)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                T("lived: {0}y {1}mo {2}d", "gelebt: {0}J {1}M {2}T"),
                years,
                months,
                days);
        }

        if (months > 0)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                T("lived: {0}mo {1}d", "gelebt: {0}M {1}T"),
                months,
                days);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            T("lived: {0}d {1:00}h {2:00}m", "gelebt: {0}T {1:00}h {2:00}m"),
            totalDays,
            hoursRemainder,
            minutesRemainder);
    }

    private static double? ResolveCharacterLifetimeHours(RunAggregate? aggregate)
    {
        if (aggregate is null)
        {
            return null;
        }

        if (aggregate.LastInGameSurvivedHours.HasValue)
        {
            return Math.Max(0.0, aggregate.LastInGameSurvivedHours.Value);
        }

        if (aggregate.FirstInGameSurvivedHours.HasValue && aggregate.LastInGameSurvivedHours.HasValue)
        {
            return Math.Max(0.0, aggregate.LastInGameSurvivedHours.Value - aggregate.FirstInGameSurvivedHours.Value);
        }

        return null;
    }

    private void UpdateHeaderTexts()
    {
        RunAHeaderText = SelectedRunA?.HeaderText ?? _runAHeaderBaseText;
        RunBHeaderText = SelectedRunB?.HeaderText ?? _runBHeaderBaseText;
        RunASaveName = SelectedRunA?.SaveName ?? "-";
        RunBSaveName = SelectedRunB?.SaveName ?? "-";
    }

    private RunOptionViewModel? ResolveSelection(IEnumerable<RunOptionViewModel> options, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return options.FirstOrDefault(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExcludedRunMeta(RunMeta meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.SourceSavePath) &&
            IsExcludedSavePath(meta.SourceSavePath))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(meta.PlayerName))
        {
            var text = meta.PlayerName.Trim();
            if (text.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("backup", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcludedSavePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = path.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                segment.Contains("backup", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatSaveName(string sourceSavePath)
    {
        if (string.IsNullOrWhiteSpace(sourceSavePath))
        {
            return "-";
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var segments = sourceSavePath.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return "-";
        }

        if (segments.Length >= 2)
        {
            return $"{segments[^2]}/{segments[^1]}";
        }

        return segments[^1];
    }

    private static string NormalizeDisplayPlayerName(RunMeta meta)
    {
        var name = (meta.PlayerName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(meta.SourceSavePath))
        {
            var world = Path.GetFileName(meta.SourceSavePath);
            if (!string.IsNullOrWhiteSpace(world))
            {
                var suffix = $" - {world}";
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return name[..^suffix.Length];
                }
            }
        }

        // Legacy imports can still contain "Player - World" in PlayerName.
        if (meta.RunId.Value.StartsWith("save-", StringComparison.OrdinalIgnoreCase))
        {
            var separatorIndex = name.LastIndexOf(" - ", StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                var trimmed = name[..separatorIndex].Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    return trimmed;
                }
            }
        }

        return name;
    }

    private string LocalizeMetric(string key)
    {
        return key switch
        {
            "kills_total" => T("Total Kills", "Kills gesamt"),
            "survived_time" => T("Survived Time", "Überlebte Zeit"),
            "kills_per_day" => T("Kills / Day", "Kills / Tag"),
            "avg_danger" => T("Average Danger", "Durchschnittliche Gefahr"),
            "avg_fatigue" => T("Average Fatigue", "Durchschnittliche Müdigkeit"),
            "avg_tiredness" => T("Average Tiredness", "Durchschnittliche Erschöpfung"),
            "sleep_hours" => T("Sleep Hours (est.)", "Schlafstunden (geschätzt)"),
            _ => key,
        };
    }

    private string FormatMetric(string key, double value)
    {
        return key switch
        {
            "kills_total" => Math.Round(value).ToString("N0", CultureInfo.CurrentCulture),
            "survived_time" => FormatDurationFromHours(value),
            "kills_per_day" => value.ToString("F2", CultureInfo.CurrentCulture),
            "avg_danger" => value.ToString("F1", CultureInfo.CurrentCulture),
            "avg_fatigue" => value.ToString("F1", CultureInfo.CurrentCulture) + "%",
            "avg_tiredness" => value.ToString("F1", CultureInfo.CurrentCulture) + "%",
            "sleep_hours" => value.ToString("F2", CultureInfo.CurrentCulture) + "h",
            _ => value.ToString("F2", CultureInfo.CurrentCulture),
        };
    }

    private string FormatDelta(string key, double delta)
    {
        var sign = delta >= 0.0 ? "+" : string.Empty;
        return key switch
        {
            "kills_total" => sign + Math.Round(delta).ToString("N0", CultureInfo.CurrentCulture),
            "survived_time" => sign + FormatDurationFromHours(Math.Abs(delta)),
            "avg_fatigue" or "avg_tiredness" => $"{sign}{delta:F1}%",
            "sleep_hours" => $"{sign}{delta:F2}h",
            _ => $"{sign}{delta:F2}",
        };
    }

    private static string FormatDurationFromHours(double hours)
    {
        var totalMinutes = (long)Math.Max(0.0, Math.Round(hours * 60.0));
        var totalDays = totalMinutes / (24 * 60);
        var years = totalDays / 365;
        var daysAfterYears = totalDays % 365;
        var months = daysAfterYears / 30;
        var days = daysAfterYears % 30;
        var hourPart = (totalMinutes / 60) % 24;
        var minutePart = totalMinutes % 60;

        if (years > 0)
        {
            return $"{years}y {months}mo {days}d";
        }

        if (months > 0)
        {
            return $"{months}mo {days}d";
        }

        return $"{totalDays}d {hourPart:00}h {minutePart:00}m";
    }

    private string T(string english, string german)
    {
        var languageCode = _languageCodeProvider?.Invoke();
        return _uiLocalizationService?.Translate(languageCode, english, german) ?? english;
    }
}
