using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ZomboidGuide.Models;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class CompanionSleepViewModel : ViewModelBase
{
    private readonly LiveStateStore _liveStateStore;
    private readonly SleepOptimizer _sleepOptimizer;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public CompanionSleepViewModel(LiveStateStore liveStateStore, SleepOptimizer sleepOptimizer)
    {
        _liveStateStore = liveStateStore;
        _sleepOptimizer = sleepOptimizer;
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    [ObservableProperty]
    private string title = "Sleep";

    [ObservableProperty]
    private string actionText = "KEEP GOING";

    [ObservableProperty]
    private int confidencePercent;

    [ObservableProperty]
    private string confidenceText = "0%";

    [ObservableProperty]
    private ObservableCollection<string> reasons = [];

    private void Refresh()
    {
        var latest = _liveStateStore.GetLatest();
        var history = _liveStateStore.GetHistory(TimeSpan.FromMinutes(45));
        var recommendation = _sleepOptimizer.BuildRecommendation(latest, history);

        ActionText = recommendation.Action switch
        {
            SleepAction.SleepNow => "SLEEP NOW",
            SleepAction.SleepSoon => "SLEEP SOON",
            SleepAction.Rest => "REST",
            SleepAction.EatDrinkFirst => "EAT/DRINK FIRST",
            SleepAction.SecureAreaFirst => "SECURE AREA FIRST",
            _ => "KEEP GOING",
        };

        ConfidencePercent = (int)Math.Round(Math.Clamp(recommendation.Confidence, 0.0, 1.0) * 100.0);
        ConfidenceText = $"{ConfidencePercent}%";

        Reasons.Clear();
        foreach (var reason in recommendation.ReasonCodes)
        {
            Reasons.Add(MapReason(reason));
        }
    }

    private static string MapReason(string reasonCode)
    {
        return reasonCode switch
        {
            "HUNGER_OR_THIRST_CRITICAL" => "Hunger/thirst is critical.",
            "PANIC_OR_STRESS_CRITICAL" => "Panic/stress is too high.",
            "TIRED_OR_FATIGUED_CRITICAL" => "Fatigue/tiredness is critical.",
            "TIRED_OR_FATIGUED_HIGH" => "Fatigue/tiredness is high.",
            "ENDURANCE_LOW" => "Endurance is very low.",
            "NO_DATA" => "Waiting for session data.",
            "STABLE" => "Vitals are currently stable.",
            _ => reasonCode,
        };
    }
}
