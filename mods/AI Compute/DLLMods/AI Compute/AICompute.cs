using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SDSI.AI
{
    public sealed class AIComputeMeta : ModMeta
    {
        public override string Name { get { return "AI Compute"; } }
        public override void ConstructOptionsScreen(RectTransform parent, bool inGame)
        {
            Text label = WindowManager.SpawnLabel();
            label.text = "AI Compute 0.3.0\nPrepare with employees, train on servers, evaluate and release.\nDevelopment > AI Compute: Simple/Advanced training, compute usage and costs.\nAI Application: Chat 2018, Coding 2019, Image 2022.\nPlayer-first research, benchmark discovery and locked assistant selection.\nRequires both mod halves. Beta 1.8.42, single player. Runtime testing pending.";
            WindowManager.AddElementToElement(label.gameObject, parent.gameObject,
                new Rect(0, 0, 560, 150), new Rect(0, 0, 0, 0));
        }
    }

    // Transient: save only primitive records, never this custom workload object.
    public sealed class TrainingLoad : IServerItem, ILossable
    {
        public AIComputeController Controller;
        public TrainingState State;
        public double NativeRequest;
        public double ServedCapacity;
        public bool HasSample;
        public bool UsesISP { get { return false; } }
        public bool CancelOnUnload() { return false; }
        public float GetLoadRequirement()
        {
            NativeRequest = 0;
            ServerGroup group;
            if (State.Running && !State.Completed && Controller.Ready &&
                GameSettings.Instance.TryGetServerGroup(State.Group, out group) &&
                group != null && !group.IsCloud && !group.Broken)
                NativeRequest = AIRules.Clamp(group.PowerSum * State.Allocation, 0, 100000000);
            float scaling = Utilities.BandwidthFactor(1, SDateTime.Now());
            return (float)(NativeRequest / Math.Max(0.001, scaling));
        }
        public void HandleLoad(float fraction)
        {
            ServedCapacity = NativeRequest * AIRules.Clamp(fraction, 0, 1);
            HasSample = true;
        }
        public void AddLoss(float amount, SoftwareProduct.LossType type, bool takeFromCompany, bool fromContract)
        {
            // The native utility bill already charges the company. Attribute
            // its hourly server cost without making another transaction.
            if (type == SoftwareProduct.LossType.Server) State.ServerSpend += Math.Max(0, amount);
            if (State.Task != null) State.Task.AddLoss(amount, type, false, false);
        }
        public void AddLicenseCost(SoftwareProduct product, float amount, bool takeFromCompany) { }
        public float GetLicenseAmount() { return 0; }
        public string GetDescription() { return "AI training: " + State.Name; }
        public void SerializeServer(string name)
        {
            State.Group = name ?? "";
            if (String.IsNullOrEmpty(name)) { ServedCapacity = 0; NativeRequest = 0; }
        }
        public IReferenceFix FixReferences() { return this; }
    }

    public sealed class TrainingState
    {
        public uint WorkID;
        public uint ProductID;
        public string Name = "";
        public string Group = "";
        public int Tier;
        public double Allocation = 0.10;
        public double ServerSpend;
        public double SpendObservedFrom;
        public double LastCapacity;
        public double Elapsed;
        public double Compute;
        public double Preparation;
        public bool Running;
        public bool Completed;
        public bool WasPaused;
        public bool WasHidden;
        public TrainingConfig Config;
        public bool DatasetPaid;
        public bool WasAutoDev;
        public double[] Seed = new double[5];
        public double[] Scores = new double[5];
        public SoftwareAlpha Task;
        public TrainingLoad Load;
        // The game's source injector mistakes a constructor call in a field
        // initializer on a data-only class for a method declaration.
        public TrainingState() { Config = new TrainingConfig(); }
    }

    public sealed class WorkSnapshot
    {
        public SoftwareWorkItem Task;
        public double[] Progress;
        public double[] Art;
        public double[] Quality;
        public double[] ArtQuality;
        public double FixedBugs;
        public bool Beta;
    }

    public sealed partial class AIComputeController : ModBehaviour
    {
        public bool Ready;
        private bool active;
        private bool savedStatePending;
        private MarketSimulation market;
        private GameSettings game;
        private MainBottomButton hudButton;
        private GUIWindow window;
        private Text status;
        private Text modelInfo;
        private GUICombobox projects;
        private GUICombobox servers;
        private GUICombobox sizes;
        private GUICombobox budgets;
        private GUICombobox models;
        private GUICombobox inferenceServers;
        private List<TrainingState> projectChoices = new List<TrainingState>();
        private List<string> serverChoices = new List<string>();
        private List<SoftwareProduct> modelChoices = new List<SoftwareProduct>();
        private Dictionary<uint, TrainingState> training = new Dictionary<uint, TrainingState>();
        // An active paid access agreement, keyed by purchasing company ID.
        private Dictionary<uint, uint> access = new Dictionary<uint, uint>();
        private Dictionary<uint, InferenceLoad> inference = new Dictionary<uint, InferenceLoad>();
        private Dictionary<uint, double> abstractDelivery = new Dictionary<uint, double>();
        private Dictionary<uint, WorkSnapshot> snapshots = new Dictionary<uint, WorkSnapshot>();
        private Dictionary<uint, KeyValuePair<SoftwarePort.PortProgress, float>> ports = new Dictionary<uint, KeyValuePair<SoftwarePort.PortProgress, float>>();
        private Dictionary<SimulatedCompany.ProductPrototype, float> rivalProgress = new Dictionary<SimulatedCompany.ProductPrototype, float>();
        private string lastHour = "";
        private string lastMonth = "";
        private float nextRefresh;
        private bool refreshing;

        public override void OnActivate()
        {
            if (active) return;
            active = true;
            GameSettings.GameReady += OnReady;
            GameSettings.OnQuit += OnQuit;
            TimeOfDay.OnHourPassed += OnHour;
            TimeOfDay.OnMonthPassed += OnMonth;
            BindGame();
        }
        public override void OnDeactivate()
        {
            active = false;
            GameSettings.GameReady -= OnReady;
            GameSettings.OnQuit -= OnQuit;
            TimeOfDay.OnHourPassed -= OnHour;
            TimeOfDay.OnMonthPassed -= OnMonth;
            Detach();
            ClosePanelSafely();
            if (hudButton != null) UnityEngine.Object.Destroy(hudButton.gameObject);
            hudButton = null;
            Ready = false;
            game = null; market = null; savedStatePending = true;
        }
        private void OnDestroy() { OnDeactivate(); }
        private void OnReady(object sender, EventArgs args) { BindGame(); }
        private void OnQuit(object sender, EventArgs args)
        {
            Detach();
            training.Clear(); access.Clear(); snapshots.Clear(); ports.Clear(); rivalProgress.Clear();
            preferred.Clear(); revisions.Clear(); rivalTraining.Clear(); revisionSequence = 0; firstPublicRelease = -1;
            savedStatePending = false; Ready = false; game = null; market = null;
            lastHour = ""; lastMonth = "";
            ClosePanelSafely();
            if (hudButton != null) UnityEngine.Object.Destroy(hudButton.gameObject);
            hudButton = null;
        }
        private void BindGame()
        {
            if (!active || GameSettings.Instance == null || HUD.Instance == null || MarketSimulation.Active == null ||
                GameSettings.Instance.MyCompany == null) return;
            if (GameSettings.Instance.IsNetworkMode || GameSettings.Instance.PreSimActive) { Ready = false; return; }
            if (game != GameSettings.Instance || market != MarketSimulation.Active)
            {
                Detach();
                if (!savedStatePending) { training.Clear(); access.Clear(); preferred.Clear(); revisions.Clear(); rivalTraining.Clear(); revisionSequence = 0; firstPublicRelease = -1; lastHour = ""; lastMonth = ""; }
                snapshots.Clear(); ports.Clear();
                game = GameSettings.Instance; market = MarketSimulation.Active;
                market.OnProductReleased += ProductReleased;
                savedStatePending = false;
            }
            Ready = true;
            if (hudButton == null)
            {
                hudButton = HUD.Instance.AddBottomButton("Development", "AI Compute", "Train AI models and manage development assistance", ObjectDatabase.GetIcon("Server"));
                hudButton.Button.onClick.AddListener(OpenPanel);
            }
            Discover();
        }
        private void Detach()
        {
            if (market != null) market.OnProductReleased -= ProductReleased;
            foreach (InferenceLoad load in inference.Values) RemoveInference(load);
            inference.Clear(); abstractDelivery.Clear();
            foreach (TrainingState state in training.Values)
            {
                Unregister(state);
                if (state.Task != null && !state.Task.Done)
                {
                    state.Task.Paused = state.WasPaused;
                    state.Task.Hidden = state.WasHidden;
                    state.Task.AutoDev = state.WasAutoDev;
                }
                state.Task = null;
            }
            snapshots.Clear(); ports.Clear(); rivalProgress.Clear();
        }
        private void Update()
        {
            if (!active) return;
            BindGame();
            if (!Ready) return;
            foreach (TrainingState state in training.Values)
            {
                if (state.Task != null && !state.Completed && !state.Task.Done)
                {
                    // Native PromoteAction rejects zero alpha progress. Keep
                    // training progress solely in this panel until the deadline.
                    ApplyTraining(state);
                    state.Task.InBeta = false;
                    state.Task.Paused = true;
                    state.Task.Hidden = true;
                }
            }
            DiscoverRivalTraining();
            if (window != null && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.5f;
                RefreshLabels();
            }
        }
        private bool IsAI(SoftwareType type) { return type != null && type.Name == AIRules.SoftwareName; }
        private void Discover()
        {
            if (!Ready) return;
            HashSet<uint> live = new HashSet<uint>();
            foreach (WorkItem work in game.MyCompany.WorkItems.ToArray())
            {
                SoftwareAlpha task = work as SoftwareAlpha;
                if (task == null || task.Done || !IsAI(task.Type) || task.AddOn) continue;
                TrainingState saved;
                if (training.TryGetValue(task.ID, out saved)) saved.Task = task;
                if (task.Child != null || (task.InBeta && (saved == null || saved.Completed))) continue;
                live.Add(task.ID);
                TrainingState state;
                if (!training.TryGetValue(task.ID, out state))
                {
                    state = new TrainingState();
                    state.WorkID = task.ID; state.Name = task.SoftwareName;
                    state.Preparation = Preparation(task); state.WasPaused = task.Paused; state.WasHidden = task.Hidden;
                    state.WasAutoDev = task.AutoDev;
                    InitializeTraining(state, task);
                    training.Add(task.ID, state);
                }
                state.Task = task;
                if (!state.Completed)
                {
                    ApplyTraining(state); task.InBeta = false;
                    task.Paused = true; task.Hidden = true;
                    Register(state);
                }
            }
            foreach (TrainingState state in training.Values.ToArray())
            {
                if (state.Completed || live.Contains(state.WorkID)) continue;
                Unregister(state);
                if (state.Task != null) { state.Task.Paused = state.WasPaused; state.Task.Hidden = state.WasHidden; }
                state.Running = false;
                state.Completed = true;
                if (state.Task == null || (state.Task.Done && !state.Task.Released)) training.Remove(state.WorkID);
            }
        }
        private double Preparation(SoftwareAlpha task)
        {
            if (task.Features == null || task.Features.Length == 0) return 0;
            return AIRules.Clamp(task.Features.Average(delegate(SoftwareWorkItem.FeatureProgress f) { return f.CodeTargetQual; }), 0, 1);
        }
        private void Register(TrainingState state)
        {
            if (!state.Running || state.Completed || state.Load != null || String.IsNullOrEmpty(state.Group)) return;
            try
            {
            state.Load = new TrainingLoad(); state.Load.Controller = this; state.Load.State = state;
            game.RegisterWithServer(state.Group, state.Load, true);
            }
            catch (Exception error)
            {
                state.Running = false; state.LastCapacity = 0;
                Unregister(state);
                RecoverPanel("registering training servers; this project was paused", error);
            }
        }
        private void Unregister(TrainingState state)
        {
            if (state.Load == null) return;
            string previous = state.Group;
            try
            {
                if (game != null) game.DeregisterServerItem(state.Load);
                state.Load = null;
            }
            catch (Exception error)
            {
                // Retain the workload reference for a later cleanup attempt.
                // A paused workload requests zero server capacity.
                state.Running = false; state.LastCapacity = 0;
                RecoverPanel("releasing training servers; this project was paused", error);
            }
            finally { state.Group = previous; }
        }
        private void OnHour(object sender, EventArgs args)
        {
            if (!Ready || !active) return;
            SDateTime now = SDateTime.Now();
            string stamp = now.Year + ":" + now.Month + ":" + now.Day + ":" + now.Hour;
            if (stamp == lastHour) return;
            lastHour = stamp;
            Discover();
            foreach (TrainingState state in training.Values.ToArray())
            {
                if (!state.Running || state.Completed || state.Task == null || state.Task.Done) continue;
                double capacity = state.Load != null && state.Load.HasSample ? state.Load.ServedCapacity : 0;
                state.LastCapacity = capacity;
                bool finished = ExpansionRules.Advance(state.Tier, state.Config, GameSettings.DaysPerMonth, state.Running, capacity, ref state.Elapsed, ref state.Compute);
                if (state.Load != null) { state.Load.HasSample = false; state.Load.ServedCapacity = 0; }
                ApplyTraining(state);
                if (finished) Complete(state);
            }
            foreach (InferenceLoad load in inference.Values) load.ConsumeSample();
            SyncInference();
            AdvanceRivalTraining();
            UpdateBenchmarkDiscovery();
            AccelerateWork();
            AccelerateRivals();
            RefreshLabels();
        }
        private int Domain(string feature)
        {
            for (int i = 0; i < AIRules.Domains.Length; i++)
                if (feature.IndexOf(AIRules.Domains[i], StringComparison.OrdinalIgnoreCase) >= 0) return i;
            return 0;
        }
        private void ApplyTraining(TrainingState state)
        {
            SoftwareAlpha task = state.Task;
            if (task == null || task.Features == null) return;
            int[] count = Counts(task.GetFeatures());
            state.Scores = ExpansionRules.Scores(state.Tier, state.Config, state.Compute, state.Elapsed, state.Preparation, count, state.Seed);
            double overall = MeanScores(state.Scores);
            double coverage = AIRules.Clamp(state.Elapsed / ExpansionRules.Window(state.Tier, state.Config), 0, 1);
            double progress = ExpansionRules.NativeProgress(state.Tier, state.Config, state.Elapsed);
            foreach (SoftwareWorkItem.FeatureProgress f in task.Features)
            {
                // Compute owns the training state; ordinary staff cannot fill it.
                double quality = AIRules.NativeQuality(state.Scores[Domain(f.Feature.Name)]);
                f.Progress = f.CDevTime * progress; f.ArtProgress = f.ADevTime * progress;
                f.Qual = quality; f.Qual2 = quality;
                f.CodeDone = progress >= 1 || f.CDevTime <= 0;
                f.ArtDone = progress >= 1 || f.ADevTime <= 0;
            }
            task.RefreshWorkDevTime(false);
            task.HasFinishedCode = progress >= 1; task.HasFinishedArt = progress >= 1; task.HasFinished = progress >= 1;
            // Evaluation is real beta work. Low capability also creates more fixes.
            task.Bugs = (float)(task.MaxBugs * (0.03 + 0.20 * (1 - overall / 100)) * coverage);
        }
        private void Complete(TrainingState state)
        {
            Unregister(state);
            state.Running = false; state.Completed = true;
            SoftwareAlpha task = state.Task;
            if (task == null || task.Done) return;
            PrepareEvaluationSchedule(task);
            task.Paused = false; task.Hidden = state.WasHidden;
            if (!task.InBeta)
            {
                bool previous = task.AutoDev;
                try { task.AutoDev = true; task.PromoteAction(); }
                finally { task.AutoDev = previous; }
            }
            task.AutoDev = state.WasAutoDev;
            Message("Training finished for " + state.Name + ". Assign employees to beta evaluation, then release. Open AI Compute to inspect its capability scores.");
        }
        private void ProductReleased(object sender, SoftwareProduct product)
        {
            if (!IsAI(product.Type)) return;
            TrainingState state = training.Values.FirstOrDefault(delegate(TrainingState s)
            {
                return s.Task != null && (System.Object.ReferenceEquals(s.Task.Final, product) ||
                    (s.Task.SWID.HasValue && s.Task.SWID.Value == product.ID));
            });
            if (state != null)
            {
                // Score state is already applied before promotion; record it for
                // assistance, licensing and the product's benchmark display.
                state.ProductID = product.ID;
                StoreModel(product, state.Tier, state.Compute, state.Elapsed, state.Scores);
                StoreConfig(product, state.Config);
                product.PutVar(AIRules.Prefix + "TrainingServerSpend", state.ServerSpend);
                product.PutVar(AIRules.Prefix + "SpendObservedFrom", state.SpendObservedFrom);
                RefreshModelServing(product);
                TransferRevisionAccess(product);
                Unregister(state); training.Remove(state.WorkID);
            }
            else EnsureModel(product);
            if (product.DevCompany == game.MyCompany && !product.InHouse && firstPublicRelease < 0)
                firstPublicRelease = product.Release.ToInt();
            UpdateBenchmarkDiscovery();
        }
        private void StoreModel(SoftwareProduct product, int tier, double compute, double elapsed, double[] scores)
        {
            product.PutVar(AIRules.Prefix + "Tier", tier);
            product.PutVar(AIRules.Prefix + "Compute", compute);
            product.PutVar(AIRules.Prefix + "TrainingMonths", elapsed);
            for (int i = 0; i < 5; i++) product.PutVar(AIRules.Prefix + AIRules.Domains[i], scores[i]);
            product.PutVar(AIRules.Prefix + "Recorded", true);
            // Preserve the native feature-derived base, so reactivation is idempotent.
            double baseServing = Math.Max(0.0005, SoftwareType.GetServerRequirement(product.Features, 1));
            product.ServerReq = (float)(baseServing * AIRules.ServingFactors[AIRules.Tier(tier)]);
            product.RegisterServer();
        }
        private double Value(SoftwareProduct product, string name, double fallback)
        {
            try { return Convert.ToDouble(product.GetVar(AIRules.Prefix + name, fallback), CultureInfo.InvariantCulture); }
            catch (FormatException) { return fallback; }
            catch (InvalidCastException) { return fallback; }
        }
        private void EnsureModel(SoftwareProduct product)
        {
            if (Convert.ToBoolean(product.GetVar(AIRules.Prefix + "Recorded", false))) return;
            RecordRivalModel(product);
        }
        private SoftwareProduct Assistant(Company company)
        {
            List<SoftwareProduct> candidates = new List<SoftwareProduct>();
            List<double> potential = new List<double>();
            List<double> fractions = new List<double>();
            uint licensed;
            access.TryGetValue(company.ID, out licensed);
            HashSet<SoftwareProduct> native = new HashSet<SoftwareProduct>(company.GetLicenses());
            uint requested;
            bool pinned = preferred.TryGetValue(company.ID, out requested);
            int requestedIndex = -1;
            foreach (SoftwareProduct product in market.GetAllProducts(false))
            {
                if (!IsAI(product.Type) || product.Archived || product.IsMock) continue;
                bool owned = product.DevCompany == company;
                if (!owned && (product.InHouse || (product.ID != licensed && !native.Contains(product)))) continue;
                EnsureModel(product);
                double fraction = PredictedServing(product, company);
                // An explicit choice is a provider lock, including outages.
                // Keep registering demand so an idle chosen model can recover.
                if (pinned && requested == product.ID) requestedIndex = candidates.Count;
                candidates.Add(product); potential.Add(ModelBoost(product)); fractions.Add(fraction);
            }
            int selected = ExpansionRules.ChooseProvider(potential.ToArray(), fractions.ToArray(), pinned, requestedIndex);
            return selected >= 0 ? candidates[selected] : null;
        }
        private double ModelBoost(SoftwareProduct product)
        {
            double[] s = ModelScores(product);
            return Math.Max(ExpansionRules.Bonus(s, "System", 0, false, false),
                Math.Max(ExpansionRules.Bonus(s, "System", 1, false, false),
                Math.Max(Math.Max(ExpansionRules.Bonus(s, "2D", 1, false, false), ExpansionRules.Bonus(s, "Audio", 1, false, false)),
                Math.Max(ExpansionRules.Bonus(s, "2D", 2, HasFeature(product, "Vision image generation"), false), ExpansionRules.Bonus(s, "Audio", 2, false, HasFeature(product, "Speech audio generation"))))));
        }
        private double ServingFraction(SoftwareProduct model)
        {
            if (model == null) return 0;
            InferenceLoad load;
            if (inference.TryGetValue(model.ID, out load)) return load.LastDelivery;
            double fraction;
            return abstractDelivery.TryGetValue(model.ID, out fraction) ? fraction : 0;
        }
        private double EffectiveBoost(SoftwareProduct model)
        { return model == null ? 0 : AIRules.DeliveredAssistance(ModelBoost(model), ServingFraction(model)); }
        private void RemoveInference(InferenceLoad load)
        {
            string previous = load.Group;
            if (game != null) game.DeregisterServerItem(load);
            load.Model.PutVar(AIRules.Prefix + "InferenceGroup", previous);
        }
        private void SyncInference()
        {
            Dictionary<SoftwareProduct, int> clients = new Dictionary<SoftwareProduct, int>();
            Dictionary<SoftwareProduct, double> demand = new Dictionary<SoftwareProduct, double>();
            Dictionary<SoftwareProduct, bool> external = new Dictionary<SoftwareProduct, bool>();
            foreach (Company company in market.Companies.Values.Cast<Company>().Concat(new Company[] { game.MyCompany }).Distinct())
            {
                SoftwareProduct model = Assistant(company);
                if (model == null) continue;
                double workers = ActiveEmployees(company); if (workers <= 0) continue;
                int count; clients.TryGetValue(model, out count); clients[model] = count + 1;
                double amount; demand.TryGetValue(model, out amount);
                demand[model] = amount + ExpansionRules.CorporateDemand((int)Value(model, "Tier", 0), ModelConfig(model), Counts(model.Features), workers);
                bool remote; external.TryGetValue(model, out remote); external[model] = remote || company != model.DevCompany;
            }
            HashSet<uint> live = new HashSet<uint>();
            abstractDelivery.Clear();
            foreach (KeyValuePair<SoftwareProduct, int> pair in clients)
            {
                SoftwareProduct model = pair.Key;
                int tier = AIRules.Tier((int)Value(model, "Tier", 0));
                if (model.DevCompany != game.MyCompany)
                {
                    // Rival companies have no physical office/server simulation.
                    // Charge a virtual hosting bill instead of granting free compute.
                    double bill = (demand[model] + ExpansionRules.Memory(tier, ModelConfig(model))) * 0.2 * AIRules.MonthStep(GameSettings.DaysPerMonth);
                    bool funded = model.DevCompany.CanMakeTransaction(-bill);
                    if (funded) model.DevCompany.MakeTransaction(-bill, Company.TransactionCategory.Bills, "AI inference hosting: " + model.Name, false);
                    abstractDelivery[model.ID] = funded ? 1 : 0;
                    continue;
                }
                live.Add(model.ID);
                InferenceLoad load;
                if (!inference.TryGetValue(model.ID, out load))
                {
                    load = new InferenceLoad(); load.Model = model;
                    load.Group = Convert.ToString(model.GetVar(AIRules.Prefix + "InferenceGroup", model.Server ?? ""), CultureInfo.InvariantCulture);
                    inference.Add(model.ID, load);
                    game.RegisterWithServer(load.Group, load, true);
                }
                load.Clients = pair.Value;
                load.ExternalClients = external[model];
                load.Demand = demand[model] + ExpansionRules.Memory(tier, ModelConfig(model));
            }
            foreach (uint id in inference.Keys.ToArray())
                if (!live.Contains(id)) { RemoveInference(inference[id]); inference.Remove(id); }
        }
        private void TakeSnapshot(SoftwareWorkItem task)
        {
            WorkSnapshot snap = new WorkSnapshot(); snap.Task = task;
            snap.Progress = task.Features.Select(delegate(SoftwareWorkItem.FeatureProgress f) { return f.Progress; }).ToArray();
            snap.Art = task.Features.Select(delegate(SoftwareWorkItem.FeatureProgress f) { return f.ArtProgress; }).ToArray();
            snap.Quality = task.Features.Select(delegate(SoftwareWorkItem.FeatureProgress f) { return f.Qual; }).ToArray();
            snap.ArtQuality = task.Features.Select(delegate(SoftwareWorkItem.FeatureProgress f) { return f.Qual2; }).ToArray();
            SoftwareAlpha alpha = task as SoftwareAlpha;
            if (alpha != null) { snap.FixedBugs = alpha.FixedBugs; snap.Beta = alpha.InBeta; }
            SoftwareUpdate update = task as SoftwareUpdate;
            if (update != null) snap.FixedBugs = update.FixedBugs;
            snapshots[task.ID] = snap;
        }
        private void AccelerateWork()
        {
            SoftwareProduct assistant = Assistant(game.MyCompany);
            double boost = EffectiveBoost(assistant);
            double fixesBoost = WorkBonus(assistant, "System", 3);
            HashSet<uint> live = new HashSet<uint>();
            foreach (WorkItem work in game.MyCompany.WorkItems.ToArray())
            {
                if (work.Done) continue;
                live.Add(work.ID);
                SoftwareWorkItem task = work as SoftwareWorkItem;
                SoftwarePort port = work as SoftwarePort;
                if (port != null)
                {
                    KeyValuePair<SoftwarePort.PortProgress, float> before;
                    if (!port.Paused && port.Current != null && ports.TryGetValue(port.ID, out before) && before.Key == port.Current && !port.Current.Finished)
                        port.Current.Progress = (float)AIRules.AssistProgress(port.Current.Progress, before.Value, port.Current.Goal, fixesBoost);
                    if (port.Current != null) ports[port.ID] = new KeyValuePair<SoftwarePort.PortProgress, float>(port.Current, port.Current.Progress);
                }
                if (task == null || task.Features == null || IsAI(task.Type)) continue;
                WorkSnapshot snap;
                if (boost > 0 && !task.Paused && snapshots.TryGetValue(task.ID, out snap) && snap.Task == task && snap.Progress.Length == task.Features.Length)
                {
                    SoftwareAlpha alpha = task as SoftwareAlpha;
                    SoftwareUpdate update = task as SoftwareUpdate;
                    DesignDocument design = task as DesignDocument;
                    if (alpha != null && alpha.InBeta && snap.Beta)
                        alpha.FixedBugs = (float)AIRules.AssistProgress(alpha.FixedBugs, snap.FixedBugs, alpha.Bugs, fixesBoost);
                    else
                    {
                        for (int i = 0; i < task.Features.Length; i++)
                        {
                            SoftwareWorkItem.FeatureProgress f = task.Features[i];
                            // Bonus uses only observed employee work, with the
                            // post-bonus snapshot preventing recursive free work.
                            double maxCode = design != null ? f.DevTime : f.CDevTime;
                            double codeBoost = WorkBonus(assistant, f.Feature.Spec, design != null ? 0 : 1);
                            double artBoost = WorkBonus(assistant, f.Feature.Spec, design != null ? 0 : 2);
                            f.Progress = AIRules.AssistProgress(f.Progress, snap.Progress[i], maxCode, codeBoost);
                            f.ArtProgress = AIRules.AssistProgress(f.ArtProgress, snap.Art[i], f.ADevTime, artBoost);
                            f.Qual = AIRules.AssistProgress(f.Qual, snap.Quality[i], f.CodeTargetQual, codeBoost);
                            f.Qual2 = AIRules.AssistProgress(f.Qual2, snap.ArtQuality[i], f.ArtTargetQual, artBoost);
                            f.UpdateStatus(design != null, 1);
                        }
                        task.RefreshWorkDevTime(false);
                        if (alpha != null)
                        {
                            alpha.HasFinishedCode = task.AllDone(false, false, true);
                            alpha.HasFinishedArt = task.AllDone(false, true, false);
                            alpha.HasFinished = alpha.HasFinishedCode && alpha.HasFinishedArt;
                        }
                        if (update != null)
                        {
                            update.FixedBugs = (float)AIRules.AssistProgress(update.FixedBugs, snap.FixedBugs, update.Target.FixableBugs, fixesBoost);
                            update.HasFinishedCode = task.AllDone(false, false, true);
                            update.HasFinishedArt = task.AllDone(false, true, false);
                            update.HasFinished = update.HasFinishedCode && update.HasFinishedArt;
                        }
                    }
                }
                TakeSnapshot(task);
            }
            foreach (uint id in snapshots.Keys.ToArray()) if (!live.Contains(id)) snapshots.Remove(id);
            foreach (uint id in ports.Keys.ToArray()) if (!live.Contains(id)) ports.Remove(id);
        }
        private double Overall(SoftwareProduct model)
        {
            List<double> selected = new List<double>();
            foreach (string domain in AIRules.Domains) { double score = Value(model, domain, 0); if (score > 0) selected.Add(score); }
            return selected.Count == 0 ? 0 : selected.Average();
        }
        private void AccelerateRivals()
        {
            HashSet<SimulatedCompany.ProductPrototype> live = new HashSet<SimulatedCompany.ProductPrototype>();
            foreach (SimulatedCompany company in market.Companies.Values)
            {
                SoftwareProduct model = Assistant(company);
                double boost = EffectiveBoost(model);
                foreach (SimulatedCompany.ProductPrototype project in company.Releases.Concat(company.ProjectQueue))
                {
                    if (project.Released || !project.DevStarted || IsAI(project.Type)) continue;
                    live.Add(project);
                    float previous;
                    if (rivalProgress.TryGetValue(project, out previous))
                    {
                        double naturalWork = Math.Max(0, previous - project.DevTimeLeft);
                        double bonus = Math.Min(project.DevTimeLeft, naturalWork * boost);
                        project.DevTimeLeft = (float)Math.Max(0, project.DevTimeLeft - bonus);
                        // Native rivals release on ReleaseDate, rather than just
                        // when DevTimeLeft reaches zero. Move both together.
                        if (bonus > 0 && !project.IsInDeal())
                            project.ReleaseDate = SDateTime.FromInt(AIRules.AdvanceReleaseMinutes(project.ReleaseDate.ToInt(), SDateTime.Now().ToInt(), bonus, GameSettings.DaysPerMonth));
                    }
                    rivalProgress[project] = project.DevTimeLeft;
                }
            }
            foreach (SimulatedCompany.ProductPrototype project in rivalProgress.Keys.ToArray())
                if (!live.Contains(project)) rivalProgress.Remove(project);
        }
        private void RivalAdoption()
        {
            List<SoftwareProduct> publicModels = market.GetAllProducts(false).Where(delegate(SoftwareProduct p)
                { return IsAI(p.Type) && !p.IsMock && !p.InHouse && !p.Archived; }).ToList();
            foreach (SoftwareProduct model in publicModels) EnsureModel(model);
            foreach (SimulatedCompany buyer in market.Companies.Values)
            {
                SoftwareProduct current = Assistant(buyer);
                double currentBoost = current == null ? 0 : ModelBoost(current);
                SoftwareProduct candidate = null;
                foreach (SoftwareProduct model in publicModels)
                {
                    if (model.DevCompany == buyer) continue;
                    double fee = AIRules.LicenseFee((int)Value(model, "Tier", 0), Overall(model));
                    if (fee > Math.Max(500, buyer.Money * 0.02) || !buyer.CanMakeTransaction(-fee * 3)) continue;
                    if (ModelBoost(model) > currentBoost + 0.05)
                    { currentBoost = ModelBoost(model); candidate = model; }
                }
                if (candidate != null) access[buyer.ID] = candidate.ID;
                else if (current != null && current.DevCompany == buyer) access.Remove(buyer.ID);
            }
        }
        private void OnMonth(object sender, EventArgs args)
        {
            if (!Ready) return;
            string stamp = SDateTime.Now().Year + ":" + SDateTime.Now().Month;
            if (lastMonth == stamp) return;
            lastMonth = stamp;
            RivalAdoption();
            MeterSubscriptions();
            foreach (KeyValuePair<uint, uint> pair in access.ToArray())
            {
                Company buyer = pair.Key == game.MyCompany.ID ? game.MyCompany : market.GetCompany(pair.Key);
                SoftwareProduct model = market.GetProduct(pair.Value, false, false, false);
                if (buyer == null || model == null || model.InHouse || model.Archived) { access.Remove(pair.Key); continue; }
                double fee = AIRules.LicenseFee((int)Value(model, "Tier", 0), Overall(model));
                if (!buyer.CanMakeTransaction(-fee)) { access.Remove(pair.Key); continue; }
                TransferLicense(buyer, model, fee);
            }
        }
        private void TransferLicense(Company buyer, SoftwareProduct model, double amount)
        {
            buyer.MakeTransaction(-amount, Company.TransactionCategory.Licenses, "AI model access: " + model.Name, false);
            model.DevCompany.MakeTransaction(amount, Company.TransactionCategory.Licenses, "AI model license: " + model.Name, false);
            model.LicenseSum += amount;
        }
        private void Message(string text)
        {
            try { if (WindowManager.Instance != null) WindowManager.Instance.ShowMessageBox(text, true, DialogWindow.DialogType.Information, (GUIWindow)null); }
            catch (Exception error) { try { Debug.LogWarning("AI Compute could not display its message: " + error.ToString()); } catch (Exception) { } }
        }
        public void OpenPanel()
        {
            if (!Ready) return;
            refreshing = true;
            try
            {
            if (window != null) { RefreshChoices(); if (window != null) window.Show(); return; }
            List<XMLParser.XMLNode> nodes = ParentMod.LoadFullXMLFile("TrainingPanel.xml");
            if (nodes.Count != 1 || nodes[0].Name.ToLowerInvariant() != "window") throw new InvalidOperationException("TrainingPanel.xml must contain one Window root.");
            // Track the root before generating children, so a native XML error
            // cannot leave an unreachable half-built window on the canvas.
            window = WindowManager.SpawnWindow();
            window.NonLocTitle = "AI Compute"; window.MinSize = new Vector2(760, 720); window.OnlyHide = false;
            window.GetComponent<RectTransform>().sizeDelta = new Vector2(780, 740);
            window.ShowCentered = true;
            GUIWindow createdWindow = window;
            createdWindow.OnClose += delegate { if (System.Object.ReferenceEquals(window, createdWindow)) ResetPanelReferences(); };
            Dictionary<string, Component> ui = WindowManager.GenerateUI(nodes[0].Children, window.MainPanel, this);
            ui["window"] = window;
            projects = (GUICombobox)ui["projects"]; servers = (GUICombobox)ui["servers"];
            sizes = (GUICombobox)ui["sizes"]; budgets = (GUICombobox)ui["budgets"];
            models = (GUICombobox)ui["models"]; status = (Text)ui["status"]; modelInfo = (Text)ui["modelInfo"];
            inferenceServers = (GUICombobox)ui["inferenceServers"];
            if (!InitializeExpansionUI(ui) || window == null) return;
            if (!InitializeTrainingUI(ui) || window == null) return;
            budgets.UpdateContent(new string[] { "1% of group", "5% of group", "10% of group", "25% of group", "50% of group", "100% of group" });
            budgets.UpdateSelectionSupressEvents(2);
            if (!WireTrainingUI() || window == null) return;
            refreshing = false;
            RefreshChoices();
            if (window == null) return;
            window.Show();
            lastPanelError = "";
            }
            catch (Exception error)
            {
                RecoverPanel("opening the training window", error);
            }
            finally { refreshing = false; }
        }
        private TrainingState Selection()
        {
            return projects != null && projects.Selected >= 0 && projects.Selected < projectChoices.Count ? projectChoices[projects.Selected] : null;
        }
        public void Changed(GUICombobox combo)
        {
            if (refreshing || window == null || !Ready) return;
            RunPanelAction("changing a training selection", delegate
            {
            TrainingState state = Selection();
            if (combo == projects && state != null)
            {
                int tierIndex = tierChoices.IndexOf(state.Tier);
                SafeSelection(sizes, tierIndex);
                SelectConfig(state.Config);
                SafeSelection(budgets, BudgetIndex(state.Allocation));
                if (window == null) return;
                int index = serverChoices.IndexOf(state.Group); if (index >= 0) servers.UpdateSelectionSupressEvents(index);
            }
            if (combo == models && models.Selected >= 0 && models.Selected < modelChoices.Count)
            {
                SoftwareProduct model = modelChoices[models.Selected];
                string group = Convert.ToString(model.GetVar(AIRules.Prefix + "InferenceGroup", model.Server ?? ""), CultureInfo.InvariantCulture);
                int index = serverChoices.IndexOf(group);
                if (index >= 0) inferenceServers.UpdateSelectionSupressEvents(index);
            }
            RefreshLabels();
            });
        }
        public void RefreshChoices()
        {
            if (window == null || !Ready) return;
            RunPanelAction("refreshing project and model choices", delegate
            {
            TrainingState previous = Selection();
            TrainingConfig preview = ReadConfig();
            int previewTier = SelectedTier(), previewBudget = budgets.Selected;
            string previewServer = servers.Selected >= 0 && servers.Selected < serverChoices.Count ? serverChoices[servers.Selected] : "";
            string previewInference = inferenceServers.Selected >= 0 && inferenceServers.Selected < serverChoices.Count ? serverChoices[inferenceServers.Selected] : "";
            SoftwareProduct previousModel = models.Selected >= 0 && models.Selected < modelChoices.Count ? modelChoices[models.Selected] : null;
            refreshing = true;
            try
            {
                Discover();
                projectChoices = training.Values.Where(delegate(TrainingState s) { return s.Task != null && !s.Task.Done; }).OrderBy(delegate(TrainingState s) { return s.Name; }).ToList();
                projects.UpdateContent(projectChoices.Select(delegate(TrainingState s) { return s.Name; }));
                if (previous != null && projectChoices.Contains(previous)) projects.UpdateSelectionSupressEvents(projectChoices.IndexOf(previous));
                serverChoices = game.GetAllServerGroups(false, false).Where(delegate(ServerGroup s) { return s != null && !s.IsCloud; }).Select(delegate(ServerGroup s) { return s.Name; }).ToList();
                servers.UpdateContent(serverChoices);
                inferenceServers.UpdateContent(serverChoices);
                RefreshTiers();
                if (window == null) return;
                modelChoices = market.GetAllProducts(false).Where(delegate(SoftwareProduct p) { return IsAI(p.Type) && !p.IsMock && !p.Archived && (!p.InHouse || p.DevCompany == game.MyCompany); }).ToList();
                foreach (SoftwareProduct model in modelChoices) EnsureModel(model);
                models.UpdateContent(modelChoices.Select(delegate(SoftwareProduct p) { return p.Name + (p.DevCompany == game.MyCompany ? " (owned)" : " - " + p.DevCompany.Name); }));
                if (previousModel != null && modelChoices.Contains(previousModel)) models.UpdateSelectionSupressEvents(modelChoices.IndexOf(previousModel));
                else
                {
                    uint pinnedID;
                    if (preferred.TryGetValue(game.MyCompany.ID, out pinnedID))
                    {
                        int pinnedIndex = modelChoices.FindIndex(delegate(SoftwareProduct p) { return p.ID == pinnedID; });
                        if (pinnedIndex >= 0) SafeSelection(models, pinnedIndex);
                    }
                }
            }
            finally { refreshing = false; }
            Changed(projects);
            Changed(models);
            if (window == null) return;
            if (previous != null && Selection() == previous)
            {
                SelectConfig(preview); SafeSelection(sizes, tierChoices.IndexOf(previewTier)); SafeSelection(budgets, previewBudget);
                int serverIndex = serverChoices.IndexOf(previewServer);
                if (serverIndex >= 0) SafeSelection(servers, serverIndex);
            }
            if (previousModel != null && models.Selected >= 0 && models.Selected < modelChoices.Count && modelChoices[models.Selected] == previousModel)
            {
                int serverIndex = serverChoices.IndexOf(previewInference);
                if (serverIndex >= 0) SafeSelection(inferenceServers, serverIndex);
            }
            RefreshLabels();
            });
        }
        public void StartTraining()
        {
            if (!Ready || window == null) return;
            RunPanelAction("starting or resuming training", delegate
            {
            TrainingState state = Selection();
            if (state == null) { Message("Finish preparing an Artificial Intelligence project, then refresh this panel."); return; }
            if (state.Completed) { Message("Training is complete. Assign employees to beta evaluation, then release this model."); return; }
            int chosenTier = SelectedTier();
            TrainingConfig chosenConfig = ReadConfig();
            string locked = ExpansionRules.LockedReason(chosenTier, chosenConfig, SDateTime.Now().RealYear);
            if (locked.Length > 0) { Message(locked); return; }
            if (state.DatasetPaid && (state.Tier != chosenTier || !SameConfig(state.Config, chosenConfig))) { Message("Training configuration locks when the dataset is purchased. Start a new project or revision to change it."); return; }
            if (state.Config.Mode == 1 && (state.Tier != chosenTier || state.Config.Architecture != chosenConfig.Architecture)) { Message("Fine-tuning preserves checkpoint size and architecture. Use full retraining to change either."); return; }
            chosenConfig.Mode = state.Config.Mode;
            int[] counts = Counts(state.Task.GetFeatures());
            if (chosenConfig.Context > 0 && !state.Task.GetFeatures().Any(delegate(FeatureBase f) { return f.Name == "Reasoning long context"; })) { Message("Include Reasoning long context in the design before selecting an extended context window."); return; }
            if (chosenConfig.Focus > 0 && counts[chosenConfig.Focus - 1] == 0) { Message("The selected training focus requires its capability module in the prepared design."); return; }
            if (servers.Selected < 0 || servers.Selected >= serverChoices.Count) { Message("Build and name a physical server group first."); return; }
            ServerGroup group;
            string selected = serverChoices[servers.Selected];
            if (!game.TryGetServerGroup(selected, out group) || group.Broken || group.PowerSum <= 0) { Message("Choose a functioning server group."); return; }
            double fee = ExpansionRules.DatasetFee(chosenTier, chosenConfig, counts);
            if (!state.DatasetPaid)
            {
                if (!game.MyCompany.CanMakeTransaction(-fee)) { Message("Dataset licensing and curation costs $" + fee.ToString("N0") + ". Insufficient funds."); return; }
                game.MyCompany.MakeTransaction(-fee, Company.TransactionCategory.Licenses, "AI dataset: " + state.Name, false);
                state.DatasetPaid = true;
            }
            Unregister(state);
            if (state.Load != null || window == null) return;
            state.Group = selected; state.Tier = chosenTier; state.Config = chosenConfig;
            state.Allocation = SelectedAllocation();
            state.LastCapacity = 0;
            state.Running = true;
            Register(state); RefreshLabels();
            });
        }
        public void PauseTraining()
        {
            RunPanelAction("pausing training", delegate
            {
            TrainingState state = Selection(); if (state == null) return;
            state.Running = false; Unregister(state); RefreshLabels();
            });
        }
        public void CancelTraining()
        {
            RunPanelAction("requesting project cancellation", delegate
            {
            TrainingState state = Selection(); if (state == null || state.Task == null) return;
            WindowManager.Instance.ShowMessageBox("Cancel " + state.Name + "? Its design and training progress will be lost.", true,
                DialogWindow.DialogType.Question, delegate
                {
                    RunPanelAction("cancelling this development project", delegate
                    {
                    if (!Ready || state.Task == null || state.Task.Done) return;
                    Unregister(state);
                    if (state.Load != null) return;
                    state.Task.Hidden = state.WasHidden; state.Task.Paused = state.WasPaused;
                    state.Task.Kill(true); training.Remove(state.WorkID); RefreshChoices();
                    });
                }, "Cancel project", null, true);
            });
        }
        public void AcquireModel()
        {
            if (!Ready || window == null) return;
            RunPanelAction("licensing a commercial model", delegate
            {
            if (models.Selected < 0 || models.Selected >= modelChoices.Count) return;
            SoftwareProduct model = modelChoices[models.Selected];
            if (model.DevCompany == game.MyCompany) { Message("Your company automatically uses the best served owned or licensed model. Use Prefer selected model to choose a priority; unavailable models fall back automatically."); return; }
            if (model.InHouse || model.Archived) { Message("This model is not available for licensing."); return; }
            uint current;
            if (access.TryGetValue(game.MyCompany.ID, out current) && current == model.ID) return;
            double fee = AIRules.LicenseFee((int)Value(model, "Tier", 0), Overall(model));
            if (!game.MyCompany.CanMakeTransaction(-fee)) { Message("Insufficient funds for the first month of model access."); return; }
            TransferLicense(game.MyCompany, model, fee);
            access[game.MyCompany.ID] = model.ID;
            RefreshLabels();
            });
        }
        public void CancelAccess() { if (Ready) access.Remove(game.MyCompany.ID); RefreshLabels(); }
        public void AssignInference()
        {
            RunPanelAction("assigning inference servers", delegate
            {
            if (!Ready || models.Selected < 0 || models.Selected >= modelChoices.Count) return;
            SoftwareProduct model = modelChoices[models.Selected];
            if (model.DevCompany != game.MyCompany) { Message("The model provider manages inference servers. Select an owned model to assign your servers."); return; }
            if (inferenceServers.Selected < 0 || inferenceServers.Selected >= serverChoices.Count) { Message("Create a physical server group first."); return; }
            string selected = serverChoices[inferenceServers.Selected];
            ServerGroup group;
            if (!game.TryGetServerGroup(selected, out group) || group.IsCloud || group.Broken || group.PowerSum <= 0) { Message("Choose a functioning physical server group."); return; }
            InferenceLoad load;
            if (inference.TryGetValue(model.ID, out load))
            {
                game.DeregisterServerItem(load);
                load.LastDelivery = 0; load.HasSample = false; load.Sample = 0;
                game.RegisterWithServer(selected, load, true);
            }
            model.PutVar(AIRules.Prefix + "InferenceGroup", selected);
            RefreshLabels();
            });
        }
        private void RefreshLabels()
        {
            if (window == null || !Ready) return;
            RunPanelAction("refreshing training status", delegate
            {
            TrainingState state = Selection();
            StringBuilder text = new StringBuilder();
            if (state == null) text.Append(SDateTime.Now().RealYear < 2018 ? "AI research opens in 2018. Build your company and server infrastructure first." : "No model awaiting training. Create Artificial Intelligence software and finish employee preparation, then refresh.");
            else
            {
                int tier = state.DatasetPaid ? state.Tier : SelectedTier();
                TrainingConfig cfg = state.DatasetPaid ? state.Config : ReadConfig(); cfg.Mode = state.Config.Mode;
                text.Append(state.Name + " | " + AIRules.TierNames[tier] + "\n");
                text.AppendFormat(CultureInfo.InvariantCulture, "Training: {0:F2}/{1:F2} months | compute invested: {2:N0} capacity-months\n", state.Elapsed, ExpansionRules.Window(tier, cfg), state.Compute);
                text.Append(state.Completed ? "Training complete. Employees can evaluate and release this model.\n" : state.Running ? "Training active. More servers improve capability; the window stays fixed.\n" : "Training paused / awaiting allocation.\n");
                if (advancedView) {
                text.AppendFormat(CultureInfo.InvariantCulture, "Capacity ceiling: {0:F0}/100 | serving load: {1:F2}x the 60B model\n", AIRules.Ceilings[tier], AIRules.ServingFactors[tier]);
                for (int i = 0; i < 5; i++) text.AppendFormat(CultureInfo.InvariantCulture, "{0}: {1:F1}  ", AIRules.Domains[i], state.Scores[i]);
                text.Append("\nAbsent capability modules score zero. Outages waste compute opportunity during the fixed window.");
                text.AppendFormat(CultureInfo.InvariantCulture, "\nDataset: ${0:N0} {1} | {2} | {3}\n", ExpansionRules.DatasetFee(tier, cfg, Counts(state.Task.GetFeatures())), state.DatasetPaid ? "paid" : "charged on start", ExpansionRules.Architectures[cfg.Architecture], cfg.Mode == 1 ? "checkpoint fine-tuning" : "full training");
                double[] shares = ExpansionRules.Shares(Counts(state.Task.GetFeatures()), cfg);
                for (int i = 0; i < 5; i++) if (shares[i] > 0) text.AppendFormat(CultureInfo.InvariantCulture, "{0} compute: {1:P0}  ", AIRules.Domains[i], shares[i]);
                }
            }
            status.text = text.ToString();
            FitPanelLabel(status);
            RefreshTrainingUsage(state);
            if (window == null) return;
            text.Length = 0;
            text.Append(RivalResearchOpen() ? "Rival research is open.\n" : firstPublicRelease < 0 ? "Frontier protected: rivals wait for your first public AI.\n" : "First-release head start: rivals wait six months.\n");
            SoftwareProduct assistant = Assistant(game.MyCompany);
            uint pinnedModel;
            text.Append(preferred.TryGetValue(game.MyCompany.ID, out pinnedModel) ? "Provider locked: no automatic switching.\n" : "Automatic provider selection.\n");
            text.Append(assistant == null ? "Development assistant: none.\n" : String.Format(CultureInfo.InvariantCulture, "Development assistant: {0} | up to {1:F2}x work, depending on capability ({2:P0} serving)\n", assistant.Name, 1 + EffectiveBoost(assistant), ServingFraction(assistant)));
            if (assistant != null && ServingFraction(assistant) <= 0) text.Append("Waiting for inference capacity; assign a working server group.\n");
            if (models.Selected >= 0 && models.Selected < modelChoices.Count)
            {
                SoftwareProduct model = modelChoices[models.Selected];
                text.Append(model.Name + " | " + AIRules.TierNames[AIRules.Tier((int)Value(model, "Tier", 0))] + "\n");
                text.AppendFormat(CultureInfo.InvariantCulture, "Benchmark: {0:F1}/100 | organic discovery target: {1:P0}\n", Overall(model), Value(model, "BenchmarkDiscovery", 0));
                double serverSpend = Value(model, "TrainingServerSpend", -1);
                if (serverSpend >= 0) text.AppendFormat(CultureInfo.InvariantCulture, "Recorded training server cost: ${0:N0}{1}\n", serverSpend, Value(model, "SpendObservedFrom", 0) > 0 ? " (partial history)" : "");
                if (advancedView) {
                    foreach (string domain in AIRules.Domains) text.AppendFormat(CultureInfo.InvariantCulture, "{0}: {1:F1}  ", domain, Value(model, domain, 0));
                    AppendModelDetails(text, model);
                }
                if (model.InHouse) text.Append("\nPrivate / in-house | owned access, no license fee");
                else text.AppendFormat(CultureInfo.InvariantCulture, "\nCommercial model | buyer license price: ${0:N0}/month", AIRules.LicenseFee((int)Value(model, "Tier", 0), Overall(model)));
                if (model.DevCompany == game.MyCompany)
                {
                    InferenceLoad load;
                    text.Append("\nInference group: " + Convert.ToString(model.GetVar(AIRules.Prefix + "InferenceGroup", model.Server ?? ""), CultureInfo.InvariantCulture));
                    if (inference.TryGetValue(model.ID, out load)) text.AppendFormat(CultureInfo.InvariantCulture, " | {0} companies | {1:N0} capacity | delivered {2:P0}", load.Clients, load.Demand, load.LastDelivery);
                    else text.Append(" | no corporate demand yet");
                }
            }
            text.Append("\nOwned models are available automatically. Licensed access renews each calendar month; cancel to stop future fees.");
            modelInfo.text = text.ToString();
            FitPanelLabel(modelInfo);
            });
        }
        public override void Serialize(WriteDictionary data, GameReader.LoadMode mode)
        {
            if (mode == GameReader.LoadMode.Building) return;
            data["Version"] = 3;
            SerializeExpansion(data);
            data["LastHour"] = lastHour; data["LastMonth"] = lastMonth;
            List<WriteDictionary> rows = new List<WriteDictionary>();
            foreach (TrainingState state in training.Values)
            {
                WriteDictionary row = new WriteDictionary("AITraining");
                row["WorkID"] = state.WorkID; row["Name"] = state.Name; row["Group"] = state.Group;
                row["Tier"] = state.Tier; row["Allocation"] = state.Allocation;
                row["ServerSpend"] = state.ServerSpend; row["SpendObservedFrom"] = state.SpendObservedFrom;
                row["Elapsed"] = state.Elapsed; row["Compute"] = state.Compute; row["Preparation"] = state.Preparation;
                row["Running"] = state.Running; row["Completed"] = state.Completed;
                row["WasPaused"] = state.WasPaused; row["WasHidden"] = state.WasHidden;
                row["Scores"] = state.Scores; rows.Add(row);
                WriteConfig(row, state.Config); row["DatasetPaid"] = state.DatasetPaid; row["Seed"] = state.Seed;
                row["WasAutoDev"] = state.WasAutoDev;
            }
            data["Training"] = rows;
            data["BuyerIDs"] = access.Keys.ToArray(); data["ModelIDs"] = access.Values.ToArray();
        }
        public override void Deserialize(WriteDictionary data, GameReader.LoadMode mode)
        {
            if (mode == GameReader.LoadMode.Building) return;
            Detach(); training.Clear(); access.Clear();
            DeserializeExpansion(data);
            lastHour = data.Get<string>("LastHour", ""); lastMonth = data.Get<string>("LastMonth", "");
            foreach (WriteDictionary row in data.Get<List<WriteDictionary>>("Training", new List<WriteDictionary>()))
            {
                TrainingState state = new TrainingState();
                state.WorkID = row.Get<uint>("WorkID", 0); state.Name = row.Get<string>("Name", ""); state.Group = row.Get<string>("Group", "");
                state.Tier = AIRules.Tier(row.Get<int>("Tier", 0)); state.Allocation = AIRules.Clamp(row.Get<double>("Allocation", 1), 0.01, 1);
                state.Config = ReadSavedConfig(row);
                state.Elapsed = AIRules.Clamp(row.Get<double>("Elapsed", 0), 0, ExpansionRules.Window(state.Tier, state.Config));
                state.Compute = Math.Max(0, row.Get<double>("Compute", 0)); state.Preparation = AIRules.Clamp(row.Get<double>("Preparation", 0), 0, 1);
                state.ServerSpend = Math.Max(0, row.Get<double>("ServerSpend", 0));
                state.SpendObservedFrom = row.Get<double>("SpendObservedFrom", state.Elapsed);
                state.Running = row.Get<bool>("Running", false); state.Completed = row.Get<bool>("Completed", false);
                state.WasPaused = row.Get<bool>("WasPaused", false); state.WasHidden = row.Get<bool>("WasHidden", false);
                state.DatasetPaid = row.Get<bool>("DatasetPaid", state.Elapsed > 0); state.WasAutoDev = row.Get<bool>("WasAutoDev", false);
                state.Seed = row.Get<double[]>("Seed", new double[5]); if (state.Seed.Length != 5) state.Seed = new double[5];
                state.Scores = row.Get<double[]>("Scores", new double[5]); if (state.Scores.Length != 5) state.Scores = new double[5];
                training[state.WorkID] = state;
            }
            uint[] buyers = data.Get<uint[]>("BuyerIDs", new uint[0]); uint[] products = data.Get<uint[]>("ModelIDs", new uint[0]);
            for (int i = 0; i < Math.Min(buyers.Length, products.Length); i++) access[buyers[i]] = products[i];
            game = null; market = null; Ready = false; savedStatePending = true;
        }
    }
}
