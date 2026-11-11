using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RadioBridge;

/// <summary>
/// Client-kant serieel brug:
///   - Linux  : maakt een pseudo-terminal (PTY) aan via openpty() — geen driver nodig
///   - Windows: gebruikt een bestaand com0com null-modem paar (detecteert automatisch)
///
/// Verkeer: Logger ↔ virtuele COM/PTY ↔ VirtualSerialBridge ↔ TCP ↔ server SerialBridge ↔ radio COM
/// </summary>
public sealed class VirtualSerialBridge : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string>? StatusChanged;
    public event Action<string>? Error;

    private void Log(string msg)  => StatusChanged?.Invoke(msg);
    private void Err(string msg)  => Error?.Invoke(msg);

    // ── State ─────────────────────────────────────────────────────────────────
    private CancellationTokenSource _cts = new();
    private bool   _running;
    private string _virtualPort = "";

    // Linux PTY handles
    private int _masterFd = -1;
    private int _slaveFd  = -1;

    // Windows: gebruik gewone FileStream op de com0com "inside" poort
    private FileStream? _comStream;

    // TCP
    private TcpClient?     _tcp;
    private NetworkStream? _net;

    /// <summary>Welke virtuele poort de logger moet gebruiken (bijv. /dev/pts/5 of COM11)</summary>
    public string VirtualPortName => _virtualPort;

    // ── Start ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verbind met de TCP serial server op host:tcpPort en koppel aan een virtuele COM.
    /// virtualPort: op Windows het "inside" com0com poort (bijv. "COM11"); op Linux leeg laten → auto PTY.
    /// </summary>
    public async Task StartAsync(string host, int tcpPort, string virtualPort = "")
    {
        try
        {
            Log($"Virtueel serieel: verbinden met {host}:{tcpPort}...");

            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(host, tcpPort, _cts.Token);
            _net = _tcp.GetStream();
            _running = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                await StartWindowsAsync(virtualPort);
            else
                await StartLinuxAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Err($"Virtueel serieel fout: {ex.Message}"); }
    }

    // ── Linux PTY ─────────────────────────────────────────────────────────────

    private Task StartLinuxAsync()
    {
        (_masterFd, _slaveFd, _virtualPort) = OpenPty();
        if (_masterFd < 0)
        {
            Err("openpty() mislukt — PTY niet beschikbaar");
            return Task.CompletedTask;
        }

        Log($"Virtuele COM aangemaakt: {_virtualPort}");
        Log($"  → Verbind je logger met {_virtualPort}");

        _ = Task.Run(() => PtyToTcpAsync(_cts.Token));
        _ = Task.Run(() => TcpToPtyAsync(_cts.Token));

        return Task.CompletedTask;
    }

    private async Task PtyToTcpAsync(CancellationToken ct)
    {
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = NativeRead(_masterFd, buf, buf.Length);
                if (n <= 0) break;
                await _net!.WriteAsync(buf.AsMemory(0, n), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task TcpToPtyAsync(CancellationToken ct)
    {
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _net!.ReadAsync(buf, ct);
                if (n == 0) break;
                NativeWrite(_masterFd, buf, n);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    // ── Windows com0com ───────────────────────────────────────────────────────

    private Task StartWindowsAsync(string virtualPort)
    {
        if (string.IsNullOrEmpty(virtualPort))
        {
            Err("Geen com0com poort opgegeven.");
            return Task.CompletedTask;
        }

        try
        {
            // Open via kernel device path zodat we FileStream kunnen gebruiken
            string devicePath = virtualPort.StartsWith(@"\\") ? virtualPort : $@"\\.\{virtualPort}";
            _comStream = new FileStream(devicePath, FileMode.Open, FileAccess.ReadWrite,
                FileShare.ReadWrite, 256, FileOptions.Asynchronous);
            _virtualPort = virtualPort;

            Log($"com0com poort geopend: {virtualPort}");
            Log($"  → Verbind je logger met {virtualPort}");

            _ = Task.Run(() => Com0ComToTcpAsync(_cts.Token));
            _ = Task.Run(() => TcpToCom0ComAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Err($"Kan {virtualPort} niet openen: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task Com0ComToTcpAsync(CancellationToken ct)
    {
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested && _comStream != null)
            {
                int n = await _comStream.ReadAsync(buf, ct);
                if (n <= 0) break;
                await _net!.WriteAsync(buf.AsMemory(0, n), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task TcpToCom0ComAsync(CancellationToken ct)
    {
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested && _comStream != null)
            {
                int n = await _net!.ReadAsync(buf, ct);
                if (n == 0) break;
                await _comStream.WriteAsync(buf.AsMemory(0, n), ct);
                await _comStream.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    public void Stop()
    {
        _cts.Cancel();
        _running = false;

        if (_masterFd >= 0) { NativeClose(_masterFd); _masterFd = -1; }
        if (_slaveFd  >= 0) { NativeClose(_slaveFd);  _slaveFd  = -1; }

        try { _comStream?.Close(); } catch { }
        _comStream = null;

        try { _net?.Close();  } catch { }
        try { _tcp?.Close();  } catch { }
        _net = null; _tcp = null;

        _cts = new CancellationTokenSource();
        _virtualPort = "";
        Log("Virtueel serieel gestopt.");
    }

    public void Dispose() => Stop();

    // ── com0com detectie (Windows) ────────────────────────────────────────────

    // Registry-paden die com0com gebruikt (verschilt per versie)
    private static readonly string[] Com0ComParamPaths =
    {
        @"SYSTEM\CurrentControlSet\Services\com0com\Parameters",
        @"SYSTEM\CurrentControlSet\Enum\ROOT\PORTPAIR",          // com0com v3 soms
    };

    /// <summary>
    /// Geeft een lijst van gevonden com0com null-modem paren terug.
    /// Elk paar heeft PortA (logger-kant) en PortB (RadioBridge-kant).
    /// </summary>
    public static List<Com0ComPair> DetectCom0ComPairs()
    {
        var pairs = new List<Com0ComPair>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return pairs;

        // Methode 1: Device class GUID — geeft altijd echte COM-nummers + CNC-naam
        pairs = DetectViaDeviceClass();
        if (pairs.Count > 0 && pairs.All(p =>
            p.PortA.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            p.PortB.StartsWith("COM", StringComparison.OrdinalIgnoreCase)))
            return pairs;

        // Methode 2: Parameters-subkey (fallback, geeft soms alleen CNCA-namen)
        var paramPairs = DetectViaParametersKey();
        if (paramPairs.Count > 0) return paramPairs;

        return pairs; // terug naar wat methode 1 had, ook al zijn het CNC-namen
    }

    private static List<Com0ComPair> DetectViaParametersKey()
    {
        var pairs = new List<Com0ComPair>();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\com0com\Parameters");
            if (key == null) return pairs;

            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub == null) continue;

                // com0com v2: "PortName" = "CNCA0", optioneel "RealPortName" = "COM10"
                // com0com v3: "PortName" direct het COM-nummer, of via apart value
                string? portName = sub.GetValue("PortName") as string;
                if (portName == null) continue;

                // Haal het echte COM-nummer op — probeer meerdere value-namen
                string? realName = sub.GetValue("RealPortName") as string
                                ?? sub.GetValue("ComPort")      as string
                                ?? sub.GetValue("SymbolicLink") as string;

                // Als RealPortName er niet is maar PortName al "COMn" is, gebruik dat
                string displayName = realName ?? portName;
                // Normaliseer: "\\Device\\Serial3" → zoek COM-naam via Enum
                if (displayName.StartsWith(@"\\Device\\", StringComparison.OrdinalIgnoreCase))
                    displayName = portName; // val terug op CNCA0-naam

                // Paar-index uit subkey naam: Port0,Port1 → paar 0; Port2,Port3 → paar 1
                var m = Regex.Match(subName, @"\d+");
                if (!m.Success) continue;
                int idx    = int.Parse(m.Value);
                int pairId = idx / 2;
                bool isA   = (idx % 2) == 0;

                while (pairs.Count <= pairId) pairs.Add(new Com0ComPair(pairId));
                if (isA) pairs[pairId].PortA = displayName;
                else     pairs[pairId].PortB = displayName;
            }
        }
        catch { }

        pairs.RemoveAll(p => p.PortA == "" && p.PortB == "");
        return pairs;
    }

    private static List<Com0ComPair> DetectViaDeviceClass()
    {
        // Zoek via HKLM\SYSTEM\CurrentControlSet\Enum\ROOT\PORTPAIR
        // of via de seriële poort class GUID {4D36E978-...}
        var pairs  = new List<Com0ComPair>();
        var cncPorts = new List<(string cncName, string comName)>();

        try
        {
            // Seriële poort device-klasse
            using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4D36E978-E325-11CE-BFC1-08002BE10318}");
            if (classKey == null) return pairs;

            foreach (var subName in classKey.GetSubKeyNames())
            {
                using var dev = classKey.OpenSubKey(subName);
                if (dev == null) continue;

                string? provider = dev.GetValue("ProviderName") as string ?? "";
                if (!provider.Contains("com0com", StringComparison.OrdinalIgnoreCase)) continue;

                string? portName = dev.GetValue("PortName") as string; // bijv. "COM10"
                string? friendlyName = dev.GetValue("FriendlyName") as string ?? ""; // bijv. "com0com - serial port emulator (CNCA0)"

                if (portName == null) continue;

                // Haal CNC-naam uit FriendlyName
                var cm = Regex.Match(friendlyName, @"(CNC[AB]\d+)", RegexOptions.IgnoreCase);
                string cncName = cm.Success ? cm.Groups[1].Value : portName;

                cncPorts.Add((cncName, portName));
            }
        }
        catch { }

        // Groepeer: CNCA1↔CNCB1 = paar 1, CNCA2↔CNCB2 = paar 2, etc.
        foreach (var (cncName, comName) in cncPorts)
        {
            var nm = Regex.Match(cncName, @"CNC([AB])(\d+)", RegexOptions.IgnoreCase);
            if (!nm.Success) continue;

            bool isA   = nm.Groups[1].Value.Equals("A", StringComparison.OrdinalIgnoreCase);
            int pairId = int.Parse(nm.Groups[2].Value); // 1-based zoals com0com aanmaakt

            while (pairs.Count <= pairId) pairs.Add(new Com0ComPair(pairId));
            if (isA) pairs[pairId].PortA = comName;
            else     pairs[pairId].PortB = comName;
        }

        pairs.RemoveAll(p => p.PortA == "" && p.PortB == "");
        return pairs;
    }

    /// <summary>
    /// Debug: geeft alle ruwe registry-waarden terug voor diagnose.
    /// </summary>
    public static string DebugRegistryDump()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "Niet Windows.";

        var sb = new System.Text.StringBuilder();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\com0com\Parameters");
            if (key == null) { sb.AppendLine("Parameters key niet gevonden."); }
            else
            {
                sb.AppendLine($"Parameters subkeys: {string.Join(", ", key.GetSubKeyNames())}");
                foreach (var sn in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(sn);
                    if (sub == null) continue;
                    sb.Append($"  [{sn}] ");
                    foreach (var vn in sub.GetValueNames())
                        sb.Append($"{vn}={sub.GetValue(vn)}  ");
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"Fout: {ex.Message}"); }
        return sb.ToString();
    }

    /// <summary>
    /// Geeft true als com0com geïnstalleerd is op dit systeem.
    /// </summary>
    public static bool IsCom0ComInstalled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\com0com");
            return key != null;
        }
        catch { return false; }
    }

    // ── Linux openpty P/Invoke ────────────────────────────────────────────────

    [DllImport("libc", SetLastError = true)]
    private static extern int openpty(out int master, out int slave,
        IntPtr name, IntPtr termios, IntPtr winsize);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr ptsname(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, byte[] buf, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int write(int fd, byte[] buf, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    private static int  NativeRead(int fd, byte[] buf, int n)  => read(fd, buf, n);
    private static int  NativeWrite(int fd, byte[] buf, int n) => write(fd, buf, n);
    private static void NativeClose(int fd)                    => close(fd);

    private static (int master, int slave, string slaveName) OpenPty()
    {
        try
        {
            int rc = openpty(out int master, out int slave, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (rc != 0) return (-1, -1, "");

            IntPtr namePtr = ptsname(master);
            string name = namePtr != IntPtr.Zero
                ? Marshal.PtrToStringAnsi(namePtr) ?? ""
                : "";

            return (master, slave, name);
        }
        catch { return (-1, -1, ""); }
    }
}

// ── Com0ComPair ───────────────────────────────────────────────────────────────

/// <summary>Een com0com null-modem paar: PortA (buiten, voor logger) en PortB (binnen, voor RadioBridge).</summary>
public class Com0ComPair
{
    public int    PairId { get; }
    public string PortA  { get; set; } = "";   // "outside" — voor logger
    public string PortB  { get; set; } = "";   // "inside"  — RadioBridge connect hierop

    public Com0ComPair(int id) => PairId = id;

    public override string ToString() =>
        $"Paar {PairId}: {PortA} ↔ {PortB}  (logger → {PortA}, RadioBridge → {PortB})";
}
