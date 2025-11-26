using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using Timer = System.Timers.Timer;

class Program
{
    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    private static readonly string[] CriticalProcesses = { "System", "svchost", "winlogon", "explorer" };

    static void Main()
    {
        if (!IsRunningAsAdministrator())
        {
            RestartAsAdministrator();
            return;
        }

        Console.WriteLine("=== RAM Cleaner Starter ===");
        Console.WriteLine("Reducing processes, trimming working sets, and forcing the OS to release memory...");

        StartMemoryUsageMonitoring();
        KillUnwantedProcessesAndServices();

        long memoryBefore = GetTotalMemoryInUse();
        CleanAllProcesses();
        for (int i = 0; i < 10; i++)
        {
            ForceWindowsToReleaseMemory();
            System.Threading.Thread.Sleep(500);
        }
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

    static void StartMemoryUsageMonitoring()
    {
        Timer timer = new Timer(60000); // Log memory usage every 60 seconds
        timer.Elapsed += (sender, e) =>
        {
            long memoryInUse = GetTotalMemoryInUse();
            Console.WriteLine($"[Memory Monitor] Total memory in use: {FormatBytes(memoryInUse)}");
        };
        timer.Start();
    }

    static void KillUnwantedProcessesAndServices()
    {
        string[] unwantedProcesses = { "Adobe", "Acrobat", "CreativeCloud", "AdobeUpdateService", "AdobeIPCBroker", "armsvc" };
        string[] unwantedServices = { "AdobeUpdateService", "AdobeARMservice" };

        foreach (var serviceName in unwantedServices)
        {
            try
            {
                var service = new ServiceController(serviceName);
                if (service.Status == ServiceControllerStatus.Running)
                {
                    Console.WriteLine($"Stopping service: {serviceName}");
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not stop service {serviceName}: {ex.Message}");
            }
        }

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (Array.Exists(CriticalProcesses, critical => proc.ProcessName.Equals(critical, StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (var unwanted in unwantedProcesses)
                {
                    if (proc.ProcessName.Contains(unwanted, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Killing process: {proc.ProcessName}");
                        proc.Kill();
                        break;
                    }
                }
            }
            catch { /* Ignore errors, continue */ }
        }
    }

    static void CleanAllProcesses()
    {
        var processes = Process.GetProcesses();
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
                if (Array.Exists(CriticalProcesses, critical => proc.ProcessName.Equals(critical, StringComparison.OrdinalIgnoreCase)))
                    continue;

                try
                { proc.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch { }

                EmptyWorkingSet(proc.Handle);
            }
            catch { /* Ignore errors, continue */ }
        }
    }

    static void ForceWindowsToReleaseMemory()
    {
        Console.WriteLine("Forcing Windows to release cache/standby RAM...");

        List<byte[]> allocations = new List<byte[]>();
        const int blockSize = 100 * 1024 * 1024; // 100 MB blocks for controlled pressure

        try
        {
            allocations.Add(new byte[blockSize]);
        }
        catch (OutOfMemoryException)
        {
            Console.WriteLine("Windows RAM fully pressured – standby cache cleared.");
        }

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
