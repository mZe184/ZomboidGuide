using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public CompanionIntelligentTodoViewModel(
        LiveStateStore liveStateStore,
        StatsEngine statsEngine,
        SleepOptimizer sleepOptimizer,
        TodoEngine todoEngine,
        TodoStateStore todoStateStore)
    {
        _liveStateStore = liveStateStore;
        _statsEngine = statsEngine;
        _sleepOptimizer = sleepOptimizer;
        _todoEngine = todoEngine;
        _todoStateStore = todoStateStore;
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
        Refresh();
    }

    [ObservableProperty]
    private string title = "Smart To-Do";

    [ObservableProperty]
    private string subtitle = "Live priorisierte Aufgaben (pro Run gespeichert)";

    [ObservableProperty]
    private string contextText = "Run: - | Danger: - | Sleep: -";

    [ObservableProperty]
    private ObservableCollection<CompanionTodoItemRowViewModel> todoItems = [];

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

        ContextText = $"Run: {runId} | Danger: {summary.DangerLabel} ({summary.DangerIndex}/100) | Sleep: {sleep.Action}";
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
}
