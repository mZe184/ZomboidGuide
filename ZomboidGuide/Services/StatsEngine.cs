using System;
using System.Collections.Generic;
using System.Linq;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class StatsEngine
{
    public StatsSummary BuildSummary(GameSnapshot? latestSnapshot, IReadOnlyList<GameSnapshot> history)
    {
        if (latestSnapshot is null)
        {
            return new StatsSummary();
        }

        var normalizedHistory = history.Count == 0
            ? [latestSnapshot]
            : history.OrderBy(snapshot => snapshot.TimestampUtc).ToList();

        var killsTotal = latestSnapshot.ZombieKillsTotal ?? 0;
        var killsPerHour = CalculateKillsPerHour(normalizedHistory, killsTotal);
        var dangerIndex = Math.Clamp(latestSnapshot.DangerIndex, 0, 100);
        var dangerLabel = ResolveDangerLabel(dangerIndex);

        var fatigueTrend = Downsample(normalizedHistory.Select(snapshot => snapshot.Fatigue), 24);
        var tirednessTrend = Downsample(normalizedHistory.Select(snapshot => snapshot.Tiredness), 24);
        var enduranceTrend = Downsample(normalizedHistory.Select(snapshot => snapshot.Endurance), 24);

        return new StatsSummary
        {
            KillsTotal = killsTotal,
            KillsPerHour = killsPerHour,
            DangerIndex = dangerIndex,
            DangerLabel = dangerLabel,
            FatigueTrend = fatigueTrend,
            TirednessTrend = tirednessTrend,
            EnduranceTrend = enduranceTrend,
            TopIssues = ResolveTopIssues(latestSnapshot),
        };
    }

    private static double CalculateKillsPerHour(IReadOnlyList<GameSnapshot> history, int fallbackKillsTotal)
    {
        var killSnapshots = history
            .Where(snapshot => snapshot.ZombieKillsTotal.HasValue)
            .ToList();

        if (killSnapshots.Count < 2)
        {
            return fallbackKillsTotal;
        }

        var first = killSnapshots.First();
        var last = killSnapshots.Last();
        var firstKills = first.ZombieKillsTotal ?? 0;
        var lastKills = last.ZombieKillsTotal ?? firstKills;
        var deltaKills = Math.Max(0, lastKills - firstKills);
        var hours = Math.Max((last.TimestampUtc - first.TimestampUtc).TotalHours, 1.0 / 60.0);

        return deltaKills / hours;
    }

    private static string ResolveDangerLabel(int dangerIndex)
    {
        if (dangerIndex >= 80)
        {
            return "RED";
        }

        if (dangerIndex >= 60)
        {
            return "ORANGE";
        }

        if (dangerIndex >= 40)
        {
            return "YELLOW";
        }

        return "GREEN";
    }

    private static IReadOnlyList<double> Downsample(IEnumerable<double> source, int targetSamples)
    {
        var values = source
            .Select(value => Math.Clamp(value, 0.0, 1.0))
            .ToList();

        if (values.Count <= targetSamples)
        {
            return values;
        }

        var result = new List<double>(targetSamples);
        for (var i = 0; i < targetSamples; i++)
        {
            var index = (int)Math.Round(i * (values.Count - 1.0) / Math.Max(1, targetSamples - 1));
            result.Add(values[index]);
        }

        return result;
    }

    private static IReadOnlyList<string> ResolveTopIssues(GameSnapshot snapshot)
    {
        var issues = new List<string>();
        if (snapshot.Tiredness >= 0.7 || snapshot.Fatigue >= 0.7)
        {
            issues.Add("High fatigue/tiredness");
        }

        if (snapshot.Endurance <= 0.3)
        {
            issues.Add("Low endurance");
        }

        if (snapshot.Hunger >= 0.7 || snapshot.Thirst >= 0.7)
        {
            issues.Add("Food/water critical");
        }

        if (snapshot.Panic >= 0.7 || snapshot.Stress >= 0.7)
        {
            issues.Add("High panic/stress");
        }

        if (snapshot.Pain >= 0.45)
        {
            issues.Add("Pain is elevated");
        }

        if (snapshot.OutOfBreath >= 0.35)
        {
            issues.Add("Out of breath");
        }

        if (snapshot.Queasy >= 0.3)
        {
            issues.Add("Queasy / sickness");
        }

        foreach (var issue in snapshot.Issues)
        {
            if (!issues.Contains(issue, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(issue);
            }
        }

        if (issues.Count == 0)
        {
            issues.Add("No major issues");
        }

        return issues;
    }
}
