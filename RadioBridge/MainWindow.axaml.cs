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
    private readonly NumericUpDown _nudPort     = new() { Minimum = 1024, Maximum = 65535, Value = 5000, Width = 120 };
    private readonly ComboBox      _cbCapture   = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox      _cbPlayback  = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    // Volume sliders
    private readonly Slider    _sldCapVol  = new() { Minimum = 0, Maximum = 400, Value = 100, Width = 120, TickFrequency = 10, IsSnapToTickEnabled = false };
    private readonly TextBlock _tblCapVol  = new() { Text = "100%", Width = 36, Foreground = new SolidColorBrush(Color.Parse("#dcdcdc")), VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider    _sldPbVol   = new() { Minimum = 0, Maximum = 400, Value = 100, Width = 120, TickFrequency = 10, IsSnapToTickEnabled = false };
    private readonly TextBlock _tblPbVol   = new() { Text = "100%", Width = 36, Foreground = new SolidColorBrush(Color.Parse("#dcdcdc")), VerticalAlignment = VerticalAlignment.Center };

    // VU meters
    private readonly Border    _vuCapBar   = new() { Height = 8, Width = 0, Background = new SolidColorBrush(Color.Parse("#4caf50")), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border    _vuPbBar    = new() { Height = 8, Width = 0, Background = new SolidColorBrush(Color.Parse("#2196f3")), HorizontalAlignment = HorizontalAlignment.Left };
    private const double VuMaxWidth = 140.0;

    // Panels voor server/client wisseling (rechterkolom)
    private readonly StackPanel _spServerPorts = new() { Spacing = 3 };
    private readonly StackPanel _spClientPorts = new() { Spacing = 3 };
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
    private readonly NumericUpDown _nudPort1Baud = new() { Minimum = 300, Maximum = 115200, Value = 9600, Width = 130 };
    private readonly NumericUpDown _nudPort1Tcp  = new() { Minimum = 1024, Maximum = 65535, Value = 5001, Width = 125 };
    private readonly StackPanel    _spPort1      = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsEnabled = false, Margin = new Thickness(0,4,0,0) };

    // Serial port 2
    private readonly CheckBox      _chkPort2     = new() { Content = "Inschakelen" };
    private readonly ComboBox      _cbPort2Com   = new() { Width = 110 };
    private readonly NumericUpDown _nudPort2Baud = new() { Minimum = 300, Maximum = 115200, Value = 9600, Width = 130 };
    private readonly NumericUpDown _nudPort2Tcp  = new() { Minimum = 1024, Maximum = 65535, Value = 5002, Width = 125 };
    private readonly StackPanel    _spPort2      = new() { Orientation = Orientation.Horizontal, Spacing = 8, IsEnabled = false, Margin = new Thickness(0,4,0,0) };

    // Client virtuele COM (com0com / PTY)
    private readonly TextBlock     _tblVirtualInfo  = new() { Foreground = new SolidColorBrush(Color.Parse("#888")), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Button        _btnDetectCom0Com = new() { Content = "🔍  com0com detecteren", MinWidth = 160 };
    private readonly ComboBox      _cbVCom1         = new() { MinWidth = 220 };
    private readonly ComboBox      _cbVCom2         = new() { MinWidth = 220 };
    private readonly StackPanel    _spVComClient    = new() { Spacing = 6 };

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
        Width      = 900;
        Height     = 760;
        MinWidth   = 720;
        MinHeight  = 600;
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
        // ── Event handlers ────────────────────────────────────────────────────

        _rbServer.IsCheckedChanged += (s, e) => UpdateUI();
        _rbClient.IsCheckedChanged += (s, e) => UpdateUI();

        _sldCapVol.PropertyChanged += (s, e) => {
            if (e.Property.Name == "Value") {
                _tblCapVol.Text = $"{(int)_sldCapVol.Value}%";
                if (_engine != null) _engine.CaptureGain = (float)_sldCapVol.Value / 100f;
            }
        };
        _sldPbVol.PropertyChanged += (s, e) => {
            if (e.Property.Name == "Value") {
                _tblPbVol.Text = $"{(int)_sldPbVol.Value}%";
                if (_engine != null) _engine.PlaybackGain = (float)_sldPbVol.Value / 100f;
            }
        };
        _cbPreset.SelectionChanged += (s, e) => UpdatePresetInfo();
        _chkNoise.IsCheckedChanged += OnNoiseChanged;
        _chkPort1.IsCheckedChanged += (s, e) => _spPort1.IsEnabled = _chkPort1.IsChecked == true;
        _chkPort2.IsCheckedChanged += (s, e) => _spPort2.IsEnabled = _chkPort2.IsChecked == true;
        _btnDetectCom0Com.Click    += OnDetectCom0Com;
        _chkTone.IsCheckedChanged  += OnToneChanged;
        _sldToneFreq.PropertyChanged += (s, e) => {
            if (e.Property.Name == "Value") { _tblToneFreq.Text = $"{(int)_sldToneFreq.Value} Hz"; ApplyTone(); }
        };
        _sldToneVol.PropertyChanged += (s, e) => {
            if (e.Property.Name == "Value") { _tblToneVol.Text = $"{(int)_sldToneVol.Value}%"; ApplyTone(); }
        };
        _btnConnect.Background = new SolidColorBrush(Color.Parse("#1a6b1a"));
        _btnConnect.Click     += OnConnectClick;
        _btnRefresh.Click     += (s, e) => { PopulateDevices(); PopulateSerialPorts(); AppendLog("Apparatenlijst vernieuwd."); };
        _btnFirewall.Click    += OnFirewallClick;

        // ── Bouwen linkerkolom (audio) ────────────────────────────────────────

        var leftCol = new StackPanel { Spacing = 3 };

        // Modus + Verbinding bovenaan (volle breedte → in linkerkolom)
        leftCol.Children.Add(Section("Modus", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 24,
            Children    = { _rbServer, _rbClient }
        }));

        var connGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*,46,100") };
        connGrid.Children.Add(ColLabel("IP-adres:", 0));
        Grid.SetColumn(_txtHost, 1); connGrid.Children.Add(_txtHost);
        connGrid.Children.Add(ColLabel("Poort:", 2));
        Grid.SetColumn(_nudPort, 3); connGrid.Children.Add(_nudPort);
        leftCol.Children.Add(Section("Verbinding", connGrid));

        // VU-meter containers
        var vuCapContainer = new Border { Height = 8, Width = VuMaxWidth,
            Background = new SolidColorBrush(Color.Parse("#333")),
            CornerRadius = new CornerRadius(3), ClipToBounds = true };
        vuCapContainer.Child = _vuCapBar;
        var vuPbContainer = new Border { Height = 8, Width = VuMaxWidth,
            Background = new SolidColorBrush(Color.Parse("#333")),
            CornerRadius = new CornerRadius(3), ClipToBounds = true };
        vuPbContainer.Child = _vuPbBar;

        // Audio apparaten + volume + VU
        var devGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("100,*"),
            RowDefinitions    = new RowDefinitions("Auto,3,Auto,6,Auto,3,Auto")
        };
        devGrid.Children.Add(RowColLabel("Opname:", 0, 0));
        Grid.SetRow(_cbCapture, 0); Grid.SetColumn(_cbCapture, 1); devGrid.Children.Add(_cbCapture);

        devGrid.Children.Add(RowColLabel("Mic vol:", 2, 0));
        var capVolRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        capVolRow.Children.Add(_sldCapVol); capVolRow.Children.Add(_tblCapVol); capVolRow.Children.Add(vuCapContainer);
        Grid.SetRow(capVolRow, 2); Grid.SetColumn(capVolRow, 1); devGrid.Children.Add(capVolRow);

        devGrid.Children.Add(RowColLabel("Afspelen:", 4, 0));
        Grid.SetRow(_cbPlayback, 4); Grid.SetColumn(_cbPlayback, 1); devGrid.Children.Add(_cbPlayback);

        devGrid.Children.Add(RowColLabel("Spkr vol:", 6, 0));
        var pbVolRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        pbVolRow.Children.Add(_sldPbVol); pbVolRow.Children.Add(_tblPbVol); pbVolRow.Children.Add(vuPbContainer);
        Grid.SetRow(pbVolRow, 6); Grid.SetColumn(pbVolRow, 1); devGrid.Children.Add(pbVolRow);

        leftCol.Children.Add(Section("🎙  Audio", devGrid));

        // Codec + ruis
        leftCol.Children.Add(Section("Codec", new StackPanel
        {
            Children = { _cbPreset, _tblPresetInfo }
        }));
        leftCol.Children.Add(Section("Ruis", new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children    = { _chkNoise, _tblNoiseMode }
        }));

        // ── Bouwen rechterkolom (poorten + test + log) ────────────────────────

        var rightCol = new StackPanel { Spacing = 3 };

        // Server: 2 seriële poorten — COM/Baud/TCP onder elkaar
        static Grid SerialGrid(Control com, Control baud, Control tcp) => new()
        {
            ColumnDefinitions = new ColumnDefinitions("50,*"),
            RowDefinitions    = new RowDefinitions("Auto,4,Auto,4,Auto"),
            Margin            = new Thickness(0, 4, 0, 0),
            Children =
            {
                RowColLabel("COM:",  0, 0), SetRowCol(com,  0, 1),
                RowColLabel("Baud:", 2, 0), SetRowCol(baud, 2, 1),
                RowColLabel("TCP:",  4, 0), SetRowCol(tcp,  4, 1),
            }
        };

        _spServerPorts.Children.Add(Section("Serieel 1  (CAT)", new StackPanel { Children = { _chkPort1, SerialGrid(_cbPort1Com, _nudPort1Baud, _nudPort1Tcp) } }));
        _spServerPorts.Children.Add(Section("Serieel 2  (PTT)", new StackPanel { Children = { _chkPort2, SerialGrid(_cbPort2Com, _nudPort2Baud, _nudPort2Tcp) } }));

        // Client: com0com / PTY
        _spVComClient.Children.Add(_tblVirtualInfo);
        _spVComClient.Children.Add(_btnDetectCom0Com);
        _spVComClient.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("80,*"),
            RowDefinitions    = new RowDefinitions("Auto,6,Auto"),
            Children          =
            {
                RowColLabel("CAT:", 0, 0),
                SetRowCol(_cbVCom1, 0, 1),
                RowColLabel("PTT:", 2, 0),
                SetRowCol(_cbVCom2, 2, 1),
            }
        });
        _spClientPorts.Children.Add(Section("💻  Virtuele COM (com0com / PTY)", _spVComClient));

        rightCol.Children.Add(_spServerPorts);
        rightCol.Children.Add(_spClientPorts);

        // Testtoon
        _spToneSliders.Children.Add(new StackPanel
        {
            Orientation = Orientation.Vertical, Spacing = 4,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { SimpleLabel("Freq:"), _sldToneFreq, _tblToneFreq } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { SimpleLabel("Vol: "), _sldToneVol,  _tblToneVol  } }
            }
        });
        rightCol.Children.Add(Section("🔊  Testtoon", new StackPanel { Children = { _chkTone, _spToneSliders } }));

        // ── Hoofd layout: 2 kolommen ──────────────────────────────────────────

        var twoCol = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(leftCol,  0); twoCol.Children.Add(leftCol);
        Grid.SetColumn(rightCol, 2); twoCol.Children.Add(rightCol);

        // ── Root: 2-kolom + knoppen + status + log ────────────────────────────

        var root = new StackPanel { Margin = new Thickness(10), Spacing = 4 };
        root.Children.Add(twoCol);

        // Knoppen
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0,4,0,2),
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
            Margin          = new Thickness(0,2,0,0),
            Child           = _logScroller
        });

        Content = new ScrollViewer { Content = root };
    }

    private static Control SetRowCol(Control c, int row, int col)
    {
        Grid.SetRow(c, row); Grid.SetColumn(c, col); return c;
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

        // Rechterkolom: serverpoorten OF client virtuele COM
        _spServerPorts.IsVisible = isServer;
        _spClientPorts.IsVisible = !isServer;

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
        _engine.LevelChanged       += OnLevelChanged;
        _engine.CaptureGain  = (float)_sldCapVol.Value  / 100f;
        _engine.PlaybackGain = (float)_sldPbVol.Value   / 100f;
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

            // Virtuele COM poorten voor client (leeg = niet gebruiken / auto PTY op Linux)
            _engine.VirtualComPort1 = (_cbVCom1.SelectedItem as VComItem)?.Port ?? "";
            _engine.VirtualComPort2 = (_cbVCom2.SelectedItem as VComItem)?.Port ?? "";

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

        // Lees audio-apparaten opnieuw uit de UI — gebruiker kan ze gewijzigd hebben
        int captureIdx  = (_cbCapture.SelectedItem  as DeviceItem)?.Index ?? _serverCaptureIdx;
        int playbackIdx = (_cbPlayback.SelectedItem as DeviceItem)?.Index ?? _serverPlaybackIdx;
        _serverCaptureIdx  = captureIdx;
        _serverPlaybackIdx = playbackIdx;

        _engine = new AudioEngine();
        _engine.StatusChanged      += msg => { AppendLog(msg); SetStatus(msg); };
        _engine.Error              += msg => { AppendLog($"⚠  {msg}"); SetStatus(msg, true); };
        _engine.ClientDisconnected += OnClientDisconnected;
        _engine.ConfigReceived     += OnConfigReceived;
        _engine.LevelChanged       += OnLevelChanged;
        _engine.CaptureGain  = (float)_sldCapVol.Value  / 100f;
        _engine.PlaybackGain = (float)_sldPbVol.Value   / 100f;
        _engine.SerialConfig1 = GetSerialConfig(1);
        _engine.SerialConfig2 = GetSerialConfig(2);
        _connected = true; _restarting = false; UpdateUI();
        ApplyTone();
        await _engine.StartServerAsync(_serverPort, captureIdx, playbackIdx);
    }

    private void OnConfigReceived(BridgeConfig cfg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_rbClient.IsChecked == true)
            {
                if (cfg.Port1.Enabled) AppendLog($"Server CAT: {cfg.Port1.ComPort} {cfg.Port1.Baud} baud → TCP {cfg.Port1.TcpPort}");
                if (cfg.Port2.Enabled) AppendLog($"Server PTT: {cfg.Port2.ComPort} {cfg.Port2.Baud} baud → TCP {cfg.Port2.TcpPort}");

                // Virtuele poort namen worden gelogged door AudioEngine zodra ze aangemaakt zijn
            }
            _tblNoiseMode.Text = _chkNoise.IsChecked == true ? "(actief)" : "";
        });
    }

    // ── VU meter ─────────────────────────────────────────────────────────────

    private void OnLevelChanged(float capLevel, float pbLevel)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Logaritmische weergave: dB = 20*log10(level), range -60..0 → 0..1
            static double ToBar(float lvl)
            {
                if (lvl <= 0.001f) return 0.0;
                double db = 20.0 * Math.Log10(lvl);  // -60..0
                return Math.Max(0.0, (db + 60.0) / 60.0);
            }

            _vuCapBar.Width = ToBar(capLevel) * VuMaxWidth;
            _vuPbBar.Width  = ToBar(pbLevel)  * VuMaxWidth;

            // Kleur: groen < -12dB, geel -12..-3dB, rood > -3dB
            static IBrush LevelColor(float lvl)
            {
                if (lvl > 0.71f) return new SolidColorBrush(Color.Parse("#f44336")); // rood  > -3dB
                if (lvl > 0.25f) return new SolidColorBrush(Color.Parse("#ffc107")); // geel  > -12dB
                return new SolidColorBrush(Color.Parse("#4caf50"));                  // groen
            }
            _vuCapBar.Background = LevelColor(capLevel);
            _vuPbBar.Background  = LevelColor(pbLevel);
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

    // ── Virtuele COM (com0com / PTY) ──────────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416")]
    private void OnDetectCom0Com(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

        if (!isWindows)
        {
            // Linux: PTY wordt automatisch aangemaakt, geen detectie nodig
            _tblVirtualInfo.Text = "Linux: een PTY (/dev/pts/N) wordt automatisch aangemaakt zodra je verbindt. " +
                                   "Je hoeft hier niets in te stellen — de poort wordt in het logvenster weergegeven.";
            _cbVCom1.Items.Clear();
            _cbVCom2.Items.Clear();
            return;
        }

        // Windows: zoek com0com paren
        if (!VirtualSerialBridge.IsCom0ComInstalled())
        {
            _tblVirtualInfo.Text = "⚠  com0com is niet geïnstalleerd. Download via: https://sourceforge.net/projects/com0com/\n" +
                                   "Na installatie opnieuw detecteren.";
            return;
        }

        var pairs = VirtualSerialBridge.DetectCom0ComPairs();
        if (pairs.Count == 0)
        {
            _tblVirtualInfo.Text = "com0com is geïnstalleerd maar er zijn geen poortparen gevonden. " +
                                   "Open de com0com setup en maak een paar aan.";
            return;
        }

        // Vul dropdowns
        // PortA = "outside" = logger-kant, PortB = "inside" = RadioBridge-kant
        _cbVCom1.Items.Clear();
        _cbVCom2.Items.Clear();

        // Eerste optie: niet gebruiken
        _cbVCom1.Items.Add(new VComItem("", "— niet gebruiken —"));
        _cbVCom2.Items.Add(new VComItem("", "— niet gebruiken —"));

        foreach (var pair in pairs)
        {
            if (pair.PortA == "" || pair.PortB == "") continue;
            // RadioBridge gebruikt PortB (inside), logger verbindt op PortA (outside)
            string label = $"{pair.PortB}  ←→  logger op {pair.PortA}";
            _cbVCom1.Items.Add(new VComItem(pair.PortB, label));
            _cbVCom2.Items.Add(new VComItem(pair.PortB, label));
        }

        // Selecteer eerste paar voor CAT, tweede voor PTT
        _cbVCom1.SelectedIndex = pairs.Count > 0 ? 1 : 0;
        _cbVCom2.SelectedIndex = pairs.Count > 1 ? 2 : 0;

        // Controleer of we echte COM-namen hebben (niet CNCA0 stijl)
        bool hasRealNames = pairs.All(p =>
            (p.PortA.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || p.PortA == "") &&
            (p.PortB.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || p.PortB == ""));

        var sb = new System.Text.StringBuilder();
        if (hasRealNames)
        {
            sb.AppendLine($"✅  {pairs.Count} com0com paar(en) gevonden:");
            foreach (var pair in pairs)
                sb.AppendLine($"  Paar {pair.PairId}: logger → {pair.PortA}   RadioBridge → {pair.PortB}");
            sb.Append("Selecteer hieronder welk paar voor CAT en PTT gebruikt wordt.");
        }
        else
        {
            sb.AppendLine($"⚠  {pairs.Count} paar(en) gevonden maar COM-nummers onbekend.");
            sb.AppendLine("Voer de poortnamen handmatig in of open com0com Setup.");
            sb.AppendLine();
            sb.AppendLine("Registry dump (diagnose):");
            sb.Append(VirtualSerialBridge.DebugRegistryDump());
        }
        _tblVirtualInfo.Text = sb.ToString();
    }

    // ── Stop ─────────────────────────────────────────────────────────────────

    private void StopAll()
    {
        _engine?.Stop(); _engine?.Dispose(); _engine = null;
        _connected = false; _restarting = false;
        _vuCapBar.Width = 0;
        _vuPbBar.Width  = 0;
        UpdateUI(); SetStatus("Gestopt"); AppendLog("Verbinding beëindigd.");
    }

    protected override void OnClosing(WindowClosingEventArgs e) { StopAll(); base.OnClosing(e); }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private SerialPortConfig GetSerialConfig(int port) => port == 1
        ? new SerialPortConfig { Enabled = _chkPort1.IsChecked == true, ComPort = _cbPort1Com.SelectedItem?.ToString() ?? "", Baud = (int)(_nudPort1Baud.Value ?? 9600), TcpPort = (int)(_nudPort1Tcp.Value ?? 5001) }
        : new SerialPortConfig { Enabled = _chkPort2.IsChecked == true, ComPort = _cbPort2Com.SelectedItem?.ToString() ?? "", Baud = (int)(_nudPort2Baud.Value ?? 9600), TcpPort = (int)(_nudPort2Tcp.Value ?? 5002) };
}

internal record DeviceItem(int Index, string Name) { public override string ToString() => Name; }
internal record VComItem(string Port, string Label)  { public override string ToString() => Label; }
