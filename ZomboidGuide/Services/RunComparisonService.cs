using System;
using System.Collections.Generic;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class RunComparisonService
{
    public RunComparisonResult Compare(RunAggregate runA, RunAggregate runB)
    {
        var killsTotalA = ResolveTotalKills(runA);
        var killsTotalB = ResolveTotalKills(runB);
        var survivedHoursA = ResolveSurvivedHours(runA);
        var survivedHoursB = ResolveSurvivedHours(runB);
        var killsPerDayA = ComputeKillsPerDay(runA, killsTotalA, survivedHoursA);
        var killsPerDayB = ComputeKillsPerDay(runB, killsTotalB, survivedHoursB);
        var avgDangerA = runA.AverageDanger;
        var avgDangerB = runB.AverageDanger;
        var avgFatigueA = runA.AverageFatigue * 100.0;
        var avgFatigueB = runB.AverageFatigue * 100.0;
        var avgTirednessA = runA.AverageTiredness * 100.0;
        var avgTirednessB = runB.AverageTiredness * 100.0;
        var sleepHoursA = runA.EstimatedSleepHours;
        var sleepHoursB = runB.EstimatedSleepHours;

        var metrics = new List<RunComparisonMetric>
        {
            BuildHigherIsBetter("kills_total", killsTotalA, killsTotalB),
            BuildHigherIsBetter("survived_time", survivedHoursA, survivedHoursB),
            BuildHigherIsBetter("kills_per_day", killsPerDayA, killsPerDayB),
            BuildLowerIsBetter("avg_danger", avgDangerA, avgDangerB),
            BuildLowerIsBetter("avg_fatigue", avgFatigueA, avgFatigueB),
            BuildLowerIsBetter("avg_tiredness", avgTirednessA, avgTirednessB),
            BuildTargetIsBetter("sleep_hours", sleepHoursA, sleepHoursB, 7.5),
        };

        return new RunComparisonResult
        {
            RunA = runA.Meta,
            RunB = runB.Meta,
            ComparedUtc = DateTimeOffset.UtcNow,
            Metrics = metrics,
        };
    }

    private static RunComparisonMetric BuildHigherIsBetter(string key, double runA, double runB)
    {
        var isTie = Math.Abs(runA - runB) < 0.0001;
        return new RunComparisonMetric
        {
            Key = key,
            RunAValue = runA,
            RunBValue = runB,
            Delta = runB - runA,
            IsRunABetter = runA > runB,
            IsTie = isTie,
        };
    }

    private static RunComparisonMetric BuildLowerIsBetter(string key, double runA, double runB)
    {
        var isTie = Math.Abs(runA - runB) < 0.0001;
        return new RunComparisonMetric
        {
            Key = key,
            RunAValue = runA,
            RunBValue = runB,
            Delta = runB - runA,
            IsRunABetter = runA < runB,
            IsTie = isTie,
        };
    }

    private static RunComparisonMetric BuildTargetIsBetter(string key, double runA, double runB, double target)
    {
        var distA = Math.Abs(runA - target);
        var distB = Math.Abs(runB - target);
        var isTie = Math.Abs(distA - distB) < 0.0001;
        return new RunComparisonMetric
        {
            Key = key,
            RunAValue = runA,
            RunBValue = runB,
            Delta = runB - runA,
            IsRunABetter = distA < distB,
            IsTie = isTie,
        };
    }

    private static double ResolveTotalKills(RunAggregate aggregate)
    {
        return aggregate.LastKillsTotal
               ?? aggregate.FirstKillsTotal
               ?? 0;
    }

    private static double ResolveSurvivedHours(RunAggregate aggregate)
    {
        if (aggregate.LastInGameSurvivedHours.HasValue)
        {
            return Math.Max(0.0, aggregate.LastInGameSurvivedHours.Value);
        }

        if (aggregate.FirstInGameSurvivedHours.HasValue && aggregate.LastInGameSurvivedHours.HasValue)
        {
            return Math.Max(0.0, aggregate.LastInGameSurvivedHours.Value - aggregate.FirstInGameSurvivedHours.Value);
        }

        var snapshotHours = (aggregate.LastSnapshotUtc - aggregate.FirstSnapshotUtc).TotalHours;
        return Math.Max(0.0, snapshotHours);
    }

    private static double ComputeKillsPerDay(RunAggregate aggregate, double totalKills, double survivedHours)
    {
        if (survivedHours > 0.0)
        {
            return totalKills * 24.0 / survivedHours;
        }

        var killsA = aggregate.FirstKillsTotal ?? aggregate.LastKillsTotal ?? 0;
        var killsB = aggregate.LastKillsTotal ?? killsA;
        var killsDelta = Math.Max(0, killsB - killsA);
        var timeSpan = aggregate.LastSnapshotUtc - aggregate.FirstSnapshotUtc;
        var days = Math.Max(timeSpan.TotalDays, 1.0 / 24.0);
        return killsDelta / days;
    }
}
