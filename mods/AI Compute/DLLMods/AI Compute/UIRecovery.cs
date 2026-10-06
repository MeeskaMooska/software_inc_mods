using System;

namespace SDSI.AI
{
    // Catch inside the source method, before the game's injected exception
    // handler can deactivate the mod. Reporting failure must also be contained.
    public static class UIRecovery
    {
        public static bool TryRun(Action action, Action<Exception> report)
        {
            try { action(); return true; }
            catch (Exception error)
            {
                try { if (report != null) report(error); }
                catch (Exception) { }
                return false;
            }
        }
    }
}
