using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SDSI.AI
{
    public sealed partial class AIComputeController
    {
        private bool advancedView;
        private Text trainingUsage, viewLabel;
        private List<Component> advancedControls = new List<Component>();
        private string lastPanelError = "";

        private bool RunPanelAction(string operation, Action action)
        {
            return UIRecovery.TryRun(action, delegate(Exception error) { RecoverPanel(operation, error); });
        }
        private void ResetPanelReferences()
        {
            window = null; status = null; modelInfo = null; trainingUsage = null; viewLabel = null;
            projects = null; servers = null; sizes = null; budgets = null; models = null; inferenceServers = null;
            architectures = null; quantizations = null; contexts = null; datasets = null; focuses = null; usage = null;
            advancedControls.Clear(); refreshing = false;
        }
        private void ClosePanelSafely()
        {
            GUIWindow previous = window;
            ResetPanelReferences();
            try { if (previous != null) previous.Close(); }
            catch (Exception error)
            {
                // Cleanup failure must never pass into the game's mod-disabling
                // exception injector. Destroy only this failed window.
                try { Debug.LogWarning("AI Compute 0.3.0 window cleanup: " + error); if (previous != null) UnityEngine.Object.Destroy(previous.gameObject); }
                catch (Exception) { }
            }
        }
        private void RecoverPanel(string operation, Exception error)
        {
            try
            {
            // Nothing here changes training, model, license or save records.
            // Suppress identical reports until a panel opens successfully.
            // GetType().Name emits a System.Reflection.MemberInfo reference;
            // the game's security verifier rejects even that diagnostic use.
            string signature = operation + ": " + error.Message;
            bool report = signature != lastPanelError;
            lastPanelError = signature;
            ClosePanelSafely();
            if (!report) return;
            Debug.LogWarning("AI Compute 0.3.0 recovered UI error during " + operation + ": " + error.ToString());
            Message("AI Compute stopped this window action safely (" + operation + "). Your saved model and training records have been kept. Reopen AI Compute to retry; an action that already took effect is not repeated automatically. Details are in output_log.txt.");
            }
            catch (Exception) { ClosePanelSafely(); }
        }

        private bool InitializeTrainingUI(Dictionary<string, Component> ui)
        {
            return RunPanelAction("binding training controls", delegate
            {
            trainingUsage = (Text)ui["trainingUsage"]; viewLabel = (Text)ui["viewLabel"];
            advancedControls.Clear();
            foreach (string id in new string[] { "architectureLabel", "architectures", "quantizations", "contexts", "dataLabel", "datasets", "focuses", "cancelTraining", "acquire", "cancelAccess", "revisionLabel", "fineTune", "retrain", "subscription", "billingLabel", "usage", "usageBilling", "flatBilling" })
                advancedControls.Add(ui[id]);
            ApplyTrainingView();
            });
        }
        private void WatchCombo(GUICombobox combo)
        {
            try { if (combo != null) combo.OnSelectedChanged.AddListener(delegate { Changed(combo); }); }
            catch (Exception error) { RecoverPanel("binding dropdown callbacks", error); }
        }
        private bool WireTrainingUI()
        {
            return RunPanelAction("binding training callbacks", delegate
            {
            // Populate every field before attaching callbacks. XML expression
            // callbacks can run during UpdateContent and re-enter initialization.
            foreach (GUICombobox combo in new GUICombobox[] { projects, sizes, servers, budgets, models, architectures, quantizations, contexts, datasets, focuses })
            {
                if (window == null) return;
                WatchCombo(combo);
            }
            });
        }
        private void SafeSelection(GUICombobox combo, int index)
        {
            try
            {
            if (combo == null || combo.Items == null || combo.Items.Count == 0) return;
            combo.UpdateSelectionSupressEvents(Math.Max(0, Math.Min(combo.Items.Count - 1, index)));
            }
            catch (Exception error) { RecoverPanel("updating a dropdown", error); }
        }
        private double SelectedAllocation()
        { return ExpansionRules.Allocations[Math.Max(0, Math.Min(ExpansionRules.Allocations.Length - 1, budgets == null ? 2 : budgets.Selected))]; }
        private int BudgetIndex(double allocation)
        {
            int best = 0;
            for (int i = 1; i < ExpansionRules.Allocations.Length; i++)
                if (Math.Abs(ExpansionRules.Allocations[i] - allocation) < Math.Abs(ExpansionRules.Allocations[best] - allocation)) best = i;
            return best;
        }
        public void ShowSimple() { advancedView = false; ApplyTrainingView(); RefreshLabels(); }
        public void ShowAdvanced() { advancedView = true; ApplyTrainingView(); RefreshLabels(); }
        private void ApplyTrainingView()
        {
            RunPanelAction("switching training views", delegate
            {
            foreach (Component control in advancedControls) if (control != null) control.gameObject.SetActive(advancedView);
            if (viewLabel != null) viewLabel.text = advancedView ? "Advanced: configure training, revisions and licensing" : "Simple: choose model size, server group and allocation";
            if (status != null) status.GetComponent<RectTransform>().sizeDelta = new Vector2(status.GetComponent<RectTransform>().sizeDelta.x, advancedView ? 270 : 90);
            if (modelInfo != null) modelInfo.GetComponent<RectTransform>().sizeDelta = new Vector2(modelInfo.GetComponent<RectTransform>().sizeDelta.x, advancedView ? 420 : 230);
            });
        }
        private double GroupWattage(ServerGroup group)
        {
            double watts = 0;
            try
            {
            foreach (IServerHost host in group.Servers)
            {
                Server server = host as Server;
                if (server != null && server.furn != null && server.furn.IsOn)
                    watts += Math.Max(0, server.furn.Wattage);
            }
            }
            catch (Exception error) { RecoverPanel("reading server power usage", error); }
            return watts;
        }
        private void FitPanelLabel(Text label)
        {
            if (label == null) return;
            RectTransform rect = label.GetComponent<RectTransform>();
            // Fixed blocks left large empty gaps and could also clip longer
            // diagnostics. Let text wrapping determine the scrollable height.
            if (rect.rect.width > 0) rect.sizeDelta = new Vector2(rect.sizeDelta.x, Math.Max(28, label.preferredHeight + 8));
        }
        private void RefreshTrainingUsage(TrainingState state)
        {
            if (trainingUsage == null) return;
            RunPanelAction("updating compute usage and cost", delegate
            {
            if (state == null) { trainingUsage.text = "Choose a prepared project to preview compute usage and cost."; FitPanelLabel(trainingUsage); return; }
            int tier = state.DatasetPaid ? state.Tier : SelectedTier();
            TrainingConfig config = state.DatasetPaid ? state.Config : ReadConfig();
            config.Mode = state.Config.Mode;
            double fee = ExpansionRules.DatasetFee(tier, config, Counts(state.Task.GetFeatures()));
            double remaining = Math.Max(0, ExpansionRules.Window(tier, config) - state.Elapsed);
            StringBuilder text = new StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture, "Dataset: ${0:N0} once ({1}) | training left: {2:F2} months\n", fee, state.DatasetPaid ? "already paid" : "charged when you start", remaining);
            ServerGroup group = null;
            if (servers.Selected >= 0 && servers.Selected < serverChoices.Count) game.TryGetServerGroup(serverChoices[servers.Selected], out group);
            if (group != null && group.PowerSum > 0)
            {
                double allocation = SelectedAllocation(), requested = AIRules.Clamp(group.PowerSum * allocation, 0, 100000000);
                double monthly = state.Completed ? 0 : ExpansionRules.TrainingElectricity(GroupWattage(group), requested / group.PowerSum, Furniture.GetElectricityPrice(), GameSettings.DaysPerMonth);
                if (window == null) return;
                text.AppendFormat(CultureInfo.InvariantCulture, "Selected: {0} | {1:P0} = {2:N0} / {3:N0} capacity\n", group.Name, allocation, requested, group.PowerSum);
                double[] projected = ExpansionRules.Scores(tier, config, state.Compute + requested * remaining,
                    ExpansionRules.Window(tier, config), state.Preparation, Counts(state.Task.GetFeatures()), state.Seed);
                text.AppendFormat(CultureInfo.InvariantCulture, "Projected benchmark: {0:F1}/100 at full delivery | reference load: {1:F1}x\n",
                    MeanScores(projected), requested / Math.Max(1, AIRules.ReferenceCapacity[tier] * ExpansionRules.ArchitectureTraining(config)));
                text.AppendFormat(CultureInfo.InvariantCulture, "Power estimate: ~${0:N0}/month | remaining + unpaid dataset: ~${1:N0}\n", monthly, monthly * remaining + (state.DatasetPaid ? 0 : fee));
                if (requested > AIRules.ReferenceCapacity[tier] * ExpansionRules.ArchitectureTraining(config) * 10)
                    text.Append("Above 10x reference: gains continue, but each point costs more compute.\n");
            }
            else text.Append("Select a functioning physical server group for a cost preview.\n");
            if (state.Running)
                text.AppendFormat(CultureInfo.InvariantCulture, "Active: {0} | {1:P0} allocated | last delivered: {2:N0} capacity\n", state.Group, state.Allocation, state.LastCapacity);
            else text.Append(state.Completed ? "Complete: no training workload.\n" : "Paused: no training workload. Preview is for starting/resuming.\n");
            text.AppendFormat(CultureInfo.InvariantCulture, "Recorded training server cost: ${0:N0}{1}\n", state.ServerSpend, state.SpendObservedFrom > 0 ? " (since this update; earlier cost unknown)" : "");
            text.Append("Selections preview changes; Start/resume applies them. Power estimate excludes staff, maintenance and hosting; generation may reduce it. Training uses no internet bandwidth.");
            trainingUsage.text = text.ToString();
            FitPanelLabel(trainingUsage);
            });
        }
    }
}
