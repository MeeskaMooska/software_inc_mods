using System;

namespace SDSI.AI
{
    public sealed class TrainingConfig
    {
        public int Architecture;
        public int Quantization;
        public int Context;
        public int Dataset;
        public int Focus;
        public int Mode;
        public TrainingConfig Copy()
        { return (TrainingConfig)MemberwiseClone(); }
    }

    // Pure rules shared by the simulation and regression tests.
    public static class ExpansionRules
    {
        public static readonly double[] Allocations = { 0.01, 0.05, 0.10, 0.25, 0.50, 1.0 };
        // Installed TimeOfDay bills server power at 0.03 * watts * utility
        // price each hour. Physical server wattage scales with group demand.
        public static double TrainingElectricity(double watts, double allocation, double price, int days)
        { return Math.Max(0, watts) * AIRules.Clamp(allocation, 0, 1) * Math.Max(0, price) * 0.03 * 24 * Math.Max(1, days); }
        public static readonly int[] TierYears = { 2020, 2021, 2022, 2024, 2018, 2019 };
        public static bool RivalResearchOpen(int year, int now, int firstPublicRelease, int days)
        {
            return year >= 2018 && firstPublicRelease >= 0 &&
                (long)now - firstPublicRelease >= 6L * 1440 * Math.Max(1, days);
        }
        // Benchmarks produce discovery independently of a publisher's genre
        // experience. Equal rules apply to every provider; this is not sales.
        public static double Discovery(double score, double frontier)
        {
            double absolute = AIRules.Clamp(score / 100, 0, 1);
            double relative = AIRules.Clamp(score / Math.Max(1, frontier), 0, 1);
            return AIRules.Clamp(0.75 * absolute * absolute * relative * relative, 0, 0.75);
        }
        public static readonly int[] TierOrder = { 4, 5, 0, 1, 2, 3 };
        public static readonly string[] Architectures = { "Dense", "Sparse mixture of experts (2021+)" };
        public static readonly string[] Quantizations = { "Full precision", "8-bit: cheaper serving, -3% capability", "4-bit: cheapest serving, -8% capability (2022+)" };
        public static readonly string[] Contexts = { "8K context", "32K context (2020+)", "128K context (2023+)" };
        public static readonly string[] Datasets = { "General licensed corpus", "Curated code corpus", "Curated enterprise corpus", "Curated multimodal corpus" };
        public static readonly string[] Focuses = { "Balanced", "Reasoning focus", "Coding focus", "Knowledge focus", "Vision focus", "Speech focus" };
        public static readonly double[] DomainCost = { 1, 0.9, 0.7, 1.2, 1.1 };
        public static int Index(int value, int max) { return Math.Max(0, Math.Min(max, value)); }
        public static TrainingConfig Sanitize(TrainingConfig c)
        {
            if (c == null) c = new TrainingConfig();
            c.Architecture = Index(c.Architecture, 1); c.Quantization = Index(c.Quantization, 2);
            c.Context = Index(c.Context, 2); c.Dataset = Index(c.Dataset, 3);
            c.Focus = Index(c.Focus, 5); c.Mode = Index(c.Mode, 1); return c;
        }
        public static string LockedReason(int tier, TrainingConfig c, int year)
        {
            tier = AIRules.Tier(tier); Sanitize(c);
            if (year < TierYears[tier]) return "This size unlocks in " + TierYears[tier] + ".";
            if (c.Architecture == 1 && year < 2021) return "Sparse experts unlock in 2021.";
            if (c.Quantization == 1 && year < 2019) return "8-bit serving unlocks in 2019.";
            if (c.Quantization == 2 && year < 2022) return "4-bit serving unlocks in 2022.";
            if (c.Context == 1 && year < 2020) return "32K context unlocks in 2020.";
            if (c.Context == 2 && year < 2023) return "128K context unlocks in 2023.";
            if (c.Dataset == 1 && year < 2019) return "Code datasets unlock in 2019.";
            if (c.Dataset == 3 && year < 2021) return "Multimodal datasets unlock in 2021.";
            return "";
        }
        public static double Window(int tier, TrainingConfig c)
        { return AIRules.Months[AIRules.Tier(tier)] * (c.Mode == 1 ? 0.5 : 1); }
        public static double NativeProgress(int tier, TrainingConfig c, double elapsed)
        { return elapsed + 0.0000001 >= Window(tier, c) ? 1 : 0; }
        public static bool Advance(int tier, TrainingConfig c, int days, bool running, double capacity, ref double elapsed, ref double compute)
        {
            if (!running) return false;
            double step = Math.Min(AIRules.MonthStep(days), Math.Max(0, Window(tier, c) - elapsed));
            elapsed += step; compute += AIRules.Clamp(capacity, 0, 100000000) * step;
            return elapsed + 0.0000001 >= Window(tier, c);
        }
        public static double ArchitectureTraining(TrainingConfig c) { return c.Architecture == 1 ? 0.45 : 1; }
        public static double QuantQuality(TrainingConfig c) { return c.Quantization == 1 ? 0.97 : c.Quantization == 2 ? 0.92 : 1; }
        public static double InferenceFactor(TrainingConfig c)
        {
            double precision = c.Quantization == 1 ? 0.65 : c.Quantization == 2 ? 0.40 : 1;
            double context = c.Context == 1 ? 1.6 : c.Context == 2 ? 3 : 1;
            return precision * context * (c.Architecture == 1 ? 0.35 : 1);
        }
        public static double Memory(int tier, TrainingConfig c)
        {
            // Capacity reservation is the game's memory proxy; not GPU bytes.
            double precision = c.Quantization == 1 ? 0.6 : c.Quantization == 2 ? 0.35 : 1;
            return 25 * AIRules.ServingFactors[AIRules.Tier(tier)] * precision * (c.Architecture == 1 ? 1.25 : 1);
        }
        public static double DatasetFee(int tier, TrainingConfig c, int[] counts)
        {
            int modules = 0; for (int i = 0; i < 5; i++) if (counts[i] > 0) modules++;
            return 5000 * Math.Max(0.1, AIRules.Parameters[AIRules.Tier(tier)] / 60) *
                (c.Dataset == 0 ? 1 : 2) * (0.5 + 0.5 * modules) * (c.Mode == 1 ? 0.35 : 1);
        }
        public static double[] Shares(int[] counts, TrainingConfig c)
        {
            double[] shares = new double[5]; double total = 0;
            for (int i = 0; i < 5; i++)
            {
                if (counts[i] <= 0) continue;
                shares[i] = DomainCost[i] * (1 + 0.12 * Math.Max(0, counts[i] - 1)) * (c.Focus == i + 1 ? 3 : 1);
                total += shares[i];
            }
            for (int i = 0; i < 5; i++) shares[i] = total > 0 ? shares[i] / total : 0;
            return shares;
        }
        public static double[] Scores(int tier, TrainingConfig c, double compute, double elapsed, double preparation, int[] counts, double[] seed)
        {
            tier = AIRules.Tier(tier); double[] shares = Shares(counts, c); double[] scores = new double[5];
            double coverage = Math.Pow(AIRules.Clamp(elapsed / Window(tier, c), 0, 1), 0.7);
            for (int i = 0; i < 5; i++)
            {
                if (counts[i] <= 0) continue;
                double complexity = 1 + 0.12 * Math.Max(0, counts[i] - 1);
                double reference = AIRules.ReferenceCapacity[tier] * Window(tier, c) * DomainCost[i] * complexity / 2.6 * ArchitectureTraining(c);
                double learned = AIRules.ComputeLearning(Math.Max(0, compute) * shares[i] / Math.Max(0.001, reference));
                double data = c.Dataset == 0 ? 1 : (c.Dataset == 1 && i == 1) || (c.Dataset == 2 && (i == 0 || i == 2)) || (c.Dataset == 3 && i >= 3) ? 1.08 : 0.94;
                double ceiling = AIRules.Ceilings[tier] * (c.Architecture == 1 ? 0.96 : 1);
                double contextBenefit = (i == 0 || i == 2) ? 1 + 0.03 * c.Context : 1;
                double potential = AIRules.DomainScore(ceiling, counts[i], preparation) * data * contextBenefit;
                double value = potential * learned * (0.88 + 0.12 * AIRules.Clamp(preparation, 0, 1)) * coverage;
                if (c.Mode == 1 && seed != null && seed.Length == 5)
                {
                    // Warm start preserves 90% of the checkpoint, but an idle or
                    // unfunded run cannot improve it; all gains require compute.
                    double inherited = AIRules.Clamp(seed[i], 0, 100) * 0.9;
                    value = inherited + Math.Max(0, potential - inherited) * learned * coverage * (0.88 + 0.12 * AIRules.Clamp(preparation, 0, 1));
                }
                scores[i] = AIRules.Clamp(value * QuantQuality(c), 0, 100);
            }
            return scores;
        }
        public static double Bonus(double[] scores, string spec, int phase, bool imageGeneration, bool audioGeneration)
        {
            // 0 design; 1 code; 2 art; 3 fixes/ports. Understanding is distinct
            // from optional generated art or generated audio.
            double r = scores[0], code = scores[1], k = scores[2], v = scores[3], s = scores[4];
            double strength;
            if (phase == 0) strength = spec == "Audio" ? 0.45 * r + 0.25 * k + 0.30 * s : (spec == "2D" || spec == "3D") ? 0.45 * r + 0.25 * k + 0.30 * v : 0.60 * r + 0.40 * k;
            else if (phase == 3) strength = 0.15 * r + 0.75 * code + 0.10 * k;
            else if (phase == 2) strength = spec == "Audio" ? (audioGeneration ? s : 0) : (spec == "2D" || spec == "3D") ? (imageGeneration ? v : 0) : 0;
            else if (spec == "Audio") strength = 0.35 * code + 0.65 * s;
            else if (spec == "2D" || spec == "3D") strength = 0.35 * code + 0.65 * v;
            else if (spec == "Algorithm") strength = 0.25 * r + 0.35 * code + 0.40 * k;
            else strength = 0.20 * r + 0.70 * code + 0.10 * k;
            return 1.5 * Math.Pow(AIRules.Clamp(strength / 100, 0, 1), 2);
        }
        public static double CorporateDemand(int tier, TrainingConfig c, int[] counts, double activeEmployees)
        {
            if (activeEmployees <= 0) return 0;
            int modules = 0; for (int i = 0; i < 5; i++) if (counts[i] > 0) modules++;
            return AIRules.ServingFactors[AIRules.Tier(tier)] * InferenceFactor(c) * activeEmployees * 10 * (1 + 0.15 * Math.Max(0, modules - 1));
        }
        public static double SubscriptionPrice(int tier, double score, double tokens, TrainingConfig c)
        {
            // Base monthly access plus metered usage, in game token units.
            return (2 + 0.12 * AIRules.ServingFactors[AIRules.Tier(tier)] * AIRules.Clamp(score / 100, 0, 1) +
                0.015 * Math.Max(0, tokens) * InferenceFactor(c));
        }
        public static int Choose(double[] potential, double[] fractions)
        {
            int best = -1; double value = 0;
            for (int i = 0; i < Math.Min(potential.Length, fractions.Length); i++)
            {
                double delivered = AIRules.DeliveredAssistance(potential[i], fractions[i]);
                if (delivered > value) { best = i; value = delivered; }
            }
            return best;
        }
        public static int ChooseProvider(double[] potential, double[] fractions, bool locked, int requested)
        {
            if (locked) return requested >= 0 && requested < Math.Min(potential.Length, fractions.Length) ? requested : -1;
            return Choose(potential, fractions);
        }
    }
}
