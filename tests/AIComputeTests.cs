using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Xml;
using SDSI.AI;
using Tyd;

public static class AIComputeTests
{
    private static int checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        checks++;
    }
    private static void Near(double actual, double expected, string name)
    { Check(Math.Abs(actual - expected) < 0.000001, name); }
    public static int Main(string[] args)
    {
        try
        {
            string managed = args[0];
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs request)
            {
                string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            TestTraining();
            TestExpansion();
            TestUIRecovery();
            TestData(args[1]);
            Console.WriteLine("PASS: " + checks + " checks. Game runtime and UI were not executed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
    private static void TestTraining()
    {
        foreach (int days in new int[] { 1, 2, 5, 30 })
        {
            for (int tier = 0; tier < 4; tier++)
            {
                double lowTime = 0, lowCompute = 0, highTime = 0, highCompute = 0;
                double zeroTime = 0, zeroCompute = 0;
                int hours = (int)(AIRules.Months[tier] * 24 * days);
                for (int hour = 0; hour < hours; hour++)
                {
                    bool lowDone = AIRules.AdvanceTraining(tier, days, true, AIRules.ReferenceCapacity[tier] * 0.1, ref lowTime, ref lowCompute);
                    bool highDone = AIRules.AdvanceTraining(tier, days, true, AIRules.ReferenceCapacity[tier] * 2, ref highTime, ref highCompute);
                    bool zeroDone = AIRules.AdvanceTraining(tier, days, true, 0, ref zeroTime, ref zeroCompute);
                    Check(lowDone == highDone && highDone == zeroDone, "compute cannot shorten training");
                    Check(lowDone == (hour == hours - 1), "completion occurs at tier deadline");
                }
                Near(lowTime, AIRules.Months[tier], "tier duration");
                Near(highTime, lowTime, "same duration with more servers");
                // Normalize large capacity-month totals before comparing;
                // repeated hourly additions carry floating-point roundoff.
                Near(highCompute / (AIRules.ReferenceCapacity[tier] * 2 * AIRules.Months[tier]), 1, "integrated actual compute");
                double lowScore = AIRules.Score(tier, lowCompute, lowTime, 1);
                double highScore = AIRules.Score(tier, highCompute, highTime, 1);
                Check(highScore > lowScore * 4, "compute strongly controls capability");
                Near(AIRules.Score(tier, zeroCompute, zeroTime, 1), 0, "outages produce no capability");
                double beforeTime = highTime, beforeCompute = highCompute;
                AIRules.AdvanceTraining(tier, days, true, 50000, ref highTime, ref highCompute);
                Near(highCompute, beforeCompute, "no compute after completed window");
                Near(highTime, beforeTime, "no excess elapsed time");
            }
        }
        double elapsed = 0, compute = 0;
        AIRules.AdvanceTraining(0, 1, false, 5000, ref elapsed, ref compute);
        Near(elapsed, 0, "explicit pause stops timer"); Near(compute, 0, "pause stops compute");
        AIRules.AdvanceTraining(0, 1, true, 1000, ref elapsed, ref compute);
        AIRules.AdvanceTraining(0, 1, true, 3000, ref elapsed, ref compute);
        Near(compute, 4000.0 / 24, "changing capacity accumulates rather than replacing history");
        for (int tier = 0; tier < 4; tier++)
        {
            double previous = -1;
            for (int amount = 0; amount <= 100; amount++)
            {
                double score = AIRules.Score(tier, AIRules.ReferenceCapacity[tier] * AIRules.Months[tier] * amount / 10, AIRules.Months[tier], 1);
                Check(score >= previous && score <= AIRules.Ceilings[tier], "monotonic capped compute score");
                previous = score;
                double q = AIRules.NativeQuality(score);
                Near(q * (1 + q) / 2, score / 100, "native quality preserves benchmark");
            }
            double full = AIRules.Score(tier, AIRules.ReferenceCapacity[tier] * AIRules.Months[tier], AIRules.Months[tier], 1);
            double unprepared = AIRules.Score(tier, AIRules.ReferenceCapacity[tier] * AIRules.Months[tier], AIRules.Months[tier], 0);
            Near(unprepared / full, 0.88, "staff preparation has limited influence");
            if (tier > 0)
            {
                Check(AIRules.Months[tier] > AIRules.Months[tier - 1], "larger model trains longer");
                Check(AIRules.ReferenceCapacity[tier] > AIRules.ReferenceCapacity[tier - 1], "larger model needs more capacity");
                Check(AIRules.ServingFactors[tier] > AIRules.ServingFactors[tier - 1], "larger model costs more to serve");
                Check(AIRules.Ceilings[tier] > AIRules.Ceilings[tier - 1], "larger model has higher potential");
            }
        }
        Near(AIRules.DomainScore(80, 0, 1), 0, "omitted module has zero score");
        Check(AIRules.DomainScore(80, 3, 1) > AIRules.DomainScore(80, 1, 1), "module features improve domain");
        Near(AIRules.Assistance(0, 0, 0), 0, "untrained AI grants no assistance");
        Near(AIRules.Assistance(100, 100, 100), 1.5, "maximum 2.5x employee work");
        Check(AIRules.Assistance(0, 100, 0) > AIRules.Assistance(100, 0, 0), "coding weighs most for assistance");
        Near(AIRules.AssistProgress(10, 10, 100, 1.5), 10, "idle employees produce no bonus work");
        Near(AIRules.AssistProgress(10, 8, 100, 1.5), 13, "only new employee work is accelerated");
        Near(AIRules.AssistProgress(13, 13, 100, 1.5), 13, "post-bonus snapshot prevents recursive work");
        Near(AIRules.AssistProgress(99, 90, 100, 1.5), 100, "bonus respects completion limit");
        Near(AIRules.AssistProgress(1, 1, 0.8, 1.5), 1, "bonus does not lower native quality");
        Near(AIRules.AssistProgress(5, 10, 100, 1.5), 5, "phase reset does not create progress");
        Near(AIRules.LicenseFee(3, 100), 30000, "premium monthly license");
        Near(AIRules.InferenceDemand(0, 1), 100, "private 60B inference needs servers");
        Near(AIRules.InferenceDemand(3, 1), 1800, "1T corporate serving requires 18x capacity");
        Near(AIRules.InferenceDemand(3, 3), 5400, "additional company licenses add inference demand");
        Near(AIRules.DeliveredAssistance(1.5, 0), 0, "no inference capacity means no development bonus");
        Near(AIRules.DeliveredAssistance(1.5, 0.5), 0.75, "capacity contention reduces assistance");
        Near(AIRules.DeliveredAssistance(1.5, 1), 1.5, "full serving preserves maximum assistance");
        Near(AIRules.AbstractHostingFee(3, 2), 720, "rival corporate hosting is funded");
        Check(AIRules.AdvanceReleaseMinutes(10000, 1000, 1, 1) == 8560, "rival release date advances with bonus work");
        Check(AIRules.AdvanceReleaseMinutes(2000, 1000, 2, 1) == 1000, "rival accelerated date cannot precede now");
        Check(AIRules.AdvanceReleaseMinutes(10000, 1000, 0, 1) == 10000, "no employee work means no earlier rival release");
    }
    private static void TestData(string package)
    {
        foreach (string path in Directory.GetFiles(package, "*.tyd", SearchOption.AllDirectories))
        { Check(TydFromText.Parse(File.ReadAllText(path)).Count > 0, "native TyD parser: " + path); }
        string dataRoot = Path.Combine(Path.Combine(package, "Mods"), "AI Compute");
        TydCollection root = (TydCollection)TydFromText.ParseOne(File.ReadAllText(Path.Combine(Path.Combine(dataRoot, "SoftwareTypes"), "Artificial Intelligence.tyd")));
        Check(root.GetChildValue("Name", true) == AIRules.SoftwareName, "type name matches controller");
        Check(root.GetChildValue<int>("Unlock", true, 0) == 2018, "model development begins in 2018");
        Check(!root.GetChildValue<bool>("OneClient", true, true), "AI eligible for native founder and hired visionary focus");
        Check(root.GetChildValue<bool>("InHouse", true, false), "private AI allowed");
        Check(root.GetChild("OSSupport", false) == null, "market not restricted to a specific OS userbase");
        TydCollection categories = (TydCollection)root.GetChild("Categories", true);
        Check(categories.Count == 1, "sizes share one market");
        TydCollection category = (TydCollection)categories[0];
        Near(category.GetChildValue<double>("Popularity", true, 0), 1, "OS popularity ceiling");
        Check(category.GetChildValue<bool>("ForcePopularity", true, false), "fixed popularity ceiling");
        Check(category.GetChildValue<double>("IdealPrice", true, 0) >= 200, "premium commercial pricing");
        TydCollection features = (TydCollection)root.GetChild("Features", true);
        Check(features.Count == AIRules.Domains.Length, "five capability domains");
        HashSet<string> specs = new HashSet<string>();
        for (int i = 0; i < features.Count; i++)
        {
            TydCollection feature = (TydCollection)features[i];
            Check(feature.GetChildValue("Name", true).StartsWith(AIRules.Domains[i]), "feature benchmark mapping");
            Check(specs.Add(feature.GetChildValue("Spec", true)), "one feature root per specialization");
            Check(feature.GetChildValue<bool>("Optional", false, false) == (i != 0), "reasoning mandatory, other modules optional");
            Check(feature.GetChildValue<double>("Server", true, 0) > 0, "native inference serving demand");
            TydCollection children = (TydCollection)feature.GetChild("Features", true);
            for (int j = 0; j < children.Count; j++)
            {
                TydCollection child = (TydCollection)children[j];
                Check(child.GetChildValue("Name", true).StartsWith(AIRules.Domains[i]), "subfeature benchmark mapping");
                int level = child.GetChildValue<int>("Level", true, 0);
                Check(level >= 1 && level <= 2, "all features supported by two specialization stars");
            }
        }
        TydCollection app = (TydCollection)TydFromText.ParseOne(File.ReadAllText(Path.Combine(Path.Combine(dataRoot, "SoftwareTypes"), "AI Application.tyd")));
        Check(app.GetChildValue("Name", true) == "AI Application", "one companion software type");
        Check(app.GetChildValue<bool>("InHouse", true, false), "private companion tools allowed");
        Check(app.GetChild("OSSupport", false) == null, "hosted applications are not OS restricted");
        TydCollection appCategories = (TydCollection)app.GetChild("Categories", true);
        string[] appNames = { "Chat Interface", "Coding Harness", "Image Studio" };
        int[] appYears = { 2018, 2019, 2022 };
        Check(appCategories.Count == appNames.Length, "three companion markets under one type");
        for (int i = 0; i < appCategories.Count; i++)
        {
            TydCollection item = (TydCollection)appCategories[i];
            Check(item.GetChildValue("Name", true) == appNames[i], "companion category identity");
            Check(item.GetChildValue<int>("Unlock", true, 0) == appYears[i], "dated companion unlock");
            Check(item.GetChildValue<double>("Popularity", true, 0) < 1, "companion market smaller than model market");
            Check(item.GetChildValue<double>("IdealPrice", true, 0) > 0, "companion category pricing");
        }
        TydCollection appFeatures = (TydCollection)app.GetChild("Features", true);
        Check(appFeatures.Count == 4, "hosted connection and three distinct experiences");
        TydCollection hosted = (TydCollection)appFeatures[0];
        Check(hosted.GetChildValue("Dependencies", true) == AIRules.SoftwareName, "all companion apps select an AI model");
        Check(hosted.GetChildValue("Spec", true) == ((TydCollection)features[0]).GetChildValue("Spec", true), "hosted picker requires the mandatory reasoning technology, not optional coding");
        Check(hosted.GetChildValue<double>("Server", true, 0) > 0, "hosted model connection has per-user bandwidth");
        for (int i = 1; i < appFeatures.Count; i++)
        {
            TydCollection item = (TydCollection)appFeatures[i];
            Check(item.GetChildValue("SoftwareCategories", true) == appNames[i - 1], "each experience appears only in its category");
            Check(item.GetChildValue<double>("Server", true, 0) > 0, "each companion has distinct hosting demand");
            Check(!item.GetChildValue<bool>("Optional", false, false), "category experience is mandatory");
        }
        TydCollection image = (TydCollection)appFeatures[3];
        Check(image.GetChildValue("Dependencies", true) == "2D Editor", "image workflow selects a 2D editing tool");
        TydCollection rivals = (TydCollection)TydFromText.ParseOne(File.ReadAllText(Path.Combine(Path.Combine(dataRoot, "CompanyTypes"), "AIApplications.tyd")));
        TydCollection rivalTypes = (TydCollection)rivals.GetChild("Types", true);
        Check(rivalTypes.Count == 1, "companion company lists software once");
        Check(((TydCollection)rivalTypes[0]).GetChild("Category", false) == null, "rivals can choose any unlocked companion category");
        foreach (string path in Directory.GetFiles(Path.Combine(dataRoot, "CompanyTypes"), "*.tyd", SearchOption.AllDirectories))
        {
            TydCollection company = (TydCollection)TydFromText.ParseOne(File.ReadAllText(path));
            Check(UniqueCompanySoftware(company), "native company software uniqueness rule: " + path);
        }
        TydCollection duplicateCompany = (TydCollection)TydFromText.ParseOne("CompanyType\n{\nSpecialization Test\nTypes\n[\n{\nSoftware App\nCategory Chat\nChance 1\n}\n{\nSoftware App\nCategory Code\nChance 1\n}\n]\n}");
        Check(!UniqueCompanySoftware(duplicateCompany), "reject duplicate software even when categories differ");
        XmlDocument xml = new XmlDocument();
        xml.Load(Path.Combine(Path.Combine(Path.Combine(package, "DLLMods"), "AI Compute"), "TrainingPanel.xml"));
        Check(xml.DocumentElement.Name == "Window", "well-formed training panel XML");
    }
    private static void TestUIRecovery()
    {
        int executed = 0, reported = 0;
        Exception failure = new InvalidOperationException("panel regression");
        Exception observed = null;
        Check(UIRecovery.TryRun(delegate { executed++; }, delegate(Exception error) { reported++; }), "successful panel action completes");
        Check(executed == 1 && reported == 0, "success does not produce an error report");
        Check(!UIRecovery.TryRun(delegate { executed++; throw failure; }, delegate(Exception error) { reported++; observed = error; }), "panel failure is contained");
        Check(executed == 2 && reported == 1 && System.Object.ReferenceEquals(observed, failure), "failure preserves the diagnostic without retrying the action");
        Check(!UIRecovery.TryRun(delegate { throw failure; }, delegate(Exception error) { throw new Exception("reporter failure"); }), "error reporter failure is also contained");
        Check(!UIRecovery.TryRun(delegate { throw failure; }, null), "missing reporter cannot leak an action error");
        Check(UIRecovery.TryRun(delegate { executed++; }, null) && executed == 3, "a later panel action can recover normally");
    }
    private static bool UniqueCompanySoftware(TydCollection company)
    {
        HashSet<string> software = new HashSet<string>();
        TydCollection types = (TydCollection)company.GetChild("Types", true);
        for (int i = 0; i < types.Count; i++)
            if (!software.Add(((TydCollection)types[i]).GetChildValue("Software", true))) return false;
        return true;
    }
    private static void TestExpansion()
    {
        Check(!ExpansionRules.RivalResearchOpen(2030, 999999, -1, 1), "rivals wait indefinitely for a player public release");
        Check(!ExpansionRules.RivalResearchOpen(2017, 999999, 0, 1), "rival research cannot precede 2018");
        foreach (int days in new int[] { 1, 2, 5, 30 })
        {
            int release = 100000, deadline = release + 6 * 1440 * days;
            Check(!ExpansionRules.RivalResearchOpen(2020, deadline - 1, release, days), "full six month player head start");
            Check(ExpansionRules.RivalResearchOpen(2020, deadline, release, days), "competition opens at exact grace deadline");
        }
        Near(ExpansionRules.Discovery(0, 80), 0, "untrained product earns no benchmark discovery");
        Check(ExpansionRules.Discovery(80, 80) > ExpansionRules.Discovery(40, 80) * 4, "superior model earns much stronger discovery");
        Check(ExpansionRules.Discovery(100, 100) <= 0.75, "discovery does not guarantee total market awareness");
        Near(AIRules.ComputeLearning(1), 0.5, "reference compute leaves meaningful headroom");
        double[] providerStrength = { 0.5, 1.5 }, providerServing = { 0, 1 };
        Check(ExpansionRules.ChooseProvider(providerStrength, providerServing, true, 0) == 0, "outage never switches an explicitly locked provider");
        Check(ExpansionRules.ChooseProvider(providerStrength, providerServing, true, -1) == -1, "missing or unlicensed locked model never falls back to a competitor");
        Check(ExpansionRules.ChooseProvider(providerStrength, providerServing, false, 0) == 1, "automatic mode can still choose a better served competitor");
        Near(AIRules.ComputeLearning(4), 0.8, "four times compute delivers tangible capability");
        Check(AIRules.ComputeLearning(10) > AIRules.ComputeLearning(4) + 0.1, "ten times compute still produces useful gains");
        Check(ExpansionRules.LockedReason(4, new TrainingConfig(), 2017) != "", "all models locked before 2018");
        Check(ExpansionRules.LockedReason(5, new TrainingConfig(), 2018) != "", "7B waits until 2019");
        int[] starterDomains = { 2, 0, 1, 0, 0 };
        double smallAllocation = ExpansionRules.Scores(4, new TrainingConfig(), 700, 1, 1, starterDomains, null)[0];
        double fullGroup = ExpansionRules.Scores(4, new TrainingConfig(), 70000, 1, 1, starterDomains, null)[0];
        Check(fullGroup > smallAllocation + 30, "observed 70000-capacity group has tangible gains between 1 percent and full allocation");
        Near(ExpansionRules.TrainingElectricity(1000, 1, 0.6, 1), 432, "native full-load server power estimate");
        Near(ExpansionRules.TrainingElectricity(1000, 0.1, 0.6, 1), 43.2, "ten percent allocation reduces power estimate tenfold");
        Near(ExpansionRules.TrainingElectricity(1000, 1, 0.6, 4), 1728, "native billing accounts for days per month");
        Near(ExpansionRules.TrainingElectricity(1000, 0, 0.6, 4), 0, "paused allocation has no training power estimate");
        Near(ExpansionRules.TrainingElectricity(1000, 2, 0.6, 1), 432, "allocation preview cannot exceed group capacity");
        Near(ExpansionRules.TrainingElectricity(-1, 1, 0.6, 1), 0, "invalid wattage creates no negative expense");
        Check(ExpansionRules.Allocations.Length == 6 && ExpansionRules.Allocations[0] == 0.01 && ExpansionRules.Allocations[5] == 1, "small and full allocations available");
        Near(ExpansionRules.DatasetFee(4, new TrainingConfig(), new int[] { 2, 0, 1, 0, 0 }), 750, "2018 reasoning and knowledge dataset is a one-time small fee");
        Check(ExpansionRules.DatasetFee(4, new TrainingConfig(), new int[] { 2, 0, 1, 0, 0 }) < 2000000, "two million expense is not the early model dataset fee");
        int[] all = { 3, 3, 3, 4, 4 }, text = { 3, 3, 3, 0, 0 }, reasoning = { 3, 0, 0, 0, 0 };
        TrainingConfig dense = new TrainingConfig();
        Check(ExpansionRules.LockedReason(4, dense, 2018) == "", "early specialist available in 2018");
        Check(ExpansionRules.LockedReason(0, dense, 2019).Length > 0, "60B cannot unlock early");
        Check(ExpansionRules.LockedReason(0, dense, 2020) == "", "60B unlocks in 2020");
        Check(ExpansionRules.LockedReason(3, dense, 2023).Length > 0, "frontier tier dated");
        TrainingConfig sparse = new TrainingConfig(); sparse.Architecture = 1;
        Check(ExpansionRules.LockedReason(0, sparse, 2020).Length > 0, "sparse architecture dated");
        Check(ExpansionRules.LockedReason(0, sparse, 2021) == "", "sparse architecture unlock");
        double budget = AIRules.ReferenceCapacity[0] * AIRules.Months[0];
        double[] narrow = ExpansionRules.Scores(0, dense, budget, 2, 1, text, null);
        double[] broad = ExpansionRules.Scores(0, dense, budget, 2, 1, all, null);
        Check(narrow[1] > broad[1], "multimodal training divides finite compute budget");
        Near(narrow[3], 0, "omitted vision cannot receive a benchmark");
        double[] shares = ExpansionRules.Shares(all, dense); double sum = 0;
        foreach (double share in shares) sum += share; Near(sum, 1, "training compute allocations conserve budget");
        TrainingConfig codingFocus = new TrainingConfig(); codingFocus.Focus = 2;
        double[] focused = ExpansionRules.Scores(0, codingFocus, budget, 2, 1, all, null);
        Check(focused[1] > broad[1] && focused[0] < broad[0], "coding focus has an opportunity cost");
        TrainingConfig codeData = new TrainingConfig(); codeData.Dataset = 1;
        double[] curated = ExpansionRules.Scores(0, codeData, budget, 2, 1, all, null);
        Check(curated[1] > broad[1] && curated[2] < broad[2], "specialist data trades general performance for coding");
        Check(ExpansionRules.DatasetFee(0, codeData, all) > ExpansionRules.DatasetFee(0, dense, all), "curation is paid");
        Check(ExpansionRules.DatasetFee(3, dense, all) > ExpansionRules.DatasetFee(0, dense, all), "large datasets cost more");
        TrainingConfig q8 = new TrainingConfig(); q8.Quantization = 1;
        TrainingConfig q4 = new TrainingConfig(); q4.Quantization = 2;
        Check(ExpansionRules.InferenceFactor(q4) < ExpansionRules.InferenceFactor(q8) && ExpansionRules.InferenceFactor(q8) < ExpansionRules.InferenceFactor(dense), "quantization reduces serving demand");
        Check(ExpansionRules.Scores(0, q8, budget, 2, 1, all, null)[1] < broad[1], "quantization has a quality cost");
        Check(ExpansionRules.Memory(3, sparse) > ExpansionRules.Memory(3, dense), "sparse model retains full expert memory cost");
        Check(ExpansionRules.Scores(0, sparse, budget / 2, 2, 1, all, null)[1] > ExpansionRules.Scores(0, dense, budget / 2, 2, 1, all, null)[1], "sparse active compute is cheaper");
        TrainingConfig context = new TrainingConfig(); context.Context = 2;
        Check(ExpansionRules.InferenceFactor(context) > ExpansionRules.InferenceFactor(dense), "long context costs capacity");
        Check(ExpansionRules.Scores(0, context, budget, 2, 1, all, null)[0] > broad[0], "long context improves reasoning potential");
        Near(ExpansionRules.Scores(0, context, budget, 2, 1, all, null)[1], broad[1], "context does not inflate every capability");
        Near(ExpansionRules.CorporateDemand(3, dense, all, 0), 0, "idle company has no inference work");
        Near(ExpansionRules.CorporateDemand(3, dense, all, 20), 2 * ExpansionRules.CorporateDemand(3, dense, all, 10), "inference scales with active employees");
        Check(ExpansionRules.CorporateDemand(0, dense, all, 10) > ExpansionRules.CorporateDemand(0, dense, reasoning, 10), "more capabilities cost more inference");
        double[] perfect = { 100, 100, 100, 100, 100 }, visionOnly = { 0, 0, 0, 100, 0 }, speechOnly = { 0, 0, 0, 0, 100 };
        Near(ExpansionRules.Bonus(perfect, "System", 0, false, false), 1.5, "maximum design bonus");
        Check(ExpansionRules.Bonus(visionOnly, "2D", 1, false, false) > 0, "vision helps visual programming");
        Near(ExpansionRules.Bonus(visionOnly, "2D", 2, false, false), 0, "understanding does not generate art");
        Near(ExpansionRules.Bonus(visionOnly, "2D", 2, true, false), 1.5, "image generation helps art");
        Near(ExpansionRules.Bonus(speechOnly, "Audio", 2, false, true), 1.5, "audio generation helps audio art");
        Near(ExpansionRules.Bonus(speechOnly, "2D", 2, true, true), 0, "speech cannot generate visual art");
        Check(ExpansionRules.Choose(new double[] { 1.5, 0.6 }, new double[] { 0, 1 }) == 1, "unavailable stronger model falls back");
        Check(ExpansionRules.Choose(new double[] { 1.5, 0.6 }, new double[] { 0.2, 1 }) == 1, "selection uses delivered benefit");
        Check(ExpansionRules.Choose(new double[] { 1.5, 0.6 }, new double[] { 0, 0 }) == -1, "no served model grants no benefit");
        Check(ExpansionRules.SubscriptionPrice(3, 100, 300, dense) > ExpansionRules.SubscriptionPrice(0, 100, 30, dense), "subscription scales with size and usage");
        Check(ExpansionRules.SubscriptionPrice(0, 100, 300, dense) > ExpansionRules.SubscriptionPrice(0, 100, 30, dense), "metered usage increases price");
        TrainingConfig tune = new TrainingConfig(); tune.Mode = 1;
        Near(ExpansionRules.Window(0, tune), 1, "fine tuning has a fixed half window");
        double[] seed = { 60, 60, 60, 0, 0 };
        double[] noComputeTune = ExpansionRules.Scores(0, tune, 0, 1, 1, text, seed);
        Near(noComputeTune[1], 54, "zero-compute tuning only preserves partial checkpoint");
        Check(ExpansionRules.Scores(0, tune, budget, 1, 1, text, seed)[1] > noComputeTune[1], "tuning improvement requires compute");
        for (int tier = 0; tier < AIRules.TierNames.Length; tier++)
        for (int architecture = 0; architecture <= 1; architecture++)
        for (int quant = 0; quant <= 2; quant++)
        for (int data = 0; data <= 3; data++)
        for (int focus = 0; focus <= 5; focus++)
        for (int mode = 0; mode <= 1; mode++)
        {
            TrainingConfig c = new TrainingConfig(); c.Architecture = architecture; c.Quantization = quant; c.Dataset = data; c.Focus = focus; c.Mode = mode;
            double window = ExpansionRules.Window(tier, c), lowElapsed = 0, highElapsed = 0, lowCompute = 0, highCompute = 0;
            int hours = (int)Math.Ceiling(window * 24);
            for (int h = 0; h < hours; h++)
            {
                bool lowDone = ExpansionRules.Advance(tier, c, 1, true, 10, ref lowElapsed, ref lowCompute);
                bool highDone = ExpansionRules.Advance(tier, c, 1, true, 100000, ref highElapsed, ref highCompute);
                Check(lowDone == highDone && lowDone == (h == hours - 1), "all configurations enforce fixed training deadline");
                Near(ExpansionRules.NativeProgress(tier, c, lowElapsed), lowDone ? 1 : 0, "native promotion stays blocked until training deadline");
            }
            Near(lowElapsed, window, "configuration duration"); Near(highElapsed, window, "servers cannot shorten configuration window");
            double before = highCompute;
            ExpansionRules.Advance(tier, c, 1, true, 100000, ref highElapsed, ref highCompute);
            Near(highCompute, before, "no post-deadline training credit");
            ExpansionRules.Advance(tier, c, 1, false, 100000, ref lowElapsed, ref lowCompute);
            Near(lowElapsed, window, "pause does not advance");
            double[] zero = ExpansionRules.Scores(tier, c, 0, window, 1, all, null);
            double[] low = ExpansionRules.Scores(tier, c, lowCompute, window, 1, all, mode == 1 ? seed : null);
            double[] high = ExpansionRules.Scores(tier, c, highCompute, window, 1, all, mode == 1 ? seed : null);
            for (int domain = 0; domain < 5; domain++)
            {
                Near(zero[domain], 0, "data and preparation cannot create an untrained model");
                Check(high[domain] >= low[domain] && high[domain] <= 100 && low[domain] >= 0, "all domain scores are monotonic and bounded");
            }
        }
    }
}
