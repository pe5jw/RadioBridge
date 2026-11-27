using System.Runtime.InteropServices;

namespace RadioBridge;

/// <summary>
/// RadioBridge zonder venster (--headless). Gebruikt de laatst opgeslagen instellingen
/// (settings.json) en logt naar de console — onder systemd komt dat in de journal.
///
///   Server: luistert, en begint na het verbreken van een client opnieuw te luisteren.
///   Client: verbindt, en probeert het opnieuw als de verbinding mislukt of wegvalt.
/// </summary>
internal static class Headless
{
    private const int RetryDelayMs = 5000;

    public static int Run(bool server)
    {
        var st = AppSettings.Load();
        Log($"RadioBridge {Program.Version} headless — {(server ? "server" : "client")}");
        Log($"Instellingen: {AppSettings.FilePath}{(File.Exists(AppSettings.FilePath) ? "" : "  (niet gevonden — standaardwaarden)")}");

        using var cts = new CancellationTokenSource();
        // Ctrl+C en systemctl stop (SIGTERM) → netjes afsluiten
        using var sigInt  = PosixSignalRegistration.Create(PosixSignal.SIGINT,  c => { c.Cancel = true; cts.Cancel(); });
        using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; cts.Cancel(); });

        try { RunLoopAsync(server, st, cts.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }

        Log("Gestopt.");
        return 0;
    }

    private static async Task RunLoopAsync(bool server, AppSettings st, CancellationToken ct)
    {
        int capIdx = FindDevice(AudioEngine.GetCaptureDevices(),  st.CaptureDevice,  "Opname");
        int pbIdx  = FindDevice(AudioEngine.GetPlaybackDevices(), st.PlaybackDevice, "Afspelen");

        if (server)
        {
            Log($"Poort {st.Port}  |  mic {st.CaptureVolume:0}%  spkr {st.PlaybackVolume:0}%");
            LogSerial(1, st.Serial1);
            LogSerial(2, st.Serial2);
        }
        else
        {
            Log($"Server {st.Host}:{st.Port}  |  codec: {Preset(st).Name}  |  mic {st.CaptureVolume:0}%  spkr {st.PlaybackVolume:0}%");
            if (!string.IsNullOrEmpty(st.VirtualCom1)) Log($"Poort 1 virtueel: {st.VirtualCom1}");
            if (!string.IsNullOrEmpty(st.VirtualCom2)) Log($"Poort 2 virtueel: {st.VirtualCom2}");
        }

        while (!ct.IsCancellationRequested)
        {
            var ended  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var engine = new AudioEngine();
            engine.StatusChanged      += Log;
            engine.Error              += m => Log($"⚠ {m}");
            engine.ClientDisconnected += () => ended.TrySetResult();
            engine.CaptureGain  = (float)(st.CaptureVolume  / 100.0);
            engine.PlaybackGain = (float)(st.PlaybackVolume / 100.0);

            using (ct.Register(() => { engine.Stop(); ended.TrySetResult(); }))
            {
                if (server)
                {
                    engine.SerialConfig1 = ToConfig(st.Serial1);
                    engine.SerialConfig2 = ToConfig(st.Serial2);
                    await engine.StartServerAsync(st.Port, capIdx, pbIdx);
                }
                else
                {
                    engine.VirtualComPort1 = st.VirtualCom1;
                    engine.VirtualComPort2 = st.VirtualCom2;
                    await engine.StartClientAsync(st.Host, st.Port, capIdx, pbIdx, Preset(st), st.NoiseFilter);
                }

                if (engine.IsRunning)
                    await ended.Task;   // tot de verbinding wegvalt of we stoppen
            }

            engine.Dispose();
            if (ct.IsCancellationRequested) break;

            int delay = server && engine.WasConnected ? 500 : RetryDelayMs;
            Log(server ? "Opnieuw luisteren..." : $"Opnieuw verbinden over {delay / 1000} s...");
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AudioPreset Preset(AppSettings st) =>
        AudioPresets.All.FirstOrDefault(p => p.Name == st.Preset) ?? AudioPresets.Default;

    private static int FindDevice(List<(int Index, string Name)> devices, string name, string label)
    {
        var match = devices.FirstOrDefault(d => d.Name == name);
        if (!string.IsNullOrEmpty(name) && match.Name == null)
            Log($"⚠ {label}-apparaat '{name}' niet gevonden — standaardapparaat gebruikt");
        var dev = match.Name != null ? match : devices[0];
        Log($"{label}: {dev.Name}");
        return dev.Index;
    }

    private static SerialPortConfig ToConfig(AppSettings.SerialSetting s) => new()
    {
        Enabled = s.Enabled, ComPort = s.ComPort, Baud = s.Baud, TcpPort = s.TcpPort
    };

    private static void LogSerial(int n, AppSettings.SerialSetting s)
    {
        if (s.Enabled) Log($"Seriële poort {n}: {s.ComPort} {s.Baud} baud → TCP {s.TcpPort}");
    }

    private static readonly object LogLock = new();
    private static void Log(string msg)
    {
        lock (LogLock) Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}");
    }
}
