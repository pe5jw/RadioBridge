using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

namespace RadioBridge;

public partial class MainWindow : Window
{
    // ── Controls ──────────────────────────────────────────────────────────────
    private readonly RadioButton   _rbServer    = new() { Content = "Server  (wacht op verbinding)", IsChecked = true, GroupName = "Mode" };
    private readonly RadioButton   _rbClient    = new() { Content = "Client  (maak verbinding)", GroupName = "Mode" };
    private readonly TextBox       _txtHost     = new() { Text = "127.0.0.1", IsEnabled = false };
    private readonly NumericUpDown _nudPort     = new() { Minimum = 1024, Maximum = 65535, Value = 5000, Width = 110 };
    private readonly ComboBox      _cbCapture   = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox      _cbPlayback  = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox      _cbPreset    = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox      _chkNoise    = new() { Content = "Ruisfilter inschakelen" };
    private readonly TextBlock     _tblNoiseMode  = new() { Foreground = new SolidColorBrush(Color.Parse("#888")), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock     _tblPresetInfo = new() { Foreground = new SolidColorBrush(Color.Parse("#888")), FontSize = 11, Margin = new Thickness(0,4,0,0) };
    private readonly Button        _btnConnect  = new() { Content = "Server starten", MinWidth = 130 };
    private readonly Button        _btnRefresh  = new() { Content = "↻  Apparaten", MinWidth = 120 };
    private readonly Button        _btnFirewall = new() { Content = "🔥  Firewall", MinWidth = 120 };
    private readonly TextBlock     _tblLog      = new() { Foreground = new SolidColorBrush(Color.Parse("#aaa")), FontFamily = new FontFamily("Consolas,monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(6) };
    private readonly TextBlock     _tblStatus   = new() { Text = "Gestopt", Foreground = new SolidColorBrush(Color.Parse("#6fbf6f")) };
    private readonly Border        _pnlStatus   = new() { Background = new SolidColorBrush(Color.Parse("#1a3a1a")), BorderBrush = new SolidColorBrush(Color.Parse("#2a6a2a")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8,6) };
    private readonly ScrollViewer  _logScroller = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Height = 260 };

    // Serial port 1
    private readonly CheckBox      _chkPort1     = new() { Content = "Inschakelen" };
    private readonly ComboBox      _cbPort1Com   = new() { Width = 110 };
    private readonly NumericUpDown _nudPort1Baud = new() { Minimum = 300, Maximum = 115200, Value = 9600, Width = 100 };
    private readonly NumericUpDown _nudPort1Tcp  = new() { Minimum = 1024, Maximum = 65535, Value = 5001, Width = 90 };
    private readonly StackPanel    _spPort1      = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsEnabled = false, Margin = new Thickness(0,4,0,0) };

    // Serial port 2
    private readonly CheckBox      _chkPort2     = new() { Content = "Inschakelen" };
    private readonly ComboBox      _cbPort2Com   = new() { Width = 110 };
    private readonly NumericUpDown _nudPort2Baud = new() { Minimum = 300, Maximum = 115200, Value = 9600, Width = 100 };
    private readonly NumericUpDown _nudPort2Tcp  = new() { Minimum = 1024, Maximum = 65535, Value = 5002, Width = 90 };
    private readonly StackPanel    _spPort2      = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsEnabled = false, Margin = new Thickness(0,4,0,0) };

