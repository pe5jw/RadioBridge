using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RadioBridge;

/// <summary>
/// Client-kant serieel brug:
///   - Linux  : maakt een pseudo-terminal (PTY) aan via openpty() — geen driver nodig
///   - Windows: gebruikt een bestaand com0com null-modem paar (detecteert automatisch)
///
/// Verkeer: App ↔ virtuele COM/PTY ↔ VirtualSerialBridge ↔ TCP ↔ server SerialBridge ↔ apparaat
///
/// De lokale (virtuele) poort wordt één keer geopend. De TCP-verbinding naar de
/// server wordt automatisch (opnieuw) opgezet als hij wegvalt of de server nog
/// niet luistert.
/// </summary>
public sealed class VirtualSerialBridge : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string>? StatusChanged;
    public event Action<string>? Error;

    private readonly string _name;
    private void Log(string msg) => StatusChanged?.Invoke($"[{_name}] {msg}");
    private void Err(string msg) => Error?.Invoke($"[{_name}] {msg}");

    public VirtualSerialBridge(string name = "Poort") => _name = name;

    /// <summary>Log elk pakket als hex. Handig bij debuggen.</summary>
    public bool TraceBytes { get; set; } = false;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private string _virtualPort = "";

    // Linux PTY handles
    private int _masterFd = -1;
    private int _slaveFd  = -1;

    // Windows: com0com "inside" poort
    private SerialPort? _com;

    // TCP (onder _lock)
    private TcpClient?     _tcp;
    private NetworkStream? _net;

    /// <summary>Welke virtuele poort de app moet gebruiken (bijv. /dev/pts/5 of COM7)</summary>
    public string VirtualPortName => _virtualPort;

    // ── Start ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open de lokale virtuele poort en start de TCP-verbinding naar host:tcpPort.
    /// virtualPort: op Windows de com0com "inside" poort (bijv. "COM8"); op Linux leeg → auto PTY.
    /// Keert terug zodra de lokale poort open is; TCP verbindt op de achtergrond.
    /// </summary>
    public Task StartAsync(string host, int tcpPort, string virtualPort = "")
    {
        var ct = _cts.Token;
        bool ok = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? OpenWindows(virtualPort)
            : OpenLinux();
        if (!ok) return Task.CompletedTask;

        _ = Task.Run(() => LocalToTcpAsync(ct));
        _ = Task.Run(() => ConnectLoopAsync(host, tcpPort, ct));
        return Task.CompletedTask;
    }

    // ── Lokale poort openen ───────────────────────────────────────────────────

    private bool OpenLinux()
    {
        (_masterFd, _slaveFd, _virtualPort) = OpenPty();
        if (_masterFd < 0)
        {
            Err("openpty() mislukt — PTY niet beschikbaar");
            return false;
        }
        MakeRaw(_slaveFd);   // geen echo / CR-LF vertaling: binaire CAT moet ongewijzigd door
        Log($"Virtuele poort aangemaakt: {_virtualPort}");
        Log($"  → Verbind je app met {_virtualPort}");
        return true;
    }

    private bool OpenWindows(string virtualPort)
    {
        if (string.IsNullOrEmpty(virtualPort))
        {
            Err("Geen com0com poort opgegeven.");
            return false;
        }
        try
        {
            // SerialPort zet DCB en timeouts goed. Een kale FileStream op een COM-poort
            // erft willekeurige timeouts, waardoor reads direct 0 bytes teruggeven.
            _com = new SerialPort(virtualPort, 9600)   // baud is irrelevant bij com0com
            {
                ReadTimeout  = SerialPort.InfiniteTimeout,
                WriteTimeout = 1000,
                DtrEnable    = true,   // app ziet DSR/DCD → "apparaat aanwezig"
                RtsEnable    = true,   // app ziet CTS → mag zenden bij RTS/CTS handshake
            };
            _com.Open();
            _virtualPort = virtualPort;
            Log($"com0com poort geopend: {virtualPort}");
            return true;
        }
        catch (Exception ex)
        {
            Err($"Kan {virtualPort} niet openen: {ex.Message}");
            return false;
        }
    }

    // ── TCP verbinden (met herverbinden) ──────────────────────────────────────

    private async Task ConnectLoopAsync(string host, int tcpPort, CancellationToken ct)
    {
        bool firstAttempt = true;
        while (!ct.IsCancellationRequested)
        {
            var tcp = new TcpClient { NoDelay = true };
            try
            {
                if (firstAttempt) Log($"Verbinden met {host}:{tcpPort}...");
                await tcp.ConnectAsync(host, tcpPort, ct);
            }
            catch (OperationCanceledException) { tcp.Dispose(); break; }
            catch (Exception ex)
            {
                tcp.Dispose();
                if (firstAttempt) Log($"Server nog niet bereikbaar ({ex.Message}) — blijf proberen");
                firstAttempt = false;
                await DelayAsync(2000, ct);
                continue;
            }

            var net = tcp.GetStream();
            lock (_lock) { _tcp = tcp; _net = net; }
            Log($"Verbonden met {host}:{tcpPort}");
            firstAttempt = true;

            await TcpToLocalAsync(net, ct);   // blokkeert tot verbinding wegvalt

            lock (_lock)
            {
                if (_tcp == tcp) { _tcp = null; _net = null; }
            }
            try { tcp.Close(); } catch { }

            if (!ct.IsCancellationRequested)
            {
                Log("Verbinding met server verbroken — opnieuw verbinden over 2 s");
                await DelayAsync(2000, ct);
            }
        }
    }

    private static async Task DelayAsync(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    // ── TCP → lokale poort ────────────────────────────────────────────────────

    private async Task TcpToLocalAsync(NetworkStream net, CancellationToken ct)
    {
        var buf = new byte[512];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await net.ReadAsync(buf, ct);
                if (n == 0) break;
                if (TraceBytes) Log($"← server {n}B: {BitConverter.ToString(buf, 0, n)}");

                if (_com != null)
                    await _com.BaseStream.WriteAsync(buf.AsMemory(0, n), ct);
                else if (_masterFd >= 0)
                    NativeWrite(_masterFd, buf, n);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) Log($"TCP → {_virtualPort} fout: {ex.Message}"); }
    }

    // ── Lokale poort → TCP ────────────────────────────────────────────────────

    private async Task LocalToTcpAsync(CancellationToken ct)
    {
        var buf = new byte[512];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try
            {
                if (_com != null)
                    n = await _com.BaseStream.ReadAsync(buf.AsMemory(), ct);
                else
                    n = NativeRead(_masterFd, buf, buf.Length);   // blokkeert (eigen thread)
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException)           { continue; }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) Err($"Lezen van {_virtualPort} mislukt: {ex.Message}");
                break;
            }

            if (n <= 0)
            {
                // PTY: geen app aan de slave-kant geeft soms EIO/0 — even wachten en opnieuw.
                await DelayAsync(20, ct);
                continue;
            }

            if (TraceBytes) Log($"→ server {n}B: {BitConverter.ToString(buf, 0, n)}");

            NetworkStream? net;
            lock (_lock) net = _net;
            if (net == null) continue;   // nog niet verbonden: weggooien

            try { await net.WriteAsync(buf.AsMemory(0, n), ct); }
            catch (Exception ex) { if (!ct.IsCancellationRequested) Log($"Schrijven naar server mislukt: {ex.Message}"); }
        }
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    public void Stop()
    {
        if (_cts.IsCancellationRequested) return;
        try { _cts.Cancel(); } catch { }

        if (_masterFd >= 0) { NativeClose(_masterFd); _masterFd = -1; }
        if (_slaveFd  >= 0) { NativeClose(_slaveFd);  _slaveFd  = -1; }

        try { _com?.Close(); _com?.Dispose(); } catch { }
        _com = null;

        lock (_lock)
        {
            try { _net?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            _net = null; _tcp = null;
        }

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416")]
    public static List<Com0ComPair> DetectCom0ComPairs()
    {
        var pairs = new List<Com0ComPair>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return pairs;
        return DetectViaBruteForceEnum();
    }

    /// <summary>
    /// Doorzoekt HKLM\SYSTEM\CurrentControlSet\Enum volledig op devices waarvan
    /// de FriendlyName "CNCA1", "CNCB2" etc. bevat — werkt ongeacht com0com versie.
    /// Het COM-nummer staat altijd onder Device Parameters\PortName.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<Com0ComPair> DetectViaBruteForceEnum()
    {
        var cncPorts = new List<(string cncName, string comName)>();
        try
        {
            using var enumKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum");
            if (enumKey == null) return new List<Com0ComPair>();

            foreach (var busName in enumKey.GetSubKeyNames())
            {
                using var busKey = enumKey.OpenSubKey(busName);
                if (busKey == null) continue;

                foreach (var devClass in busKey.GetSubKeyNames())
                {
                    using var classKey = busKey.OpenSubKey(devClass);
                    if (classKey == null) continue;

                    foreach (var instance in classKey.GetSubKeyNames())
                    {
                        using var inst = classKey.OpenSubKey(instance);
                        if (inst == null) continue;

                        string friendly = inst.GetValue("FriendlyName") as string ?? "";
                        // Zoek patroon: CNCA1, CNCB2, CNCA1 (COM7), etc.
                        var cm = Regex.Match(friendly, @"CNC([AB])(\d+)", RegexOptions.IgnoreCase);
                        if (!cm.Success) continue;

                        // COM-nummer onder Device Parameters\PortName
                        using var dp = inst.OpenSubKey("Device Parameters");
                        string? portName = dp?.GetValue("PortName") as string;
                        if (string.IsNullOrEmpty(portName)) continue;

                        cncPorts.Add((cm.Value, portName));
                    }
                }
            }
        }
        catch { }

        return GroupCncPorts(cncPorts);
    }

    private static List<Com0ComPair> GroupCncPorts(List<(string cncName, string comName)> cncPorts)
    {
        var pairs = new List<Com0ComPair>();
        foreach (var (cncName, comName) in cncPorts)
        {
            var nm = Regex.Match(cncName, @"CNC([AB])(\d+)", RegexOptions.IgnoreCase);
            if (!nm.Success) continue;
            bool isA   = nm.Groups[1].Value.Equals("A", StringComparison.OrdinalIgnoreCase);
            int pairId = int.Parse(nm.Groups[2].Value);
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
    [SupportedOSPlatform("windows")]
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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416")]
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

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, byte[] termios);

    [DllImport("libc")]
    private static extern void cfmakeraw(byte[] termios);

    /// <summary>Zet een tty in raw mode (geen echo, geen regelbuffer, geen CR/LF vertaling).</summary>
    private static void MakeRaw(int fd)
    {
        try
        {
            var t = new byte[256];   // ruim groter dan struct termios
            if (tcgetattr(fd, t) == 0) { cfmakeraw(t); tcsetattr(fd, 0 /*TCSANOW*/, t); }
        }
        catch { }
    }

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
