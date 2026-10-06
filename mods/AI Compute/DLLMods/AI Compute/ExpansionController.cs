using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace SDSI.AI
{
    public sealed class RevisionRequest
    {
        public string Name;
        public uint ParentID;
        public TrainingConfig Config;
        public RevisionRequest() { Config = new TrainingConfig(); }
    }
    public sealed class RivalTrainingState
    {
        public uint ID;
        public uint CompanyID;
        public int Tier;
        public TrainingConfig Config;
        public double Preparation;
        public double Elapsed;
        public double Compute;
        public double Spent;
        public bool DatasetPaid;
        public bool Completed;
        public double[] Seed = new double[5];
        public double[] Scores = new double[5];
        public SimulatedCompany.ProductPrototype Project;
        public RivalTrainingState() { Config = new TrainingConfig(); }
    }

    public sealed partial class AIComputeController
    {
        private GUICombobox architectures, quantizations, contexts, datasets, focuses, usage;
        private List<int> tierChoices = new List<int>();
        private Dictionary<string, RevisionRequest> revisions = new Dictionary<string, RevisionRequest>();
        private Dictionary<uint, RivalTrainingState> rivalTraining = new Dictionary<uint, RivalTrainingState>();
        private Dictionary<uint, uint> preferred = new Dictionary<uint, uint>();
        private int revisionSequence;
        private int firstPublicRelease = -1;

        private bool RivalResearchOpen()
        {
            if (firstPublicRelease < 0)
                foreach (SoftwareProduct product in market.GetAllProducts(false))
                    if (IsAI(product.Type) && product.DevCompany == game.MyCompany && !product.InHouse && !product.IsMock)
                        if (firstPublicRelease < 0 || product.Release.ToInt() < firstPublicRelease) firstPublicRelease = product.Release.ToInt();
            return ExpansionRules.RivalResearchOpen(SDateTime.Now().RealYear, SDateTime.Now().ToInt(), firstPublicRelease, GameSettings.DaysPerMonth);
        }

        private void UpdateBenchmarkDiscovery()
        {
            List<SoftwareProduct> publicModels = market.GetAllProducts(false).Where(delegate(SoftwareProduct p)
                { return IsAI(p.Type) && !p.InHouse && !p.IsMock && !p.Archived; }).ToList();
            double frontier = 1;
            foreach (SoftwareProduct model in publicModels) { EnsureModel(model); frontier = Math.Max(frontier, Overall(model)); }
            // Snapshot the native category scale before applying floors, so
            // provider iteration order cannot amplify the awareness baseline.
            double scale = publicModels.Count > 0 ? Math.Max(1, market.GetMaxAwareness(publicModels[0])) : 1;
            foreach (SoftwareProduct model in publicModels)
            {
                double floor = scale * ExpansionRules.Discovery(Overall(model), frontier);
                if (model.GetRealAwareness() < floor) model.SetAwareness((float)floor, false);
                model.PutVar(AIRules.Prefix + "BenchmarkDiscovery", ExpansionRules.Discovery(Overall(model), frontier));
            }
        }

        private bool InitializeExpansionUI(Dictionary<string, Component> ui)
        {
            string stage = "binding controls";
            try {
            architectures = (GUICombobox)ui["architectures"]; quantizations = (GUICombobox)ui["quantizations"];
            contexts = (GUICombobox)ui["contexts"]; datasets = (GUICombobox)ui["datasets"];
            focuses = (GUICombobox)ui["focuses"]; usage = (GUICombobox)ui["usage"];
            stage = "architecture"; architectures.UpdateContent(ExpansionRules.Architectures);
            stage = "precision"; quantizations.UpdateContent(ExpansionRules.Quantizations);
            stage = "context"; contexts.UpdateContent(ExpansionRules.Contexts);
            stage = "dataset"; datasets.UpdateContent(ExpansionRules.Datasets);
            stage = "focus";
            focuses.UpdateContent(ExpansionRules.Focuses);
            stage = "subscription usage";
            usage.UpdateContent(new string[] { "Light: 30 usage units / user / month", "Standard: 100 usage units / user / month", "Heavy: 300 usage units / user / month" });
            SafeSelection(usage, 1);
            if (window == null) return false;
            RefreshTiers();
            return window != null;
            }
            catch (Exception error) { RecoverPanel("initializing " + stage, error); return false; }
        }
        private void RefreshTiers()
        {
            RunPanelAction("refreshing model sizes", delegate
            {
            int previous = SelectedTier();
            tierChoices = ExpansionRules.TierOrder.Where(delegate(int t) { return SDateTime.Now().RealYear >= ExpansionRules.TierYears[t]; }).ToList();
            sizes.UpdateContent(tierChoices.Select(delegate(int t) { return AIRules.TierNames[t] + " (" + ExpansionRules.TierYears[t] + "+)"; }));
            int index = tierChoices.IndexOf(previous); SafeSelection(sizes, index);
            });
        }
        private int SelectedTier()
        { return sizes != null && sizes.Selected >= 0 && sizes.Selected < tierChoices.Count ? tierChoices[sizes.Selected] : 4; }
        private TrainingConfig ReadConfig()
        {
            TrainingConfig c = new TrainingConfig();
            c.Architecture = architectures == null ? 0 : architectures.Selected;
            c.Quantization = quantizations == null ? 0 : quantizations.Selected;
            c.Context = contexts == null ? 0 : contexts.Selected;
            c.Dataset = datasets == null ? 0 : datasets.Selected;
            c.Focus = focuses == null ? 0 : focuses.Selected;
            return ExpansionRules.Sanitize(c);
        }
        private void SelectConfig(TrainingConfig c)
        {
            if (architectures == null) return;
            SafeSelection(architectures, c.Architecture); SafeSelection(quantizations, c.Quantization);
            SafeSelection(contexts, c.Context); SafeSelection(datasets, c.Dataset); SafeSelection(focuses, c.Focus);
        }
        private bool SameConfig(TrainingConfig a, TrainingConfig b)
        { return a.Architecture == b.Architecture && a.Quantization == b.Quantization && a.Context == b.Context && a.Dataset == b.Dataset && a.Focus == b.Focus; }
        private int[] Counts(IEnumerable<FeatureBase> features)
        {
            int[] result = new int[5];
            if (features != null) foreach (FeatureBase f in features) result[Domain(f.Name)]++;
            return result;
        }
        private double MeanScores(double[] scores)
        { double[] selected = scores.Where(delegate(double x) { return x > 0; }).ToArray(); return selected.Length > 0 ? selected.Average() : 0; }
        private double[] ModelScores(SoftwareProduct model)
        {
            double[] scores = new double[5];
            if (model != null) for (int i = 0; i < 5; i++) scores[i] = Value(model, AIRules.Domains[i], 0);
            return scores;
        }
        private bool HasFeature(SoftwareProduct model, string name)
        { return model != null && model.Features.Any(delegate(FeatureBase f) { return f.Name == name; }); }
        private double WorkBonus(SoftwareProduct model, string spec, int phase)
        {
            if (model == null) return 0;
            return ExpansionRules.Bonus(ModelScores(model), spec, phase, HasFeature(model, "Vision image generation"), HasFeature(model, "Speech audio generation")) * ServingFraction(model);
        }
        private TrainingConfig ModelConfig(SoftwareProduct model)
        {
            TrainingConfig c = new TrainingConfig();
            c.Architecture = (int)Value(model, "Architecture", 0); c.Quantization = (int)Value(model, "Quantization", 0);
            c.Context = (int)Value(model, "Context", 0); c.Dataset = (int)Value(model, "Dataset", 0);
            c.Focus = (int)Value(model, "Focus", 0); c.Mode = (int)Value(model, "Mode", 0);
            return ExpansionRules.Sanitize(c);
        }
        private void StoreConfig(SoftwareProduct model, TrainingConfig c)
        {
            model.PutVar(AIRules.Prefix + "Architecture", c.Architecture); model.PutVar(AIRules.Prefix + "Quantization", c.Quantization);
            model.PutVar(AIRules.Prefix + "Context", c.Context); model.PutVar(AIRules.Prefix + "Dataset", c.Dataset);
            model.PutVar(AIRules.Prefix + "Focus", c.Focus); model.PutVar(AIRules.Prefix + "Mode", c.Mode);
            model.PutVar(AIRules.Prefix + "RulesVersion", 3);
        }
        private void RefreshModelServing(SoftwareProduct model)
        {
            double native = Math.Max(0.0005, SoftwareType.GetServerRequirement(model.Features, 1));
            double usageFactor = model.SubscriptionBased ? Math.Max(0.1, Value(model, "Usage", 100) / 100) : 1;
            model.ServerReq = (float)(native * AIRules.ServingFactors[AIRules.Tier((int)Value(model, "Tier", 4))] * ExpansionRules.InferenceFactor(ModelConfig(model)) * usageFactor);
            model.RegisterServer();
        }
        private void InitializeTraining(TrainingState state, SoftwareAlpha task)
        {
            state.Tier = ExpansionRules.TierOrder.FirstOrDefault(delegate(int t) { return ExpansionRules.TierYears[t] <= SDateTime.Now().RealYear; });
            RevisionRequest request;
            if (!revisions.TryGetValue(task.SoftwareName, out request)) return;
            SoftwareProduct source = market.GetProduct(request.ParentID, false, false, false);
            if (source == null || source.DevCompany != game.MyCompany) { revisions.Remove(request.Name); return; }
            EnsureModel(source);
            state.Tier = AIRules.Tier((int)Value(source, "Tier", 4)); state.Config = request.Config.Copy();
            if (state.Config.Mode == 1)
            {
                double precision = ExpansionRules.QuantQuality(ModelConfig(source));
                state.Seed = ModelScores(source).Select(delegate(double v) { return AIRules.Clamp(v / precision, 0, 100); }).ToArray();
            }
            revisions.Remove(request.Name);
        }
        public void FineTuneModel() { CreateRevision(true, false); }
        public void RetrainModel() { CreateRevision(false, false); }
        public void CreateSubscriptionModel() { CreateRevision(false, true); }
        private void CreateRevision(bool fineTune, bool subscription)
        {
            RunPanelAction("creating a model revision", delegate
            {
            if (!Ready || models.Selected < 0 || models.Selected >= modelChoices.Count) return;
            SoftwareProduct source = modelChoices[models.Selected];
            if (source.DevCompany != game.MyCompany) { Message("Choose an owned model to create a revision."); return; }
            if (game.MyCompany.WorkItems.OfType<SoftwareWorkItem>().Any(delegate(SoftwareWorkItem w) { return !w.Done && w.SequelTo == source; }))
            { Message("A revision of this model is already in development."); return; }
            TrainingConfig cfg = ModelConfig(source); cfg.Mode = fineTune ? 1 : 0;
            string name = source.Name + (fineTune ? " Fine-tune " : subscription ? " API " : " Retrain ") + (++revisionSequence).ToString(CultureInfo.InvariantCulture);
            RevisionRequest request = new RevisionRequest(); request.Name = name; request.ParentID = source.ID; request.Config = cfg;
            // Native design and beta phases provide real employee preparation
            // and evaluation. The revision is a new native product/sequel.
            DesignDocument design = new DesignDocument(name, source.Type, source.Category,
                new Dictionary<string, SoftwareProduct>(), new SoftwareProduct[0], source.Price, subscription || source.SubscriptionBased,
                (double[])source.Submarkets.Clone(), SDateTime.Now(), game.MyCompany, source, source.InHouse && !subscription, 0,
                source.Features.ToList(), new Dictionary<string, TechLevel>(source.TechLevels), null,
                source.Server ?? "", "", source.Framework, null, new List<SoftwareProduct>(), false, 0, null);
            revisions[name] = request; game.MyCompany.AddWorkItem(design);
            game.ApplyDefaultTeams(design, "Design");
            Message("Created " + name + (subscription ? " as a commercial subscription successor." : ".") + " Assign a design team and complete preparation. " + (fineTune ? "Fine-tuning uses half the size's training window and a warm checkpoint." : "Full retraining starts with a fresh training budget; you can change its configuration.") + " Evaluate and release the successor normally.");
            RefreshChoices();
            });
        }
        private void TransferRevisionAccess(SoftwareProduct successor)
        {
            SoftwareProduct parent = successor.SequelTo;
            if (parent == null || parent.DevCompany != successor.DevCompany) return;
            if (!successor.InHouse) foreach (uint buyer in access.Keys.ToArray()) if (access[buyer] == parent.ID) access[buyer] = successor.ID;
            foreach (uint owner in preferred.Keys.ToArray()) if (preferred[owner] == parent.ID) preferred[owner] = successor.ID;
            successor.PutVar(AIRules.Prefix + "InferenceGroup", parent.GetVar(AIRules.Prefix + "InferenceGroup", parent.Server ?? ""));
        }
        private double ActiveEmployees(Company company)
        {
            if (company != game.MyCompany)
            {
                SimulatedCompany rival = company as SimulatedCompany;
                return rival == null ? 0 : rival.Releases.Concat(rival.ProjectQueue).Where(delegate(SimulatedCompany.ProductPrototype p) { return p.DevStarted && !p.Released && !IsAI(p.Type); }).Sum(delegate(SimulatedCompany.ProductPrototype p) { return (double)Math.Max(1, p.DevTeam); });
            }
            HashSet<Actor> actors = new HashSet<Actor>();
            foreach (WorkItem work in company.WorkItems)
            {
                if (work.Done || work.Paused || (!(work is SoftwareWorkItem) && !(work is SoftwarePort))) continue;
                SoftwareWorkItem software = work as SoftwareWorkItem;
                if (software != null && IsAI(software.Type)) continue;
                foreach (Team team in work.GetDevTeams()) foreach (Actor actor in team.GetEmployees())
                    if (actor != null && actor.IsWorking && actor.GetCurrentWorkItems().Any(delegate(WorkItem w) { return w == work; })) actors.Add(actor);
            }
            return actors.Count;
        }
        private void PrepareEvaluationSchedule(SoftwareAlpha task)
        {
            if (task.contract != null || task.ActiveDeal != null) return;
            SDateTime now = SDateTime.Now();
            double minuteMonth = 1440.0 * Math.Max(1, GameSettings.DaysPerMonth);
            foreach (AutoDevWorkItem manager in game.MyCompany.WorkItems.OfType<AutoDevWorkItem>())
            foreach (AutoDevWorkItem.AutoDevItem item in manager.Items)
            {
                if (item.Alpha != task) continue;
                // The native PM calendar does not know about server training.
                // Give beta a fresh month instead of immediately releasing a
                // project whose original cut date passed during training.
                double spent = Math.Max(0, (now.ToInt() - item.ActualStartDate.ToInt()) / minuteMonth);
                item.MonthsToSpend = Math.Max(item.MonthsToSpend, (float)(spent + item.AlreadyDev + 1));
                task.ReleaseDate = item.GetCutDate(1); manager.RefreshNextReleaseDate();
            }
        }
        private double PredictedServing(SoftwareProduct model, Company buyer)
        {
            double demand = ExpansionRules.CorporateDemand((int)Value(model, "Tier", 4), ModelConfig(model), Counts(model.Features), Math.Max(1, ActiveEmployees(buyer))) +
                ExpansionRules.Memory((int)Value(model, "Tier", 4), ModelConfig(model));
            if (model.DevCompany != game.MyCompany) return model.DevCompany.CanMakeTransaction(-demand * 0.2 * AIRules.MonthStep(GameSettings.DaysPerMonth)) ? 1 : 0;
            string name = Convert.ToString(model.GetVar(AIRules.Prefix + "InferenceGroup", model.Server ?? ""), CultureInfo.InvariantCulture);
            ServerGroup group;
            if (String.IsNullOrEmpty(name) || !game.TryGetServerGroup(name, out group) || group.IsCloud || group.Broken || group.PowerSum <= 0) return 0;
            InferenceLoad load;
            double committed = inference.TryGetValue(model.ID, out load) ? load.Demand : 0;
            // LastUsed is capped at PowerSum and hides overload. Sum the full
            // registered requests, in physical capacity units, instead.
            double factor = Utilities.BandwidthFactor(1, SDateTime.Now());
            double total = 0;
            foreach (IServerItem item in group.Items) total += Math.Max(0, item.GetLoadRequirement()) * factor;
            double other = Math.Max(0, total - committed);
            double predicted = AIRules.Clamp(group.PowerSum / Math.Max(0.001, other + Math.Max(demand, committed)), 0, 1);
            // Actual delivery also captures network failures. Idle candidates
            // are probed without registering duplicate full workloads.
            return load != null && load.HasDelivery ? Math.Min(predicted, load.LastDelivery) : predicted;
        }
        public void PreferModel()
        {
            RunPanelAction("choosing a preferred model", delegate
            {
            if (!Ready || models.Selected < 0 || models.Selected >= modelChoices.Count) return;
            SoftwareProduct model = modelChoices[models.Selected]; uint paid; access.TryGetValue(game.MyCompany.ID, out paid);
            if (model.DevCompany != game.MyCompany && paid != model.ID && !game.MyCompany.GetLicenses().Contains(model))
            { Message("License this model before selecting it as your preferred assistant."); return; }
            preferred[game.MyCompany.ID] = model.ID; RefreshLabels();
            });
        }
        public void AutoSelectModel() { if (Ready) preferred.Remove(game.MyCompany.ID); RefreshLabels(); }
        public void SetUsageBilling()
        {
            RunPanelAction("setting subscription usage", delegate
            {
            if (!Ready || models.Selected < 0 || models.Selected >= modelChoices.Count) return;
            SoftwareProduct model = modelChoices[models.Selected];
            if (model.DevCompany != game.MyCompany || model.InHouse) { Message("Choose an owned commercial model."); return; }
            if (!model.SubscriptionBased) { Message("This product was released with one-time pricing. Create a subscription revision, or select subscription pricing when designing your next model."); return; }
            model.PutVar(AIRules.Prefix + "Metered", true);
            model.PutVar(AIRules.Prefix + "Usage", usage.Selected == 0 ? 30.0 : usage.Selected == 2 ? 300.0 : 100.0);
            RefreshModelServing(model); ApplySubscriptionPrice(model); RefreshLabels();
            });
        }
        public void FixedSubscriptionBilling()
        {
            RunPanelAction("setting subscription billing", delegate
            {
            if (!Ready || models.Selected < 0 || models.Selected >= modelChoices.Count) return;
            SoftwareProduct model = modelChoices[models.Selected]; if (model.DevCompany != game.MyCompany) return;
            model.PutVar(AIRules.Prefix + "Metered", false);
            if (model.SubscriptionBased) model.ChangePrice((float)ExpansionRules.SubscriptionPrice((int)Value(model, "Tier", 4), Overall(model), 0, ModelConfig(model)));
            RefreshLabels();
            });
        }
        private void ApplySubscriptionPrice(SoftwareProduct model)
        {
            if (!model.SubscriptionBased || model.InHouse || model.Archived) return;
            double units = Convert.ToBoolean(model.GetVar(AIRules.Prefix + "Metered", true)) ? Value(model, "Usage", 100) : 0;
            double price = ExpansionRules.SubscriptionPrice((int)Value(model, "Tier", 4), Overall(model), units, ModelConfig(model));
            // Native price, subscribers, payments, churn and hosting handle the
            // recurring transaction; never add a second userbase cash credit.
            if (Math.Abs(model.Price - price) > 0.005) model.ChangePrice((float)price);
        }
        private void MeterSubscriptions()
        { foreach (SoftwareProduct model in market.GetAllProducts(false)) if (IsAI(model.Type)) { EnsureModel(model); ApplySubscriptionPrice(model); } }
        private void AppendModelDetails(StringBuilder text, SoftwareProduct model)
        {
            TrainingConfig cfg = ModelConfig(model);
            text.Append("\n" + ExpansionRules.Architectures[cfg.Architecture] + " | " + ExpansionRules.Quantizations[cfg.Quantization] + " | " + ExpansionRules.Contexts[cfg.Context]);
            text.Append("\nDesign " + (1 + WorkBonus(model, "System", 0)).ToString("F2") + "x | Code " + (1 + WorkBonus(model, "System", 1)).ToString("F2") + "x | Visual art " + (1 + WorkBonus(model, "2D", 2)).ToString("F2") + "x | Audio art " + (1 + WorkBonus(model, "Audio", 2)).ToString("F2") + "x");
            if (model.SubscriptionBased) text.AppendFormat(CultureInfo.InvariantCulture, "\nSubscription: ${0:F2}/user/month | {1:N0} active users | {2}", model.Price, model.Userbase, Convert.ToBoolean(model.GetVar(AIRules.Prefix + "Metered", true)) ? "usage-based" : "flat access");
            else if (!model.InHouse) text.Append("\nOne-time consumer sales; create a subscription revision for recurring usage billing.");
            text.Append("\nTraining investment: " + Value(model, "Compute", 0).ToString("N0") + " capacity-months");
        }
        private void DiscoverRivalTraining()
        {
            if (!Ready) return;
            bool researchOpen = RivalResearchOpen();
            HashSet<uint> live = new HashSet<uint>();
            foreach (SimulatedCompany company in market.Companies.Values)
            foreach (SimulatedCompany.ProductPrototype project in company.Releases.Concat(company.ProjectQueue))
            {
                if (!IsAI(project.Type) || project.Released) continue;
                if (!project.DevStarted)
                {
                    // Reserve the release gate now, but do not capture native
                    // preparation quality until development has initialized it.
                    int heldDate = SDateTime.Now().ToInt() + 1440 * Math.Max(1, GameSettings.DaysPerMonth);
                    if (project.ReleaseDate.ToInt() < heldDate) project.ReleaseDate = SDateTime.FromInt(heldDate);
                    continue;
                }
                uint id = project.ForceID(); live.Add(id); RivalTrainingState state;
                if (!rivalTraining.TryGetValue(id, out state))
                {
                    state = new RivalTrainingState(); state.ID = id; state.CompanyID = company.ID;
                    state.Preparation = AIRules.Clamp(project.CodeQuality, 0, 1);
                    int[] available = ExpansionRules.TierOrder.Where(delegate(int t) { return ExpansionRules.TierYears[t] <= SDateTime.Now().RealYear; }).ToArray();
                    state.Tier = available.Length == 0 ? 4 : available[0];
                    foreach (int tier in available)
                    {
                        double budget = AIRules.ReferenceCapacity[tier] * AIRules.Months[tier] * 0.2 + 5000 * Math.Max(0.1, AIRules.Parameters[tier] / 60) * 3;
                        if (budget < Math.Max(0, company.Money) * 0.12) state.Tier = tier;
                    }
                    state.Config.Architecture = SDateTime.Now().RealYear >= 2021 && id % 2 == 0 ? 1 : 0;
                    state.Config.Quantization = SDateTime.Now().RealYear >= 2019 ? 1 : 0;
                    state.Config.Focus = Counts(project.Features)[1] > 0 && id % 3 == 0 ? 2 : 0;
                    rivalTraining[id] = state;
                }
                state.Project = project;
                if (!state.Completed || !researchOpen)
                {
                    // Native rivals are abstract companies. Hold their release
                    // date while their paid virtual training window is running.
                    int earliest = SDateTime.Now().ToInt() + 1440 * Math.Max(1, GameSettings.DaysPerMonth);
                    if (project.ReleaseDate.ToInt() < earliest) project.ReleaseDate = SDateTime.FromInt(earliest);
                    project.DevTimeLeft = Math.Max(project.DevTimeLeft, 0.1f);
                }
            }
            foreach (uint id in rivalTraining.Keys.ToArray()) if (!live.Contains(id)) rivalTraining.Remove(id);
        }
        private void AdvanceRivalTraining()
        {
            if (!RivalResearchOpen()) return;
            foreach (RivalTrainingState state in rivalTraining.Values.ToArray())
            {
                SimulatedCompany.ProductPrototype project = state.Project;
                if (project == null || project.Released || !project.DevStarted || state.Completed) continue;
                Company company = project.DevCompany; int[] counts = Counts(project.Features);
                if (!state.DatasetPaid)
                {
                    double fee = ExpansionRules.DatasetFee(state.Tier, state.Config, counts);
                    if (!company.CanMakeTransaction(-fee)) continue;
                    company.MakeTransaction(-fee, Company.TransactionCategory.Licenses, "AI training dataset: " + project.Name, false);
                    state.DatasetPaid = true; state.Spent += fee;
                }
                double step = AIRules.MonthStep(GameSettings.DaysPerMonth);
                double capacity = AIRules.ReferenceCapacity[state.Tier] * (0.35 + 0.65 * state.Preparation) * ExpansionRules.ArchitectureTraining(state.Config);
                double bill = capacity * 0.2 * step;
                if (company.CanMakeTransaction(-bill)) { company.MakeTransaction(-bill, Company.TransactionCategory.Bills, "AI training compute: " + project.Name, false); state.Spent += bill; }
                else capacity = 0; // A funded dataset starts the clock; outages waste it.
                state.Completed = ExpansionRules.Advance(state.Tier, state.Config, GameSettings.DaysPerMonth, true, capacity, ref state.Elapsed, ref state.Compute);
                state.Scores = ExpansionRules.Scores(state.Tier, state.Config, state.Compute, state.Elapsed, state.Preparation, counts, state.Seed);
                double quality = AIRules.NativeQuality(MeanScores(state.Scores));
                project.SetQuality(1, 1, quality, quality, false);
                if (state.Completed)
                {
                    project.DevTimeLeft = 0;
                    int evaluation = SDateTime.Now().ToInt() + 1440 * Math.Max(1, GameSettings.DaysPerMonth) / 4;
                    project.ReleaseDate = SDateTime.FromInt(evaluation);
                }
            }
        }
        private void RecordRivalModel(SoftwareProduct product)
        {
            RivalTrainingState state;
            if (rivalTraining.TryGetValue(product.ID, out state))
            {
                StoreModel(product, state.Tier, state.Compute, state.Elapsed, state.Scores); StoreConfig(product, state.Config);
                product.PutVar(AIRules.Prefix + "TrainingSpend", state.Spent); RefreshModelServing(product); rivalTraining.Remove(state.ID);
                return;
            }
            // Products predating activation are marked as legacy. No invented
            // paid training history; new rival projects use the full adapter.
            double[] scores = new double[5];
            foreach (FeatureBase feature in product.Features) scores[Domain(feature.Name)] = 72 * AIRules.Clamp(product.RealQuality, 0, 1);
            StoreModel(product, 0, 0, 0, scores); StoreConfig(product, new TrainingConfig());
            product.PutVar(AIRules.Prefix + "Legacy", true);
        }
        private void WriteConfig(WriteDictionary row, TrainingConfig c)
        {
            row["Architecture"] = c.Architecture; row["Quantization"] = c.Quantization; row["Context"] = c.Context;
            row["Dataset"] = c.Dataset; row["Focus"] = c.Focus; row["Mode"] = c.Mode;
        }
        private TrainingConfig ReadSavedConfig(WriteDictionary row)
        {
            TrainingConfig c = new TrainingConfig(); c.Architecture = row.Get<int>("Architecture", 0); c.Quantization = row.Get<int>("Quantization", 0);
            c.Context = row.Get<int>("Context", 0); c.Dataset = row.Get<int>("Dataset", 0); c.Focus = row.Get<int>("Focus", 0); c.Mode = row.Get<int>("Mode", 0);
            return ExpansionRules.Sanitize(c);
        }
        private void SerializeExpansion(WriteDictionary data)
        {
            data["FirstPublicRelease"] = firstPublicRelease;
            data["PreferredBuyerIDs"] = preferred.Keys.ToArray(); data["PreferredModelIDs"] = preferred.Values.ToArray();
            data["RevisionSequence"] = revisionSequence;
            List<WriteDictionary> requests = new List<WriteDictionary>();
            foreach (RevisionRequest r in revisions.Values)
            {
                WriteDictionary row = new WriteDictionary("AIRevision"); row["Name"] = r.Name; row["ParentID"] = r.ParentID; WriteConfig(row, r.Config); requests.Add(row);
            }
            data["Revisions"] = requests;
            List<WriteDictionary> rivals = new List<WriteDictionary>();
            foreach (RivalTrainingState r in rivalTraining.Values)
            {
                WriteDictionary row = new WriteDictionary("AIRivalTraining"); row["ID"] = r.ID; row["CompanyID"] = r.CompanyID; row["Tier"] = r.Tier;
                row["Preparation"] = r.Preparation; row["Elapsed"] = r.Elapsed; row["Compute"] = r.Compute; row["Spent"] = r.Spent;
                row["DatasetPaid"] = r.DatasetPaid; row["Completed"] = r.Completed; row["Scores"] = r.Scores; row["Seed"] = r.Seed; WriteConfig(row, r.Config); rivals.Add(row);
            }
            data["RivalTraining"] = rivals;
        }
        private void DeserializeExpansion(WriteDictionary data)
        {
            firstPublicRelease = data.Get<int>("FirstPublicRelease", -1);
            preferred.Clear(); revisions.Clear(); rivalTraining.Clear(); revisionSequence = Math.Max(0, data.Get<int>("RevisionSequence", 0));
            uint[] buyers = data.Get<uint[]>("PreferredBuyerIDs", new uint[0]), modelsSaved = data.Get<uint[]>("PreferredModelIDs", new uint[0]);
            for (int i = 0; i < Math.Min(buyers.Length, modelsSaved.Length); i++) preferred[buyers[i]] = modelsSaved[i];
            foreach (WriteDictionary row in data.Get<List<WriteDictionary>>("Revisions", new List<WriteDictionary>()))
            {
                RevisionRequest r = new RevisionRequest(); r.Name = row.Get<string>("Name", ""); r.ParentID = row.Get<uint>("ParentID", 0); r.Config = ReadSavedConfig(row); revisions[r.Name] = r;
            }
            foreach (WriteDictionary row in data.Get<List<WriteDictionary>>("RivalTraining", new List<WriteDictionary>()))
            {
                RivalTrainingState r = new RivalTrainingState(); r.ID = row.Get<uint>("ID", 0); r.CompanyID = row.Get<uint>("CompanyID", 0); r.Tier = AIRules.Tier(row.Get<int>("Tier", 4));
                r.Config = ReadSavedConfig(row); r.Preparation = AIRules.Clamp(row.Get<double>("Preparation", 0), 0, 1);
                r.Elapsed = AIRules.Clamp(row.Get<double>("Elapsed", 0), 0, ExpansionRules.Window(r.Tier, r.Config)); r.Compute = Math.Max(0, row.Get<double>("Compute", 0)); r.Spent = Math.Max(0, row.Get<double>("Spent", 0));
                r.DatasetPaid = row.Get<bool>("DatasetPaid", false); r.Completed = row.Get<bool>("Completed", false);
                r.Scores = row.Get<double[]>("Scores", new double[5]); r.Seed = row.Get<double[]>("Seed", new double[5]);
                if (r.Scores.Length != 5) r.Scores = new double[5]; if (r.Seed.Length != 5) r.Seed = new double[5]; rivalTraining[r.ID] = r;
            }
        }
    }
}
