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
        Console.WriteLine("Reducing processes, trimming working sets, and forcing the OS to release memory...");

        long memoryBefore = GetTotalMemoryInUse();
        CleanAllProcesses();
        ForceWindowsToReleaseMemory();
        long memoryAfter = GetTotalMemoryInUse();

        Console.WriteLine($"Done. RAM released as much as Windows allows. Released: {FormatBytes(memoryBefore - memoryAfter)}");
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
            Console.WriteLine("Could not restart as administrator: " + ex.Message);
        }
    }

    static void CleanAllProcesses()
    {
        var processes = Process.GetProcesses();

        // Sort processes by memory usage in descending order
        Array.Sort(processes, (p1, p2) =>
        {
            try
            {
                return p2.WorkingSet64.CompareTo(p1.WorkingSet64);
            }
            catch
            {
                return 0;
            }
        });

        foreach (var proc in processes)
        {
            try
            {
                // Set low process priority (if possible)
                try
                { proc.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch { }

                // Attempt to trim working set (release RAM)
                EmptyWorkingSet(proc.Handle);
            }
            catch { /* Ignore errors, continue */ }
        }
    }

    static void ForceWindowsToReleaseMemory()
    {
        Console.WriteLine("Forcing Windows to release cache/standby RAM...");

        List<byte[]> allocations = new List<byte[]>();
        const int blockSize = 200 * 1024 * 1024; // 200 MB blocks for faster pressure

        try
        {
            while (true)
            {
                allocations.Add(new byte[blockSize]);
            }
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine("Windows RAM fully pressured – standby cache cleared.");
        }

        // Release again
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
            catch { /* Ignore errors, continue */ }
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
