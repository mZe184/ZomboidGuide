using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ZomboidGuide.Models;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class CompanionIntelligentTodoViewModel : ViewModelBase
{
    private readonly LiveStateStore _liveStateStore;
    private readonly StatsEngine _statsEngine;
    private readonly SleepOptimizer _sleepOptimizer;
    private readonly TodoEngine _todoEngine;
    private readonly TodoStateStore _todoStateStore;
    private readonly UiLocalizationService? _uiLocalizationService;
    private readonly Func<string>? _languageCodeProvider;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public CompanionIntelligentTodoViewModel(
        LiveStateStore liveStateStore,
        StatsEngine statsEngine,
        SleepOptimizer sleepOptimizer,
        TodoEngine todoEngine,
        TodoStateStore todoStateStore,
        UiLocalizationService? uiLocalizationService = null,
        Func<string>? languageCodeProvider = null)
    {
        _liveStateStore = liveStateStore;
        _statsEngine = statsEngine;
        _sleepOptimizer = sleepOptimizer;
        _todoEngine = todoEngine;
        _todoStateStore = todoStateStore;
        _uiLocalizationService = uiLocalizationService;
        _languageCodeProvider = languageCodeProvider;
        ApplyLocalization();
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    [ObservableProperty]
    private string title = "Smart To-Do";

    [ObservableProperty]
    private string subtitle = "Live prioritized tasks (saved per run)";

    [ObservableProperty]
    private string contextText = "Run: - | Danger: - | Sleep: -";

    [ObservableProperty]
    private ObservableCollection<CompanionTodoItemRowViewModel> todoItems = [];

    public void ApplyLocalization()
    {
        Title = T("Smart To-Do", "Smart To-Do");
        Subtitle = T(
            "Live prioritized tasks (saved per run)",
            "Live priorisierte Aufgaben (pro Run gespeichert)");
        Refresh();
    }

    private void Refresh()
    {
        var latest = _liveStateStore.GetLatest();
        var history = _liveStateStore.GetHistory(TimeSpan.FromMinutes(60));
        var summary = _statsEngine.BuildSummary(latest, history);
        var history45m = history
            .Where(sample => sample.TimestampUtc >= DateTimeOffset.UtcNow.AddMinutes(-45))
            .ToList();
        var sleep = _sleepOptimizer.BuildRecommendation(latest, history45m);
        var runId = _liveStateStore.GetRunId();
        var generated = _todoEngine.BuildTopTodos(latest, summary, sleep);
        var stateById = _todoStateStore.GetRunState(runId);
        var rows = new List<CompanionTodoItemRowViewModel>();

        foreach (var todo in generated)
        {
            var state = stateById.TryGetValue(todo.Id, out var persisted)
                ? persisted
                : new TodoItemState();
            if (state.IsDismissed)
            {
                continue;
            }

            var isPinned = state.IsPinned || todo.IsPinned;
            var isDone = state.IsDone || todo.IsDone;
            rows.Add(new CompanionTodoItemRowViewModel
            {
                Id = todo.Id,
                Title = todo.Title,
                Priority = todo.Priority,
                Category = todo.Category,
                CreatedUtc = todo.CreatedUtc,
                IsPinned = isPinned,
                IsDone = isDone,
                PriorityText = LocalizePriority(todo.Priority),
                MetaText = string.Format(
                    CultureInfo.CurrentCulture,
                    T("Category: {0} | Time: {1:HH:mm:ss} UTC", "Kategorie: {0} | Zeit: {1:HH:mm:ss} UTC"),
                    LocalizeCategory(todo.Category),
                    todo.CreatedUtc),
                PinButtonText = isPinned
                    ? T("Unpin", "Lösen")
                    : T("Pin", "Anheften"),
                DoneButtonText = isDone
                    ? T("Undo", "Rückgängig")
                    : T("Done", "Erledigt"),
                DismissButtonText = T("Dismiss", "Ausblenden"),
                PinCommand = CompanionTodoItemRowViewModel.BuildCommand(() => TogglePinned(runId, todo.Id, isPinned)),
                DoneCommand = CompanionTodoItemRowViewModel.BuildCommand(() => ToggleDone(runId, todo.Id, isDone)),
                DismissCommand = CompanionTodoItemRowViewModel.BuildCommand(() => Dismiss(runId, todo.Id)),
            });
        }

        var ordered = rows
            .OrderByDescending(item => item.IsPinned)
            .ThenByDescending(item => PriorityRank(item.Priority))
            .ThenBy(item => item.CreatedUtc)
            .Take(10)
            .ToList();

        TodoItems.Clear();
        foreach (var item in ordered)
        {
            TodoItems.Add(item);
        }

        ContextText = string.Format(
            CultureInfo.CurrentCulture,
            T("Run: {0} | Danger: {1} ({2}/100) | Sleep: {3}", "Run: {0} | Gefahr: {1} ({2}/100) | Schlaf: {3}"),
            runId,
            LocalizeDangerLabel(summary.DangerLabel),
            summary.DangerIndex,
            LocalizeSleepAction(sleep.Action));
    }

    private void TogglePinned(string runId, string todoId, bool currentPinned)
    {
        _todoStateStore.SetItemState(runId, todoId, isPinned: !currentPinned);
        Refresh();
    }

    private void ToggleDone(string runId, string todoId, bool currentDone)
    {
        _todoStateStore.SetItemState(runId, todoId, isDone: !currentDone);
        Refresh();
    }

    private void Dismiss(string runId, string todoId)
    {
        _todoStateStore.SetItemState(runId, todoId, isDismissed: true);
        Refresh();
    }

    private static int PriorityRank(TodoPriority priority)
    {
        return priority switch
        {
            TodoPriority.CRITICAL => 4,
            TodoPriority.HIGH => 3,
            TodoPriority.MED => 2,
            _ => 1,
        };
    }

    private string LocalizePriority(TodoPriority priority)
    {
        return priority switch
        {
            TodoPriority.CRITICAL => T("CRITICAL", "KRITISCH"),
            TodoPriority.HIGH => T("HIGH", "HOCH"),
            TodoPriority.MED => T("MED", "MITTEL"),
            _ => T("LOW", "NIEDRIG"),
        };
    }

    private string LocalizeSleepAction(SleepAction action)
    {
        return action switch
        {
            SleepAction.SleepNow => T("Sleep now", "Jetzt schlafen"),
            SleepAction.SleepSoon => T("Sleep soon", "Bald schlafen"),
            SleepAction.Rest => T("Rest", "Ausruhen"),
            SleepAction.EatDrinkFirst => T("Eat/drink first", "Erst essen/trinken"),
            SleepAction.SecureAreaFirst => T("Secure area first", "Erst Bereich absichern"),
            _ => T("Keep going", "Weiter machen"),
        };
    }

    private string LocalizeDangerLabel(string label)
    {
        var normalized = (label ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "GREEN" => T("Safe", "Sicher"),
            "YELLOW" => T("Caution", "Unsicher"),
            "ORANGE" => T("Risky", "Gefährlich"),
            "RED" => T("Critical", "Kritisch"),
            _ => T("Unknown", "Unbekannt"),
        };
    }

    private string LocalizeCategory(string category)
    {
        var normalized = (category ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "SYSTEM" => T("System", "System"),
            "SAFETY" => T("Safety", "Sicherheit"),
            "SICHERHEIT" => T("Safety", "Sicherheit"),
            "SLEEP" => T("Sleep", "Schlaf"),
            "SCHLAF" => T("Sleep", "Schlaf"),
            "SUPPLY" => T("Supply", "Versorgung"),
            "VERSORGUNG" => T("Supply", "Versorgung"),
            "MEDICAL" => T("Medical", "Medizin"),
            "MEDIZIN" => T("Medical", "Medizin"),
            "RECOVERY" => T("Recovery", "Erholung"),
            "ERHOLUNG" => T("Recovery", "Erholung"),
            "PSYCHE" => T("Psyche", "Psyche"),
            "LOOT" => T("Loot", "Loot"),
            "ROUTINE" => T("Routine", "Routine"),
            _ => category ?? string.Empty,
        };
    }

    private string T(string english, string german)
    {
        var languageCode = _languageCodeProvider?.Invoke();
        return _uiLocalizationService?.Translate(languageCode, english, german) ?? english;
    }
}
