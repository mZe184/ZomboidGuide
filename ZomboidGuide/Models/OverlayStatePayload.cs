using System;
using System.Collections.Generic;

namespace ZomboidGuide.Models;

public sealed record OverlayStatePayload
{
    public string LabelKillsTotal { get; init; } = "Kills Total";

    public string LabelKillsThisSession { get; init; } = "Kills This Session";

    public string LabelKillsPerHour { get; init; } = "Kills / Hour (played)";

    public string LabelTimeSurvived { get; init; } = "Time Survived";

    public string LabelDanger { get; init; } = "Danger Level";

    public string LabelFatigue { get; init; } = "Fatigue";

    public string LabelTiredness { get; init; } = "Tiredness";

    public string LabelEndurance { get; init; } = "Endurance";

    public string LabelHunger { get; init; } = "Hunger";

    public string LabelThirst { get; init; } = "Thirst";

    public string LabelPain { get; init; } = "Pain";

    public string LabelOutOfBreath { get; init; } = "Out of Breath";

    public string LabelQueasy { get; init; } = "Queasy";

    public string LabelMoodles { get; init; } = "Moodles";

    public string RunId { get; init; } = "run-unknown";

    public string WorldTime { get; init; } = "-";

    public int KillsTotal { get; init; }

    public int KillsThisSession { get; init; }

    public double KillsPerHour { get; init; }

    public string TimeSurvived { get; init; } = "0d 00h 00m";

    public int DangerIndex { get; init; }

    public string DangerLabel { get; init; } = "GRAY";

    public string DangerLabelText { get; init; } = "GRAY";

    public double Fatigue { get; init; }

    public double Tiredness { get; init; }

    public double Endurance { get; init; } = 1.0;

    public double Hunger { get; init; }

    public double Thirst { get; init; }

    public double Pain { get; init; }

    public double OutOfBreath { get; init; }

    public double Queasy { get; init; }

    public IReadOnlyList<string> Moodles { get; init; } = Array.Empty<string>();

    public bool RotateSlides { get; init; } = true;

    public string SleepAction { get; init; } = "KEEP_GOING";

    public double SleepConfidence { get; init; }

    public IReadOnlyList<string> TopTodos { get; init; } = Array.Empty<string>();
}
