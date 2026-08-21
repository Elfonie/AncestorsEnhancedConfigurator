namespace AncestorsEnhanced.Infrastructure.Platform;

public static class GameProcessProbe
{
    public static bool IsAncestorsRunning()
    {
        try
        {
            return HasProcess("Ancestors-Win64-Shipping") || HasProcess("Ancestors");
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool HasProcess(string name)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (System.Diagnostics.Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}
