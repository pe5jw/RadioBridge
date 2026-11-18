using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace RadioBridge;

/// <summary>
/// Server-kant seriële brug: lokale COM-poort ↔ TCP.
///
/// - De COM-poort wordt één keer geopend en blijft open zolang de brug draait.
/// - Er is altijd maximaal één TCP-client actief. Een nieuwe verbinding vervangt
///   de vorige, zodat een hangende of half-dode verbinding nooit blokkeert.
/// - Data van de COM-poort zonder actieve client wordt weggegooid.
///
/// Volledig transparant: RadioBridge kent het protocol niet, dus werkt met
/// elke transceiver of elk ander apparaat met een seriële poort.
/// </summary>
public sealed class SerialBridge : IDisposable
{
    private readonly string _name;               // "Poort 1" / "Poort 2" voor logging
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();

    private SerialPort?  _serial;
    private TcpListener? _listener;

    // Huidige client (onder _lock)
    private TcpClient?               _client;
    private NetworkStream?           _clientStream;
    private CancellationTokenSource? _sessionCts;

    /// <summary>Log elk pakket als hex. Handig bij debuggen, uitzetten als alles werkt.</summary>
    public bool TraceBytes { get; set; } = true;

    public event Action<string>? StatusChanged;
    public event Action<string>? Error;

    public SerialBridge(string name) => _name = name;

    private void Log(string msg) => StatusChanged?.Invoke($"[{_name}] {msg}");
    private void Err(string msg) => Error?.Invoke($"[{_name}] {msg}");

    // ── Start ─────────────────────────────────────────────────────────────────

    public async Task StartServerAsync(string comPort, int baud, int tcpPort)
    {
        var ct = _cts.Token;

        // 1. COM-poort openen (blijft open)
        try
        {
            _serial = new SerialPort(comPort, baud)
            {
                ReadTimeout  = SerialPort.InfiniteTimeout,
                WriteTimeout = 1000,
                DtrEnable    = true,   // veel CAT-kabels halen voeding uit DTR
                RtsEnable    = false,  // RTS hoog = PTT op sommige interfaces
            };
            _serial.Open();
            Log($"{comPort} geopend ({baud} baud)");
        }
        catch (Exception ex)
        {
            Err($"Kan {comPort} niet openen: {ex.Message}");
            return;
        }

        _ = Task.Run(() => SerialReadLoopAsync(ct));

        // 2. TCP accepteren
        try
        {
            _listener = new TcpListener(IPAddress.Any, tcpPort);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
            Log($"Luisteren op TCP {tcpPort} → {comPort}");

            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                AttachClient(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException)    { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) Err($"TCP server fout: {ex.Message}"); }
    }

    // ── Client beheer ─────────────────────────────────────────────────────────

    private void AttachClient(TcpClient client)
    {
        CancellationTokenSource session;
        NetworkStream stream = client.GetStream();
        lock (_lock)
        {
            if (_client != null)
            {
                Log("Nieuwe client — vorige verbinding wordt gesloten");
                DropClientLocked();
            }
            _client       = client;
            _clientStream = stream;
            _sessionCts   = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            session       = _sessionCts;
        }
        Log($"Client verbonden: {client.Client.RemoteEndPoint}");
        _ = Task.Run(() => TcpReadLoopAsync(client, stream, session.Token));
    }

    private void DropClientLocked()
    {
        try { _sessionCts?.Cancel(); } catch { }
        try { _client?.Close();      } catch { }
        _client = null; _clientStream = null; _sessionCts = null;
    }

    // ── TCP → COM ─────────────────────────────────────────────────────────────

    private async Task TcpReadLoopAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[512];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buf, ct);
                if (n == 0) break;   // client sloot netjes af
                if (TraceBytes) Log($"→ radio {n}B: {BitConverter.ToString(buf, 0, n)}");

                var s = _serial;
                if (s?.IsOpen == true) s.Write(buf, 0, n);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) Log($"Client fout: {ex.Message}"); }

        lock (_lock)
        {
            if (_client == client)
            {
                DropClientLocked();
                Log("Client verbroken — wacht op nieuwe verbinding");
            }
        }
    }

    // ── COM → TCP ─────────────────────────────────────────────────────────────

    private async Task SerialReadLoopAsync(CancellationToken ct)
    {
        var buf = new byte[512];
        while (!ct.IsCancellationRequested)
        {
            int n;
            try
            {
                n = await _serial!.BaseStream.ReadAsync(buf.AsMemory(), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException)           { continue; }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) Err($"Lezen van COM-poort mislukt: {ex.Message}");
                break;
            }

            if (n <= 0) { await Task.Delay(5, ct).ContinueWith(_ => { }); continue; }
            if (TraceBytes) Log($"← radio {n}B: {BitConverter.ToString(buf, 0, n)}");

            NetworkStream? stream;
            lock (_lock) stream = _clientStream;
            if (stream == null) continue;   // geen client: weggooien

            try { await stream.WriteAsync(buf.AsMemory(0, n), ct); }
            catch (Exception ex) { if (!ct.IsCancellationRequested) Log($"Schrijven naar client mislukt: {ex.Message}"); }
        }
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    public void Stop()
    {
        if (_cts.IsCancellationRequested) return;
        try { _cts.Cancel(); } catch { }

        lock (_lock) DropClientLocked();

        try { _listener?.Stop(); } catch { }
        _listener = null;

        try { _serial?.Close(); _serial?.Dispose(); } catch { }
        _serial = null;

        Log("Gestopt.");
    }

    public void Dispose() => Stop();

    // ── Beschikbare poorten ───────────────────────────────────────────────────

    public static string[] GetPortNames()
    {
        try { return SerialPort.GetPortNames().OrderBy(p => p).ToArray(); }
        catch { return Array.Empty<string>(); }
    }
}
