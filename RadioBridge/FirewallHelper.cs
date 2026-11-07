using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RadioBridge;

/// <summary>
/// Cross-platform firewall helper.
/// Windows: gebruikt netsh advfirewall.
/// Linux: toont de benodigde iptables/ufw commando's in de log.
/// </summary>
public static class FirewallHelper
{
    /// <summary>
    /// Open één TCP poort in de firewall.
    /// Gooit een Exception als het mislukt (vang op in MainWindow).
    /// </summary>
    public static void OpenPort(int port)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            OpenPortWindows(port);
        else
            OpenPortLinux(port);
    }

    private static void OpenPortWindows(int port)
    {
        string ruleName = $"RadioBridge TCP {port}";

        // Verwijder bestaande regel (negeer fouten)
        RunCommand("netsh",
            $"advfirewall firewall delete rule name=\"{ruleName}\"",
            elevate: true);

        // Voeg nieuwe regel toe
        int exit = RunCommand("netsh",
            $"advfirewall firewall add rule name=\"{ruleName}\" " +
            $"dir=in action=allow protocol=TCP localport={port} " +
            $"description=\"RadioBridge audio bridge\"",
            elevate: true);

        if (exit != 0)
            throw new Exception(
                $"netsh mislukt (exit {exit}). Probeer als administrator uit te voeren.");
    }

    private static void OpenPortLinux(int port)
    {
        // Probeer ufw (Ubuntu/Debian)
        int exit = RunCommand("ufw", $"allow {port}/tcp", elevate: false);
        if (exit == 0) return;

        // Probeer iptables als fallback
        exit = RunCommand("iptables",
            $"-A INPUT -p tcp --dport {port} -j ACCEPT", elevate: false);
        if (exit == 0) return;

        // Geef de handmatige commando's terug als hint
        throw new Exception(
            $"Geen automatische firewall tool gevonden.\n" +
            $"Voer handmatig uit:\n" +
            $"  sudo ufw allow {port}/tcp\n" +
            $"  of: sudo iptables -A INPUT -p tcp --dport {port} -j ACCEPT");
    }

    private static int RunCommand(string exe, string args, bool elevate)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute        = elevate && RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            Verb                   = elevate && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                                     ? "runas" : "",
            CreateNoWindow         = true,
            RedirectStandardOutput = !elevate,
            RedirectStandardError  = !elevate,
            WindowStyle            = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit(10_000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    // Achterwaartse compatibiliteit
    public static bool HandleStartupArgs(string[] args) => false;
}
