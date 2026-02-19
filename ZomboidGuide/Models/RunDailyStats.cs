using System;

namespace ZomboidGuide.Models;

public sealed class RunDailyStats
{
    public int DayIndex { get; set; }

    public int SampleCount { get; set; }

    public double DangerSum { get; set; }

    public double FatigueSum { get; set; }

    public double TirednessSum { get; set; }

    public int? FirstKillsTotal { get; set; }

    public int? LastKillsTotal { get; set; }

    public DateTimeOffset FirstSnapshotUtc { get; set; }

    public DateTimeOffset LastSnapshotUtc { get; set; }

    public double SleepHours { get; set; }

    public double AverageDanger => SampleCount <= 0 ? 0.0 : DangerSum / SampleCount;

    public double AverageFatigue => SampleCount <= 0 ? 0.0 : FatigueSum / SampleCount;

    public double AverageTiredness => SampleCount <= 0 ? 0.0 : TirednessSum / SampleCount;

    public int KillsGained => FirstKillsTotal.HasValue && LastKillsTotal.HasValue
        ? Math.Max(0, LastKillsTotal.Value - FirstKillsTotal.Value)
        : 0;
}
