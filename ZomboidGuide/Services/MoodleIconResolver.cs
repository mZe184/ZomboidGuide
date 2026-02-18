using System;
using System.IO;

namespace ZomboidGuide.Services;

public static class MoodleIconResolver
{
    public static string ResolveIconFileName(string moodleLabel)
    {
        var text = (moodleLabel ?? string.Empty).Trim().ToLowerInvariant();

        if (text.StartsWith("hungry", StringComparison.Ordinal))
        {
            return "Status_Hunger.png";
        }

        if (text.StartsWith("thirsty", StringComparison.Ordinal))
        {
            return "Status_Thirst.png";
        }

        if (text.StartsWith("fatigue", StringComparison.Ordinal) || text.StartsWith("tired", StringComparison.Ordinal))
        {
            return "Mood_Exhausted.png";
        }

        if (text.StartsWith("pain", StringComparison.Ordinal))
        {
            return "Mood_Pained.png";
        }

        if (text.StartsWith("out of breath", StringComparison.Ordinal))
        {
            return "Status_DifficultyBreathing.png";
        }

        if (text.StartsWith("panic", StringComparison.Ordinal))
        {
            return "Mood_Panicked.png";
        }

        if (text.StartsWith("stress", StringComparison.Ordinal))
        {
            return "Mood_Stressed.png";
        }

        if (text.StartsWith("queasy", StringComparison.Ordinal) ||
            text.StartsWith("nause", StringComparison.Ordinal))
        {
            return "Mood_Nauseous.png";
        }

        if (text.StartsWith("bad smell", StringComparison.Ordinal) ||
            text.StartsWith("noxious smell", StringComparison.Ordinal))
        {
            return "Mood_NoxiousSmell.png";
        }

        if (text.StartsWith("dead", StringComparison.Ordinal))
        {
            return "Mood_Dead.png";
        }

        return "moodle_guide.png";
    }

    public static bool TryResolveIconPath(string? gamePath, string iconFileName, out string iconPath)
    {
        iconPath = string.Empty;

        if (string.IsNullOrWhiteSpace(gamePath) || string.IsNullOrWhiteSpace(iconFileName))
        {
            return false;
        }

        var safeName = iconFileName.Trim();
        if (safeName.IndexOfAny(['\\', '/', ':']) >= 0 || !safeName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fromMood32 = Path.Combine(gamePath, "media", "ui", "Moodles", "32", safeName);
        if (File.Exists(fromMood32))
        {
            iconPath = fromMood32;
            return true;
        }

        var fromMoodRoot = Path.Combine(gamePath, "media", "ui", "Moodles", safeName);
        if (File.Exists(fromMoodRoot))
        {
            iconPath = fromMoodRoot;
            return true;
        }

        var fromUiRoot = Path.Combine(gamePath, "media", "ui", safeName);
        if (File.Exists(fromUiRoot))
        {
            iconPath = fromUiRoot;
            return true;
        }

        return false;
    }
}
