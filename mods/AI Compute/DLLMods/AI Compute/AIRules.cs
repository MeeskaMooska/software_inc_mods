using System;

namespace SDSI.AI
{
    // All quantities are game-balance units, not real FLOPs or ML benchmarks.
    public static class AIRules
    {
        public const string SoftwareName = "Artificial Intelligence";
        public const string Prefix = "SDSI.AI.";
        // Preserve 0.1 save indices; early-game tiers are appended.
        public static readonly string[] TierNames = { "60B parameters", "175B parameters", "600B parameters", "1T parameters", "1B specialist model", "7B parameters" };
        public static readonly string[] Domains = { "Reasoning", "Coding", "Knowledge", "Vision", "Speech" };
        public static readonly double[] Parameters = { 60, 175, 600, 1000, 1, 7 };
        public static readonly double[] Months = { 2, 4, 6, 9, 1, 1.5 };
        public static readonly double[] Ceilings = { 88, 94, 98, 100, 65, 78 };
        // Native groups already reach tens of thousands of capacity early on.
        // The former 100-unit specialist reference saturated at tiny allocations.
        public static readonly double[] ReferenceCapacity = { 200000, 800000, 3000000, 6000000, 10000, 50000 };
        public static readonly double[] ServingFactors = { 1, 3, 10, 18, 0.10, 0.35 };
        public static readonly double[] MonthlyLicense = { 1500, 4500, 15000, 30000, 150, 500 };

        public static int Tier(int value) { return Math.Max(0, Math.Min(TierNames.Length - 1, value)); }
        public static double Clamp(double value, double min, double max)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return min;
            return Math.Max(min, Math.Min(max, value));
        }
        public static double MonthStep(int daysPerMonth) { return 1.0 / (24.0 * Math.Max(1, daysPerMonth)); }
        public static bool AdvanceTraining(int tier, int daysPerMonth, bool running, double servedCapacity, ref double elapsed, ref double compute)
        {
            tier = Tier(tier);
            if (!running) return false;
            double increment = Math.Min(MonthStep(daysPerMonth), Math.Max(0, Months[tier] - elapsed));
            elapsed += increment;
            compute += Clamp(servedCapacity, 0, 100000000) * increment;
            return elapsed + 0.0000001 >= Months[tier];
        }

        // A fixed active training window per tier. Outages waste the window;
        // explicit player pause suspends it. Additional capacity improves quality.
        public static double Score(int tier, double computeMonths, double elapsedMonths, double preparation)
        {
            tier = Tier(tier);
            double ratio = Math.Max(0, computeMonths) / (ReferenceCapacity[tier] * Months[tier]);
            double compute = ComputeLearning(ratio);
            double coverage = Math.Pow(Clamp(elapsedMonths / Months[tier], 0, 1), 0.7);
            // Preparation cannot make an untrained model useful on its own.
            return Ceilings[tier] * compute * (0.88 + 0.12 * Clamp(preparation, 0, 1)) * coverage;
        }
        // 50% at reference compute, 80% at 4x, 91% at 10x. Unlike
        // the old exponential, extra servers remain useful above reference.
        public static double ComputeLearning(double ratio)
        { ratio = Clamp(ratio, 0, 100000000); return ratio / (1 + ratio); }
        public static double DomainScore(double overall, int featureCount, double design)
        {
            if (featureCount <= 0) return 0;
            return Clamp(overall * (0.90 + 0.07 * Math.Min(2, featureCount - 1)) * (0.95 + 0.05 * Clamp(design, 0, 1)), 0, 100);
        }
        public static double Assistance(double reasoning, double coding, double knowledge)
        {
            double strength = Clamp((0.25 * reasoning + 0.55 * coding + 0.20 * knowledge) / 100.0, 0, 1);
            return 1.5 * strength * strength; // Up to +150% work, or 2.5x total.
        }
        public static double AssistProgress(double current, double previous, double limit, double boost)
        {
            double bonus = Math.Max(0, current - previous) * Clamp(boost, 0, 1.5);
            // Never undo native work when a target is below an existing value.
            return Math.Max(current, Math.Min(limit, current + bonus));
        }
        public static double NativeQuality(double score)
        {
            // Native finished-product quality is q*(1+q)/2; invert it.
            double target = Clamp(score / 100.0, 0, 1);
            return (Math.Sqrt(1 + 8 * target) - 1) / 2;
        }
        public static double LicenseFee(int tier, double score)
        {
            return MonthlyLicense[Tier(tier)] * (0.25 + 0.75 * Clamp(score / 100, 0, 1));
        }
        public static double InferenceDemand(int tier, int companies)
        { return 100 * ServingFactors[Tier(tier)] * Math.Max(0, companies); }
        public static double DeliveredAssistance(double boost, double servingFraction)
        { return Clamp(boost, 0, 1.5) * Clamp(servingFraction, 0, 1); }
        public static double AbstractHostingFee(int tier, int companies)
        { return InferenceDemand(tier, companies) * 0.2; }
        public static int AdvanceReleaseMinutes(int release, int now, double savedMonths, int daysPerMonth)
        {
            if (savedMonths <= 0) return release;
            double minutes = savedMonths * 1440 * Math.Max(1, daysPerMonth);
            return Math.Max(now, release - (int)Math.Min(Int32.MaxValue, minutes));
        }
    }
}