    // Testtoon
    private readonly CheckBox      _chkTone      = new() { Content = "Testtoon injecteren (sinusgolf)" };
    private readonly Slider        _sldToneFreq  = new() { Minimum = 100, Maximum = 3000, Value = 1000, Width = 220, TickFrequency = 100, IsSnapToTickEnabled = true };
    private readonly TextBlock     _tblToneFreq  = new() { Text = "1000 Hz", Foreground = new SolidColorBrush(Color.Parse("#dcdcdc")), VerticalAlignment = VerticalAlignment.Center, Width = 60 };
    private readonly Slider        _sldToneVol   = new() { Minimum = 1, Maximum = 100, Value = 25, Width = 160, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock     _tblToneVol   = new() { Text = "25%", Foreground = new SolidColorBrush(Color.Parse("#dcdcdc")), VerticalAlignment = VerticalAlignment.Center, Width = 40 };
    private readonly StackPanel    _spToneSliders = new() { Orientation = Orientation.Vertical, Spacing = 6, IsEnabled = false, Margin = new Thickness(0,6,0,0) };

    // ── App state ─────────────────────────────────────────────────────────────
    private AudioEngine? _engine;
    private bool _connected;
    private bool _restarting;
    private int  _serverPort;
    private int  _serverCaptureIdx;
    private int  _serverPlaybackIdx;

    private readonly StringBuilder _logBuf = new();
    private int _logLineCount;
    private const int MaxLogLines = 300;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindow()
    {
        // Window properties — set directly, no XAML loader needed
        Title      = "RadioBridge — Audio over netwerk v0.9";
        Width      = 680;
        Height     = 1020;
        MinWidth   = 560;
        MinHeight  = 800;
        Background = new SolidColorBrush(Color.Parse("#1e1e1e"));
        Foreground = new SolidColorBrush(Color.Parse("#dcdcdc"));
        FontSize   = 13;

        BuildUI();
        PopulateDevices();
        PopulatePresets();
        PopulateSerialPorts();
        UpdateUI();
    }

    // ── Build UI ──────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        var root = new StackPanel { Margin = new Thickness(10), Spacing = 0 };
        var scroll = new ScrollViewer { Content = root };
        Content = scroll;

        // Modus
        _rbServer.IsCheckedChanged += (s, e) => UpdateUI();
        _rbClient.IsCheckedChanged += (s, e) => UpdateUI();
        root.Children.Add(Section("Modus", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 24,
            Children = { _rbServer, _rbClient }
        }));

        // Verbinding
        var connGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,60,110") };
        connGrid.Children.Add(ColLabel("IP-adres (client):", 0));
        Grid.SetColumn(_txtHost, 1); connGrid.Children.Add(_txtHost);
        connGrid.Children.Add(ColLabel("Poort:", 2));
        Grid.SetColumn(_nudPort, 3); connGrid.Children.Add(_nudPort);
        root.Children.Add(Section("Verbinding", connGrid));

