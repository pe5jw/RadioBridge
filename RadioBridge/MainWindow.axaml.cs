using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

namespace RadioBridge;

public partial class MainWindow : Window
{
    private AudioEngine?  _engine;
    private SerialBridge? _cat;
    private SerialBridge? _ptt;
    private bool _connected;
    private bool _restarting;
    private int  _serverPort;
    private int  _serverCaptureIdx;
    private int  _serverPlaybackIdx;

    private readonly StringBuilder _logBuf = new();
    private const int MaxLogLines = 300;
    private int _logLineCount;

    public MainWindow()
    {
        InitializeComponent();
        PopulateDevices();
        PopulatePresets();
        PopulateSerialPorts();
        UpdateUI();
    }

    // ── Device lists ──────────────────────────────────────────────────────────

    private void PopulateDevices()
    {
        CbCapture.Items.Clear();
        foreach (var (idx, name) in AudioEngine.GetCaptureDevices())
            CbCapture.Items.Add(new DeviceItem(idx, name));
        CbCapture.SelectedIndex = 0;

        CbPlayback.Items.Clear();
        foreach (var (idx, name) in AudioEngine.GetPlaybackDevices())
            CbPlayback.Items.Add(new DeviceItem(idx, name));
        CbPlayback.SelectedIndex = 0;
    }

    private void PopulatePresets()
    {
        CbPreset.Items.Clear();
        foreach (var p in AudioPresets.All)
            CbPreset.Items.Add(p);
        // Default: Opus Spraak 16kHz mono 12kbps
        CbPreset.SelectedIndex = 6;
        CbPreset.SelectionChanged += OnPresetChanged;
        UpdatePresetInfo();
    }

    private void PopulateSerialPorts()
    {
        var ports = SerialPort.GetPortNames();
        CbPort1Com.Items.Clear();
        CbPort2Com.Items.Clear();
        foreach (var p in ports)
        {
            CbPort1Com.Items.Add(p);
            CbPort2Com.Items.Add(p);
        }
        if (CbPort1Com.Items.Count > 0) CbPort1Com.SelectedIndex = 0;
        if (CbPort2Com.Items.Count > 0) CbPort2Com.SelectedIndex = 0;
    }

    // ── UI state ──────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        bool isServer = RbServer.IsChecked == true;
        TxtHost.IsEnabled  = !isServer;
        GridPort1.IsEnabled = isServer;
        GridPort2.IsEnabled = isServer;

