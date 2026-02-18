using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class TodoStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _sync = new();
    private readonly string _stateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZomboidGuide",
        "todo-state.json");

    private bool _loaded;
    private TodoStateDocument _document = new();

    public IReadOnlyDictionary<string, TodoItemState> GetRunState(string runId)
    {
        var normalizedRunId = NormalizeRunId(runId);
        lock (_sync)
        {
            EnsureLoadedUnsafe();
            if (!_document.Runs.TryGetValue(normalizedRunId, out var runState))
            {
                return new Dictionary<string, TodoItemState>(StringComparer.OrdinalIgnoreCase);
            }

            return runState.ToDictionary(
                pair => pair.Key,
                pair => new TodoItemState
                {
                    IsPinned = pair.Value.IsPinned,
                    IsDone = pair.Value.IsDone,
                    IsDismissed = pair.Value.IsDismissed,
                },
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public TodoItemState GetItemState(string runId, string todoId)
    {
        var normalizedRunId = NormalizeRunId(runId);
        var normalizedTodoId = NormalizeTodoId(todoId);
        lock (_sync)
        {
            EnsureLoadedUnsafe();
            if (!_document.Runs.TryGetValue(normalizedRunId, out var runState) ||
                !runState.TryGetValue(normalizedTodoId, out var itemState))
            {
                return new TodoItemState();
            }

            return new TodoItemState
            {
                IsPinned = itemState.IsPinned,
                IsDone = itemState.IsDone,
                IsDismissed = itemState.IsDismissed,
            };
        }
    }

    public void SetItemState(
        string runId,
        string todoId,
        bool? isPinned = null,
        bool? isDone = null,
        bool? isDismissed = null)
    {
        var normalizedRunId = NormalizeRunId(runId);
        var normalizedTodoId = NormalizeTodoId(todoId);
        if (normalizedTodoId.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            EnsureLoadedUnsafe();
            var runState = GetOrCreateRunStateUnsafe(normalizedRunId);
            if (!runState.TryGetValue(normalizedTodoId, out var itemState))
            {
                itemState = new TodoStateEntry();
                runState[normalizedTodoId] = itemState;
            }

            if (isPinned.HasValue)
            {
                itemState.IsPinned = isPinned.Value;
            }

            if (isDone.HasValue)
            {
                itemState.IsDone = isDone.Value;
            }

            if (isDismissed.HasValue)
            {
                itemState.IsDismissed = isDismissed.Value;
            }

            SaveUnsafe();
        }
    }

    private static string NormalizeRunId(string runId)
    {
        return string.IsNullOrWhiteSpace(runId)
            ? "run-unknown"
            : runId.Trim();
    }

    private static string NormalizeTodoId(string todoId)
    {
        return string.IsNullOrWhiteSpace(todoId)
            ? string.Empty
            : todoId.Trim();
    }

    private Dictionary<string, TodoStateEntry> GetOrCreateRunStateUnsafe(string runId)
    {
        if (_document.Runs.TryGetValue(runId, out var runState))
        {
            return runState;
        }

        runState = new Dictionary<string, TodoStateEntry>(StringComparer.OrdinalIgnoreCase);
        _document.Runs[runId] = runState;
        return runState;
    }

    private void EnsureLoadedUnsafe()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        if (!File.Exists(_stateFilePath))
        {
            _document = new TodoStateDocument();
            return;
        }

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var loaded = JsonSerializer.Deserialize<TodoStateDocument>(json, JsonOptions);
            var rawDocument = loaded ?? new TodoStateDocument();
            var normalizedRuns = new Dictionary<string, Dictionary<string, TodoStateEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var run in rawDocument.Runs ?? [])
            {
                var normalizedRunItems = new Dictionary<string, TodoStateEntry>(StringComparer.OrdinalIgnoreCase);
                if (run.Value is not null)
                {
                    foreach (var item in run.Value)
                    {
                        if (string.IsNullOrWhiteSpace(item.Key))
                        {
                            continue;
                        }

                        normalizedRunItems[item.Key.Trim()] = item.Value ?? new TodoStateEntry();
                    }
                }

                if (string.IsNullOrWhiteSpace(run.Key))
                {
                    continue;
                }

                normalizedRuns[run.Key.Trim()] = normalizedRunItems;
            }

            _document = new TodoStateDocument
            {
                Runs = normalizedRuns,
            };
        }
        catch
        {
            _document = new TodoStateDocument();
        }
    }

    private void SaveUnsafe()
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_document, JsonOptions);
        File.WriteAllText(_stateFilePath, json);
    }

    private sealed class TodoStateDocument
    {
        public Dictionary<string, Dictionary<string, TodoStateEntry>> Runs { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TodoStateEntry
    {
        public bool IsPinned { get; set; }

        public bool IsDone { get; set; }

        public bool IsDismissed { get; set; }
    }
}
