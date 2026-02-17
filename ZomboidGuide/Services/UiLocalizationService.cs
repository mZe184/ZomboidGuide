using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Platform;

namespace ZomboidGuide.Services;

public sealed class UiLocalizationService
{
    private static readonly string[] SupportedLanguageCodes =
    [
        "EN", "DE", "AR", "CA", "CH", "CN", "CS", "DA", "ES", "FI", "FR", "HU", "ID", "IT",
        "JP", "KO", "NL", "NO", "PH", "PL", "PT", "PTBR", "RO", "RU", "TH", "TR", "UA",
    ];

    private readonly Dictionary<string, Dictionary<string, string>> _translationsByLanguage =
        new(StringComparer.OrdinalIgnoreCase);

    public UiLocalizationService()
    {
        LoadAllTranslations();
    }

    public string Translate(string? languageCode, string englishText, string germanFallback)
    {
        if (string.IsNullOrWhiteSpace(englishText))
        {
            return string.Empty;
        }

        var code = NormalizeLanguageCode(languageCode);
        if (code.Equals("EN", StringComparison.OrdinalIgnoreCase))
        {
            return englishText;
        }

        if (_translationsByLanguage.TryGetValue(code, out var languageMap) &&
            languageMap.TryGetValue(englishText, out var translated) &&
            !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }

        if (code.Equals("DE", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(germanFallback))
        {
            return germanFallback;
        }

        return englishText;
    }

    public IReadOnlyList<string> GetSupportedLanguageCodes()
    {
        return SupportedLanguageCodes;
    }

    private void LoadAllTranslations()
    {
        foreach (var code in SupportedLanguageCodes)
        {
            var map = LoadLanguageMap(code);
            _translationsByLanguage[code] = map;
        }
    }

    private static Dictionary<string, string> LoadLanguageMap(string languageCode)
    {
        try
        {
            var uri = new Uri($"avares://ZomboidGuide/Assets/Localization/{languageCode}.json");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return data is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(data, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "EN";
        }

        var normalized = languageCode
            .Trim()
            .ToUpperInvariant()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty);

        return normalized switch
        {
            "PTB" => "PTBR",
            "UK" => "UA",
            _ => normalized,
        };
    }
}