        BtnConnect.Content    = _connected ? "Verbreken" : (isServer ? "Server starten" : "Verbinden");
        BtnConnect.Background = _connected
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8b1a1a"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a6b1a"));

        CbPreset.IsEnabled  = !_connected && !isServer;
        CbCapture.IsEnabled = !_connected;
        CbPlayback.IsEnabled = !_connected;
    }

    private void UpdatePresetInfo()
    {
        if (CbPreset.SelectedItem is AudioPreset p)
        {
            int kbps = p.UseOpus
                ? p.OpusBitrate / 1000
                : (p.SampleRate * p.BitsPerSample * p.Channels) / 1000;
            TblPresetInfo.Text =
                $"{p.SampleRate} Hz  {p.BitsPerSample} bit  {p.Channels} ch  " +
                $"buf {p.BufferMs}ms  ~{kbps} kbps  " +
                (p.UseOpus ? $"[Opus]" : "[PCM geen compressie]");
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TblStatus.Text = text;
            PnlStatus.Background = error
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3a1a1a"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a3a1a"));
            TblStatus.Foreground = error
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#bf6f6f"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6fbf6f"));
        });
    }

    private void AppendLog(string msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            if (_logLineCount >= MaxLogLines)
            {
                // Trim oldest ~50 lines
                var text = TblLog.Text ?? "";
                int nl   = 0;
                int pos  = 0;
                while (pos < text.Length && nl < 50)
                {
                    if (text[pos] == '\n') nl++;
                    pos++;
                }
                _logBuf.Clear();
                _logBuf.Append(text[pos..]);
                _logLineCount -= nl;
            }
            _logBuf.AppendLine($"[{ts}]  {msg}");
            _logLineCount++;
            TblLog.Text = _logBuf.ToString();
            LogScroller.ScrollToEnd();
        });
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnModeChanged(object? sender, RoutedEventArgs e) => UpdateUI();

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e) => UpdatePresetInfo();

    private void OnNoiseChanged(object? sender, RoutedEventArgs e)
    {
        bool en = ChkNoise.IsChecked == true;
        _engine?.SetNoiseSuppression(en);
        TblNoiseMode.Text = en ? "(berekening...)" : "";
    }

    private void OnPort1Changed(object? sender, RoutedEventArgs e)
        => GridPort1.IsEnabled = ChkPort1.IsChecked == true;

    private void OnPort2Changed(object? sender, RoutedEventArgs e)
        => GridPort2.IsEnabled = ChkPort2.IsChecked == true;

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        if (_connected)
        {
            StopAll();
            return;
        }

        bool isServer    = RbServer.IsChecked == true;
        int  captureIdx  = (CbCapture.SelectedItem  as DeviceItem)?.Index ?? -1;
        int  playbackIdx = (CbPlayback.SelectedItem as DeviceItem)?.Index ?? -1;
        int  port        = (int)(NudPort.Value ?? 5000);

        _engine?.Dispose();
        _engine = new AudioEngine();
        _engine.StatusChanged     += msg => { AppendLog(msg); SetStatus(msg); };
        _engine.Error             += msg => { AppendLog($"⚠  {msg}"); SetStatus(msg, true); };
        _engine.ClientDisconnected += OnClientDisconnected;
        _engine.ConfigReceived    += OnConfigReceived;

        if (isServer)
        {
            _engine.SerialConfig1 = GetSerialConfig(1);
            _engine.SerialConfig2 = GetSerialConfig(2);
            _serverPort        = port;
            _serverCaptureIdx  = captureIdx;
            _serverPlaybackIdx = playbackIdx;
            _connected = true;
            UpdateUI();
            await _engine.StartServerAsync(port, captureIdx, playbackIdx);
        }
        else
        {
            var preset       = (AudioPreset?)CbPreset.SelectedItem ?? AudioPresets.Default;
            bool noiseSuppress = ChkNoise.IsChecked == true;
            string host      = TxtHost.Text ?? "127.0.0.1";
            _connected = true;
            UpdateUI();
            await _engine.StartClientAsync(host, port, captureIdx, playbackIdx, preset, noiseSuppress);
        }
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        PopulateDevices();
        PopulateSerialPorts();
        AppendLog("Apparatenlijst vernieuwd.");
    }

    private void OnFirewallClick(object? sender, RoutedEventArgs e)
    {
        int port = (int)(NudPort.Value ?? 5000);
        try
        {
            FirewallHelper.OpenPort(port);
            AppendLog($"Firewall: poort {port} TCP geopend.");
        }
        catch (Exception ex)
        {
            AppendLog($"Firewall mislukt: {ex.Message}");
        }
    }

    // ── Engine events ─────────────────────────────────────────────────────────

    private void OnClientDisconnected()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_restarting && RbServer.IsChecked == true)
            {
                _restarting = true;
                AppendLog("Client verbroken — wachten op nieuwe verbinding...");
                _engine?.Dispose();
                _engine = null;
                _connected = false;
                _ = RestartServerAsync();
            }
        });
    }

    private async Task RestartServerAsync()
    {
        await Task.Delay(500);
        _engine = new AudioEngine();
        _engine.StatusChanged     += msg => { AppendLog(msg); SetStatus(msg); };
        _engine.Error             += msg => { AppendLog($"⚠  {msg}"); SetStatus(msg, true); };
        _engine.ClientDisconnected += OnClientDisconnected;
        _engine.ConfigReceived    += OnConfigReceived;
        _engine.SerialConfig1 = GetSerialConfig(1);
        _engine.SerialConfig2 = GetSerialConfig(2);
        _connected = true;
        _restarting = false;
        UpdateUI();
        await _engine.StartServerAsync(_serverPort, _serverCaptureIdx, _serverPlaybackIdx);
    }

    private void OnConfigReceived(BridgeConfig cfg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (RbClient.IsChecked == true)
            {
                // Show serial port info received from server
                if (cfg.Port1.Enabled) AppendLog($"Server CAT: {cfg.Port1.ComPort} {cfg.Port1.Baud} baud → TCP {cfg.Port1.TcpPort}");
                if (cfg.Port2.Enabled) AppendLog($"Server PTT: {cfg.Port2.ComPort} {cfg.Port2.Baud} baud → TCP {cfg.Port2.TcpPort}");
            }
            // Update noise mode label
            TblNoiseMode.Text = ChkNoise.IsChecked == true ? "(actief)" : "";
        });
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    private void StopAll()
    {
        _engine?.Stop();
        _engine?.Dispose();
        _engine = null;

        _cat?.Dispose();
        _cat = null;
        _ptt?.Dispose();
        _ptt = null;

        _connected = false;
        _restarting = false;
        UpdateUI();
        SetStatus("Gestopt");
        AppendLog("Verbinding beëindigd.");
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        StopAll();
        base.OnClosing(e);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SerialPortConfig GetSerialConfig(int port)
    {
        if (port == 1)
            return new SerialPortConfig
            {
                Enabled = ChkPort1.IsChecked == true,
                ComPort = CbPort1Com.SelectedItem?.ToString() ?? "",
                Baud    = (int)(NudPort1Baud.Value ?? 9600),
                TcpPort = (int)(NudPort1Tcp.Value ?? 5001),
            };
        else
            return new SerialPortConfig
            {
                Enabled = ChkPort2.IsChecked == true,
                ComPort = CbPort2Com.SelectedItem?.ToString() ?? "",
                Baud    = (int)(NudPort2Baud.Value ?? 9600),
                TcpPort = (int)(NudPort2Tcp.Value ?? 5002),
            };
    }
}

/// <summary>Wrapper to display device name in ComboBox while keeping index.</summary>
internal record DeviceItem(int Index, string Name)
{
    public override string ToString() => Name;
}