        // Audio apparaten
        var devGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            RowDefinitions    = new RowDefinitions("Auto,6,Auto")
        };
        devGrid.Children.Add(RowColLabel("Opname (mic):", 0, 0));
        Grid.SetRow(_cbCapture, 0); Grid.SetColumn(_cbCapture, 1); devGrid.Children.Add(_cbCapture);
        devGrid.Children.Add(RowColLabel("Afspelen (spkr):", 2, 0));
        Grid.SetRow(_cbPlayback, 2); Grid.SetColumn(_cbPlayback, 1); devGrid.Children.Add(_cbPlayback);
        root.Children.Add(Section("Audio apparaten", devGrid));

        // Preset
        _cbPreset.SelectionChanged += (s, e) => UpdatePresetInfo();
        root.Children.Add(Section("Codec / kwaliteit", new StackPanel
        {
            Children = { _cbPreset, _tblPresetInfo }
        }));

        // Ruis
        _chkNoise.IsCheckedChanged += OnNoiseChanged;
        root.Children.Add(Section("Ruisonderdrukking", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children    = { _chkNoise, _tblNoiseMode }
        }));

        // Serieel 1
        _spPort1.Children.Add(SimpleLabel("COM:"));
        _spPort1.Children.Add(_cbPort1Com);
        _spPort1.Children.Add(SimpleLabel("Baud:"));
        _spPort1.Children.Add(_nudPort1Baud);
        _spPort1.Children.Add(SimpleLabel("TCP:"));
        _spPort1.Children.Add(_nudPort1Tcp);
        _chkPort1.IsCheckedChanged += (s, e) => _spPort1.IsEnabled = _chkPort1.IsChecked == true;
        root.Children.Add(Section("Serieel poort 1  (CAT)", new StackPanel
        {
            Children = { _chkPort1, _spPort1 }
        }));

        // Serieel 2
        _spPort2.Children.Add(SimpleLabel("COM:"));
        _spPort2.Children.Add(_cbPort2Com);
        _spPort2.Children.Add(SimpleLabel("Baud:"));
        _spPort2.Children.Add(_nudPort2Baud);
        _spPort2.Children.Add(SimpleLabel("TCP:"));
        _spPort2.Children.Add(_nudPort2Tcp);
        _chkPort2.IsCheckedChanged += (s, e) => _spPort2.IsEnabled = _chkPort2.IsChecked == true;
        root.Children.Add(Section("Serieel poort 2  (PTT)", new StackPanel
        {
            Children = { _chkPort2, _spPort2 }
        }));

        // Testtoon
        _chkTone.IsCheckedChanged += OnToneChanged;
        _sldToneFreq.PropertyChanged += (s, e) => {
            if (e.Property.Name == "Value") {
                _tblToneFreq.Text = $"{(int)_sldToneFreq.Value} Hz";
                ApplyTone();
            }
        };
        _sldToneVol.PropertyChanged += (s, e) => {
            if (e.Property.Name == "Value") {
                _tblToneVol.Text = $"{(int)_sldToneVol.Value}%";
                ApplyTone();
            }
        };
        _spToneSliders.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = {
                SimpleLabel("Frequentie:"), _sldToneFreq, _tblToneFreq,
                SimpleLabel("  Volume:"),  _sldToneVol,  _tblToneVol
            }
        });
        root.Children.Add(Section("🔊  Testtoon", new StackPanel
        {
            Children = { _chkTone, _spToneSliders }
        }));

        // Knoppen
        _btnConnect.Background = new SolidColorBrush(Color.Parse("#1a6b1a"));
        _btnConnect.Click     += OnConnectClick;
        _btnRefresh.Click     += (s, e) => { PopulateDevices(); PopulateSerialPorts(); AppendLog("Apparatenlijst vernieuwd."); };
        _btnFirewall.Click    += OnFirewallClick;
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0,6,0,4),
            Children    = { _btnConnect, _btnRefresh, _btnFirewall }
        });

        // Status
        _pnlStatus.Child = _tblStatus;
        root.Children.Add(_pnlStatus);

        // Log
        _logScroller.Content = _tblLog;
        root.Children.Add(new Border
        {
            BorderBrush     = new SolidColorBrush(Color.Parse("#444")),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Margin          = new Thickness(0,4,0,0),
            Child           = _logScroller
        });
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private static Border Section(string title, Control content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text       = title,
            Foreground = new SolidColorBrush(Color.Parse("#888")),
            FontSize   = 11,
            Margin     = new Thickness(0,0,0,4)
        });
        panel.Children.Add(content);
        return new Border
        {
            BorderBrush     = new SolidColorBrush(Color.Parse("#444")),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10,8),
            Margin          = new Thickness(0,3),
            Child           = panel
        };
    }

    private static Control ColLabel(string text, int col)
    {
        var tb = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.Parse("#aaa")), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static Control RowColLabel(string text, int row, int col)
    {
        var tb = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.Parse("#aaa")), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static TextBlock SimpleLabel(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(Color.Parse("#aaa")), VerticalAlignment = VerticalAlignment.Center
    };

    // ── Populate ──────────────────────────────────────────────────────────────

    private void PopulateDevices()
    {
        _cbCapture.Items.Clear();
        foreach (var (idx, name) in AudioEngine.GetCaptureDevices())
            _cbCapture.Items.Add(new DeviceItem(idx, name));
        if (_cbCapture.Items.Count > 0) _cbCapture.SelectedIndex = 0;

        _cbPlayback.Items.Clear();
        foreach (var (idx, name) in AudioEngine.GetPlaybackDevices())
            _cbPlayback.Items.Add(new DeviceItem(idx, name));
        if (_cbPlayback.Items.Count > 0) _cbPlayback.SelectedIndex = 0;
    }

    private void PopulatePresets()
    {
        _cbPreset.Items.Clear();
        foreach (var p in AudioPresets.All)
            _cbPreset.Items.Add(p);
        _cbPreset.SelectedIndex = 6;
        UpdatePresetInfo();
    }

    private void PopulateSerialPorts()
    {
        _cbPort1Com.Items.Clear();
        _cbPort2Com.Items.Clear();
        foreach (var p in SerialPort.GetPortNames())
        {
            _cbPort1Com.Items.Add(p);
            _cbPort2Com.Items.Add(p);
        }
        if (_cbPort1Com.Items.Count > 0) { _cbPort1Com.SelectedIndex = 0; _cbPort2Com.SelectedIndex = 0; }
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        bool isServer = _rbServer.IsChecked == true;
        _txtHost.IsEnabled   = !isServer;
        _chkPort1.IsEnabled  = isServer;
        _chkPort2.IsEnabled  = isServer;
        _spPort1.IsEnabled   = isServer && _chkPort1.IsChecked == true;
        _spPort2.IsEnabled   = isServer && _chkPort2.IsChecked == true;

        _btnConnect.Content    = _connected ? "Verbreken" : (isServer ? "Server starten" : "Verbinden");
        _btnConnect.Background = _connected
            ? new SolidColorBrush(Color.Parse("#8b1a1a"))
            : new SolidColorBrush(Color.Parse("#1a6b1a"));

        _cbPreset.IsEnabled   = !_connected && !isServer;
        _cbCapture.IsEnabled  = !_connected;
        _cbPlayback.IsEnabled = !_connected;
    }

    private void UpdatePresetInfo()
    {
        if (_cbPreset.SelectedItem is AudioPreset p)
        {
            int kbps = p.UseOpus
                ? p.OpusBitrate / 1000
                : (p.SampleRate * p.BitsPerSample * p.Channels) / 1000;
            _tblPresetInfo.Text =
                $"{p.SampleRate} Hz  {p.BitsPerSample} bit  {p.Channels} ch  buf {p.BufferMs}ms  ~{kbps} kbps  " +
                (p.UseOpus ? "[Opus]" : "[PCM]");
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _tblStatus.Text       = text;
            _pnlStatus.Background = error
                ? new SolidColorBrush(Color.Parse("#3a1a1a"))
                : new SolidColorBrush(Color.Parse("#1a3a1a"));
            _tblStatus.Foreground = error
                ? new SolidColorBrush(Color.Parse("#bf6f6f"))
                : new SolidColorBrush(Color.Parse("#6fbf6f"));
        });
    }

    private void AppendLog(string msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var ts = DateTime.Now.ToString("HH:mm:ss");
            if (_logLineCount >= MaxLogLines)
            {
                var text = _tblLog.Text ?? "";
                int nl = 0, pos = 0;
                while (pos < text.Length && nl < 50) { if (text[pos++] == '\n') nl++; }
                _logBuf.Clear();
                _logBuf.Append(text[pos..]);
                _logLineCount -= nl;
            }
            _logBuf.AppendLine($"[{ts}]  {msg}");
            _logLineCount++;
            _tblLog.Text = _logBuf.ToString();
            _logScroller.ScrollToEnd();
        });
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnNoiseChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool en = _chkNoise.IsChecked == true;
        _engine?.SetNoiseSuppression(en);
        _tblNoiseMode.Text = en ? "(berekening...)" : "";
    }

    private async void OnConnectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_connected) { StopAll(); return; }

        bool   isServer    = _rbServer.IsChecked == true;
        int    captureIdx  = (_cbCapture.SelectedItem  as DeviceItem)?.Index ?? -1;
        int    playbackIdx = (_cbPlayback.SelectedItem as DeviceItem)?.Index ?? -1;
        int    port        = (int)(_nudPort.Value ?? 5000);

        _engine?.Dispose();
        _engine = new AudioEngine();
        _engine.StatusChanged      += msg => { AppendLog(msg); SetStatus(msg); };
        _engine.Error              += msg => { AppendLog($"⚠  {msg}"); SetStatus(msg, true); };
        _engine.ClientDisconnected += OnClientDisconnected;
        _engine.ConfigReceived     += OnConfigReceived;
        ApplyTone();

        if (isServer)
        {
            _engine.SerialConfig1  = GetSerialConfig(1);
            _engine.SerialConfig2  = GetSerialConfig(2);
            _serverPort            = port;
            _serverCaptureIdx      = captureIdx;
            _serverPlaybackIdx     = playbackIdx;
            _connected = true; UpdateUI();
            await _engine.StartServerAsync(port, captureIdx, playbackIdx);
        }
        else
        {
            var    preset      = (_cbPreset.SelectedItem as AudioPreset) ?? AudioPresets.Default;
            bool   noiseSup    = _chkNoise.IsChecked == true;
            string host        = _txtHost.Text ?? "127.0.0.1";
            _connected = true; UpdateUI();
            await _engine.StartClientAsync(host, port, captureIdx, playbackIdx, preset, noiseSup);
        }
    }

    private void OnFirewallClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        int port = (int)(_nudPort.Value ?? 5000);
        try { FirewallHelper.OpenPort(port); AppendLog($"Firewall: poort {port} TCP geopend."); }
        catch (Exception ex)               { AppendLog($"Firewall mislukt: {ex.Message}"); }
    }

    private void OnClientDisconnected()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_restarting && _rbServer.IsChecked == true)
            {
                _restarting = true;
                AppendLog("Client verbroken — wachten op nieuwe verbinding...");
                _engine?.Dispose(); _engine = null;
                _connected = false;
                _ = RestartServerAsync();
            }
        });
    }

    private async Task RestartServerAsync()
    {
        await Task.Delay(500);
        _engine = new AudioEngine();
        _engine.StatusChanged      += msg => { AppendLog(msg); SetStatus(msg); };
        _engine.Error              += msg => { AppendLog($"⚠  {msg}"); SetStatus(msg, true); };
        _engine.ClientDisconnected += OnClientDisconnected;
        _engine.ConfigReceived     += OnConfigReceived;
        _engine.SerialConfig1 = GetSerialConfig(1);
        _engine.SerialConfig2 = GetSerialConfig(2);
        _connected = true; _restarting = false; UpdateUI();
        ApplyTone();
        await _engine.StartServerAsync(_serverPort, _serverCaptureIdx, _serverPlaybackIdx);
    }

    private void OnConfigReceived(BridgeConfig cfg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_rbClient.IsChecked == true)
            {
                if (cfg.Port1.Enabled) AppendLog($"Server CAT: {cfg.Port1.ComPort} {cfg.Port1.Baud} baud → TCP {cfg.Port1.TcpPort}");
                if (cfg.Port2.Enabled) AppendLog($"Server PTT: {cfg.Port2.ComPort} {cfg.Port2.Baud} baud → TCP {cfg.Port2.TcpPort}");
            }
            _tblNoiseMode.Text = _chkNoise.IsChecked == true ? "(actief)" : "";
        });
    }

    // ── Testtoon ──────────────────────────────────────────────────────────────

    private void OnToneChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool en = _chkTone.IsChecked == true;
        _spToneSliders.IsEnabled = en;
        ApplyTone();
    }

    private void ApplyTone()
    {
        if (_engine == null) return;
        bool  en   = _chkTone.IsChecked == true;
        float freq = (float)_sldToneFreq.Value;
        float vol  = (float)_sldToneVol.Value / 100f;
        _engine.SetTone(en, freq, vol);
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    private void StopAll()
    {
        _engine?.Stop(); _engine?.Dispose(); _engine = null;
        _connected = false; _restarting = false;
        UpdateUI(); SetStatus("Gestopt"); AppendLog("Verbinding beëindigd.");
    }

    protected override void OnClosing(WindowClosingEventArgs e) { StopAll(); base.OnClosing(e); }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SerialPortConfig GetSerialConfig(int port) => port == 1
        ? new SerialPortConfig { Enabled = _chkPort1.IsChecked == true, ComPort = _cbPort1Com.SelectedItem?.ToString() ?? "", Baud = (int)(_nudPort1Baud.Value ?? 9600), TcpPort = (int)(_nudPort1Tcp.Value ?? 5001) }
        : new SerialPortConfig { Enabled = _chkPort2.IsChecked == true, ComPort = _cbPort2Com.SelectedItem?.ToString() ?? "", Baud = (int)(_nudPort2Baud.Value ?? 9600), TcpPort = (int)(_nudPort2Tcp.Value ?? 5002) };
}

internal record DeviceItem(int Index, string Name) { public override string ToString() => Name; }
