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

        var dangerIndex = Math.Clamp(summary.DangerIndex, 0, 100);
        var isDangerRed = string.Equals(summary.DangerLabel, "RED", StringComparison.OrdinalIgnoreCase) || dangerIndex >= 86;
        var isDangerHigh = dangerIndex >= 70;
        var isDangerVeryHigh = dangerIndex >= 92;

        if (isDangerRed)
        {
            AddTodo("danger-secure-area", "Sicheren Ort herstellen", TodoPriority.CRITICAL, "Sicherheit");
        }

        if (isDangerVeryHigh)
        {
            AddTodo("danger-break-contact", "Sofort Kontakt abbrechen und Rueckzug", TodoPriority.CRITICAL, "Sicherheit");
        }

        if (isDangerHigh && !isDangerRed)
        {
            AddTodo("danger-fallback", "Fallback-Route vorbereiten", TodoPriority.HIGH, "Sicherheit");
        }

        if (sleepRecommendation.Action == SleepAction.SleepNow)
        {
            AddTodo("sleep-secure-spot", "Schlafplatz sichern", TodoPriority.CRITICAL, "Schlaf");
        }

        if (sleepRecommendation.Action == SleepAction.SecureAreaFirst)
        {
            AddTodo("secure-before-rest", "Bereich absichern vor dem Ausruhen", TodoPriority.HIGH, "Sicherheit");
        }

        if (sleepRecommendation.Action == SleepAction.SleepSoon)
        {
            AddTodo("sleep-plan", "Kurze Route planen und bald schlafen", TodoPriority.MED, "Schlaf");
        }

        if (sleepRecommendation.Action == SleepAction.Rest)
        {
            AddTodo("rest-short-break", "Kurze Pause fuer Regeneration einlegen", TodoPriority.MED, "Erholung");
        }

        if (sleepRecommendation.Action == SleepAction.EatDrinkFirst)
        {
            AddTodo("eat-drink-before-sleep", "Vor dem Schlafen essen und trinken", TodoPriority.HIGH, "Versorgung");
        }

        if (latestSnapshot.Thirst >= 0.75)
        {
            AddTodo("thirst-water", "Wasser besorgen", TodoPriority.CRITICAL, "Versorgung");
        }
        else if (latestSnapshot.Thirst >= 0.45)
        {
            AddTodo("thirst-refill", "Wasser nachfuellen und Flaschen auffuellen", TodoPriority.HIGH, "Versorgung");
        }

        if (latestSnapshot.Hunger >= 0.75)
        {
            AddTodo("hunger-food", "Essen besorgen", TodoPriority.HIGH, "Versorgung");
        }
        else if (latestSnapshot.Hunger >= 0.5)
        {
            AddTodo("hunger-snack", "Naechste sichere Mahlzeit vorbereiten", TodoPriority.MED, "Versorgung");
        }

        if (latestSnapshot.OutOfBreath >= 0.70 ||
            HasAnySignal(latestSnapshot, summary, "out of breath", "outofbreath", "heavy breathing"))
        {
            AddTodo("breath-recover", "Sichtlinie brechen und Atmung stabilisieren", TodoPriority.HIGH, "Erholung");
        }

        if (latestSnapshot.Queasy >= 0.60 ||
            HasAnySignal(latestSnapshot, summary, "queasy", "nausea", "sick", "food poison", "infection", "fever"))
        {
            AddTodo("queasy-treat", "Krankheitsrisiko behandeln und Quelle vermeiden", TodoPriority.CRITICAL, "Medizin");
        }
        else if (latestSnapshot.Queasy >= 0.35)
        {
            AddTodo("queasy-monitor", "Uebelkeit beobachten und unnoetige Risiken meiden", TodoPriority.HIGH, "Medizin");
        }

        if (latestSnapshot.Pain >= 0.75 ||
            HasAnySignal(latestSnapshot, summary, "bleeding", "deep wound", "fracture", "burn", "bite", "laceration", "scratch"))
        {
            AddTodo("injury-stabilize", "Verletzungen sofort versorgen und Blutung stoppen", TodoPriority.CRITICAL, "Medizin");
        }
        else if (latestSnapshot.Pain >= 0.45 || HasAnySignal(latestSnapshot, summary, "injuries", "pain"))
        {
            AddTodo("pain-first-aid", "Erste Hilfe und Schmerzmittel einplanen", TodoPriority.HIGH, "Medizin");
        }

        if (latestSnapshot.Endurance <= 0.18)
        {
            AddTodo("endurance-critical", "Belastung sofort stoppen bis Ausdauer stabil", TodoPriority.HIGH, "Erholung");
        }
        else if (latestSnapshot.Endurance <= 0.35)
        {
            AddTodo("endurance-rest", "Pause und Erholung priorisieren", TodoPriority.MED, "Erholung");
        }

        if (latestSnapshot.Fatigue >= 0.80 || latestSnapshot.Tiredness >= 0.80)
        {
            AddTodo("fatigue-critical", "Sofort Schlaf priorisieren", TodoPriority.CRITICAL, "Schlaf");
        }
        else if (latestSnapshot.Fatigue >= 0.62 || latestSnapshot.Tiredness >= 0.62)
        {
            AddTodo("fatigue-manage", "Schlafroute vorbereiten und Aktivitaet reduzieren", TodoPriority.HIGH, "Schlaf");
        }

        if (latestSnapshot.Panic >= 0.85 || latestSnapshot.Stress >= 0.85)
        {
            AddTodo("panic-break", "Kampfdruck loesen und Abstand gewinnen", TodoPriority.HIGH, "Psyche");
        }
        else if (latestSnapshot.Panic >= 0.70 || latestSnapshot.Stress >= 0.70)
        {
            AddTodo("panic-retreat", "Rueckzug und Beruhigen", TodoPriority.MED, "Psyche");
        }

        if (HasAnySignal(latestSnapshot, summary, "weight warning", "encumbered", "heavy load", "overweight", "carrying too much"))
        {
            AddTodo("weight-drop-loot", "Gewicht reduzieren und Loot droppen", TodoPriority.MED, "Loot");
            AddTodo("weight-route", "Loot priorisieren und Rueckweg einplanen", TodoPriority.MED, "Loot");
        }

        if (HasAnySignal(latestSnapshot, summary, "wet", "cold", "chilled", "hypothermia"))
        {
            AddTodo("exposure-cold", "Trocknen, aufwaermen und nasse Kleidung ersetzen", TodoPriority.HIGH, "Versorgung");
        }

        if (HasAnySignal(latestSnapshot, summary, "hot", "overheat", "hyperthermia"))
        {
            AddTodo("exposure-heat", "Abkuehlen, trinken und direkte Sonne meiden", TodoPriority.HIGH, "Versorgung");
        }

        if (HasAnySignal(latestSnapshot, summary, "exhausted", "drowsy", "tired", "fatigue"))
        {
            AddTodo("recovery-cycle", "Risikoarme Aktivitaet bis Erholung priorisieren", TodoPriority.MED, "Erholung");
        }

        if (dangerIndex <= 35 &&
            latestSnapshot.Thirst < 0.5 &&
            latestSnapshot.Hunger < 0.5 &&
            latestSnapshot.Endurance > 0.45 &&
            latestSnapshot.Fatigue < 0.6 &&
            latestSnapshot.Tiredness < 0.6)
        {
            AddTodo("window-supplies", "Sicheres Zeitfenster fuer Vorratslauf nutzen", TodoPriority.MED, "Versorgung");
        }

        if (latestSnapshot.ZombieKillsTotal.HasValue && latestSnapshot.ZombieKillsTotal.Value > 0 && isDangerHigh)
        {
            AddTodo("post-fight-reset", "Nach Gefecht neu gruppieren und Lage pruefen", TodoPriority.MED, "Sicherheit");
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

    private static bool HasAnySignal(GameSnapshot snapshot, StatsSummary summary, params string[] terms)
    {
        if (terms is null || terms.Length == 0)
        {
            return false;
        }

        var needles = terms
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (needles.Length == 0)
        {
            return false;
        }

        var haystack = snapshot.Issues
            .Concat(snapshot.Moodles)
            .Concat(summary.TopIssues)
            .Select(Normalize)
            .Where(value => value.Length > 0);

        foreach (var value in haystack)
        {
            foreach (var needle in needles)
            {
                if (value.Contains(needle, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }
}
