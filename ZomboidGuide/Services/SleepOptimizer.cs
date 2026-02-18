using System;
using System.Collections.Generic;
using System.Linq;
using ZomboidGuide.Models;

namespace ZomboidGuide.Services;

public sealed class SleepOptimizer
{
    public SleepRecommendation BuildRecommendation(GameSnapshot? latestSnapshot, IReadOnlyList<GameSnapshot> history)
    {
        if (latestSnapshot is null)
        {
            return new SleepRecommendation
            {
                Action = SleepAction.KeepGoing,
                Confidence = 0.0,
                ReasonCodes = ["NO_DATA"],
            };
        }

        var samples = history.Count == 0 ? [latestSnapshot] : history;
        var avgFatigue = samples.Average(snapshot => snapshot.Fatigue);
        var avgTiredness = samples.Average(snapshot => snapshot.Tiredness);
        var avgEndurance = samples.Average(snapshot => snapshot.Endurance);
        var avgHunger = samples.Average(snapshot => snapshot.Hunger);
        var avgThirst = samples.Average(snapshot => snapshot.Thirst);
        var avgPanic = samples.Average(snapshot => snapshot.Panic);
        var avgStress = samples.Average(snapshot => snapshot.Stress);

        var reasons = new List<string>();

        var hungerOrThirstCritical = avgHunger >= 0.85 || avgThirst >= 0.85;
        if (hungerOrThirstCritical)
        {
            reasons.Add("HUNGER_OR_THIRST_CRITICAL");
            return Build(SleepAction.EatDrinkFirst, ComputeConfidence(avgHunger, avgThirst), reasons);
        }

        var panicOrStressCritical = avgPanic >= 0.85 || avgStress >= 0.85;
        if (panicOrStressCritical)
        {
            reasons.Add("PANIC_OR_STRESS_CRITICAL");
            return Build(SleepAction.SecureAreaFirst, ComputeConfidence(avgPanic, avgStress), reasons);
        }

        var veryTiredOrFatigued = avgTiredness >= 0.85 || avgFatigue >= 0.85;
        if (veryTiredOrFatigued)
        {
            reasons.Add("TIRED_OR_FATIGUED_CRITICAL");
            return Build(SleepAction.SleepNow, ComputeConfidence(avgTiredness, avgFatigue), reasons);
        }

        var highTiredOrFatigued = avgTiredness >= 0.65 || avgFatigue >= 0.65;
        if (highTiredOrFatigued)
        {
            reasons.Add("TIRED_OR_FATIGUED_HIGH");
            return Build(SleepAction.SleepSoon, ComputeConfidence(avgTiredness, avgFatigue), reasons);
        }

        if (avgEndurance <= 0.2)
        {
            reasons.Add("ENDURANCE_LOW");
            return Build(SleepAction.Rest, ComputeConfidence(1.0 - avgEndurance), reasons);
        }

        reasons.Add("STABLE");
        return Build(SleepAction.KeepGoing, 0.35, reasons);
    }

    private static SleepRecommendation Build(SleepAction action, double confidence, IReadOnlyList<string> reasonCodes)
    {
        return new SleepRecommendation
        {
            Action = action,
            Confidence = Math.Clamp(confidence, 0.0, 1.0),
            ReasonCodes = reasonCodes,
        };
    }

    private static double ComputeConfidence(params double[] values)
    {
        if (values.Length == 0)
        {
            return 0.0;
        }

        return Math.Clamp(values.Max(), 0.0, 1.0);
    }
}
