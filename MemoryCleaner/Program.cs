// Program.cs
// .NET 6+
// Kør som Administrator for bedst effekt (ellers kan visse processer ikke åbnes/ændres).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

class Program
{
    // P/Invoke til EmptyWorkingSet
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Access rights
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint PROCESS_SET_INFORMATION = 0x0200;

    static void Main()
    {
        Console.WriteLine("Simple RAM Cleaner (transparent) - Brug med omtanke");
        Console.WriteLine("Kør som Administrator for bedst resultat.\n");

        while (true)
        {
            var procs = ListProcesses();

            Console.WriteLine("\nIndtast index (komma-separeret) eller 'all' for alle processer.");
            Console.WriteLine("Eller tast 'q' for at afslutte.");
            Console.Write("Valg: ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(input))
                continue;
            if (input.Equals("q", StringComparison.OrdinalIgnoreCase))
                break;

            List<int> indexes = new();
            if (input.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < procs.Count; i++)
                    indexes.Add(i);
            }
            else
            {
                var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Trim(), out int idx) && idx >= 0 && idx < procs.Count)
                        indexes.Add(idx);
                }
            }

            if (indexes.Count == 0)
            {
                Console.WriteLine("Ingen gyldige valg. Prøv igen.");
                continue;
            }

            Console.WriteLine("\nHvad vil du gøre?");
            Console.WriteLine("1) Trim working set (EmptyWorkingSet)");
            Console.WriteLine("2) Sænk prioritet (BelowNormal)");
            Console.WriteLine("3) Kill process (terminer)");
            Console.Write("Vælg handling (fx 1): ");
            var action = Console.ReadLine()?.Trim();

            foreach (var idx in indexes)
            {
                var meta = procs[idx];
                Console.WriteLine($"\n-- Behandler: [{idx}] {meta.Name} (PID {meta.Id}) --");
                try
                {
                    if (action == "1")
                    {
                        TryTrimWorkingSet(meta.Id);
                    }
                    else if (action == "2")
                    {
                        TryLowerPriority(meta.Id);
                    }
                    else if (action == "3")
                    {
                        TryKill(meta.Id);
                    }
                    else
                    {
                        Console.WriteLine("Ugyldig handling.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fejl ved behandling af PID {meta.Id}: {ex.Message}");
                }
            }

            Console.WriteLine("\nHandling færdig. Tryk ENTER for at liste processer igen...");
            Console.ReadLine();
        }

        Console.WriteLine("Afslutter.");
    }

    // Hent simpel metadata for processer
    private static List<(int Id, string Name, long WorkingSet64, long PrivateBytes)> ListProcesses()
    {
        var result = new List<(int, string, long, long)>();
        var processes = Process.GetProcesses();
        Console.WriteLine("Index\tPID\tWorkingSet(MB)\tPrivate(MB)\tName");
        for (int i = 0; i < processes.Length; i++)
        {
            var p = processes[i];
            long ws = 0, pb = 0;
            try
            {
                ws = p.WorkingSet64;
                pb = p.PrivateMemorySize64;
            }
            catch { /* nogle processer kan være utilgængelige */ }

            Console.WriteLine($"{i}\t{p.Id}\t{ws / 1024 / 1024}\t\t{pb / 1024 / 1024}\t\t{p.ProcessName}");
            result.Add((p.Id, p.ProcessName, ws, pb));
        }

        return result;
    }

    private static void TryTrimWorkingSet(int pid)
    {
        IntPtr h = IntPtr.Zero;
        try
        {
            // Åbn processen med de rettigheder der kræves for EmptyWorkingSet
            h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
            if (h == IntPtr.Zero)
            {
                // fallback: prøv at bruge System.Diagnostics.Process (kræver nogle rettigheder)
                var p = Process.GetProcessById(pid);
                Console.WriteLine($"Kunne ikke OpenProcess (fejl {Marshal.GetLastWin32Error()}). Forsøger via Process-objekt...");

                // Vi kan ikke kald EmptyWorkingSet uden handle, men vi prøver at sænke prioritet i stedet
                TryLowerPriority(pid, warnOnly: true);
                return;
            }

            bool ok = EmptyWorkingSet(h);
            if (ok)
            {
                Console.WriteLine("EmptyWorkingSet: anmodning sendt. Windows kan have frigivet working set (tjek Task Manager).");
            }
            else
            {
                Console.WriteLine($"EmptyWorkingSet fejlede (GetLastError: {Marshal.GetLastWin32Error()}). Prøv at køre som Administrator.");
            }
        }
        finally
        {
            if (h != IntPtr.Zero)
            {
                CloseHandle(h);
            }
        }
    }

    private static void TryLowerPriority(int pid, bool warnOnly = false)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            Console.WriteLine($"Nuværende priority: {p.PriorityClass}");
            if (warnOnly)
            {
                Console.WriteLine("Fallback: kan ikke trimme working set, sænker i stedet prioritet (kun fallback).");
            }

            p.PriorityClass = ProcessPriorityClass.BelowNormal;
            Console.WriteLine("Prioritet sat til BelowNormal.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Kunne ikke sænke prioritet: {ex.Message} (prøv at køre som Administrator)");
        }
    }

    private static void TryKill(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            Console.Write($"Er du sikker på at du vil terminere process {p.ProcessName} (PID {pid})? (y/N): ");
            var ans = Console.ReadLine();
            if (!string.Equals(ans, "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Annulleret.");
                return;
            }

            p.Kill(true); // kill and wait
            p.WaitForExit(5000);
            Console.WriteLine("Process terminert.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Kunne ikke terminere: {ex.Message} (prøv at køre som Administrator)");
        }
    }
}
