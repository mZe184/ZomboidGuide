using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class MultiBaseSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly object _sync = new();
    private readonly Dictionary<string, TrackedBaseState> _basesByRunAndId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _inventoryFullTypesByRun = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inventoryFullTypes = new(StringComparer.OrdinalIgnoreCase);
    private string _activeRunKey = string.Empty;
    private DateTimeOffset _lastSnapshotUtc = DateTimeOffset.MinValue;

    public void LoadFromState(AppState state)
    {
        lock (_sync)
        {
            _basesByRunAndId.Clear();
            _inventoryFullTypesByRun.Clear();
            _inventoryFullTypes.Clear();

            _activeRunKey = state.MultiBaseActiveRunKey?.Trim() ?? string.Empty;
            _lastSnapshotUtc = state.LastMultiBaseSnapshotAt ?? DateTimeOffset.MinValue;

            foreach (var baseState in state.TrackedBases ?? [])
            {
                if (string.IsNullOrWhiteSpace(baseState.BaseId))
                {
                    continue;
                }

                var normalized = NormalizeTrackedBase(baseState);
                _basesByRunAndId[BuildCompositeBaseKey(normalized.RunKey, normalized.BaseId)] = normalized;
            }

            foreach (var entry in state.MultiBaseInventoryFullTypesByRun ?? [])
            {
                var runKey = entry.Key?.Trim() ?? string.Empty;
                if (runKey.Length == 0)
                {
                    continue;
                }

                var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fullType in entry.Value ?? [])
                {
                    AddNormalizedToken(tokens, fullType);
                }

                if (tokens.Count > 0)
                {
                    _inventoryFullTypesByRun[runKey] = tokens;
                }
            }

            if (_inventoryFullTypesByRun.Count == 0 && _activeRunKey.Length > 0)
            {
                var legacyTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var fullType in state.MultiBaseInventoryFullTypes ?? [])
                {
                    AddNormalizedToken(legacyTokens, fullType);
                }

                if (legacyTokens.Count > 0)
                {
                    _inventoryFullTypesByRun[_activeRunKey] = legacyTokens;
                }
            }

            LoadActiveRunInventoryTokens();
            _lastSnapshotUtc = ResolveLastSnapshotForRun(_activeRunKey);
        }
    }

    public void SaveToState(AppState state)
    {
        lock (_sync)
        {
            state.TrackedBases = _basesByRunAndId.Values
                .Select(CloneBaseState)
                .OrderBy(entry => entry.RunKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.BaseName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.BaseId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            state.MultiBaseInventoryFullTypesByRun = _inventoryFullTypesByRun
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            IReadOnlyCollection<string> activeTokens = _inventoryFullTypesByRun.TryGetValue(_activeRunKey, out var tokensForRun)
                ? tokensForRun
                : Array.Empty<string>();
            state.MultiBaseInventoryFullTypes = activeTokens
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            state.MultiBaseActiveRunKey = _activeRunKey;
            state.LastMultiBaseSnapshotAt = _lastSnapshotUtc == DateTimeOffset.MinValue
                ? null
                : _lastSnapshotUtc;
        }
    }

    public MultiBaseIngestResult IngestScanJson(string payloadJson, string? runKeyOverride = null)
    {
        MultiBaseScanPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MultiBaseScanPayload>(payloadJson ?? string.Empty, JsonOptions);
        }
        catch (Exception exception)
        {
            return new MultiBaseIngestResult(false, $"Invalid JSON payload: {exception.Message}");
        }

        if (payload is null)
        {
            return new MultiBaseIngestResult(false, "Payload is empty.");
        }

        var baseId = payload.BaseId?.Trim() ?? string.Empty;
        if (baseId.Length == 0)
        {
            return new MultiBaseIngestResult(false, "baseId is required.");
        }

        if (payload.PlayerInventoryItems is null)
        {
            return new MultiBaseIngestResult(false, "playerInventoryItems is required for reliable comparison.");
        }

        var runKey = runKeyOverride?.Trim() ?? string.Empty;
        if (runKey.Length == 0)
        {
            runKey = ResolveRunKey(payload);
        }
        var baseName = payload.BaseName?.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            var suffix = baseId.Length > 8 ? baseId[..8] : baseId;
            baseName = $"Base {suffix}";
        }

        var timestampUtc = payload.TimestampUtc ?? DateTimeOffset.UtcNow;
        if (timestampUtc == default)
        {
            timestampUtc = DateTimeOffset.UtcNow;
        }

        var inventoryTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inventoryItem in payload.PlayerInventoryItems)
        {
            AddNormalizedToken(inventoryTokens, inventoryItem?.FullType);
        }

        var baseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var baseItem in payload.BaseItems ?? [])
        {
            AddNormalizedToken(baseTokens, baseItem?.FullType);
        }

        var structureTypes = (payload.Structures ?? [])
            .Select(entry => entry?.Type?.Trim() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (_sync)
        {
            _activeRunKey = runKey;
            _lastSnapshotUtc = timestampUtc;
            _inventoryFullTypesByRun[runKey] = inventoryTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
            LoadActiveRunInventoryTokens();

            var compositeKey = BuildCompositeBaseKey(runKey, baseId);
            _basesByRunAndId[compositeKey] = new TrackedBaseState
            {
                RunKey = runKey,
                SaveId = payload.SaveId?.Trim() ?? string.Empty,
                PlayerName = payload.PlayerName?.Trim() ?? string.Empty,
                BaseId = baseId,
                BaseName = baseName,
                BuildingId = payload.BuildingId?.Trim() ?? string.Empty,
                LastSeenUtc = timestampUtc,
                ItemFullTypes = baseTokens.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                StructureTypes = structureTypes,
            };
        }

        return new MultiBaseIngestResult(
            true,
            "Snapshot accepted.",
            runKey,
            baseId,
            baseName,
            inventoryTokens.Count,
            baseTokens.Count,
            structureTypes.Count,
            timestampUtc);
    }

    public bool RenameBase(string baseId, string baseName)
    {
        var normalizedBaseId = baseId?.Trim() ?? string.Empty;
        var normalizedName = baseName?.Trim() ?? string.Empty;
        if (normalizedBaseId.Length == 0 || normalizedName.Length == 0)
        {
            return false;
        }

        lock (_sync)
        {
            if (_activeRunKey.Length == 0)
            {
                return false;
            }

            var compositeKey = BuildCompositeBaseKey(_activeRunKey, normalizedBaseId);
            if (!_basesByRunAndId.TryGetValue(compositeKey, out var trackedBase))
            {
                return false;
            }

            trackedBase.BaseName = normalizedName;
            return true;
        }
    }

    public void ClearActiveRun()
    {
        lock (_sync)
        {
            if (_activeRunKey.Length == 0)
            {
                _inventoryFullTypes.Clear();
                _lastSnapshotUtc = DateTimeOffset.MinValue;
                return;
            }

            var activeRunPrefix = $"{_activeRunKey}::";
            var keys = _basesByRunAndId.Keys
                .Where(key => key.StartsWith(activeRunPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keys)
            {
                _basesByRunAndId.Remove(key);
            }

            _inventoryFullTypesByRun.Remove(_activeRunKey);
            _inventoryFullTypes.Clear();
            _lastSnapshotUtc = DateTimeOffset.MinValue;
        }
    }

    public MultiBaseCatalogMatch BuildCatalogMatch(IReadOnlyCollection<GuideItem> catalogItems)
    {
        lock (_sync)
        {
            var activeBases = _basesByRunAndId.Values
                .Where(entry => entry.RunKey.Equals(_activeRunKey, StringComparison.OrdinalIgnoreCase))
                .Select(CloneBaseState)
                .ToList();

            var inventoryTokens = _inventoryFullTypesByRun.TryGetValue(_activeRunKey, out var tokensForRun)
                ? tokensForRun.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return BuildCatalogMatchCore(catalogItems, inventoryTokens, activeBases, _activeRunKey, _lastSnapshotUtc);
        }
    }

    public MultiBaseStateSnapshot GetStateSnapshot()
    {
        lock (_sync)
        {
            var activeBases = _basesByRunAndId.Values
                .Where(entry => entry.RunKey.Equals(_activeRunKey, StringComparison.OrdinalIgnoreCase))
                .Select(CloneBaseState)
                .OrderBy(entry => entry.BaseName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.BaseId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var inventoryItemsCount = _inventoryFullTypesByRun.TryGetValue(_activeRunKey, out var tokensForRun)
                ? tokensForRun.Count
                : 0;
            return new MultiBaseStateSnapshot(
                _activeRunKey,
                _lastSnapshotUtc,
                inventoryItemsCount,
                activeBases);
        }
    }

    public bool TryActivateRunForSession(string savePath, string playerName)
    {
        lock (_sync)
        {
            _ = playerName;
            var runGroups = _basesByRunAndId.Values
                .Where(entry => !string.IsNullOrWhiteSpace(entry.RunKey))
                .GroupBy(entry => entry.RunKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    RunKey = group.Key,
                })
                .ToList();
            if (runGroups.Count == 0)
            {
                return false;
            }

            var mode = ExtractModeFromSavePath(savePath);
            var saveName = ExtractSaveNameFromSavePath(savePath);

            var exactCandidates = new List<string>();
            if (mode.Length > 0 && saveName.Length > 0)
            {
                exactCandidates.Add($"{mode}::{saveName}");
            }

            if (saveName.Length > 0)
            {
                exactCandidates.Add(saveName);
            }

            var exactMatch = runGroups.FirstOrDefault(group =>
                exactCandidates.Any(candidate =>
                    group.RunKey.Equals(candidate, StringComparison.OrdinalIgnoreCase)));
            if (exactMatch is not null)
            {
                ActivateRun(exactMatch.RunKey);
                return true;
            }

            var fallbackRunKey = BuildFallbackRunKey(mode, saveName);
            ActivateRun(fallbackRunKey);
            return false;
        }
    }

    public string BuildDiagnosticsText()
    {
        var snapshot = GetStateSnapshot();
        var lastSeen = snapshot.LastSnapshotUtc == DateTimeOffset.MinValue
            ? "n/a"
            : snapshot.LastSnapshotUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return $"run={snapshot.RunKey}; bases={snapshot.Bases.Count}; inventoryTokens={snapshot.InventoryItemTokenCount}; lastSeen={lastSeen}";
    }

    private static MultiBaseCatalogMatch BuildCatalogMatchCore(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> inventoryTokens,
        IReadOnlyCollection<TrackedBaseState> activeBases,
        string activeRunKey,
        DateTimeOffset lastSnapshotUtc)
    {
        var books = catalogItems.Where(item => item.Type == GuideItemType.Book).ToList();
        var magazines = catalogItems.Where(item => item.Type == GuideItemType.Magazine).ToList();
        var recipes = catalogItems.Where(item => item.Type == GuideItemType.Recipe).ToList();
        var magazinesById = magazines.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        var inventoryBookIds = MatchItemsByTokens(books, inventoryTokens);
        var inventoryMagazineIds = MatchItemsByTokens(magazines, inventoryTokens);

        var baseBookIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseMagazineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseRecipeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseNamesByBookId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var baseNamesByMagazineId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var baseNamesByRecipeId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var trackedBase in activeBases)
        {
            var baseName = string.IsNullOrWhiteSpace(trackedBase.BaseName)
                ? trackedBase.BaseId
                : trackedBase.BaseName;
            var tokens = trackedBase.ItemFullTypes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchedBooks = MatchItemsByTokens(books, tokens);
            var matchedMagazines = MatchItemsByTokens(magazines, tokens);
            var matchedRecipes = MatchRecipesFromMagazines(matchedMagazines, magazinesById, recipes);

            foreach (var bookId in matchedBooks)
            {
                baseBookIds.Add(bookId);
                AddOrigin(baseNamesByBookId, bookId, baseName);
            }

            foreach (var magazineId in matchedMagazines)
            {
                baseMagazineIds.Add(magazineId);
                AddOrigin(baseNamesByMagazineId, magazineId, baseName);
            }

            foreach (var recipeId in matchedRecipes)
            {
                baseRecipeIds.Add(recipeId);
                AddOrigin(baseNamesByRecipeId, recipeId, baseName);
            }
        }

        return new MultiBaseCatalogMatch
        {
            ActiveRunKey = activeRunKey,
            LastSnapshotUtc = lastSnapshotUtc,
            InventoryBookItemIds = inventoryBookIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            InventoryMagazineItemIds = inventoryMagazineIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            BaseBookItemIds = baseBookIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            BaseMagazineItemIds = baseMagazineIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            BaseRecipeItemIds = baseRecipeIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            BaseNamesByBookItemId = baseNamesByBookId.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyCollection<string>)entry.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase),
            BaseNamesByMagazineItemId = baseNamesByMagazineId.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyCollection<string>)entry.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase),
            BaseNamesByRecipeItemId = baseNamesByRecipeId.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyCollection<string>)entry.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase),
        };
    }

    private static HashSet<string> MatchRecipesFromMagazines(
        IReadOnlyCollection<string> matchedMagazineIds,
        IReadOnlyDictionary<string, GuideItem> magazinesById,
        IReadOnlyCollection<GuideItem> recipes)
    {
        if (matchedMagazineIds.Count == 0 || recipes.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var recipeTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var magazineId in matchedMagazineIds)
        {
            if (!magazinesById.TryGetValue(magazineId, out var magazine))
            {
                continue;
            }

            foreach (var recipe in magazine.Recipes)
            {
                AddNormalizedToken(recipeTokens, recipe);
            }
        }

        return recipeTokens.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : MatchItemsByTokens(recipes, recipeTokens);
    }

    private static HashSet<string> MatchItemsByTokens(
        IReadOnlyCollection<GuideItem> items,
        IReadOnlyCollection<string> normalizedTokens)
    {
        var tokenSet = normalizedTokens
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var candidates = BuildCodeCandidates(item);
            if (candidates.Any(tokenSet.Contains))
            {
                matched.Add(item.Id);
            }
        }

        return matched;
    }

    private static IEnumerable<string> BuildCodeCandidates(GuideItem item)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddNormalizedToken(candidates, item.Id);
        AddNormalizedToken(candidates, item.Name);
        AddNormalizedToken(candidates, item.GermanName);

        foreach (var alias in item.Aliases)
        {
            AddNormalizedToken(candidates, alias);
        }

        return candidates;
    }

    private static void AddOrigin(
        IDictionary<string, HashSet<string>> target,
        string itemId,
        string originName)
    {
        if (!target.TryGetValue(itemId, out var values))
        {
            values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            target[itemId] = values;
        }

        values.Add(originName);
    }

    private void ActivateRun(string runKey)
    {
        _activeRunKey = runKey?.Trim() ?? string.Empty;
        LoadActiveRunInventoryTokens();
        _lastSnapshotUtc = ResolveLastSnapshotForRun(_activeRunKey);
    }

    private void LoadActiveRunInventoryTokens()
    {
        _inventoryFullTypes.Clear();
        if (!_inventoryFullTypesByRun.TryGetValue(_activeRunKey, out var tokens))
        {
            return;
        }

        foreach (var token in tokens)
        {
            _inventoryFullTypes.Add(token);
        }
    }

    private DateTimeOffset ResolveLastSnapshotForRun(string runKey)
    {
        if (string.IsNullOrWhiteSpace(runKey))
        {
            return DateTimeOffset.MinValue;
        }

        return _basesByRunAndId.Values
            .Where(entry => entry.RunKey.Equals(runKey, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.LastSeenUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
    }

    private static string ExtractModeFromSavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return string.Empty;
        }

        try
        {
            var parent = Path.GetDirectoryName(savePath);
            return string.IsNullOrWhiteSpace(parent)
                ? string.Empty
                : Path.GetFileName(parent)?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractSaveNameFromSavePath(string savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileName(savePath)?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildFallbackRunKey(string mode, string saveName)
    {
        var trimmedMode = mode?.Trim() ?? string.Empty;
        var trimmedSaveName = saveName?.Trim() ?? string.Empty;
        if (trimmedMode.Length > 0 && trimmedSaveName.Length > 0)
        {
            return $"{trimmedMode}::{trimmedSaveName}";
        }

        if (trimmedSaveName.Length > 0)
        {
            return trimmedSaveName;
        }

        if (trimmedMode.Length > 0)
        {
            return trimmedMode;
        }

        return string.Empty;
    }

    private static string ResolveRunKey(MultiBaseScanPayload payload)
    {
        var runKey = payload.RunKey?.Trim();
        if (!string.IsNullOrWhiteSpace(runKey))
        {
            return runKey;
        }

        var saveId = payload.SaveId?.Trim();
        var playerName = payload.PlayerName?.Trim();
        if (!string.IsNullOrWhiteSpace(saveId) && !string.IsNullOrWhiteSpace(playerName))
        {
            return $"{saveId}::{playerName}";
        }

        if (!string.IsNullOrWhiteSpace(saveId))
        {
            return saveId;
        }

        return "default";
    }

    private static TrackedBaseState NormalizeTrackedBase(TrackedBaseState source)
    {
        var normalizedItemTypes = source.ItemFullTypes ?? [];
        var normalizedStructures = source.StructureTypes ?? [];
        var baseId = source.BaseId?.Trim() ?? string.Empty;
        var baseName = source.BaseName?.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = baseId.Length == 0 ? "Base" : baseId;
        }

        return new TrackedBaseState
        {
            RunKey = source.RunKey?.Trim() ?? string.Empty,
            SaveId = source.SaveId?.Trim() ?? string.Empty,
            PlayerName = source.PlayerName?.Trim() ?? string.Empty,
            BaseId = baseId,
            BaseName = baseName,
            BuildingId = source.BuildingId?.Trim() ?? string.Empty,
            LastSeenUtc = source.LastSeenUtc,
            ItemFullTypes = normalizedItemTypes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StructureTypes = normalizedStructures
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static TrackedBaseState CloneBaseState(TrackedBaseState source)
    {
        return new TrackedBaseState
        {
            RunKey = source.RunKey,
            SaveId = source.SaveId,
            PlayerName = source.PlayerName,
            BaseId = source.BaseId,
            BaseName = source.BaseName,
            BuildingId = source.BuildingId,
            LastSeenUtc = source.LastSeenUtc,
            ItemFullTypes = source.ItemFullTypes.ToList(),
            StructureTypes = source.StructureTypes.ToList(),
        };
    }

    private static string BuildCompositeBaseKey(string runKey, string baseId)
    {
        return $"{runKey}::{baseId}".ToLowerInvariant();
    }

    private static void AddNormalizedToken(ISet<string> target, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = NormalizeToken(value);
        if (normalized.Length == 0)
        {
            return;
        }

        target.Add(normalized);

        var raw = value.Trim().Replace(':', '.');
        var separator = raw.LastIndexOf('.');
        if (separator > 0 && separator < raw.Length - 1)
        {
            var trailing = NormalizeToken(raw[(separator + 1)..]);
            if (trailing.Length > 0)
            {
                target.Add(trailing);
            }
        }
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Replace(':', '.');
        var chars = normalized
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private sealed class MultiBaseScanPayload
    {
        public string? Source { get; init; }

        public string? RunKey { get; init; }

        public string? SaveId { get; init; }

        public string? PlayerName { get; init; }

        public string? BaseId { get; init; }

        public string? BaseName { get; init; }

        public string? BuildingId { get; init; }

        public DateTimeOffset? TimestampUtc { get; init; }

        public IReadOnlyList<MultiBaseItemPayload>? PlayerInventoryItems { get; init; }

        public IReadOnlyList<MultiBaseItemPayload>? BaseItems { get; init; }

        public IReadOnlyList<MultiBaseStructurePayload>? Structures { get; init; }
    }

    private sealed class MultiBaseItemPayload
    {
        public string? FullType { get; init; }

        public int Count { get; init; }

        public string? Container { get; init; }
    }

    private sealed class MultiBaseStructurePayload
    {
        public string? Type { get; init; }

        public int X { get; init; }

        public int Y { get; init; }

        public int Z { get; init; }
    }
}

public sealed record MultiBaseIngestResult(
    bool Success,
    string Message,
    string RunKey = "",
    string BaseId = "",
    string BaseName = "",
    int InventoryItemCount = 0,
    int BaseItemCount = 0,
    int StructureCount = 0,
    DateTimeOffset TimestampUtc = default);

public sealed class MultiBaseCatalogMatch
{
    public string ActiveRunKey { get; init; } = string.Empty;

    public DateTimeOffset LastSnapshotUtc { get; init; } = DateTimeOffset.MinValue;

    public IReadOnlyCollection<string> InventoryBookItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> InventoryMagazineItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> BaseBookItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> BaseMagazineItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyCollection<string> BaseRecipeItemIds { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> BaseNamesByBookItemId { get; init; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> BaseNamesByMagazineItemId { get; init; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> BaseNamesByRecipeItemId { get; init; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
}

public sealed record MultiBaseStateSnapshot(
    string RunKey,
    DateTimeOffset LastSnapshotUtc,
    int InventoryItemTokenCount,
    IReadOnlyCollection<TrackedBaseState> Bases);
