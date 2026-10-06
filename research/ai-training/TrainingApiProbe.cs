// Compilation-only API probe for Software Inc. Beta 1.8.42.
// This is not a playable mod. There is no ModMeta entry point, no automatic
// registration, and none of these methods have been run inside the game.
using System;

namespace SoftwareIncAIResearch
{
    public sealed class TrainingLoadProbe : IServerItem
    {
        public float RequestedCapacity;
        public float ServedFraction;
        public string ServerName;

        public bool UsesISP { get { return false; } }
        public bool CancelOnUnload() { return true; }
        public float GetLoadRequirement() { return RequestedCapacity; }
        public void HandleLoad(float fraction) { ServedFraction = fraction; }
        public string GetDescription() { return "AI training API probe"; }
        public void SerializeServer(string name) { ServerName = name; }
        public IReferenceFix FixReferences() { return this; }

        public void Register(string group)
        {
            GameSettings.Instance.RegisterWithServer(group, this, true);
        }

        public void Unregister()
        {
            GameSettings.Instance.DeregisterServerItem(this);
        }
    }

    public sealed class TrainingHooksProbe : ModBehaviour
    {
        public override void OnActivate() { }
        public override void OnDeactivate() { }

        // Verify accessible simulation-time and persistence hooks.
        public static void Subscribe(EventHandler handler)
        {
            TimeOfDay.OnHourPassed += handler;
        }

        public static void Unsubscribe(EventHandler handler)
        {
            TimeOfDay.OnHourPassed -= handler;
        }

        public override void Serialize(WriteDictionary data, GameReader.LoadMode mode)
        {
            data["ProbeProgress"] = 0.0;
        }

        public override void Deserialize(WriteDictionary data, GameReader.LoadMode mode)
        {
            double progress = data.Get<double>("ProbeProgress", 0.0);
            if (progress < 0.0) throw new InvalidOperationException("Invalid probe data");
        }

        // Verify that the native feature API can accept work without an Actor.
        // Production use must account for quality, bugs, stage transitions,
        // setup labor and authorization to change this particular task.
        public static void CheckProgressApi(SoftwareAlpha task, double amount)
        {
            foreach (SoftwareWorkItem.FeatureProgress feature in task.Features)
            {
                bool changed;
                double actuallyAdded;
                feature.AddProgress(amount, Employee.EmployeeRole.Programmer,
                    false, out changed, out actuallyAdded, 1.0, false);
                feature.AddQuality(0.5, actuallyAdded, true);
            }
            task.RefreshWorkDevTime(false);
            task.HasFinishedCode = task.AllDone(false, false, true);
            task.HasFinishedArt = task.AllDone(false, true, false);
            task.HasFinished = task.HasFinishedCode && task.HasFinishedArt;
        }
    }
}
