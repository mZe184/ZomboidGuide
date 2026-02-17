using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class ZomboidDataParser
{
    private static readonly Regex ModuleRegex = new(@"^\s*module\s+([A-Za-z0-9_.]+)\s*\{", RegexOptions.Compiled);
    private static readonly Regex ItemRegex = new(@"^\s*item\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex GetTextRegex = new(@"getText\(""([^""]+)""\)", RegexOptions.Compiled);
    private static readonly Regex ProfessionRegex = new(
        @"ProfessionFactory\.addProfession\(\s*""(?<id>[^""]+)""\s*,\s*(?<name>[^,]+),",
        RegexOptions.Compiled);
    private static readonly Regex SteamLibraryPathRegex = new(
        @"""path""\s*""(?<path>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public GuideCatalog Parse(string gamePath, bool includeMods, string? selectedLanguageCode = null)
    {
        var roots = DiscoverGameRoots(gamePath, includeMods);
        if (roots.Count == 0)
        {
            return new GuideCatalog();
        }

        var normalizedLanguageCode = NormalizeLanguageCode(selectedLanguageCode);
        var translationsEn = LoadTranslations(roots, "EN");
        var selectedTranslations = normalizedLanguageCode.Equals("EN", StringComparison.OrdinalIgnoreCase)
            ? translationsEn
            : LoadTranslations(roots, normalizedLanguageCode);
        var translationsDe = LoadTranslations(roots, "DE");
        var professions = ParseProfessions(roots, translationsEn, gamePath);
        var literature = ParseLiterature(roots, translationsEn, selectedTranslations);
        var recipes = BuildRecipeItems(literature, translationsDe, translationsEn);

        var allItems = new List<GuideItem>();
        allItems.AddRange(professions);
        allItems.AddRange(literature);
        allItems.AddRange(recipes);

        return new GuideCatalog
        {
            Items = allItems
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(item => item.Type)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            LoadedFromGameFiles = allItems.Count > 0,
            SourcesScanned = roots,
            CreatedAt = DateTimeOffset.Now,
        };
    }

    public string? TryAutoDetectGamePath()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            AddSteamInstallCandidates(candidates, Path.Combine(programFilesX86, "Steam"));
        }

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            AddSteamInstallCandidates(candidates, Path.Combine(programFiles, "Steam"));
        }

        foreach (var steamRoot in GetSteamRootsFromRegistry())
        {
            AddSteamInstallCandidates(candidates, steamRoot);
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive is { IsReady: true, DriveType: DriveType.Fixed }))
        {
            AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", "ProjectZomboid"));
            AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, "Games", "SteamLibrary", "steamapps", "common", "ProjectZomboid"));
        }

        return candidates.FirstOrDefault(path => Directory.Exists(Path.Combine(path, "media")));
    }

    private static void AddSteamInstallCandidates(ISet<string> candidates, string? steamRootPath)
    {
        if (string.IsNullOrWhiteSpace(steamRootPath))
        {
            return;
        }

        var normalizedSteamRoot = steamRootPath
            .Trim()
            .Replace('/', Path.DirectorySeparatorChar);
        AddCandidate(candidates, Path.Combine(normalizedSteamRoot, "steamapps", "common", "ProjectZomboid"));

        var libraryFoldersPath = Path.Combine(normalizedSteamRoot, "steamapps", "libraryfolders.vdf");
        foreach (var libraryRoot in ReadSteamLibraryRoots(libraryFoldersPath))
        {
            AddCandidate(candidates, Path.Combine(libraryRoot, "steamapps", "common", "ProjectZomboid"));
        }
    }

    private static void AddCandidate(ISet<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        candidates.Add(path.Trim());
    }

    private static IReadOnlyCollection<string> GetSteamRootsFromRegistry()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return roots;
        }

        AddSteamRootFromRegistry(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddSteamRootFromRegistry(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamExe");
        AddSteamRootFromRegistry(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddSteamRootFromRegistry(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        return roots;
    }

    [SupportedOSPlatform("windows")]
    private static void AddSteamRootFromRegistry(
        ISet<string> roots,
        RegistryKey baseKey,
        string subKey,
        string valueName)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            var rawValue = key?.GetValue(valueName)?.ToString();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return;
            }

            var value = rawValue.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                value = Path.GetDirectoryName(value) ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                roots.Add(value);
            }
        }
        catch
        {
            // Ignore registry access issues and continue with other detection paths.
        }
    }

    private static IReadOnlyCollection<string> ReadSteamLibraryRoots(string libraryFoldersPath)
    {
        if (!File.Exists(libraryFoldersPath))
        {
            return [];
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var content = SafeReadText(libraryFoldersPath);
        if (string.IsNullOrWhiteSpace(content))
        {
            return result;
        }

        foreach (Match match in SteamLibraryPathRegex.Matches(content))
        {
            var rawPath = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var normalized = rawPath
                .Replace(@"\\", @"\")
                .Replace('/', Path.DirectorySeparatorChar)
                .Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    public IReadOnlyList<string> GetAvailableLanguageCodes(string? gamePath, bool includeMods)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "EN",
        };

        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return result.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var roots = DiscoverGameRoots(gamePath, includeMods);
        foreach (var root in roots)
        {
            var translateRoot = Path.Combine(root, "media", "lua", "shared", "Translate");
            if (!Directory.Exists(translateRoot))
            {
                continue;
            }

            foreach (var languageDirectory in SafeEnumerateDirectories(translateRoot))
            {
                var directoryName = Path.GetFileName(languageDirectory);
                if (string.IsNullOrWhiteSpace(directoryName))
                {
                    continue;
                }

                result.Add(directoryName.ToUpperInvariant());
            }
        }

        return result
            .OrderBy(code => code.Equals("EN", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyDictionary<string, (string LocalizedName, string Source)> ResolveLocalizedItemNames(
        string? gamePath,
        bool includeMods,
        IReadOnlyCollection<GuideItem> items,
        string? languageCode)
    {
        if (items.Count == 0)
        {
            return new Dictionary<string, (string LocalizedName, string Source)>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedLanguageCode = NormalizeLanguageCode(languageCode);
        var roots = string.IsNullOrWhiteSpace(gamePath)
            ? []
            : DiscoverGameRoots(gamePath, includeMods);
        var englishTranslations = roots.Count > 0
            ? LoadTranslations(roots, "EN")
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetTranslations = roots.Count > 0
            ? LoadTranslations(roots, normalizedLanguageCode)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var englishRecipeToTarget = BuildEnglishRecipeToTargetMap(englishTranslations, targetTranslations);

        var result = new Dictionary<string, (string LocalizedName, string Source)>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.GermanName) &&
                item.GermanNameSource.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                result[item.Id] = (item.GermanName, "game");
                continue;
            }

            var gameLocalizedName = ResolveLocalizedNameFromGameTranslations(item, targetTranslations, englishRecipeToTarget);
            if (!string.IsNullOrWhiteSpace(gameLocalizedName))
            {
                result[item.Id] = (gameLocalizedName, "game");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.GermanName) &&
                item.GermanNameSource.Equals("app", StringComparison.OrdinalIgnoreCase))
            {
                result[item.Id] = (item.GermanName, "app");
                continue;
            }

            var appLocalizedName = BuildFallbackLocalizedName(item, normalizedLanguageCode);
            if (!string.IsNullOrWhiteSpace(appLocalizedName))
            {
                result[item.Id] = (appLocalizedName, "app");
            }
        }

        return result;
    }

    private static List<string> DiscoverGameRoots(string gamePath, bool includeMods)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(Path.Combine(gamePath, "media")))
        {
            roots.Add(gamePath);
        }

        if (!includeMods)
        {
            return roots.ToList();
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var userModsPath = Path.Combine(userProfile, "Zomboid", "mods");
            AddModDirectories(userModsPath, roots);
        }

        AddModDirectories(Path.Combine(gamePath, "mods"), roots);

        var steamAppsPath = FindParentSteamApps(gamePath);
        if (!string.IsNullOrWhiteSpace(steamAppsPath))
        {
            var workshopPath = Path.Combine(steamAppsPath, "workshop", "content", "108600");
            if (Directory.Exists(workshopPath))
            {
                foreach (var workshopItem in SafeEnumerateDirectories(workshopPath))
                {
                    AddModDirectories(Path.Combine(workshopItem, "mods"), roots);
                    if (Directory.Exists(Path.Combine(workshopItem, "media")))
                    {
                        roots.Add(workshopItem);
                    }
                }
            }
        }

        return roots.ToList();
    }

    private static string? FindParentSteamApps(string gamePath)
    {
        var current = new DirectoryInfo(gamePath);
        while (current is not null)
        {
            if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void AddModDirectories(string modsRoot, HashSet<string> roots)
    {
        if (!Directory.Exists(modsRoot))
        {
            return;
        }

        foreach (var mod in SafeEnumerateDirectories(modsRoot))
        {
            if (Directory.Exists(Path.Combine(mod, "media")))
            {
                roots.Add(mod);
            }

            foreach (var nestedMod in SafeEnumerateDirectories(mod))
            {
                if (Directory.Exists(Path.Combine(nestedMod, "media")))
                {
                    roots.Add(nestedMod);
                }
            }
        }
    }

    private static Dictionary<string, string> LoadTranslations(IEnumerable<string> roots, string languageCode)
    {
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var translationPath = Path.Combine(root, "media", "lua", "shared", "Translate", languageCode);
            if (!Directory.Exists(translationPath))
            {
                continue;
            }

            foreach (var file in SafeEnumerateFiles(translationPath, "*.txt", SearchOption.AllDirectories))
            {
                foreach (var rawLine in SafeReadLines(file))
                {
                    var line = StripLineComments(rawLine).Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = line[..separator].Trim();
                    var value = line[(separator + 1)..].Trim().TrimEnd(',');
                    value = Unquote(value);

                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        translations[key] = value.Replace("\\\"", "\"", StringComparison.Ordinal);
                    }
                }
            }
        }

        return translations;
    }

    private static IReadOnlyList<GuideItem> ParseProfessions(
        IReadOnlyCollection<string> roots,
        IReadOnlyDictionary<string, string> translations,
        string baseGamePath)
    {
        var professions = new Dictionary<string, GuideItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            foreach (var file in SafeEnumerateFiles(root, "ProfessionFactory.lua", SearchOption.AllDirectories))
            {
                var content = SafeReadText(file);
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                foreach (Match match in ProfessionRegex.Matches(content))
                {
                    var id = match.Groups["id"].Value.Trim();
                    var rawNameExpression = match.Groups["name"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var resolvedName = ResolveExpression(rawNameExpression, translations);
                    var name = string.IsNullOrWhiteSpace(resolvedName) ? HumanizeId(id) : resolvedName;

                    professions[id] = new GuideItem
                    {
                        Id = $"profession.{NormalizeId(id)}",
                        Name = name,
                        Type = GuideItemType.Profession,
                        Detail = "Beruf",
                        Category = "Startberuf",
                        Source = string.Equals(root, baseGamePath, StringComparison.OrdinalIgnoreCase)
                            ? "Base game"
                            : $"Mod: {Path.GetFileName(root)}",
                        Aliases = BuildAliases(name, id, $"base:{id}"),
                    };
                }
            }
        }

        return professions.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<GuideItem> ParseLiterature(
        IReadOnlyCollection<string> roots,
        IReadOnlyDictionary<string, string> englishTranslations,
        IReadOnlyDictionary<string, string> germanTranslations)
    {
        var entries = new Dictionary<string, GuideItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            var scriptsPath = Path.Combine(root, "media", "scripts");
            if (!Directory.Exists(scriptsPath))
            {
                continue;
            }

            foreach (var file in SafeEnumerateFiles(scriptsPath, "*.txt", SearchOption.AllDirectories))
            {
                var module = "Base";
                var collectingItem = false;
                var itemName = string.Empty;
                var itemBuffer = new List<string>();
                var bracketDepth = 0;
                var foundOpeningBracket = false;

                foreach (var line in SafeReadLines(file))
                {
                    if (!collectingItem)
                    {
                        var moduleMatch = ModuleRegex.Match(line);
                        if (moduleMatch.Success)
                        {
                            module = moduleMatch.Groups[1].Value.Trim();
                            continue;
                        }

                        var itemMatch = ItemRegex.Match(line);
                        if (!itemMatch.Success)
                        {
                            continue;
                        }

                        collectingItem = true;
                        itemName = itemMatch.Groups[1].Value.Trim();
                        itemBuffer.Clear();
                        itemBuffer.Add(line);
                        var delta = CountCharacter(line, '{') - CountCharacter(line, '}');
                        bracketDepth = delta;
                        foundOpeningBracket = line.Contains('{', StringComparison.Ordinal);
                        continue;
                    }

                    itemBuffer.Add(line);
                    bracketDepth += CountCharacter(line, '{') - CountCharacter(line, '}');
                    foundOpeningBracket = foundOpeningBracket || line.Contains('{', StringComparison.Ordinal);

                    if (!foundOpeningBracket || bracketDepth > 0)
                    {
                        continue;
                    }

                    collectingItem = false;
                    var entry = ParseItem(module, itemName, itemBuffer, englishTranslations, germanTranslations, root);
                    if (entry is null)
                    {
                        continue;
                    }

                    entries[entry.Id] = entry;
                }
            }
        }

        return entries.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GuideItem? ParseItem(
        string module,
        string itemName,
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, string> englishTranslations,
        IReadOnlyDictionary<string, string> germanTranslations,
        string root)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = StripLineComments(raw).Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("item ", StringComparison.OrdinalIgnoreCase) || line is "{" or "}")
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            value = value.TrimEnd(',').Trim();

            if (!string.IsNullOrWhiteSpace(key))
            {
                properties[key] = value;
            }
        }

        var type = GetResolvedProperty(properties, englishTranslations, "Type", "ItemType");
        if (!LooksLikeLiteratureType(type))
        {
            return null;
        }

        var moduleItemId = $"{module}.{itemName}";
        var initialAliases = BuildAliases(itemName, moduleItemId, $"Base.{itemName}");

        var displayName = GetResolvedProperty(properties, englishTranslations, "DisplayName");
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = ResolveLocalizedItemName(initialAliases, englishTranslations);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = HumanizeId(itemName);
        }

        var skill = GetResolvedProperty(properties, englishTranslations, "SkillTrained");
        var levelRaw = GetResolvedProperty(properties, englishTranslations, "LvlSkillTrained");
        var numLevelsRaw = GetResolvedProperty(properties, englishTranslations, "NumLevelsTrained");
        var recipes = ParseRecipeList(
            GetResolvedProperty(properties, englishTranslations, "TeachedRecipes", "LearnedRecipes"),
            englishTranslations);
        var rawLevel = int.TryParse(levelRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel) && parsedLevel > 0
            ? parsedLevel
            : 0;
        var numLevelsTrained = int.TryParse(numLevelsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumLevels) && parsedNumLevels > 0
            ? parsedNumLevels
            : 1;
        var level = NormalizeBookTierLevel(rawLevel, numLevelsTrained);

        var displayCategory = GetResolvedProperty(properties, englishTranslations, "DisplayCategory");
        var shouldInclude = !string.IsNullOrWhiteSpace(skill) ||
                            recipes.Count > 0 ||
                            string.Equals(displayCategory, "SkillBook", StringComparison.OrdinalIgnoreCase);
        if (!shouldInclude)
        {
            return null;
        }

        var itemType = ClassifyLiteratureType(displayName, itemName, skill, recipes.Count);
        var detail = BuildDetail(itemType, skill, level, recipes.Count);
        var category = BuildCategory(itemType, skill, displayName, itemName, recipes);
        var normalizedId = $"{itemType.ToString().ToLowerInvariant()}.{NormalizeId(moduleItemId)}";
        var resolvedId = itemType is GuideItemType.Book or GuideItemType.Magazine
            ? moduleItemId
            : normalizedId;
        var aliases = BuildAliases(displayName, itemName, moduleItemId, $"Base.{itemName}");
        var germanName = ResolveLocalizedItemNameByAliases(aliases, germanTranslations);
        if (string.IsNullOrWhiteSpace(germanName))
        {
            var germanDisplayName = GetResolvedProperty(properties, germanTranslations, "DisplayName");
            germanName = string.Equals(germanDisplayName, displayName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : germanDisplayName;
        }
        var germanNameSource = string.IsNullOrWhiteSpace(germanName) ? string.Empty : "game";

        return new GuideItem
        {
            Id = resolvedId,
            Name = displayName,
            GermanName = germanName,
            GermanNameSource = germanNameSource,
            Type = itemType,
            Detail = detail,
            Level = itemType == GuideItemType.Book ? level : 0,
            Category = category,
            Source = string.Equals(module, "Base", StringComparison.OrdinalIgnoreCase)
                ? "Base game"
                : $"Modul: {module} ({Path.GetFileName(root)})",
            Recipes = recipes,
            Aliases = aliases,
        };
    }

    private static IReadOnlyList<GuideItem> BuildRecipeItems(
        IEnumerable<GuideItem> literature,
        IReadOnlyDictionary<string, string> germanTranslations,
        IReadOnlyDictionary<string, string> englishTranslations)
    {
        var recipes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var englishRecipeToGerman = BuildEnglishRecipeToTargetMap(englishTranslations, germanTranslations);

        foreach (var literatureItem in literature)
        {
            if (literatureItem.Recipes.Count == 0)
            {
                continue;
            }

            foreach (var recipe in literatureItem.Recipes)
            {
                if (!recipes.TryGetValue(recipe, out var sources))
                {
                    sources = [];
                    recipes[recipe] = sources;
                }

                if (!sources.Contains(literatureItem.Name, StringComparer.OrdinalIgnoreCase))
                {
                    sources.Add(literatureItem.Name);
                }
            }
        }

        return recipes
            .Select(entry =>
            {
                var germanName = ResolveLocalizedRecipeName([entry.Key], germanTranslations, englishRecipeToGerman);
                return new GuideItem
                {
                    Id = $"recipe.{NormalizeId(entry.Key)}",
                    Name = entry.Key,
                    GermanName = germanName,
                    GermanNameSource = string.IsNullOrWhiteSpace(germanName) ? string.Empty : "game",
                    Type = GuideItemType.Recipe,
                    Detail = "Freischaltbares Rezept",
                    Category = "Rezepte",
                    Source = $"Gelernt aus: {string.Join(", ", entry.Value)}",
                    Aliases = BuildAliases(entry.Key),
                };
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static GuideItemType ClassifyLiteratureType(string displayName, string itemName, string skill, int recipeCount)
    {
        if (!string.IsNullOrWhiteSpace(skill))
        {
            return GuideItemType.Book;
        }

        if (recipeCount > 0)
        {
            return GuideItemType.Magazine;
        }

        var haystack = $"{displayName} {itemName}".ToLowerInvariant();
        if (haystack.Contains("magazine", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("comic", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("newspaper", StringComparison.OrdinalIgnoreCase) ||
            haystack.Contains("journal", StringComparison.OrdinalIgnoreCase))
        {
            return GuideItemType.Magazine;
        }

        return GuideItemType.Book;
    }

    private static string BuildDetail(GuideItemType itemType, string skill, int level, int recipeCount)
    {
        if (itemType == GuideItemType.Book && !string.IsNullOrWhiteSpace(skill))
        {
            if (level > 0)
            {
                return $"Skill: {skill} (Stufe {level})";
            }

            return $"Skill: {skill}";
        }

        if (itemType == GuideItemType.Magazine && recipeCount > 0)
        {
            return $"{recipeCount} Rezepte";
        }

        return itemType == GuideItemType.Book ? "Buch" : "Magazin";
    }

    private static string BuildCategory(
        GuideItemType itemType,
        string skill,
        string displayName,
        string itemName,
        IReadOnlyCollection<string> recipes)
    {
        if (itemType == GuideItemType.Book)
        {
            if (!string.IsNullOrWhiteSpace(skill))
            {
                return skill;
            }

            var raw = $"{displayName} {itemName}".ToLowerInvariant();
            if (raw.Contains("carp"))
            {
                return "Carpentry";
            }

            if (raw.Contains("cook"))
            {
                return "Cooking";
            }

            if (raw.Contains("mechan"))
            {
                return "Mechanics";
            }

            if (raw.Contains("forag"))
            {
                return "Foraging";
            }

            if (raw.Contains("fish"))
            {
                return "Fishing";
            }

            return "Allgemein";
        }

        return itemType == GuideItemType.Magazine
            ? ResolveMagazineCategory(itemName, displayName, recipes)
            : "Rezepte";
    }

    private static string ResolveMagazineCategory(string itemName, string displayName, IReadOnlyCollection<string> recipes)
    {
        var haystack = $"{itemName} {displayName} {string.Join(" ", recipes)}".ToLowerInvariant();

        if (haystack.Contains("mechanicmag", StringComparison.Ordinal) ||
            haystack.Contains("auto manual", StringComparison.Ordinal) ||
            haystack.Contains("mechanic", StringComparison.Ordinal))
        {
            return "Mechanics";
        }

        if (haystack.Contains("smithingmag", StringComparison.Ordinal) ||
            haystack.Contains("smith", StringComparison.Ordinal))
        {
            return "Blacksmith";
        }

        if (haystack.Contains("metalworkmag", StringComparison.Ordinal))
        {
            return "Metalworking";
        }

        if (haystack.Contains("electronicsmag", StringComparison.Ordinal) ||
            haystack.Contains("engineermagazine", StringComparison.Ordinal) ||
            haystack.Contains("radio", StringComparison.Ordinal))
        {
            return "Electrical";
        }

        if (haystack.Contains("fishingmag", StringComparison.Ordinal))
        {
            return "Fishing";
        }

        if (haystack.Contains("huntingmag", StringComparison.Ordinal) ||
            haystack.Contains("trap", StringComparison.Ordinal))
        {
            return "Trapping";
        }

        if (haystack.Contains("herbalist", StringComparison.Ordinal) ||
            haystack.Contains("forag", StringComparison.Ordinal))
        {
            return "Foraging";
        }

        if (haystack.Contains("tailor", StringComparison.Ordinal) ||
            haystack.Contains("sew", StringComparison.Ordinal) ||
            haystack.Contains("knit", StringComparison.Ordinal))
        {
            return "Tailoring";
        }

        if (haystack.Contains("cook", StringComparison.Ordinal))
        {
            return "Cooking";
        }

        if (haystack.Contains("farming", StringComparison.Ordinal) ||
            haystack.Contains("growing season", StringComparison.Ordinal))
        {
            return "Farming";
        }

        if (haystack.Contains("weapon", StringComparison.Ordinal) ||
            haystack.Contains("armor", StringComparison.Ordinal))
        {
            return "Combat";
        }

        return "Allgemein";
    }

    private static string ResolveLocalizedNameFromGameTranslations(
        GuideItem item,
        IReadOnlyDictionary<string, string> localizedTranslations,
        IReadOnlyDictionary<string, string> englishRecipeToTarget)
    {
        if (localizedTranslations.Count == 0)
        {
            return string.Empty;
        }

        if (item.Type == GuideItemType.Recipe)
        {
            var aliases = item.Aliases.Count == 0 ? [item.Name] : item.Aliases;
            return ResolveLocalizedRecipeName(aliases, localizedTranslations, englishRecipeToTarget);
        }

        var nameAliases = item.Aliases.Count == 0 ? [item.Name] : item.Aliases;
        return ResolveLocalizedItemNameByAliases(nameAliases, localizedTranslations);
    }

    private static string ResolveLocalizedRecipeName(
        IReadOnlyCollection<string> aliases,
        IReadOnlyDictionary<string, string> localizedTranslations,
        IReadOnlyDictionary<string, string>? englishRecipeToTarget = null)
    {
        if (localizedTranslations.Count == 0)
        {
            return string.Empty;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            var clean = alias.Trim();
            candidates.Add(clean);
            candidates.Add(clean.Replace(" ", "_", StringComparison.Ordinal));
            candidates.Add(clean.Replace('-', '_').Replace('.', '_').Replace(':', '_'));
            candidates.Add(new string(clean.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()));
        }

        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            if (candidate.StartsWith("Recipe_", StringComparison.OrdinalIgnoreCase))
            {
                if (localizedTranslations.TryGetValue(candidate, out var recipeTranslated) &&
                    !string.IsNullOrWhiteSpace(recipeTranslated))
                {
                    return recipeTranslated;
                }
            }
            else
            {
                var recipeKey = $"Recipe_{candidate}";
                if (localizedTranslations.TryGetValue(recipeKey, out var recipeTranslated) &&
                    !string.IsNullOrWhiteSpace(recipeTranslated))
                {
                    return recipeTranslated;
                }
            }
        }

        if (englishRecipeToTarget is { Count: > 0 })
        {
            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                var normalizedAlias = NormalizeRecipeValue(alias);
                if (englishRecipeToTarget.TryGetValue(normalizedAlias, out var translated) &&
                    !string.IsNullOrWhiteSpace(translated))
                {
                    return translated;
                }
            }
        }

        return string.Empty;
    }

    private static IReadOnlyDictionary<string, string> BuildEnglishRecipeToTargetMap(
        IReadOnlyDictionary<string, string> englishTranslations,
        IReadOnlyDictionary<string, string> targetTranslations)
    {
        if (englishTranslations.Count == 0 || targetTranslations.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var targetByCanonicalKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var translation in targetTranslations)
        {
            if (string.IsNullOrWhiteSpace(translation.Value))
            {
                continue;
            }

            foreach (var canonicalKey in BuildCanonicalTranslationKeys(translation.Key))
            {
                if (canonicalKey.Length == 0)
                {
                    continue;
                }

                targetByCanonicalKey[canonicalKey] = translation.Value;
            }
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var english in englishTranslations)
        {
            if (string.IsNullOrWhiteSpace(english.Value))
            {
                continue;
            }

            if (!targetTranslations.TryGetValue(english.Key, out var targetValue) ||
                string.IsNullOrWhiteSpace(targetValue))
            {
                foreach (var englishKeyVariant in BuildCanonicalTranslationKeys(english.Key))
                {
                    if (targetByCanonicalKey.TryGetValue(englishKeyVariant, out targetValue) &&
                        !string.IsNullOrWhiteSpace(targetValue))
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(targetValue))
            {
                continue;
            }

            var normalizedEnglishValue = NormalizeRecipeValue(english.Value);
            if (normalizedEnglishValue.Length == 0)
            {
                continue;
            }

            map[normalizedEnglishValue] = targetValue;
        }

        return map;
    }

    private static string NormalizeTranslationKey(string value)
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

    private static IReadOnlyCollection<string> BuildCanonicalTranslationKeys(string value)
    {
        var normalized = NormalizeTranslationKey(value);
        if (normalized.Length == 0)
        {
            return [];
        }

        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalized,
        };

        if (normalized.StartsWith("recipe", StringComparison.OrdinalIgnoreCase) &&
            normalized.Length > "recipe".Length)
        {
            variants.Add(normalized["recipe".Length..]);
        }

        foreach (var item in variants.ToArray())
        {
            if (item.EndsWith("tobomb", StringComparison.OrdinalIgnoreCase) &&
                item.Length > "tobomb".Length)
            {
                variants.Add(item[..^"tobomb".Length]);
            }
        }

        return variants;
    }

    private static string NormalizeRecipeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch == ' ')
            .ToArray())
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeLanguageCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "EN";
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length == 0 ? "EN" : normalized;
    }

    private static string BuildFallbackLocalizedName(GuideItem item, string languageCode)
    {
        if (!string.Equals(languageCode, "DE", StringComparison.OrdinalIgnoreCase))
        {
            return item.Name;
        }

        if (!string.IsNullOrWhiteSpace(item.GermanName))
        {
            return item.GermanName;
        }

        var seed = item.Name;
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = item.Id;
        }

        if (string.IsNullOrWhiteSpace(seed))
        {
            return string.Empty;
        }

        var normalized = seed.Contains('_', StringComparison.Ordinal) ||
                         seed.Contains(':', StringComparison.Ordinal) ||
                         seed.Contains('.', StringComparison.Ordinal)
            ? HumanizeId(seed)
            : seed;

        var translated = normalized
            .Replace("Beginners", "Einsteiger", StringComparison.OrdinalIgnoreCase)
            .Replace("Intermediate", "Fortgeschrittene", StringComparison.OrdinalIgnoreCase)
            .Replace("Advanced", "Fortgeschrittene", StringComparison.OrdinalIgnoreCase)
            .Replace("Expert", "Experte", StringComparison.OrdinalIgnoreCase)
            .Replace("Master", "Meister", StringComparison.OrdinalIgnoreCase)
            .Replace("Magazine", "Magazin", StringComparison.OrdinalIgnoreCase)
            .Replace("Mechanics", "Mechanik", StringComparison.OrdinalIgnoreCase)
            .Replace("Mechanic", "Mechanik", StringComparison.OrdinalIgnoreCase)
            .Replace("Foraging", "Nahrungssuche", StringComparison.OrdinalIgnoreCase)
            .Replace("Carpentry", "Tischlerei", StringComparison.OrdinalIgnoreCase)
            .Replace("Electrical", "Elektrotechnik", StringComparison.OrdinalIgnoreCase)
            .Replace("Electrician", "Elektriker", StringComparison.OrdinalIgnoreCase)
            .Replace("Metalworking", "Metallbearbeitung", StringComparison.OrdinalIgnoreCase)
            .Replace("Fishing", "Angeln", StringComparison.OrdinalIgnoreCase)
            .Replace("Farming", "Landwirtschaft", StringComparison.OrdinalIgnoreCase)
            .Replace("Cooking", "Kochen", StringComparison.OrdinalIgnoreCase)
            .Replace("Tailoring", "Schneiderei", StringComparison.OrdinalIgnoreCase)
            .Replace("Trapping", "Fallenstellen", StringComparison.OrdinalIgnoreCase)
            .Replace("Blacksmith", "Schmieden", StringComparison.OrdinalIgnoreCase)
            .Replace("Smithing", "Schmieden", StringComparison.OrdinalIgnoreCase)
            .Replace("First Aid", "Erste Hilfe", StringComparison.OrdinalIgnoreCase)
            .Replace("Maintenance", "Instandhaltung", StringComparison.OrdinalIgnoreCase)
            .Replace("Pottery", "Töpferei", StringComparison.OrdinalIgnoreCase)
            .Replace("Masonry", "Mauerwerk", StringComparison.OrdinalIgnoreCase)
            .Replace("Glassmaking", "Glasherstellung", StringComparison.OrdinalIgnoreCase)
            .Replace("Knapping", "Knapping", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return translated;
    }

    private static IReadOnlyList<string> ParseRecipeList(string rawRecipes, IReadOnlyDictionary<string, string> translations)
    {
        if (string.IsNullOrWhiteSpace(rawRecipes))
        {
            return [];
        }

        return rawRecipes
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(recipe => ResolveExpression(recipe, translations))
            .Where(recipe => !string.IsNullOrWhiteSpace(recipe))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetResolvedProperty(
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyDictionary<string, string> translations,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!properties.TryGetValue(key, out var value))
            {
                continue;
            }

            var resolved = ResolveExpression(value, translations);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return string.Empty;
    }

    private static string ResolveExpression(string expression, IReadOnlyDictionary<string, string> translations)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return string.Empty;
        }

        var clean = expression.Trim();
        clean = clean.TrimEnd(',');
        clean = Unquote(clean);

        var getTextMatch = GetTextRegex.Match(clean);
        if (getTextMatch.Success)
        {
            var key = getTextMatch.Groups[1].Value;
            if (translations.TryGetValue(key, out var translated) && !string.IsNullOrWhiteSpace(translated))
            {
                return translated;
            }

            return HumanizeId(key);
        }

        if (translations.TryGetValue(clean, out var existingValue))
        {
            return existingValue;
        }

        return clean;
    }

    private static string Unquote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string StripLineComments(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        var commentPosition = line.Length;
        var slashComment = line.IndexOf("//", StringComparison.Ordinal);
        if (slashComment >= 0)
        {
            commentPosition = Math.Min(commentPosition, slashComment);
        }

        var dashComment = line.IndexOf("--", StringComparison.Ordinal);
        if (dashComment >= 0)
        {
            commentPosition = Math.Min(commentPosition, dashComment);
        }

        return commentPosition == line.Length ? line : line[..commentPosition];
    }

    private static int CountCharacter(string value, char target)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == target)
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern, searchOption);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeReadLines(string filePath)
    {
        try
        {
            return File.ReadLines(filePath, Encoding.UTF8);
        }
        catch
        {
            return [];
        }
    }

    private static string SafeReadText(string filePath)
    {
        try
        {
            return File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string HumanizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(id.Length + 6);
        var previousWasLower = false;
        foreach (var ch in id.Replace('_', ' ').Replace('.', ' ').Replace('-', ' '))
        {
            if (previousWasLower && char.IsUpper(ch))
            {
                builder.Append(' ');
            }

            builder.Append(ch);
            previousWasLower = char.IsLower(ch);
        }

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());

        return string.Join(" ", words);
    }

    private static string NormalizeId(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static IReadOnlyList<string> BuildAliases(params string[] values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveLocalizedItemNameByAliases(
        IReadOnlyCollection<string> aliases,
        IReadOnlyDictionary<string, string> translations)
    {
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            var cleanAlias = alias.Trim();
            var normalizedAlias = cleanAlias.Replace(':', '.');

            var translationKeys = new List<string>
            {
                $"ItemName_{normalizedAlias}",
                normalizedAlias,
            };

            if (!normalizedAlias.Contains('.', StringComparison.Ordinal))
            {
                translationKeys.Add($"ItemName_Base.{normalizedAlias}");
            }

            foreach (var key in translationKeys)
            {
                if (!translations.TryGetValue(key, out var translated) || string.IsNullOrWhiteSpace(translated))
                {
                    continue;
                }

                return translated;
            }
        }

        return string.Empty;
    }

    private static string ResolveLocalizedItemName(
        IReadOnlyCollection<string> aliases,
        IReadOnlyDictionary<string, string> translations)
    {
        foreach (var alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            var cleanAlias = alias.Trim();
            var normalizedAlias = cleanAlias.Replace(':', '.');

            var translationKeys = new List<string>
            {
                $"ItemName_{normalizedAlias}",
                normalizedAlias,
            };

            if (!normalizedAlias.Contains('.', StringComparison.Ordinal))
            {
                translationKeys.Add($"ItemName_Base.{normalizedAlias}");
            }

            foreach (var key in translationKeys)
            {
                if (!translations.TryGetValue(key, out var translated) || string.IsNullOrWhiteSpace(translated))
                {
                    continue;
                }

                return translated;
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeLiteratureType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(value, "Literature", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.EndsWith(":literature", StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeBookTierLevel(int rawLevel, int numLevelsTrained)
    {
        if (rawLevel <= 0)
        {
            return 0;
        }

        if (numLevelsTrained <= 1)
        {
            return rawLevel;
        }

        return ((rawLevel - 1) / numLevelsTrained) + 1;
    }
}
