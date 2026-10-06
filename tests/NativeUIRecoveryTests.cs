using System;
using SDSI.AI;

// The native source injector inserts calls to these types. Stand-ins let us
// execute its rewritten recovery boundary without loading Unity or the game.
public sealed class ModException : Exception { }
public static class ModController
{
    public static int Handled;
    public static void HandleException(string name, Exception error) { Handled++; }
}
public static class NativeUIRecoveryTests
{
    public static int Main()
    {
        try
        {
            int executions = 0, reports = 0;
            Exception failure = new InvalidOperationException("controlled window failure");
            Exception observed = null;
            if (UIRecovery.TryRun(delegate { executions++; throw failure; }, delegate(Exception error) {
                reports++; observed = error;
            })) throw new Exception("window failure escaped recovery");
            if (executions != 1 || reports != 1 || !Object.ReferenceEquals(observed, failure) || ModController.Handled != 0)
                throw new Exception("native exception handler ran or action was repeated");
            if (UIRecovery.TryRun(delegate { throw failure; }, delegate(Exception error) { throw failure; }))
                throw new Exception("reporting failure escaped recovery");
            if (!UIRecovery.TryRun(delegate { executions++; }, null) || executions != 2 || ModController.Handled != 0)
                throw new Exception("subsequent action could not recover");
            Console.WriteLine("PASS: rewritten UI recovery contains action/reporting failures without calling the native mod-disabling handler. Unity was not executed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
