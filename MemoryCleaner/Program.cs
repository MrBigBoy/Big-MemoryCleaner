using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

class Program
{
    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    static void Main()
    {
        if (!IsRunningAsAdministrator())
        {
            RestartAsAdministrator();
            return;
        }

        Console.WriteLine("=== RAM Cleaner Starter ===");
        Console.WriteLine("Reducerer processer, trimmer working sets og presser OS til RAM frigivelse...");

        long memoryBefore = GetTotalMemoryInUse();
        CleanAllProcesses();
        ForceWindowsToReleaseMemory();
        long memoryAfter = GetTotalMemoryInUse();

        Console.WriteLine($"Færdig. RAM frigivet så meget som Windows tillader. Frigivet: {FormatBytes(memoryBefore - memoryAfter)}");
    }

    static bool IsRunningAsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    static void RestartAsAdministrator()
    {
        var exeName = Process.GetCurrentProcess().MainModule.FileName;
        var startInfo = new ProcessStartInfo(exeName)
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Kunne ikke genstarte som administrator: " + ex.Message);
        }
    }

    static void CleanAllProcesses()
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                // Sæt lav procesprioritet (hvis muligt)
                try
                { proc.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch { }

                // Forsøg at trimme working set (frigiver RAM)
                EmptyWorkingSet(proc.Handle);
            }
            catch { /* Ignorér fejl, fortsæt */ }
        }
    }

    static void ForceWindowsToReleaseMemory()
    {
        Console.WriteLine("Presser Windows til at frigive cache/standby RAM...");

        List<byte[]> allocations = new List<byte[]>();
        const int blockSize = 200 * 1024 * 1024; // 200 MB blokke for hurtigere pres

        try
        {
            while (true)
            {
                allocations.Add(new byte[blockSize]);
            }
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine("Windows RAM presset maks – standby cache smidt ud.");
        }

        // Frigiv igen
        allocations.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static long GetTotalMemoryInUse()
    {
        long totalMemory = 0;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                totalMemory += proc.WorkingSet64;
            }
            catch { /* Ignorér fejl, fortsæt */ }
        }
        return totalMemory;
    }

    static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return string.Format("{0:0.##} {1}", len, sizes[order]);
    }
}
