using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class GuideCatalogService
{
    private readonly DefaultCatalogService _defaultCatalogService = new();
    private readonly ZomboidDataParser _zomboidDataParser = new();

    public string? TryAutoDetectGamePath()
    {
        return _zomboidDataParser.TryAutoDetectGamePath();
    }

    public string GetAutoDetectGamePathDiagnostics()
    {
        return _zomboidDataParser.BuildAutoDetectDiagnostics();
    }

    public IReadOnlyList<string> GetAvailableLanguageCodes(string? gamePath, bool includeMods)
    {
        return _zomboidDataParser.GetAvailableLanguageCodes(gamePath, includeMods);
    }

    public Task<GuideCatalog> LoadAsync(string? gamePath, bool includeMods, bool preferGameFiles, string languageCode)
    {
        return Task.Run(() =>
        {
            var defaultCatalog = _defaultCatalogService.BuildDefaultCatalog();
            GuideCatalog catalog;

            if (!preferGameFiles || string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                catalog = defaultCatalog;
            }
            else
            {
                var parsedCatalog = _zomboidDataParser.Parse(gamePath, includeMods, languageCode);
                if (!parsedCatalog.LoadedFromGameFiles || parsedCatalog.Items.Count == 0)
                {
                    catalog = defaultCatalog;
                }
                else
                {
                    catalog = MergeWithFallback(parsedCatalog, defaultCatalog);
                }
            }

            var localizedNames = _zomboidDataParser.ResolveLocalizedItemNames(gamePath, includeMods, catalog.Items, languageCode);
            catalog = ApplyLocalizedNames(catalog, localizedNames, languageCode);

            return catalog;
        });
    }

    private static GuideCatalog MergeWithFallback(GuideCatalog parsed, GuideCatalog fallback)
    {
        var merged = new List<GuideItem>();

        MergeCategory(GuideItemType.Profession, parsed, fallback, merged);
        MergeCategory(GuideItemType.Book, parsed, fallback, merged);
        MergeCategory(GuideItemType.Magazine, parsed, fallback, merged);
        MergeCategory(GuideItemType.Recipe, parsed, fallback, merged);

        return new GuideCatalog
        {
            Items = merged
                .GroupBy(BuildMergeKey)
                .Select(group => group.First())
                .OrderBy(item => item.Type)
                .ThenBy(item => item.Name)
                .ToList(),
            LoadedFromGameFiles = true,
            SourcesScanned = parsed.SourcesScanned.Count == 0 ? fallback.SourcesScanned : parsed.SourcesScanned,
            CreatedAt = parsed.CreatedAt,
        };
    }

    private static void MergeCategory(
        GuideItemType type,
        GuideCatalog parsed,
        GuideCatalog fallback,
        ICollection<GuideItem> target)
    {
        var parsedItems = parsed.Items.Where(item => item.Type == type);
        var fallbackItems = fallback.Items.Where(item => item.Type == type);

        foreach (var item in parsedItems)
        {
            target.Add(item);
        }

        foreach (var item in fallbackItems)
        {
            target.Add(item);
        }
    }

    private static GuideCatalog ApplyLocalizedNames(
        GuideCatalog catalog,
        IReadOnlyDictionary<string, (string LocalizedName, string Source)> localizedNames,
        string languageCode)
    {
        if (localizedNames.Count == 0)
        {
            return catalog;
        }

        var items = catalog.Items
            .Select(item =>
            {
                if (!localizedNames.TryGetValue(item.Id, out var localizedValue))
                {
                    return item;
                }

                var localizedName = localizedValue.LocalizedName;
                var localizedSource = localizedValue.Source;
                return new GuideItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    GermanName = localizedName,
                    GermanNameSource = localizedSource,
                    GermanNameLanguageCode = languageCode,
                    Type = item.Type,
                    Detail = item.Detail,
                    Level = item.Level,
                    Category = item.Category,
                    Source = item.Source,
                    Recipes = item.Recipes,
                    Aliases = item.Aliases,
                };
            })
            .ToList();

        return new GuideCatalog
        {
            Items = items,
            LoadedFromGameFiles = catalog.LoadedFromGameFiles,
            SourcesScanned = catalog.SourcesScanned,
            CreatedAt = catalog.CreatedAt,
        };
    }

    private static string NormalizeNameKey(string value)
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

    private static string BuildMergeKey(GuideItem item)
    {
        if ((item.Type == GuideItemType.Book || item.Type == GuideItemType.Magazine) &&
            LooksLikeModuleItemId(item.Id))
        {
            return $"{item.Type}:id:{NormalizeNameKey(item.Id)}";
        }

        return $"{item.Type}:name:{NormalizeNameKey(item.Name)}";
    }

    private static bool LooksLikeModuleItemId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var firstDot = id.IndexOf('.');
        if (firstDot <= 0 || firstDot >= id.Length - 1)
        {
            return false;
        }

        return id.IndexOf('.', firstDot + 1) < 0;
    }
}
