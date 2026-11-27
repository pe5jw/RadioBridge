using System;
using System.Linq;
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
    /// Open één of meer TCP-poorten in de firewall (audio + seriële bruggen).
    /// Gooit een Exception als het mislukt (vang op in MainWindow).
    /// </summary>
    public static void OpenPorts(params int[] ports)
    {
        ports = ports.Where(p => p > 0).Distinct().OrderBy(p => p).ToArray();
        if (ports.Length == 0) return;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            OpenPortsWindows(ports);
        else
            OpenPortsLinux(ports);
    }

    private static void OpenPortsWindows(int[] ports)
    {
        const string ruleName = "RadioBridge";
        string list = string.Join(",", ports);

        // Eén verhoogde cmd: oude regel weg + nieuwe regel → maar één UAC-vraag
        string cmd =
            $"/c netsh advfirewall firewall delete rule name=\"{ruleName}\" >nul 2>&1 & " +
            $"netsh advfirewall firewall add rule name=\"{ruleName}\" " +
            $"dir=in action=allow protocol=TCP localport={list} " +
            $"description=\"RadioBridge audio + serieel\"";

        int exit = RunCommand("cmd.exe", cmd, elevate: true);
        if (exit != 0)
            throw new Exception(
                $"netsh mislukt (exit {exit}). Probeer als administrator uit te voeren.");
    }

    private static void OpenPortsLinux(int[] ports)
    {
        // Probeer ufw (Ubuntu/Debian), anders iptables
        bool ok = ports.All(p => RunCommand("ufw", $"allow {p}/tcp", elevate: false) == 0)
               || ports.All(p => RunCommand("iptables", $"-A INPUT -p tcp --dport {p} -j ACCEPT", elevate: false) == 0);
        if (ok) return;

        var ufw = string.Join("\n", ports.Select(p => $"  sudo ufw allow {p}/tcp"));
        throw new Exception(
            "Kon de firewall niet automatisch aanpassen (root nodig of geen ufw/iptables).\n" +
            "Voer handmatig uit:\n" + ufw);
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
