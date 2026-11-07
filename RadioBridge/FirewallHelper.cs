using System.Diagnostics;
using System.Security.Principal;

namespace RadioBridge;

public static class FirewallHelper
{
    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Add firewall rules. If not admin, re-launch just this logic elevated via netsh directly.
    /// </summary>
    public static void EnsureRules(IEnumerable<int> ports, Action<string> log)
    {
        var portList = ports.ToList();

        if (IsAdmin())
        {
            // Already admin - apply directly
            ApplyRules(portList, log);
            return;
        }

        // Not admin - run netsh commands elevated one by one via runas
        bool anyFailed = false;
        foreach (var port in portList)
        {
            string ruleName = $"RadioBridge TCP {port}";
            // Delete existing first
            RunElevated("netsh", $"advfirewall firewall delete rule name=\"{ruleName}\"");
            // Add new rule
            int exit = RunElevated("netsh",
                $"advfirewall firewall add rule name=\"{ruleName}\" " +
                $"dir=in action=allow protocol=TCP localport={port} " +
                $"description=\"RadioBridge\"");
            if (exit == 0)
                log($"Firewall regel toegevoegd: TCP {port}");
            else
            {
                log($"Firewall regel mislukt voor poort {port} (exit {exit})");
                anyFailed = true;
            }
        }

        if (!anyFailed)
            MessageBox.Show(
                $"Firewall regels ingesteld voor poorten:\n{string.Join("\n", portList.Select(p => $"  TCP {p}"))}",
                "Firewall ingesteld", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            MessageBox.Show(
                "Niet alle firewall regels konden worden ingesteld.\n" +
                "Probeer de applicatie als administrator uit te voeren.",
                "Firewall waarschuwing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void ApplyRules(List<int> ports, Action<string> log)
    {
        foreach (var port in ports)
        {
            string ruleName = $"RadioBridge TCP {port}";
            RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
            int exit = RunNetsh(
                $"advfirewall firewall add rule name=\"{ruleName}\" " +
                $"dir=in action=allow protocol=TCP localport={port} " +
                $"description=\"RadioBridge\"");
            log(exit == 0
                ? $"Firewall regel toegevoegd: TCP {port}"
                : $"Firewall regel mislukt voor poort {port}");
        }
        MessageBox.Show(
            $"Firewall regels ingesteld voor poorten:\n{string.Join("\n", ports.Select(p => $"  TCP {p}"))}",
            "Firewall ingesteld", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Run a command elevated (UAC prompt), wait for it to finish, return exit code
    private static int RunElevated(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            Verb            = "runas",
            UseShellExecute = true,
            CreateNoWindow  = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit(10000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    private static int RunNetsh(string args)
    {
        var psi = new ProcessStartInfo("netsh", args)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit(5000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    // No longer needed - kept for compatibility
    public static bool HandleStartupArgs(string[] args) => false;
}
