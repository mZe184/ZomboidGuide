using System;
using System.Collections.Generic;
using System.Linq;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class DefaultCatalogService
{
    public GuideCatalog BuildDefaultCatalog()
    {
        var items = new List<GuideItem>();
        items.AddRange(BuildDefaultProfessions());

        var magazines = BuildDefaultMagazines();
        items.AddRange(magazines);
        items.AddRange(BuildRecipeItems(magazines));

        items.AddRange(BuildDefaultBooks());

        return new GuideCatalog
        {
            Items = items
                .OrderBy(item => item.Type)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            LoadedFromGameFiles = false,
            SourcesScanned = ["Built-in defaults"],
            CreatedAt = DateTimeOffset.Now,
        };
    }

    private static IReadOnlyList<GuideItem> BuildDefaultProfessions()
    {
        var professions = new (string Name, string Id)[]
        {
            ("Unemployed", "unemployed"),
            ("Burglar", "burglar"),
            ("Carpenter", "carpenter"),
            ("Chef", "chef"),
            ("Construction Worker", "constructionworker"),
            ("Doctor", "doctor"),
            ("Electrician", "electrician"),
            ("Engineer", "engineer"),
            ("Farmer", "farmer"),
            ("Fire Officer", "fireofficer"),
            ("Fisherman", "fisherman"),
            ("Fitness Instructor", "fitnessinstructor"),
            ("Lumberjack", "lumberjack"),
            ("Mechanic", "mechanic"),
            ("Metalworker", "metalworker"),
            ("Nurse", "nurse"),
            ("Park Ranger", "parkranger"),
            ("Police Officer", "policeofficer"),
            ("Repairman", "repairman"),
            ("Security Guard", "securityguard"),
            ("Veteran", "veteran"),
        };

        return professions.Select(profession => new GuideItem
        {
            Id = $"default.profession.{profession.Id}",
            Name = profession.Name,
            Type = GuideItemType.Profession,
            Detail = "Start-Beruf",
            Category = "Startberuf",
            Source = "Base game (fallback)",
            Aliases = BuildAliases(profession.Name, profession.Id, $"base:{profession.Id}"),
        }).ToList();
    }

    private static IReadOnlyList<GuideItem> BuildDefaultBooks()
    {
        var skills = new (string DisplayName, string Category, string ItemToken, IReadOnlyList<string> AlternateTokens)[]
        {
            ("Carpentry", "Carpentry", "BookCarpentry", []),
            ("Carving", "Carving", "BookCarving", []),
            ("Cooking", "Cooking", "BookCooking", []),
            ("Electrical", "Electrical", "BookElectrician", ["BookElectrical", "BookElectricity"]),
            ("Farming", "Farming", "BookFarming", []),
            ("First Aid", "First Aid", "BookFirstAid", ["BookDoctor"]),
            ("Fishing", "Fishing", "BookFishing", []),
            ("Flint Knapping", "FlintKnapping", "BookFlintKnapping", ["BookKnapping"]),
            ("Foraging", "Foraging", "BookForaging", []),
            ("Glassmaking", "Glassmaking", "BookGlassmaking", []),
            ("Masonry", "Masonry", "BookMasonry", []),
            ("Mechanics", "Mechanics", "BookMechanic", ["BookMechanics"]),
            ("Metalworking", "Metalworking", "BookMetalWelding", ["BookMetalworking", "BookMetalwork"]),
            ("Blacksmith", "Blacksmith", "BookBlacksmith", ["BookSmithing"]),
            ("Maintenance", "Maintenance", "BookMaintenance", []),
            ("Pottery", "Pottery", "BookPottery", []),
            ("Tailoring", "Tailoring", "BookTailoring", []),
            ("Trapping", "Trapping", "BookTrapping", []),
        };

        var levels = new[]
        {
            "Beginners",
            "Intermediate",
            "Advanced",
            "Expert",
            "Master",
        };

        var books = new List<GuideItem>();
        for (var skillIndex = 0; skillIndex < skills.Length; skillIndex++)
        {
            for (var levelIndex = 0; levelIndex < levels.Length; levelIndex++)
            {
                var skill = skills[skillIndex];
                var levelName = levels[levelIndex];
                var level = levelIndex + 1;
                var itemCode = $"Base.{skill.ItemToken}{level}";
                var aliases = new List<string>
                {
                    $"{levelName} {skill.DisplayName}",
                    $"{skill.ItemToken}{level}",
                    itemCode,
                };
                foreach (var alternateToken in skill.AlternateTokens)
                {
                    aliases.Add($"{alternateToken}{level}");
                    aliases.Add($"Base.{alternateToken}{level}");
                }

                books.Add(new GuideItem
                {
                    Id = itemCode,
                    Name = $"{levelName} {skill.DisplayName}",
                    Type = GuideItemType.Book,
                    Detail = $"Skill-Buch, Stufe {level}",
                    Level = level,
                    Category = skill.Category,
                    Source = "Base game (fallback)",
                    Aliases = BuildAliases(aliases.ToArray()),
                });
            }
        }

        return books;
    }

    private static IReadOnlyList<GuideItem> BuildDefaultMagazines()
    {
        var items = new List<GuideItem>
        {
            BuildMagazine("Base.HerbalistMag", "Herbalist Magazine", ["Herbalist"]),
            BuildMagazine("Base.HuntingMag1", "The Hunter Magazine Vol. 1", ["Make Stick Trap", "Make Trap Box"]),
            BuildMagazine("Base.HuntingMag2", "The Hunter Magazine Vol. 2", ["Make Snare Trap", "Make Wooden Cage Trap"]),
            BuildMagazine("Base.HuntingMag3", "The Hunter Magazine Vol. 3", ["Make Trap Crate"]),
            BuildMagazine("Base.HuntingMag4", "The Hunter Magazine Vol. 4", ["Make Cage Trap"]),
            BuildMagazine("Base.FishingMag1", "Fishing Magazine Vol. 1", ["Make Fishing Rod", "Fix Fishing Rod"]),
            BuildMagazine("Base.FishingMag2", "Fishing Magazine Vol. 2", ["Make Fishing Net"]),
            BuildMagazine("Base.MechanicMag1", "Magazine: Laines Standard Auto Manual", ["Basic Mechanics"]),
            BuildMagazine("Base.MechanicMag2", "Magazine: Laines Commercial Auto Manual", ["Intermediate Mechanics"]),
            BuildMagazine("Base.MechanicMag3", "Magazine: Laines Performance Auto Manual", ["Advanced Mechanics"]),
            BuildMagazine("Base.MetalworkMag1", "Metalwork Magazine Vol. 1", ["Make Metal Walls"]),
            BuildMagazine("Base.MetalworkMag2", "Metalwork Magazine Vol. 2", ["Make Metal Fences", "Make Metal Containers"]),
            BuildMagazine("Base.MetalworkMag3", "Metalwork Magazine Vol. 3", ["Make Metal Floors"]),
            BuildMagazine("Base.MetalworkMag4", "Metalwork Magazine Vol. 4", ["Make Metal Roofing"]),
            BuildMagazine("Base.ElectronicsMag1", "Electronics Magazine Vol. 1", ["Make Aerosol Bomb", "Make Noise Maker"]),
            BuildMagazine("Base.ElectronicsMag2", "Electronics Magazine Vol. 2", ["Make Smoke Bomb", "Make Remote Trigger"]),
            BuildMagazine("Base.ElectronicsMag3", "Electronics Magazine Vol. 3", ["Make Flame Trap", "Make Timer"]),
            BuildMagazine("Base.ElectronicsMag4", "Electronics Magazine Vol. 4", ["Make Motion Sensor"]),
            BuildMagazine("Base.ElectronicsMag5", "Electronics Magazine Vol. 5", ["Make Triggered Trap"]),
            BuildMagazine("Base.EngineerMagazine1", "Engineer Magazine Vol. 1", ["Make Pipe Bomb"]),
            BuildMagazine("Base.EngineerMagazine2", "Engineer Magazine Vol. 2", ["Make Molotov", "Make Flame Bomb"]),
            BuildMagazine("Base.EngineerMagazine3", "Engineer Magazine Vol. 3", ["Make Metal Drum Bomb"]),
            BuildMagazine("Base.SmithingMag1", "Magazine: Everyday Smithing - June 1993", []),
            BuildMagazine("Base.SmithingMag2", "Magazine: Everyday Smithing - August 1992", []),
            BuildMagazine("Base.SmithingMag3", "Magazine: Everyday Smithing - September 1994", []),
            BuildMagazine("Base.SmithingMag4", "Magazine: Everyday Smithing - June 1992", []),
            BuildMagazine("Base.SmithingMag5", "Magazine: Everyday Smithing - May 1992", []),
            BuildMagazine("Base.SmithingMag6", "Magazine: Smithing Workshop Blueprint", []),
            BuildMagazine("Base.SmithingMag7", "Magazine: Medieval Armory - September 1993", []),
            BuildMagazine("Base.SmithingMag8", "Magazine: Bladecraft Journal", []),
            BuildMagazine("Base.SmithingMag9", "Magazine: Frontier Blacksmithing", []),
            BuildMagazine("Base.SmithingMag10", "Magazine: Medieval Blacksmithing", []),
            BuildMagazine("Base.SmithingMag11", "Magazine: Medieval Armory - May 1993", []),
        };

        return items;
    }

    private static IReadOnlyList<GuideItem> BuildRecipeItems(IEnumerable<GuideItem> magazines)
    {
        var recipes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var magazine in magazines)
        {
            foreach (var recipe in magazine.Recipes)
            {
                if (!recipes.TryGetValue(recipe, out var sources))
                {
                    sources = [];
                    recipes[recipe] = sources;
                }

                if (!sources.Contains(magazine.Name, StringComparer.OrdinalIgnoreCase))
                {
                    sources.Add(magazine.Name);
                }
            }
        }

        return recipes.Select(entry => new GuideItem
        {
            Id = $"default.recipe.{NormalizeId(entry.Key)}",
            Name = entry.Key,
            Type = GuideItemType.Recipe,
            Detail = "Freischaltbares Rezept",
            Category = "Rezepte",
            Source = $"Magazin: {string.Join(", ", entry.Value)}",
            Aliases = BuildAliases(entry.Key),
        }).ToList();
    }

    private static GuideItem BuildMagazine(string id, string name, IReadOnlyList<string> recipes)
    {
        var category = ResolveMagazineCategory(id, name, recipes);
        return new GuideItem
        {
            Id = id,
            Name = name,
            Type = GuideItemType.Magazine,
            Detail = $"{recipes.Count} Rezepte",
            Category = category,
            Source = "Base game (fallback)",
            Recipes = recipes,
            Aliases = BuildAliases(name, id, ExtractItemCode(id)),
        };
    }

    private static string ResolveMagazineCategory(string id, string name, IReadOnlyCollection<string> recipes)
    {
        var haystack = $"{id} {name} {string.Join(" ", recipes)}".ToLowerInvariant();

        if (haystack.Contains("mechanicmag", StringComparison.Ordinal) ||
            haystack.Contains("mechanic", StringComparison.Ordinal) ||
            haystack.Contains("auto manual", StringComparison.Ordinal))
        {
            return "Mechanics";
        }

        if (haystack.Contains("smithingmag", StringComparison.Ordinal) ||
            haystack.Contains("blacksmith", StringComparison.Ordinal) ||
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

    private static string ExtractItemCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var separatorIndex = value.LastIndexOf('.');
        return separatorIndex >= 0 && separatorIndex < value.Length - 1
            ? value[(separatorIndex + 1)..]
            : value;
    }

    private static IReadOnlyList<string> BuildAliases(params string[] values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeId(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }
}
