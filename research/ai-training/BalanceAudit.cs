using System;
using System.Globalization;
using SDSI.AI;

// Runs the actual balance rules without loading Unity or the game simulation.
public static class BalanceAudit
{
    private static string Number(double value) { return value.ToString("F6", CultureInfo.InvariantCulture); }
    public static void Main()
    {
        Console.WriteLine("{\"gameRuntimeExecuted\":false,\"fixedCapacity\":2000,\"preparation\":1,\"tiers\":[");
        for (int tier = 0; tier < 4; tier++)
        {
            double months = AIRules.Months[tier];
            double overall = AIRules.Score(tier, 2000 * months, months, 1);
            double domain = AIRules.DomainScore(overall, 3, 1);
            double potential = AIRules.DomainScore(AIRules.Ceilings[tier], 3, 1);
            double reference = AIRules.Score(tier, AIRules.ReferenceCapacity[tier] * months, months, 1);
            Console.WriteLine("{\"name\":\"" + AIRules.TierNames[tier] + "\",\"months\":" + Number(months) +
                ",\"overallAt2000\":" + Number(overall) + ",\"advancedDomainAt2000\":" + Number(domain) +
                ",\"referenceComputeMonths\":" + Number(AIRules.ReferenceCapacity[tier] * months) +
                ",\"referenceOverall\":" + Number(reference) +
                ",\"maximumRCKMultiplier\":" + Number(1 + AIRules.Assistance(potential, potential, potential)) +
                ",\"corporateCapacityPerCompany\":" + Number(AIRules.InferenceDemand(tier, 1)) + "}" + (tier < 3 ? "," : ""));
        }
        double prepared = AIRules.DomainScore(AIRules.Score(0, 4000, 2, 1), 3, 1);
        double unprepared = AIRules.DomainScore(AIRules.Score(0, 4000, 2, 0), 3, 0);
        double sample = AIRules.DomainScore(AIRules.Score(0, 4000, 2, 1), 3, 1);
        Console.WriteLine("],\"lowestPreparationDomainFraction\":" + Number(unprepared / prepared) +
            ",\"RCKAtReferenceMultiplier\":" + Number(1 + AIRules.Assistance(sample, sample, sample)) +
            ",\"RCKVisionSpeechAtReferenceMultiplier\":" + Number(1 + AIRules.Assistance(sample, sample, sample)) +
            ",\"referenceComputeRatio1TTo60B\":" + Number(AIRules.ReferenceCapacity[3] * AIRules.Months[3] / (AIRules.ReferenceCapacity[0] * AIRules.Months[0])) + "}");
    }
}
