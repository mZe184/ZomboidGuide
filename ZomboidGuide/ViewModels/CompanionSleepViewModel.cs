using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ZomboidGuide.Models;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class CompanionSleepViewModel : ViewModelBase
{
    private readonly LiveStateStore _liveStateStore;
    private readonly SleepOptimizer _sleepOptimizer;
    private readonly UiLocalizationService? _uiLocalizationService;
    private readonly Func<string>? _languageCodeProvider;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public CompanionSleepViewModel(
        LiveStateStore liveStateStore,
        SleepOptimizer sleepOptimizer,
        UiLocalizationService? uiLocalizationService = null,
        Func<string>? languageCodeProvider = null)
    {
        _liveStateStore = liveStateStore;
        _sleepOptimizer = sleepOptimizer;
        _uiLocalizationService = uiLocalizationService;
        _languageCodeProvider = languageCodeProvider;
        ApplyLocalization();
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
    private string recommendationTitleText = "Recommendation";

    [ObservableProperty]
    private string confidenceLineText = "Confidence: 0%";

    [ObservableProperty]
    private string reasonsTitleText = "Reasons";

    [ObservableProperty]
    private ObservableCollection<string> reasons = [];

    public void ApplyLocalization()
    {
        Title = T("Sleep", "Schlaf");
        RecommendationTitleText = T("Recommendation", "Empfehlung");
        ReasonsTitleText = T("Reasons", "Gründe");
        ConfidenceLineText = string.Format(
            CultureInfo.CurrentCulture,
            T("Confidence: {0}", "Sicherheit: {0}"),
            ConfidenceText);
        Refresh();
    }

    private void Refresh()
    {
        var latest = _liveStateStore.GetLatest();
        var history = _liveStateStore.GetHistory(TimeSpan.FromMinutes(45));
        var recommendation = _sleepOptimizer.BuildRecommendation(latest, history);

        ActionText = recommendation.Action switch
        {
            SleepAction.SleepNow => T("SLEEP NOW", "JETZT SCHLAFEN"),
            SleepAction.SleepSoon => T("SLEEP SOON", "BALD SCHLAFEN"),
            SleepAction.Rest => T("REST", "AUSRUHEN"),
            SleepAction.EatDrinkFirst => T("EAT/DRINK FIRST", "ERST ESSEN/TRINKEN"),
            SleepAction.SecureAreaFirst => T("SECURE AREA FIRST", "ERST BEREICH ABSICHERN"),
            _ => T("KEEP GOING", "WEITER MACHEN"),
        };

        ConfidencePercent = (int)Math.Round(Math.Clamp(recommendation.Confidence, 0.0, 1.0) * 100.0);
        ConfidenceText = $"{ConfidencePercent}%";
        ConfidenceLineText = string.Format(
            CultureInfo.CurrentCulture,
            T("Confidence: {0}", "Sicherheit: {0}"),
            ConfidenceText);

        Reasons.Clear();
        foreach (var reason in recommendation.ReasonCodes)
        {
            Reasons.Add(MapReason(reason));
        }
    }

    private string MapReason(string reasonCode)
    {
        return reasonCode switch
        {
            "HUNGER_OR_THIRST_CRITICAL" => T("Hunger/thirst is critical.", "Hunger/Durst ist kritisch."),
            "PANIC_OR_STRESS_CRITICAL" => T("Panic/stress is too high.", "Panik/Stress ist zu hoch."),
            "TIRED_OR_FATIGUED_CRITICAL" => T("Fatigue/tiredness is critical.", "Müdigkeit/Erschöpfung ist kritisch."),
            "TIRED_OR_FATIGUED_HIGH" => T("Fatigue/tiredness is high.", "Müdigkeit/Erschöpfung ist hoch."),
            "ENDURANCE_LOW" => T("Endurance is very low.", "Ausdauer ist sehr niedrig."),
            "NO_DATA" => T("Waiting for session data.", "Warte auf Session-Daten."),
            "STABLE" => T("Vitals are currently stable.", "Werte sind aktuell stabil."),
            _ => reasonCode,
        };
    }

    private string T(string english, string german)
    {
        var languageCode = _languageCodeProvider?.Invoke();
        return _uiLocalizationService?.Translate(languageCode, english, german) ?? english;
    }
}
