using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class SessionSyncService
{
    private static readonly Regex TokenRegex = new(@"[A-Za-z0-9_:\.\-]{3,}", RegexOptions.Compiled);
    private static readonly Regex ModuleItemCodeRegex = new(@"[A-Za-z0-9_]+\.[A-Za-z0-9_]+", RegexOptions.Compiled);
    private static readonly Regex WorldDictionaryRegistryRegex = new(@"registryID\s*=\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex WorldDictionaryFullTypeRegex = new(@"fulltype\s*=\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex PrintablePhraseRegex = new(@"[A-Za-z][A-Za-z0-9 '\-:]{4,}", RegexOptions.Compiled);
    private static readonly Regex InventoryItemTokenRegex = new(@"^[A-Za-z0-9_]+\.[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex InventoryBookTokenRegex = new(@"(?:^|[.:])Book[A-Za-z]+[0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LevelRegex = new(@"(\d+)", RegexOptions.Compiled);
    private static readonly string[] ContainerTypeHints =
    [
        "bag",
        "backpack",
        "duffel",
        "satchel",
        "purse",
        "wallet",
        "keyring",
        "toolbox",
        "case",
        "crate",
        "box",
        "sack",
        "firstaid",
        "medkit",
    ];

    private static readonly IReadOnlyDictionary<string, string> SkillDisplayMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fitness"] = "Fitness",
            ["Strength"] = "Strength",
            ["Sprinting"] = "Sprinting",
            ["Lightfoot"] = "Lightfooted",
            ["Nimble"] = "Nimble",
            ["Sneak"] = "Sneaking",
            ["Axe"] = "Axe",
            ["Blunt"] = "Long Blunt",
            ["SmallBlunt"] = "Short Blunt",
            ["LongBlade"] = "Long Blade",
            ["SmallBlade"] = "Short Blade",
            ["Spear"] = "Spear",
            ["Maintenance"] = "Maintenance",
            ["Aiming"] = "Aiming",
            ["Reloading"] = "Reloading",
            ["Farming"] = "Farming",
            ["Fishing"] = "Fishing",
            ["Trapping"] = "Trapping",
            ["Foraging"] = "Foraging",
            ["Cooking"] = "Cooking",
            ["Carving"] = "Carving",
            ["FlintKnapping"] = "Flint Knapping",
            ["Glassmaking"] = "Glassmaking",
            ["Masonry"] = "Masonry",
            ["Pottery"] = "Pottery",
            ["Woodwork"] = "Carpentry",
            ["Blacksmith"] = "Blacksmith",
            ["Mechanics"] = "Mechanics",
            ["Tailoring"] = "Tailoring",
            ["Electricity"] = "Electrical",
            ["MetalWelding"] = "Metalworking",
            ["Doctor"] = "First Aid",
        };

    public string? TryResolveActiveSavePathForCurrentSession()
    {
        return TryResolveActiveSavePath();
    }

    public SessionSyncResult SyncFromCurrentSession(IReadOnlyCollection<GuideItem> catalogItems)
    {
        if (catalogItems.Count == 0)
        {
            return new SessionSyncResult
            {
                Success = false,
                Message = "Kein Katalog geladen.",
            };
        }

        var savePath = TryResolveActiveSavePath();
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return new SessionSyncResult
            {
                Success = false,
                Message = "Kein aktiver Save gefunden.",
            };
        }

        var playersDbPath = Path.Combine(savePath, "players.db");
        if (!File.Exists(playersDbPath))
        {
            return new SessionSyncResult
            {
                Success = false,
                SavePath = savePath,
                Message = "players.db im aktiven Save nicht gefunden.",
            };
        }

        var tempDir = string.Empty;
        try
        {
            tempDir = CreatePlayersDbSnapshot(playersDbPath);
            var snapshotPath = Path.Combine(tempDir, "players.db");
            var playerRow = ReadActivePlayerRow(snapshotPath);
            if (playerRow.Data.Length == 0)
            {
                return new SessionSyncResult
                {
                    Success = false,
                    SavePath = savePath,
                    Message = "Keine Spieler-Daten in players.db gefunden.",
                };
            }

            var tokenSet = ExtractTokens(playerRow.Data);
            var normalizedTokenSet = tokenSet
                .Select(Normalize)
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var normalizedPhraseSet = ExtractPrintablePhrases(playerRow.Data);
            var inventoryItemTokens = TryExtractInventoryItemTokensFromStructuredData(playerRow.Data, savePath, out var structuredInventoryTokens)
                ? structuredInventoryTokens
                : ExtractInventoryItemTokensFromText(tokenSet);

            var matchedBooks = MatchInventoryBooks(catalogItems, inventoryItemTokens);
            var matchedMagazines = MatchInventoryMagazines(catalogItems, inventoryItemTokens);
            var skills = ExtractSkillLevels(playerRow.Data);
            var skillLevels = skills.ToDictionary(skill => skill.Name, skill => skill.Level, StringComparer.OrdinalIgnoreCase);
            var readBooks = MatchReadBooksHeuristic(catalogItems, normalizedPhraseSet, matchedBooks);
            var obsoleteBooks = DetermineObsoleteBooks(catalogItems, skillLevels);
            var learnedRecipes = MatchLearnedRecipes(catalogItems, tokenSet, normalizedTokenSet, normalizedPhraseSet);
            var readMagazines = MatchReadMagazinesHeuristic(catalogItems, learnedRecipes, normalizedPhraseSet);

            foreach (var recipe in RecipesFromReadMagazines(catalogItems, readMagazines))
            {
                learnedRecipes.Add(recipe);
            }

            var professionId = MatchProfession(catalogItems, tokenSet, normalizedTokenSet);

            return new SessionSyncResult
            {
                Success = true,
                SavePath = savePath,
                PlayerName = string.IsNullOrWhiteSpace(playerRow.Name) ? "Unbekannt" : playerRow.Name,
                ProfessionItemId = professionId,
                CheckedBookItemIds = matchedBooks.ToArray(),
                ReadBookItemIds = readBooks.ToArray(),
                ObsoleteBookItemIds = obsoleteBooks.ToArray(),
                CheckedMagazineItemIds = matchedMagazines.ToArray(),
                ReadMagazineItemIds = readMagazines.ToArray(),
                LearnedRecipeItemIds = learnedRecipes.ToArray(),
                SkillLevels = skills.ToArray(),
                Message =
                    $"Session geladen: Buecher inv/gelesen/obsolete {matchedBooks.Count}/{readBooks.Count}/{obsoleteBooks.Count}, " +
                    $"Magazine inv/gelesen {matchedMagazines.Count}/{readMagazines.Count}, Rezepte gelernt {learnedRecipes.Count}",
            };
        }
        catch (Exception exception)
        {
            return new SessionSyncResult
            {
                Success = false,
                SavePath = savePath,
                Message = $"Session-Sync fehlgeschlagen: {exception.Message}",
            };
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string? TryResolveActiveSavePath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return null;
        }

        var zomboidPath = Path.Combine(userProfile, "Zomboid");
        var savesRoot = Path.Combine(zomboidPath, "Saves");
        if (!Directory.Exists(savesRoot))
        {
            return null;
        }

        var latestSavePath = Path.Combine(zomboidPath, "latestSave.ini");
        if (File.Exists(latestSavePath))
        {
            var lines = File.ReadAllLines(latestSavePath, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            if (lines.Length >= 2)
            {
                var saveId = lines[0];
                var mode = lines[1];
                var worldName = lines.Length >= 3 ? lines[2] : string.Empty;

                var candidates = new List<string>
                {
                    Path.Combine(savesRoot, mode, saveId),
                };

                if (!string.IsNullOrWhiteSpace(worldName))
                {
                    candidates.Add(Path.Combine(savesRoot, mode, worldName));
                }

                if (string.Equals(mode, "Multiplayer", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(Path.Combine(savesRoot, "Multiplayer", saveId));
                    if (!string.IsNullOrWhiteSpace(worldName))
                    {
                        candidates.Add(Path.Combine(savesRoot, "Multiplayer", worldName));
                    }
                }

                var existing = candidates.FirstOrDefault(Directory.Exists);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    return existing;
                }
            }
        }

        var newestSave = Directory.EnumerateFiles(savesRoot, "players.db", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return newestSave?.DirectoryName;
    }

    private static string CreatePlayersDbSnapshot(string playersDbPath)
    {
        var sourceDirectory = Path.GetDirectoryName(playersDbPath) ?? throw new InvalidOperationException("Ungueltiger Save-Pfad.");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "ZomboidGuide", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        CopyIfExists(playersDbPath, Path.Combine(tempDirectory, "players.db"));
        CopyIfExists(playersDbPath + "-journal", Path.Combine(tempDirectory, "players.db-journal"));
        CopyIfExists(playersDbPath + "-wal", Path.Combine(tempDirectory, "players.db-wal"));
        CopyIfExists(playersDbPath + "-shm", Path.Combine(tempDirectory, "players.db-shm"));
        CopyIfExists(Path.Combine(sourceDirectory, "players.db-journal"), Path.Combine(tempDirectory, "players.db-journal"));

        return tempDirectory;
    }

    private static void CopyIfExists(string source, string target)
    {
        if (File.Exists(source))
        {
            File.Copy(source, target, overwrite: true);
        }
    }

    private static PlayerRow ReadActivePlayerRow(string playersDbPath)
    {
        using var connection = new SqliteConnection($"Data Source={playersDbPath};Mode=ReadOnly;");
        connection.Open();

        var candidates = new List<PlayerRow>();
        candidates.AddRange(ReadRows(connection, "localPlayers", "name"));
        candidates.AddRange(ReadRows(connection, "networkPlayers", "COALESCE(name, username)"));

        if (candidates.Count == 0)
        {
            return new PlayerRow();
        }

        var alive = candidates.Where(row => !row.IsDead).ToList();
        return alive.Count > 0
            ? alive
                .OrderByDescending(row => row.WorldVersion)
                .ThenByDescending(row => row.Data.Length)
                .ThenByDescending(row => row.Id)
                .First()
            : candidates
                .OrderByDescending(row => row.WorldVersion)
                .ThenByDescending(row => row.Data.Length)
                .ThenByDescending(row => row.Id)
                .First();
    }

    private static IEnumerable<PlayerRow> ReadRows(SqliteConnection connection, string table, string nameExpression)
    {
        var rows = new List<PlayerRow>();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT id, {nameExpression} AS playerName, data, isDead, worldversion FROM {table} ORDER BY id DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var data = reader.IsDBNull(2) ? Array.Empty<byte>() : (byte[])reader["data"];
                var isDead = !reader.IsDBNull(3) && Convert.ToInt32(reader["isDead"]) != 0;
                var worldVersion = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

                rows.Add(new PlayerRow
                {
                    Id = id,
                    Name = name,
                    Data = data,
                    IsDead = isDead,
                    WorldVersion = worldVersion,
                });
            }
        }
        catch
        {
            return rows;
        }

        return rows;
    }

    private static HashSet<string> MatchInventoryBooks(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> inventoryItemTokens)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in catalogItems.Where(item => item.Type == GuideItemType.Book))
        {
            if (BuildItemCodeCandidates(item).Any(candidate => inventoryItemTokens.Contains(Normalize(candidate))))
            {
                matched.Add(item.Id);
            }
        }

        return matched;
    }

    private static bool TryExtractInventoryItemTokensFromStructuredData(
        byte[] playerData,
        string savePath,
        out HashSet<string> inventoryItemTokens)
    {
        inventoryItemTokens = [];
        if (!TryLoadWorldDictionaryMap(savePath, out var registryToTypeMap))
        {
            return false;
        }

        var validRegistryIds = registryToTypeMap.Keys.ToHashSet();
        if (validRegistryIds.Count == 0)
        {
            return false;
        }

        if (!TryFindBestInventoryContainer(playerData, validRegistryIds, registryToTypeMap, out var bestItems))
        {
            return false;
        }

        foreach (var item in bestItems)
        {
            AddBookToken(inventoryItemTokens, item.FullType);
            AddBookToken(inventoryItemTokens, ExtractTrailingToken(item.FullType));
            if (IsLikelyContainerItem(item.FullType))
            {
                CollectNestedInventoryItemTokens(playerData, item, validRegistryIds, registryToTypeMap, inventoryItemTokens, depth: 1);
            }
        }

        return inventoryItemTokens.Count > 0;
    }

    private static bool TryFindBestInventoryContainer(
        byte[] playerData,
        IReadOnlyCollection<int> validRegistryIds,
        IReadOnlyDictionary<int, string> registryToTypeMap,
        out List<SerializedItemEntry> items)
    {
        items = [];
        var candidates = new List<List<SerializedItemEntry>>();

        for (var start = 0; start <= playerData.Length - 2; start++)
        {
            if (!TryParseSerializedContainer(playerData, start, playerData.Length, validRegistryIds, registryToTypeMap, out var _, out var parsedItems))
            {
                continue;
            }

            if (parsedItems.Count < 4)
            {
                continue;
            }

            candidates.Add(parsedItems);
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        items = candidates
            .OrderByDescending(candidate => candidate.Count)
            .First();

        return true;
    }

    private static bool TryParseSerializedContainer(
        byte[] data,
        int start,
        int endLimit,
        IReadOnlyCollection<int> validRegistryIds,
        IReadOnlyDictionary<int, string> registryToTypeMap,
        out int endPosition,
        out List<SerializedItemEntry> items)
    {
        items = [];
        endPosition = start;
        if (start + 2 > endLimit)
        {
            return false;
        }

        var position = start;
        var count = ReadUInt16BigEndian(data, position);
        position += 2;

        if (count > 3000)
        {
            return false;
        }

        var unknownCount = 0;
        for (var index = 0; index < count; index++)
        {
            if (position + 4 > endLimit)
            {
                return false;
            }

            var identical = ReadInt32BigEndian(data, position);
            position += 4;
            if (identical is < 1 or > 5000)
            {
                return false;
            }

            if (position + 4 > endLimit)
            {
                return false;
            }

            var dataLength = ReadInt32BigEndian(data, position);
            position += 4;
            if (dataLength <= 0 || position + dataLength > endLimit || dataLength < 3)
            {
                return false;
            }

            var itemDataStart = position;
            var itemDataEnd = position + dataLength;

            var registryId = ReadUInt16BigEndian(data, position);
            position += 2;
            position += 1; // saveType marker

            if (!validRegistryIds.Contains(registryId))
            {
                unknownCount++;
                if (unknownCount > Math.Max(3, count / 10))
                {
                    return false;
                }
            }

            var fullType = registryToTypeMap.TryGetValue(registryId, out var mapped)
                ? mapped
                : $"#{registryId}";

            items.Add(new SerializedItemEntry
            {
                FullType = fullType,
                DataStart = itemDataStart,
                DataEnd = itemDataEnd,
            });

            position = itemDataEnd;

            var idListBytes = (identical - 1) * 4;
            if (position + idListBytes > endLimit)
            {
                return false;
            }

            position += idListBytes;
        }

        endPosition = position;
        return true;
    }

    private static void CollectNestedInventoryItemTokens(
        byte[] data,
        SerializedItemEntry entry,
        IReadOnlyCollection<int> validRegistryIds,
        IReadOnlyDictionary<int, string> registryToTypeMap,
        ISet<string> inventoryItemTokens,
        int depth)
    {
        if (depth > 6)
        {
            return;
        }

        var searchStart = entry.DataStart + 3;
        var searchEnd = entry.DataEnd;
        for (var nestedStart = searchStart + 8; nestedStart <= searchEnd - 2; nestedStart++)
        {
            var weightReduction = ReadInt32BigEndian(data, nestedStart - 4);
            if (weightReduction is < 0 or > 100)
            {
                continue;
            }

            if (!TryParseSerializedContainer(
                    data,
                    nestedStart,
                    searchEnd,
                    validRegistryIds,
                    registryToTypeMap,
                    out var nestedEnd,
                    out var nestedItems))
            {
                continue;
            }

            if (nestedEnd != searchEnd)
            {
                continue;
            }

            foreach (var nested in nestedItems)
            {
                AddBookToken(inventoryItemTokens, nested.FullType);
                AddBookToken(inventoryItemTokens, ExtractTrailingToken(nested.FullType));
                if (IsLikelyContainerItem(nested.FullType))
                {
                    CollectNestedInventoryItemTokens(data, nested, validRegistryIds, registryToTypeMap, inventoryItemTokens, depth + 1);
                }
            }

            return;
        }
    }

    private static bool IsLikelyContainerItem(string fullType)
    {
        if (string.IsNullOrWhiteSpace(fullType))
        {
            return false;
        }

        var token = ExtractTrailingToken(fullType).ToLowerInvariant();
        if (token.Length == 0)
        {
            return false;
        }

        foreach (var hint in ContainerTypeHints)
        {
            if (token.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryLoadWorldDictionaryMap(string savePath, out Dictionary<int, string> registryToTypeMap)
    {
        registryToTypeMap = new Dictionary<int, string>();
        var dictionaryPath = Path.Combine(savePath, "WorldDictionaryReadable.lua");
        if (!File.Exists(dictionaryPath))
        {
            return false;
        }

        int? currentRegistryId = null;
        foreach (var line in File.ReadLines(dictionaryPath, Encoding.UTF8))
        {
            var registryMatch = WorldDictionaryRegistryRegex.Match(line);
            if (registryMatch.Success &&
                int.TryParse(registryMatch.Groups[1].Value, out var parsedRegistryId) &&
                parsedRegistryId > 0)
            {
                currentRegistryId = parsedRegistryId;
                continue;
            }

            if (!currentRegistryId.HasValue)
            {
                continue;
            }

            var typeMatch = WorldDictionaryFullTypeRegex.Match(line);
            if (!typeMatch.Success)
            {
                continue;
            }

            var fullType = typeMatch.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(fullType))
            {
                continue;
            }

            registryToTypeMap[currentRegistryId.Value] = fullType;
            currentRegistryId = null;
        }

        return registryToTypeMap.Count > 0;
    }

    private static HashSet<string> ExtractInventoryItemTokensFromText(IReadOnlyCollection<string> tokenSet)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokenSet)
        {
            if (!InventoryItemTokenRegex.IsMatch(token))
            {
                continue;
            }

            AddBookToken(result, token);
            AddBookToken(result, ExtractTrailingToken(token));
        }

        return result;
    }

    private static HashSet<string> MatchInventoryMagazines(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> inventoryItemTokens)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in catalogItems.Where(item => item.Type == GuideItemType.Magazine))
        {
            if (BuildItemCodeCandidates(item).Any(candidate => inventoryItemTokens.Contains(Normalize(candidate))))
            {
                matched.Add(item.Id);
            }
        }

        return matched;
    }

    private static HashSet<string> MatchLearnedRecipes(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> tokenSet,
        IReadOnlyCollection<string> normalizedTokenSet,
        IReadOnlyCollection<string> normalizedPhrases)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipe in catalogItems.Where(item => item.Type == GuideItemType.Recipe))
        {
            var aliases = recipe.Aliases.Count == 0 ? [recipe.Name] : recipe.Aliases;
            if (aliases.Any(alias => AliasMatches(alias, tokenSet, normalizedTokenSet)))
            {
                matched.Add(recipe.Id);
                continue;
            }

            var normalizedAliases = aliases
                .Select(NormalizePhrase)
                .Where(alias => alias.Length > 0)
                .ToList();

            if (normalizedAliases.Any(alias => normalizedPhrases.Contains(alias)))
            {
                matched.Add(recipe.Id);
            }
        }

        return matched;
    }

    private static HashSet<string> MatchReadMagazinesHeuristic(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> learnedRecipeIds,
        IReadOnlyCollection<string> normalizedPhrases)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var learnedRecipeNameSet = catalogItems
            .Where(item => item.Type == GuideItemType.Recipe && learnedRecipeIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            .Select(item => Normalize(item.Name))
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var magazine in catalogItems.Where(item => item.Type == GuideItemType.Magazine))
        {
            var readFromRecipes = magazine.Recipes
                .Select(Normalize)
                .Any(learnedRecipeNameSet.Contains);

            var nameCandidates = new[]
            {
                NormalizePhrase(magazine.Name),
                NormalizePhrase(magazine.GermanName),
            }.Where(candidate => candidate.Length > 0);

            var readFromPhrase = nameCandidates.Any(normalizedPhrases.Contains);

            if (readFromRecipes || readFromPhrase)
            {
                result.Add(magazine.Id);
            }
        }

        return result;
    }

    private static HashSet<string> RecipesFromReadMagazines(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> readMagazineIds)
    {
        var recipeNames = catalogItems
            .Where(item => item.Type == GuideItemType.Magazine && readMagazineIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            .SelectMany(item => item.Recipes)
            .Select(Normalize)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return catalogItems
            .Where(item => item.Type == GuideItemType.Recipe && recipeNames.Contains(Normalize(item.Name)))
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> MatchReadBooksHeuristic(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> normalizedPhrases,
        IReadOnlyCollection<string> inventoryBookIds)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in catalogItems.Where(item => item.Type == GuideItemType.Book))
        {
            var candidates = BuildReadableBookNameCandidates(item);
            if (!candidates.Any(candidate => normalizedPhrases.Contains(candidate)))
            {
                continue;
            }

            if (inventoryBookIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            matched.Add(item.Id);
        }

        return matched;
    }

    private static HashSet<string> MatchReadBooksByCode(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> readLiteratureItemTokens)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalogItems.Where(item => item.Type == GuideItemType.Book))
        {
            if (BuildItemCodeCandidates(item).Any(candidate => readLiteratureItemTokens.Contains(Normalize(candidate))))
            {
                matched.Add(item.Id);
            }
        }

        return matched;
    }

    private static HashSet<string> MatchReadMagazinesByCode(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> readLiteratureItemTokens)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalogItems.Where(item => item.Type == GuideItemType.Magazine))
        {
            if (BuildItemCodeCandidates(item).Any(candidate => readLiteratureItemTokens.Contains(Normalize(candidate))))
            {
                matched.Add(item.Id);
            }
        }

        return matched;
    }

    private static List<ItemCodeOccurrence> ExtractModuleItemCodeOccurrences(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        return ModuleItemCodeRegex.Matches(text)
            .Select(match => new ItemCodeOccurrence
            {
                ItemCode = match.Value.Trim(),
                Position = match.Index,
            })
            .Where(occurrence => InventoryItemTokenRegex.IsMatch(occurrence.ItemCode))
            .ToList();
    }

    private static HashSet<int> ExtractReadLiteratureOccurrencePositions(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<ItemCodeOccurrence> occurrences)
    {
        var knownLiteratureCodes = BuildKnownLiteratureCodeLookup(catalogItems);
        if (knownLiteratureCodes.Count == 0 || occurrences.Count == 0)
        {
            return [];
        }

        var literatureOccurrences = occurrences
            .Where(occurrence => knownLiteratureCodes.Contains(Normalize(occurrence.ItemCode)))
            .OrderBy(occurrence => occurrence.Position)
            .ToList();

        if (literatureOccurrences.Count == 0)
        {
            return [];
        }

        var result = new HashSet<int>();
        const int maxGapForReadCluster = 64;
        var currentCluster = new List<ItemCodeOccurrence>();

        foreach (var occurrence in literatureOccurrences)
        {
            if (currentCluster.Count == 0)
            {
                currentCluster.Add(occurrence);
                continue;
            }

            var previous = currentCluster[^1];
            if (occurrence.Position - previous.Position <= maxGapForReadCluster)
            {
                currentCluster.Add(occurrence);
                continue;
            }

            if (currentCluster.Count >= 2)
            {
                foreach (var member in currentCluster)
                {
                    result.Add(member.Position);
                }
            }

            currentCluster.Clear();
            currentCluster.Add(occurrence);
        }

        if (currentCluster.Count >= 2)
        {
            foreach (var member in currentCluster)
            {
                result.Add(member.Position);
            }
        }

        return result;
    }

    private static HashSet<string> ExtractReadLiteratureTokenSet(
        IReadOnlyCollection<ItemCodeOccurrence> occurrences,
        IReadOnlyCollection<int> readLiteraturePositions)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var occurrence in occurrences)
        {
            if (!readLiteraturePositions.Contains(occurrence.Position))
            {
                continue;
            }

            AddBookToken(result, occurrence.ItemCode);
            AddBookToken(result, ExtractTrailingToken(occurrence.ItemCode));
        }

        return result;
    }

    private static HashSet<string> ExtractInventoryItemTokenSet(
        IReadOnlyCollection<ItemCodeOccurrence> occurrences,
        IReadOnlyCollection<int> readLiteraturePositions)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var occurrence in occurrences)
        {
            if (readLiteraturePositions.Contains(occurrence.Position))
            {
                continue;
            }

            AddBookToken(result, occurrence.ItemCode);
            AddBookToken(result, ExtractTrailingToken(occurrence.ItemCode));
        }

        return result;
    }

    private static HashSet<string> BuildKnownLiteratureCodeLookup(IReadOnlyCollection<GuideItem> catalogItems)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalogItems.Where(item => item.Type is GuideItemType.Book or GuideItemType.Magazine))
        {
            foreach (var code in BuildItemCodeCandidates(item))
            {
                AddBookToken(result, code);
            }
        }

        return result;
    }

    private static HashSet<string> BuildItemCodeCandidates(GuideItem item)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddItemCodeCandidate(result, item.Id);
        foreach (var alias in item.Aliases)
        {
            AddItemCodeCandidate(result, alias);
        }

        if (item.Type == GuideItemType.Book)
        {
            AddGeneratedBookItemCodeCandidates(result, item);
        }

        return result;
    }

    private static void AddItemCodeCandidate(ISet<string> target, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var value = raw.Trim();
        if (InventoryItemTokenRegex.IsMatch(value))
        {
            target.Add(value);
            return;
        }

        if (Regex.IsMatch(value, @"^Book[A-Za-z0-9_]+[0-9]+$", RegexOptions.IgnoreCase))
        {
            target.Add($"Base.{value}");
            return;
        }

        if (Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9_]*Mag(?:azine)?[A-Za-z0-9_]*$", RegexOptions.IgnoreCase))
        {
            target.Add($"Base.{value}");
        }
    }

    private static void AddGeneratedBookItemCodeCandidates(ISet<string> target, GuideItem item)
    {
        var level = item.Level > 0 ? item.Level : ResolveBookLevel(item);
        if (level <= 0)
        {
            return;
        }

        var skillSeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            skillSeeds.Add(item.Category);
        }

        foreach (var seed in skillSeeds)
        {
            var token = seed.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            target.Add($"Base.Book{token}{level}");

            if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                target.Add($"Base.Book{token[..^1]}{level}");
            }
            else
            {
                target.Add($"Base.Book{token}s{level}");
            }

            if (token.Equals("Electrical", StringComparison.OrdinalIgnoreCase))
            {
                target.Add($"Base.BookElectrician{level}");
                target.Add($"Base.BookElectricity{level}");
            }
        }
    }

    private static HashSet<string> DetermineObsoleteBooks(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyDictionary<string, int> skillLevels)
    {
        var obsolete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalogItems.Where(item => item.Type == GuideItemType.Book))
        {
            var level = item.Level > 0 ? item.Level : ResolveBookLevel(item);
            if (level <= 0)
            {
                continue;
            }

            var skillName = MapCategoryToSkillName(item.Category);
            if (string.IsNullOrWhiteSpace(skillName))
            {
                continue;
            }

            if (!skillLevels.TryGetValue(skillName, out var playerLevel))
            {
                continue;
            }

            var tierMaxLevel = level * 2;
            if (playerLevel >= tierMaxLevel)
            {
                obsolete.Add(item.Id);
            }
        }

        return obsolete;
    }

    private static HashSet<string> ExtractInventoryBookTokenSet(IReadOnlyCollection<string> tokenSet)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokenSet)
        {
            if (!InventoryBookTokenRegex.IsMatch(token))
            {
                continue;
            }

            AddBookToken(result, token);
            AddBookToken(result, ExtractTrailingToken(token));
        }

        return result;
    }

    private static HashSet<string> ExtractInventoryItemTokenSet(IReadOnlyCollection<string> tokenSet)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokenSet)
        {
            if (!InventoryItemTokenRegex.IsMatch(token))
            {
                continue;
            }

            AddBookToken(result, token);
            AddBookToken(result, ExtractTrailingToken(token));
        }

        return result;
    }

    private static HashSet<string> BuildBookCandidates(GuideItem item)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = item.Aliases.Count == 0 ? [item.Name] : item.Aliases;
        foreach (var alias in aliases)
        {
            AddBookToken(result, alias);
            AddBookToken(result, ExtractTrailingToken(alias));
            AddMechanicPluralVariants(result, alias);
        }

        AddGeneratedCandidatesFromCategoryAndLevel(item, result);
        return result;
    }

    private static HashSet<string> BuildGeneralItemCandidates(GuideItem item)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliases = item.Aliases.Count == 0 ? [item.Name] : item.Aliases;
        foreach (var alias in aliases)
        {
            AddBookToken(result, alias);
            AddBookToken(result, ExtractTrailingToken(alias));
        }

        AddBookToken(result, item.Name);
        AddBookToken(result, item.GermanName);

        return result;
    }

    private static void AddGeneratedCandidatesFromCategoryAndLevel(GuideItem item, HashSet<string> target)
    {
        var level = item.Level > 0 ? item.Level : ResolveBookLevel(item);
        if (level <= 0)
        {
            return;
        }

        var skillSeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            skillSeeds.Add(item.Category);
        }

        if (item.Name.StartsWith("Beginners ", StringComparison.OrdinalIgnoreCase) ||
            item.Name.StartsWith("Intermediate ", StringComparison.OrdinalIgnoreCase) ||
            item.Name.StartsWith("Advanced ", StringComparison.OrdinalIgnoreCase) ||
            item.Name.StartsWith("Expert ", StringComparison.OrdinalIgnoreCase) ||
            item.Name.StartsWith("Master ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = item.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                skillSeeds.Add(parts[^1]);
            }
        }

        foreach (var seed in skillSeeds)
        {
            var token = seed.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            AddBookToken(target, $"Book{token}{level}");
            AddBookToken(target, $"Base.Book{token}{level}");

            if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                AddBookToken(target, $"Book{token[..^1]}{level}");
                AddBookToken(target, $"Base.Book{token[..^1]}{level}");
            }
            else
            {
                AddBookToken(target, $"Book{token}s{level}");
                AddBookToken(target, $"Base.Book{token}s{level}");
            }

            if (token.Equals("Electrical", StringComparison.OrdinalIgnoreCase))
            {
                AddBookToken(target, $"BookElectricity{level}");
                AddBookToken(target, $"Base.BookElectricity{level}");
            }

            if (token.Equals("FirstAid", StringComparison.OrdinalIgnoreCase))
            {
                AddBookToken(target, $"BookFirstAid{level}");
                AddBookToken(target, $"Base.BookFirstAid{level}");
            }
        }
    }

    private static void AddMechanicPluralVariants(HashSet<string> target, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        if (alias.Contains("Mechanics", StringComparison.OrdinalIgnoreCase))
        {
            AddBookToken(target, alias.Replace("Mechanics", "Mechanic", StringComparison.OrdinalIgnoreCase));
        }
        else if (alias.Contains("Mechanic", StringComparison.OrdinalIgnoreCase))
        {
            AddBookToken(target, alias.Replace("Mechanic", "Mechanics", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string ExtractTrailingToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var separatorIndex = Math.Max(value.LastIndexOf('.'), value.LastIndexOf(':'));
        return separatorIndex >= 0 && separatorIndex < value.Length - 1
            ? value[(separatorIndex + 1)..]
            : value;
    }

    private static void AddBookToken(ISet<string> target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = Normalize(value);
        if (normalized.Length > 0)
        {
            target.Add(normalized);
        }
    }

    private static int ResolveBookLevel(GuideItem item)
    {
        if (item.Level > 0)
        {
            return item.Level;
        }

        var levelMatch = LevelRegex.Match(item.Detail);
        if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var detailLevel) && detailLevel > 0)
        {
            return detailLevel;
        }

        var aliasMatch = item.Aliases
            .Select(alias => Regex.Match(alias, @"(\d+)$"))
            .FirstOrDefault(match => match.Success);

        if (aliasMatch is { Success: true } && int.TryParse(aliasMatch.Groups[1].Value, out var aliasLevel) && aliasLevel > 0)
        {
            return aliasLevel;
        }

        return 0;
    }

    private static string MapCategoryToSkillName(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return string.Empty;
        }

        var normalized = Normalize(category);
        return normalized switch
        {
            "woodwork" => "Carpentry",
            "carpentry" => "Carpentry",
            "cooking" => "Cooking",
            "electricity" => "Electrical",
            "electrical" => "Electrical",
            "electrician" => "Electrical",
            "doctor" => "First Aid",
            "firstaid" => "First Aid",
            "fishing" => "Fishing",
            "carving" => "Carving",
            "flintknapping" => "Flint Knapping",
            "knapping" => "Flint Knapping",
            "glassmaking" => "Glassmaking",
            "foraging" => "Foraging",
            "masonry" => "Masonry",
            "mechanics" => "Mechanics",
            "mechanic" => "Mechanics",
            "metalwelding" => "Metalworking",
            "metalworking" => "Metalworking",
            "metalwork" => "Metalworking",
            "blacksmith" => "Blacksmith",
            "blacksmithing" => "Blacksmith",
            "smithing" => "Blacksmith",
            "pottery" => "Pottery",
            "tailoring" => "Tailoring",
            "trapping" => "Trapping",
            _ => category,
        };
    }

    private static HashSet<string> ExtractPrintablePhrases(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data).Replace('\0', ' ');
        return PrintablePhraseRegex.Matches(text)
            .Select(match => NormalizePhrase(match.Value))
            .Where(value => value.Length >= 5)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> BuildReadableBookNameCandidates(GuideItem item)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizePhrase(item.Name),
        };

        if (!string.IsNullOrWhiteSpace(item.GermanName))
        {
            result.Add(NormalizePhrase(item.GermanName));
        }

        var level = item.Level > 0 ? item.Level : ResolveBookLevel(item);
        var skill = MapCategoryToReadableSkill(item.Category);
        if (!string.IsNullOrWhiteSpace(skill) && level is >= 1 and <= 5)
        {
            var tier = level switch
            {
                1 => "Basic",
                2 => "Intermediate",
                3 => "Advanced",
                4 => "Expert",
                _ => "Master",
            };

            result.Add(NormalizePhrase($"{tier} {skill}"));
            if (skill.Equals("Metalworking", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(NormalizePhrase($"{tier} Welding"));
                result.Add(NormalizePhrase($"{tier} Metalwork"));
            }

            if (skill.Equals("Electrical", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(NormalizePhrase($"{tier} Electrician"));
            }
        }

        return result;
    }

    private static string MapCategoryToReadableSkill(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return string.Empty;
        }

        var normalized = Normalize(category);
        return normalized switch
        {
            "woodwork" => "Carpentry",
            "carpentry" => "Carpentry",
            "cooking" => "Cooking",
            "electricity" => "Electrical",
            "electrical" => "Electrical",
            "electrician" => "Electrical",
            "doctor" => "First Aid",
            "firstaid" => "First Aid",
            "fishing" => "Fishing",
            "carving" => "Carving",
            "flintknapping" => "Flint Knapping",
            "knapping" => "Flint Knapping",
            "glassmaking" => "Glassmaking",
            "foraging" => "Foraging",
            "masonry" => "Masonry",
            "mechanics" => "Mechanics",
            "mechanic" => "Mechanics",
            "metalwelding" => "Metalworking",
            "metalworking" => "Metalworking",
            "metalwork" => "Metalworking",
            "blacksmith" => "Blacksmith",
            "blacksmithing" => "Blacksmith",
            "smithing" => "Blacksmith",
            "pottery" => "Pottery",
            "tailoring" => "Tailoring",
            "trapping" => "Trapping",
            _ => category,
        };
    }

    private static string NormalizePhrase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(value, @"[^A-Za-z0-9 ]+", " ");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.ToLowerInvariant();
    }

    private static string? MatchProfession(
        IReadOnlyCollection<GuideItem> catalogItems,
        IReadOnlyCollection<string> tokenSet,
        IReadOnlyCollection<string> normalizedTokenSet)
    {
        foreach (var profession in catalogItems.Where(item => item.Type == GuideItemType.Profession))
        {
            var aliases = profession.Aliases.Count == 0 ? [profession.Name] : profession.Aliases;
            if (aliases.Any(alias => AliasMatches(alias, tokenSet, normalizedTokenSet)))
            {
                return profession.Id;
            }
        }

        return null;
    }

    private static bool AliasMatches(
        string alias,
        IReadOnlyCollection<string> tokenSet,
        IReadOnlyCollection<string> normalizedTokenSet)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        var trimmedAlias = alias.Trim();
        if (tokenSet.Contains(trimmedAlias, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedAlias = Normalize(trimmedAlias);
        return normalizedAlias.Length > 0 && normalizedTokenSet.Contains(normalizedAlias);
    }

    private static IReadOnlyList<SessionSkillLevel> ExtractSkillLevels(byte[] data)
    {
        var skillLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in SkillDisplayMap)
        {
            var rawSkillName = pair.Key;
            var displayName = pair.Value;
            var skillBytes = Encoding.ASCII.GetBytes(rawSkillName);

            var start = 0;
            while (start < data.Length)
            {
                var index = IndexOf(data, skillBytes, start);
                if (index < 0)
                {
                    break;
                }

                start = index + 1;
                if (index < 2 || index + skillBytes.Length + 4 > data.Length)
                {
                    continue;
                }

                if (data[index - 2] != 0 || data[index - 1] != skillBytes.Length)
                {
                    continue;
                }

                var level = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(index + skillBytes.Length, 4));
                if (level is < 0 or > 20)
                {
                    continue;
                }

                if (!skillLevels.TryGetValue(displayName, out var existingLevel) || level > existingLevel)
                {
                    skillLevels[displayName] = level;
                }
            }
        }

        return skillLevels
            .Where(pair => pair.Value > 0)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new SessionSkillLevel
            {
                Name = pair.Key,
                Level = pair.Value,
            })
            .ToList();
    }

    private static int IndexOf(byte[] source, byte[] value, int startIndex)
    {
        for (var i = startIndex; i <= source.Length - value.Length; i++)
        {
            var found = true;
            for (var j = 0; j < value.Length; j++)
            {
                if (source[i + j] != value[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return i;
            }
        }

        return -1;
    }

    private static HashSet<string> ExtractTokens(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        var tokens = TokenRegex.Matches(text)
            .Select(match => match.Value.Trim())
            .Where(value => value.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var additional = tokens
            .Where(token => token.Contains(':', StringComparison.Ordinal) || token.Contains('.', StringComparison.Ordinal))
            .SelectMany(token => token.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .SelectMany(token => token.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(part => part.Length >= 3)
            .ToList();

        foreach (var extra in additional)
        {
            tokens.Add(extra);
        }

        return tokens;
    }

    private static int ReadUInt16BigEndian(byte[] data, int position)
    {
        return (data[position] << 8) | data[position + 1];
    }

    private static int ReadInt32BigEndian(byte[] data, int position)
    {
        return (data[position] << 24) |
               (data[position + 1] << 16) |
               (data[position + 2] << 8) |
               data[position + 3];
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(chars);
    }

    private static void TryDeleteDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private sealed class ItemCodeOccurrence
    {
        public string ItemCode { get; init; } = string.Empty;

        public int Position { get; init; }
    }

    private sealed class SerializedItemEntry
    {
        public string FullType { get; init; } = string.Empty;

        public int DataStart { get; init; }

        public int DataEnd { get; init; }
    }

    private sealed class PlayerRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public byte[] Data { get; init; } = Array.Empty<byte>();

        public bool IsDead { get; init; }

        public int WorldVersion { get; init; }
    }
}
