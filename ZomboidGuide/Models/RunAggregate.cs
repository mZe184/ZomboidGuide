using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed class RunAggregate
{
    public RunMeta Meta { get; set; } = new();

    public int SampleCount { get; set; }

    public DateTimeOffset FirstSnapshotUtc { get; set; }

    public DateTimeOffset LastSnapshotUtc { get; set; }

    public int? FirstKillsTotal { get; set; }

    public int? LastKillsTotal { get; set; }

    public double? FirstInGameSurvivedHours { get; set; }

    public double? LastInGameSurvivedHours { get; set; }

    public double DangerSum { get; set; }

    public double FatigueSum { get; set; }

    public double TirednessSum { get; set; }

    public double EstimatedSleepHours { get; set; }

    public double InGameHoursPerRealHour { get; set; } = 24.0;

    public DateTimeOffset? LastObservedTimestampUtc { get; set; }

    public int? LastObservedKillsTotal { get; set; }

    public double? LastObservedInGameSurvivedHours { get; set; }

    public List<RunDailyStats> DailyStats { get; set; } = [];

    public double AverageDanger => SampleCount <= 0 ? 0.0 : DangerSum / SampleCount;

    public double AverageFatigue => SampleCount <= 0 ? 0.0 : FatigueSum / SampleCount;

    public double AverageTiredness => SampleCount <= 0 ? 0.0 : TirednessSum / SampleCount;
}
