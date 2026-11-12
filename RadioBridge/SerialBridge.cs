using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace RadioBridge;

/// <summary>
/// Bridges a local serial port over TCP.
/// Server side: listens on a TCP port, forwards data to/from a local COM port.
/// Client side: connects to server, forwards data to/from a local COM port.
///
/// Two instances are used: one for CAT (port1) and one for PTT (port2).
/// </summary>
public class SerialBridge : IDisposable
{
    private SerialPort?  _serial;
    private TcpListener? _listener;
    private TcpClient?   _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource _cts = new();
    private bool _running;
    private bool _disconnectFired;
    private readonly string _name; // "CAT" or "PTT" for logging

    public event Action<string>? StatusChanged;
    public event Action<string>? Error;
    public event Action?         ClientDisconnected;

    public SerialBridge(string name) => _name = name;

    private void Log(string msg) => StatusChanged?.Invoke($"[{_name}] {msg}");

    // ── Server ────────────────────────────────────────────────────────────────

    public async Task StartServerAsync(string comPort, int baud, int tcpPort)
    {
        try
        {
            Log($"Luisteren op TCP {tcpPort} → {comPort} {baud} baud");
            _listener = new TcpListener(IPAddress.Any, tcpPort);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();

            while (!_cts.Token.IsCancellationRequested)
            {
                // Wacht op volgende verbinding
                _tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
                _tcp.NoDelay = true;
                _stream = _tcp.GetStream();
                _disconnectFired = false;
                Log($"Serieel verbonden");
                await StartSerialAsync(comPort, baud);

                // Verbinding verbroken — wacht even en luister opnieuw
                try { _serial?.Close(); } catch { }
                try { _stream?.Close(); } catch { }
                try { _tcp?.Close();   } catch { }
                _serial = null; _stream = null; _tcp = null;
                _running = false;

                if (!_cts.Token.IsCancellationRequested)
                    Log($"Wacht op nieuwe verbinding op TCP {tcpPort}...");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke($"[{_name}] Server fout: {ex.Message}"); }
    }

    // ── Client ────────────────────────────────────────────────────────────────

    public async Task StartClientAsync(string host, int tcpPort, string comPort, int baud)
    {
        try
        {
            Log($"Verbinden met {host}:{tcpPort} → {comPort} {baud} baud");
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(host, tcpPort, _cts.Token);
            _stream = _tcp.GetStream();
            _disconnectFired = false;
            Log($"Verbonden");
            await StartSerialAsync(comPort, baud);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Error?.Invoke($"[{_name}] Verbinding mislukt: {ex.Message}"); }
    }

    // ── Serial setup ──────────────────────────────────────────────────────────

    private async Task StartSerialAsync(string comPort, int baud)
    {
        _serial = new SerialPort(comPort, baud)
        {
            ReadTimeout  = SerialPort.InfiniteTimeout,
            WriteTimeout = 500,
            DtrEnable    = true,
            RtsEnable    = false,
        };
        try
        {
            _serial.Open();
        }
        catch (Exception ex)
        {
            Error?.Invoke($"[{_name}] Kan {comPort} niet openen: {ex.Message}");
            return;
        }

        _running = true;
        Log($"Serieel geopend: {comPort} {baud} baud, start data-loops");

        // Wacht tot één van beide richtingen klaar is (verbinding verbroken)
        // Serial → TCP (polling loop — DataReceived event werkt niet betrouwbaar op Linux)
        var t1 = Task.Run(() => SerialToTcpAsync(_cts.Token));
        // TCP → Serial
        var t2 = Task.Run(() => TcpToSerialAsync(_cts.Token));

        Log($"Data-loops gestart, wachten...");
        await Task.WhenAny(t1, t2);
        Log($"Een data-loop gestopt, opruimen");
        // Wacht kort zodat de andere taak ook kan afsluiten
        try { await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch { }
        Log($"Beide data-loops gestopt");
    }

    // ── Serial → TCP ─────────────────────────────────────────────────────────

    private async Task SerialToTcpAsync(CancellationToken ct)
    {
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested && _serial?.IsOpen == true)
            {
                int n = 0;
                try
                {
                    // BaseStream.ReadAsync werkt cross-platform betrouwbaar
                    n = await _serial.BaseStream.ReadAsync(buf, 0, buf.Length, ct);
                }
                catch (TimeoutException) { continue; }

                if (n <= 0) break;
                Log($"← Serieel {n}B → TCP: {BitConverter.ToString(buf, 0, n)}");
                if (_stream != null)
                    await _stream.WriteAsync(buf.AsMemory(0, n), ct);
            }
        }
        catch (OperationCanceledException) { Log("SerialToTcp: geannuleerd"); }
        catch (ObjectDisposedException ex) { Log($"SerialToTcp: disposed — {ex.Message}"); _running = false; }
        catch (Exception ex) { Log($"SerialToTcp fout ({ex.GetType().Name}): {ex.Message}"); }
        Log("SerialToTcp loop klaar");
    }

    // ── TCP → Serial ─────────────────────────────────────────────────────────

    private async Task TcpToSerialAsync(CancellationToken ct)
    {
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int n = await _stream.ReadAsync(buf, ct);
                if (n == 0)
                {
                    FireDisconnect();
                    break;
                }
                Log($"→ TCP {n}B → Serieel: {BitConverter.ToString(buf, 0, n)}");
                if (_serial?.IsOpen == true)
                    _serial.Write(buf, 0, n);
            }
        }
        catch (OperationCanceledException) { Log("TcpToSerial: geannuleerd"); }
        catch (ObjectDisposedException ex) { Log($"TcpToSerial: disposed — {ex.Message}"); }
        catch (Exception ex)
        {
            Log($"TcpToSerial fout ({ex.GetType().Name}): {ex.Message}");
            if (_running)
            {
                Error?.Invoke($"[{_name}] Verbinding verbroken: {ex.Message}");
                FireDisconnect();
            }
        }
        Log("TcpToSerial loop klaar");
    }

    private void FireDisconnect()
    {
        if (!_disconnectFired) { _disconnectFired = true; ClientDisconnected?.Invoke(); }
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    public void Stop()
    {
        if (!_running && _tcp == null && _listener == null) return;
        _cts.Cancel();
        _running = false;

        try { _serial?.Close(); }  catch { }
        try { _serial?.Dispose(); } catch { }
        _serial = null;

        try { _stream?.Close(); } catch { }
        _stream = null;

        try
        {
            if (_tcp?.Client?.Connected == true)
                _tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, new LingerOption(true, 0));
            _tcp?.Close();
        }
        catch { }
        _tcp = null;

        try { _listener?.Stop(); } catch { }
        _listener = null;

        _cts = new CancellationTokenSource();
        Log("Gestopt.");
    }

    public void Dispose() => Stop();

    // ── Available ports ───────────────────────────────────────────────────────

    public static string[] GetPortNames()
    {
        try { return SerialPort.GetPortNames().OrderBy(p => p).ToArray(); }
        catch { return Array.Empty<string>(); }
    }
}
