using System;
using System.Collections.Generic;
using System.Linq;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class TodoEngine
{
    public IReadOnlyList<TodoItem> BuildTopTodos(GameSnapshot? latestSnapshot, StatsSummary summary, SleepRecommendation sleepRecommendation)
    {
        var todos = new List<TodoItem>();
        var createdBase = DateTimeOffset.UtcNow;
        var order = 0;

        void AddTodo(string id, string title, TodoPriority priority, string category)
        {
            if (todos.Any(todo => todo.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            todos.Add(new TodoItem
            {
                Id = id,
                Title = title,
                Priority = priority,
                Category = category,
                CreatedUtc = createdBase.AddSeconds(order++),
            });
        }

        if (latestSnapshot is null)
        {
            AddTodo("wait-data", "Warte auf frische Savegame-Daten", TodoPriority.LOW, "System");
            return todos;
        }

        if (string.Equals(summary.DangerLabel, "RED", StringComparison.OrdinalIgnoreCase))
        {
            AddTodo("danger-secure-area", "Sicheren Ort herstellen", TodoPriority.CRITICAL, "Sicherheit");
        }

        if (sleepRecommendation.Action == SleepAction.SleepNow)
        {
            AddTodo("sleep-secure-spot", "Schlafplatz sichern", TodoPriority.CRITICAL, "Schlaf");
        }

        if (latestSnapshot.Thirst >= 0.75)
        {
            AddTodo("thirst-water", "Wasser besorgen", TodoPriority.CRITICAL, "Versorgung");
        }

        if (latestSnapshot.Hunger >= 0.75)
        {
            AddTodo("hunger-food", "Essen besorgen", TodoPriority.HIGH, "Versorgung");
        }

        if (latestSnapshot.Pain >= 0.45 || HasIssue(latestSnapshot, "Injuries"))
        {
            AddTodo("pain-first-aid", "Erste Hilfe / Schmerzmittel", TodoPriority.HIGH, "Medizin");
        }

        if (latestSnapshot.Endurance <= 0.35)
        {
            AddTodo("endurance-rest", "Pause / Ausruhen", TodoPriority.MED, "Erholung");
        }

        if (latestSnapshot.Panic >= 0.70 || latestSnapshot.Stress >= 0.70)
        {
            AddTodo("panic-retreat", "Rückzug / Beruhigen", TodoPriority.MED, "Psyche");
        }

        if (HasIssue(latestSnapshot, "Weight warning"))
        {
            AddTodo("weight-drop-loot", "Gewicht reduzieren / Loot droppen", TodoPriority.MED, "Loot");
        }

        // Additional smart follow-up rules.
        if (sleepRecommendation.Action == SleepAction.SleepSoon)
        {
            AddTodo("sleep-plan", "Kurze Route planen und bald schlafen", TodoPriority.MED, "Schlaf");
        }

        if (sleepRecommendation.Action == SleepAction.SecureAreaFirst)
        {
            AddTodo("secure-before-rest", "Bereich absichern vor dem Ausruhen", TodoPriority.HIGH, "Sicherheit");
        }

        if (summary.DangerIndex >= 60 && !string.Equals(summary.DangerLabel, "RED", StringComparison.OrdinalIgnoreCase))
        {
            AddTodo("danger-fallback", "Fallback-Route vorbereiten", TodoPriority.MED, "Sicherheit");
        }

        if (todos.Count == 0)
        {
            AddTodo("steady-scavenge", "Keine akuten Aufgaben - vorsichtig weiter looten", TodoPriority.LOW, "Routine");
        }

        return todos
            .OrderByDescending(todo => PriorityRank(todo.Priority))
            .ThenBy(todo => todo.CreatedUtc)
            .Take(10)
            .ToList();
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

    private static bool HasIssue(GameSnapshot snapshot, string issue)
    {
        return snapshot.Issues.Any(entry => entry.Equals(issue, StringComparison.OrdinalIgnoreCase));
    }
}
