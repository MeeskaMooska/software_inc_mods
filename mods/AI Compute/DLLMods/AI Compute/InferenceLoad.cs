using System;

namespace SDSI.AI
{
    // Corporate inference is separate from native consumer hosting. Both share
    // the selected physical server group and therefore contend with training.
    public sealed class InferenceLoad : IServerItem
    {
        public SoftwareProduct Model;
        public double Demand;
        public int Clients;
        public bool ExternalClients;
        public double LastDelivery;
        public double Sample;
        public bool HasSample;
        public bool HasDelivery;
        public string Group;
        public bool UsesISP { get { return ExternalClients; } }
        public bool CancelOnUnload() { return false; }
        public float GetLoadRequirement()
        {
            return (float)(Demand / Math.Max(0.001, Utilities.BandwidthFactor(1, SDateTime.Now())));
        }
        public void HandleLoad(float fraction)
        { Sample = AIRules.Clamp(fraction, 0, 1); HasSample = true; }
        public string GetDescription()
        { return "AI inference: " + Model.Name + " (" + Clients + " companies)"; }
        public void SerializeServer(string name)
        {
            Group = name ?? "";
            Model.PutVar(AIRules.Prefix + "InferenceGroup", Group);
            if (String.IsNullOrEmpty(Group)) { LastDelivery = 0; Sample = 0; HasSample = false; }
        }
        public IReferenceFix FixReferences() { return this; }
        public void ConsumeSample()
        {
            LastDelivery = HasSample ? Sample : 0;
            HasDelivery = HasSample;
            HasSample = false; Sample = 0;
        }
    }
}
