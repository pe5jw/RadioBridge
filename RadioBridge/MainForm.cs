namespace RadioBridge;

public class MainForm : Form
{
    private RadioButton   _rbServer    = new();
    private RadioButton   _rbClient    = new();
    private TextBox       _txtHost     = new();
    private NumericUpDown _nudPort     = new();
    private ComboBox      _cbCapture   = new();
    private ComboBox      _cbPlayback  = new();
    private ComboBox      _cbPreset    = new();
    private CheckBox      _chkNoise    = new();
    private Label         _lblNoiseMode  = new();
    private Label         _lblPresetInfo = new();
    private Button        _btnConnect  = new();
    private Button        _btnRefresh  = new();
    private Button        _btnFirewall = new();
    private RichTextBox   _rtbLog      = new();
    private Label         _lblStatus   = new();
    private Panel         _pnlStatus   = new();

    private AudioEngine?   _engine;
    private SerialBridge?  _cat;
    private SerialBridge?  _ptt;
    private bool _connected;
    private bool _restarting;  // prevent overlapping RestartServer calls
    private int  _serverPort;
    private int  _serverCaptureIdx;
    private int  _serverPlaybackIdx;

    // Serial UI controls
    // Port 1
    private CheckBox      _chkPort1     = new();
    private ComboBox      _cbPort1Com   = new();
    private NumericUpDown _nudPort1Baud = new();
    private NumericUpDown _nudPort1Tcp  = new();
    // Port 2
    private CheckBox      _chkPort2     = new();
    private ComboBox      _cbPort2Com   = new();
    private NumericUpDown _nudPort2Baud = new();
    private NumericUpDown _nudPort2Tcp  = new();

    public MainForm()
    {
        BuildUI();
        PopulateDevices();
        PopulatePresets();
        UpdateUI();
        PopulateSerialPorts();
        UpdateSerialUI();
    }

    private void BuildUI()
    {
        Text            = "RadioBridge  —  Audio over netwerk  v0.7";
        Size            = new Size(640, 980);
        MinimumSize     = new Size(560, 820);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = true;
        BackColor       = Color.FromArgb(30, 30, 30);
        ForeColor       = Color.FromArgb(220, 220, 220);
        Font            = new Font("Segoe UI", 9f);

        int y   = 12;
        int pad = 12;

        // ── Modus ──
        var pnlMode = MakeGroupBox("Modus", pad, y, 600, 64);
        _rbServer = MakeRadio("Server  (wacht op verbinding)", 12, 24, 240);
        _rbClient = MakeRadio("Client  (maak verbinding)", 260, 24, 220);
        _rbServer.Checked = true;
        _rbServer.CheckedChanged += (s, e) => UpdateUI();
        _rbClient.CheckedChanged += (s, e) => UpdateUI();
        pnlMode.Controls.AddRange(new Control[] { _rbServer, _rbClient });
        y += 76;

        // ── Verbinding ──
        var pnlConn = MakeGroupBox("Verbinding", pad, y, 600, 82);
        MakeLabel(pnlConn, "IP-adres (client):", 12, 26);
        _txtHost = MakeTextBox(pnlConn, "127.0.0.1", 130, 23, 230);
        MakeLabel(pnlConn, "TCP Poort:", 380, 26);
        _nudPort = new NumericUpDown
        {
            Minimum = 1024, Maximum = 65535, Value = 7000,
            Location = new Point(452, 23), Size = new Size(72, 24),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        pnlConn.Controls.Add(_nudPort);
        MakeLabel(pnlConn, "Gebruik hetzelfde IP en poort op beide pc's.", 12, 54, Color.FromArgb(130, 130, 130));
        y += 94;

        // ── Audio kwaliteit ──
        var pnlQual = MakeGroupBox("Audio kwaliteit / latency  —  client bepaalt", pad, y, 600, 88);
        _lblPresetInfo = new Label
        {
            Text      = "ℹ  Server past zich automatisch aan op de keuze van de client.",
            Location  = new Point(12, 20),
            AutoSize  = false,
            Size      = new Size(572, 18),
            ForeColor = Color.FromArgb(120, 180, 120),
            BackColor = Color.Transparent,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        pnlQual.Controls.Add(_lblPresetInfo);
        MakeLabel(pnlQual, "Preset:", 12, 46);
        _cbPreset = MakeCombo(pnlQual, 65, 43, 520);
        _cbPreset.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        y += 102;

        // ── Audio apparaten ──
        var pnlAudio = MakeGroupBox("Audio apparaten", pad, y, 600, 152);
        MakeLabel(pnlAudio, "Opname  (microfoon / VAC input):", 12, 18, Color.FromArgb(160, 200, 255));
        _cbCapture = MakeCombo(pnlAudio, 12, 36, 572);
        _cbCapture.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        MakeLabel(pnlAudio, "Afspelen  (speaker / VAC output):", 12, 86, Color.FromArgb(160, 200, 255));
        _cbPlayback = MakeCombo(pnlAudio, 12, 104, 572);
        _cbPlayback.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        y += 166;

        // ── Ruisonderdrukking ──
        var pnlNoise = MakeGroupBox("Ruisonderdrukking  —  client bepaalt", pad, y, 600, 90);
        _chkNoise = new CheckBox
        {
            Text = "Ruisonderdrukking inschakelen",
            Location = new Point(12, 24), Size = new Size(280, 24),
            ForeColor = Color.FromArgb(220, 220, 220), BackColor = Color.Transparent,
        };
        _chkNoise.CheckedChanged += OnNoiseCheckChanged;
        _lblNoiseMode = new Label
        {
            Text = "Modus: UIT", Location = new Point(300, 27), Size = new Size(280, 20),
            ForeColor = Color.FromArgb(130, 130, 130), BackColor = Color.Transparent,
        };
        MakeLabel(pnlNoise, "RNNoise (neuraal netwerk) bij 48kHz mono preset. Anders: noise gate.", 12, 58, Color.FromArgb(120, 120, 120));
        pnlNoise.Controls.AddRange(new Control[] { _chkNoise, _lblNoiseMode });
        y += 104;

        // ── Seriële poorten ──
        var pnlSerial = MakeGroupBox("Seriële poorten  —  optioneel", pad, y, 600, 166);

        // Header labels
        MakeLabel(pnlSerial, "COM poort",  92, 6,  Color.FromArgb(130, 160, 200));
        MakeLabel(pnlSerial, "Baud",      228, 6,  Color.FromArgb(130, 160, 200));
        MakeLabel(pnlSerial, "TCP poort", 310, 6,  Color.FromArgb(130, 160, 200));

        // ── Poort 1 ──
        _chkPort1 = new CheckBox
        {
            Text = "Poort 1:", Location = new Point(12, 30), Size = new Size(80, 22),
            ForeColor = Color.FromArgb(220, 220, 220), BackColor = Color.Transparent,
        };
        _chkPort1.CheckedChanged += (s, e) => UpdateSerialUI();
        pnlSerial.Controls.Add(_chkPort1);

        _cbPort1Com   = MakeCombo(pnlSerial, 92, 28, 126);
        _nudPort1Baud = MakeNud(pnlSerial, 228, 28, 76, 300, 115200, 9600);
        _nudPort1Tcp  = MakeNud(pnlSerial, 310, 28, 84, 1024, 65535, 5000);

        // ── Poort 2 ──
        _chkPort2 = new CheckBox
        {
            Text = "Poort 2:", Location = new Point(12, 72), Size = new Size(80, 22),
            ForeColor = Color.FromArgb(220, 220, 220), BackColor = Color.Transparent,
        };
        _chkPort2.CheckedChanged += (s, e) => UpdateSerialUI();
        pnlSerial.Controls.Add(_chkPort2);

        _cbPort2Com   = MakeCombo(pnlSerial, 92, 70, 126);
        _nudPort2Baud = MakeNud(pnlSerial, 228, 70, 76, 300, 115200, 9600);
        _nudPort2Tcp  = MakeNud(pnlSerial, 310, 70, 84, 1024, 65535, 5001);

        MakeLabel(pnlSerial, "Server: luistert op TCP poort.  Client: verbindt naar server IP + TCP poort.", 12, 106, Color.FromArgb(110, 110, 110));
        MakeLabel(pnlSerial, "Baudrate moet aan beide kanten gelijk zijn.", 12, 124, Color.FromArgb(110, 110, 110));

        y += 180;

        // ── Knoppen ──
        _btnConnect = new Button
        {
            Text = "Verbinden", Location = new Point(pad, y), Size = new Size(130, 36),
            BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand, Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _btnConnect.FlatAppearance.BorderSize = 0;
        _btnConnect.Click += OnConnectClick;

        _btnRefresh = new Button
        {
            Text = "↺  Apparaten", Location = new Point(154, y), Size = new Size(120, 36),
            BackColor = Color.FromArgb(55, 55, 55), ForeColor = Color.FromArgb(200, 200, 200),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _btnRefresh.FlatAppearance.BorderSize = 0;
        _btnRefresh.Click += (s, e) => PopulateDevices();

        _btnFirewall = new Button
        {
            Text      = "🛡  Firewall",
            Location  = new Point(286, y),
            Size      = new Size(110, 36),
            BackColor = Color.FromArgb(55, 80, 55),
            ForeColor = Color.FromArgb(180, 220, 180),
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Anchor    = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        _btnFirewall.FlatAppearance.BorderSize = 0;
        _btnFirewall.Click += OnFirewallClick;
        y += 48;

        // ── Status ──
        _pnlStatus = new Panel
        {
            Location = new Point(pad, y), Size = new Size(600, 30),
            BackColor = Color.FromArgb(45, 45, 45),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        _lblStatus = new Label
        {
            Text = "⬤  Niet verbonden", ForeColor = Color.FromArgb(160, 160, 160),
            Location = new Point(10, 7), AutoSize = true,
        };
        _pnlStatus.Controls.Add(_lblStatus);
        y += 42;

        // ── Log ──
        var lblLog = new Label
        {
            Text = "Log:", Location = new Point(pad, y), AutoSize = true,
            ForeColor = Color.FromArgb(160, 160, 160), BackColor = Color.Transparent,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        y += 18;
        _rtbLog = new RichTextBox
        {
            Location = new Point(pad, y), Size = new Size(600, 130),
            BackColor = Color.FromArgb(20, 20, 20), ForeColor = Color.FromArgb(180, 220, 180),
            Font = new Font("Consolas", 8.5f), ReadOnly = true,
            BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.Vertical,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        Controls.AddRange(new Control[]
        {
            pnlMode, pnlConn, pnlQual, pnlAudio, pnlNoise, pnlSerial,
            _btnConnect, _btnRefresh, _btnFirewall, _pnlStatus, lblLog, _rtbLog
        });

        // Resize handler: keep all panels and log the right width
        Resize += OnFormResize;
    }

    private void OnFormResize(object? sender, EventArgs e)
    {
        int w = ClientSize.Width - 24;
        if (w < 400) return;
        foreach (Control c in Controls)
        {
            if (c is GroupBox || c == _pnlStatus)
                c.Width = w;
            if (c == _rtbLog)
                c.Width = w;
        }
        // Stretch combos inside groupboxes
        StretchCombo(_cbPreset);
        StretchCombo(_cbCapture);
        StretchCombo(_cbPlayback);
        _lblPresetInfo.Width = _cbPreset.Width;

        // Keep log height filling remaining space
        int logTop = _rtbLog.Top;
        int logBottom = ClientSize.Height - 12;
        if (logBottom - logTop > 40)
            _rtbLog.Height = logBottom - logTop;
    }

    private static void StretchCombo(ComboBox cb)
    {
        if (cb.Parent == null) return;
        cb.Width = cb.Parent.Width - cb.Left - 16;
    }

    // ── UpdateUI ──────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        bool isClient = _rbClient.Checked;
        bool isServer = _rbServer.Checked;
        _txtHost.Enabled  = isClient && !_connected;
        _cbPreset.Enabled = isClient && !_connected;
        _chkNoise.Enabled = isClient || _connected;
        _lblPresetInfo.Text = isServer
            ? "ℹ  Server past zich automatisch aan op de keuze van de client."
            : "ℹ  Jouw keuze wordt naar de server gestuurd bij verbinden.";
        _lblPresetInfo.ForeColor = isServer
            ? Color.FromArgb(120, 180, 120)
            : Color.FromArgb(100, 160, 220);
        UpdateNoiseModeLabel();
    }

    private void OnNoiseCheckChanged(object? sender, EventArgs e)
    {
        if (_connected && _engine != null && _rbClient.Checked)
            _engine.SetNoiseSuppression(_chkNoise.Checked);
        UpdateNoiseModeLabel();
    }

    private void UpdateNoiseModeLabel()
    {
        if (!_chkNoise.Checked)
        {
            _lblNoiseMode.Text = "Modus: UIT";
            _lblNoiseMode.ForeColor = Color.FromArgb(130, 130, 130);
            return;
        }
        var preset = _cbPreset.SelectedItem as AudioPreset;
        bool rnn = preset?.SampleRate == 48000 && preset?.Channels == 1;
        _lblNoiseMode.Text      = rnn ? "Modus: RNNoise (neuraal)" : "Modus: Noise gate";
        _lblNoiseMode.ForeColor = rnn ? Color.FromArgb(100, 220, 100) : Color.FromArgb(220, 180, 80);
    }

    // ── Populate ──────────────────────────────────────────────────────────────

    private void PopulatePresets()
    {
        _cbPreset.Items.Clear();
        foreach (var p in AudioPresets.All) _cbPreset.Items.Add(p);
        _cbPreset.SelectedIndex = Array.IndexOf(AudioPresets.All, AudioPresets.Default);
        _cbPreset.SelectedIndexChanged += (s, e) => UpdateNoiseModeLabel();
    }

    private void PopulateDevices()
    {
        // Remember current selection by device index (-1 = default)
        var selCapIdx  = (_cbCapture.SelectedItem  as DeviceItem)?.Index ?? -1;
        var selPlayIdx = (_cbPlayback.SelectedItem as DeviceItem)?.Index ?? -1;

        _cbCapture.Items.Clear();
        foreach (var (idx, name) in AudioEngine.GetCaptureDevices())
            _cbCapture.Items.Add(new DeviceItem(idx, name));

        _cbPlayback.Items.Clear();
        foreach (var (idx, name) in AudioEngine.GetPlaybackDevices())
            _cbPlayback.Items.Add(new DeviceItem(idx, name));

        // Restore previous selection by device index, or default to ⭐ (index 0)
        SelectByDeviceIndex(_cbCapture,  selCapIdx);
        SelectByDeviceIndex(_cbPlayback, selPlayIdx);

        AppendLog($"Apparaten: {_cbCapture.Items.Count} opname, {_cbPlayback.Items.Count} afspeel");
        PopulateSerialPorts();
    }

    private static void SelectByDeviceIndex(ComboBox cb, int deviceIndex)
    {
        // Try to find previously selected device
        for (int i = 0; i < cb.Items.Count; i++)
        {
            if (cb.Items[i] is DeviceItem d && d.Index == deviceIndex)
            { cb.SelectedIndex = i; return; }
        }
        // Default: select first item (⭐ Windows standaard)
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    // ── Serial helpers ───────────────────────────────────────────────────────

    private void PopulateSerialPorts()
    {
        var ports = SerialBridge.GetPortNames();
        RefillCombo(_cbPort1Com, ports, 0);
        RefillCombo(_cbPort2Com, ports, ports.Length > 1 ? 1 : 0);
    }

    private static void RefillCombo(ComboBox cb, string[] ports, int defaultIdx)
    {
        var sel = cb.SelectedItem?.ToString();
        cb.Items.Clear();
        foreach (var p in ports) cb.Items.Add(p);
        if (sel != null && cb.Items.Contains(sel)) cb.SelectedItem = sel;
        else if (cb.Items.Count > defaultIdx) cb.SelectedIndex = defaultIdx;
        else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private void UpdateSerialUI()
    {
        bool p1 = _chkPort1.Checked && !_connected;
        bool p2 = _chkPort2.Checked && !_connected;
        _cbPort1Com.Enabled   = p1;
        _nudPort1Baud.Enabled = p1;
        _nudPort1Tcp.Enabled  = p1;
        _cbPort2Com.Enabled   = p2;
        _nudPort2Baud.Enabled = p2;
        _nudPort2Tcp.Enabled  = p2;

    }

    private void StartSerial()
    {
        var host = _txtHost.Text.Trim();
        if (_chkPort1.Checked) StartOnePort(ref _cat, "Poort1", _cbPort1Com, _nudPort1Baud, _nudPort1Tcp, host);
        if (_chkPort2.Checked) StartOnePort(ref _ptt, "Poort2", _cbPort2Com, _nudPort2Baud, _nudPort2Tcp, host);
    }

    private void StartOnePort(ref SerialBridge? bridge, string name,
        ComboBox cbCom, NumericUpDown nudBaud, NumericUpDown nudTcp, string host)
    {
        string comPort = cbCom.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrEmpty(comPort))
        { AppendLog($"{name}: geen COM poort geselecteerd.", Color.Orange); return; }

        int baud   = (int)nudBaud.Value;
        int tcpPort = (int)nudTcp.Value;

        bridge = new SerialBridge(name);
        bridge.StatusChanged += msg => SafeInvoke(() => AppendLog(msg));
        bridge.Error         += msg => SafeInvoke(() => AppendLog(msg, Color.FromArgb(255, 140, 80)));
        bridge.ClientDisconnected += () =>
            Task.Delay(300).ContinueWith(_ => SafeInvoke(() =>
            { if (_rbServer.Checked && _connected) RestartSerial(); }));

        if (_rbServer.Checked)
            _ = bridge.StartServerAsync(comPort, baud, tcpPort);
        else
            _ = bridge.StartClientAsync(host, tcpPort, comPort, baud);
    }

    private void StopSerial()
    {
        _cat?.Stop(); _cat?.Dispose(); _cat = null;
        _ptt?.Stop(); _ptt?.Dispose(); _ptt = null;
    }

    private void RestartSerial()
    {
        StopSerial();
        Task.Delay(500).ContinueWith(_ => SafeInvoke(StartSerial));
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────────

    private async void OnConnectClick(object? sender, EventArgs e)
    {
        if (_connected) { Disconnect(); return; }
        if (_cbCapture.SelectedItem == null || _cbPlayback.SelectedItem == null)
        { AppendLog("Selecteer opname- en afspeelApparaat.", Color.Orange); return; }

        var captureIdx  = ((DeviceItem)_cbCapture.SelectedItem).Index;
        var playbackIdx = ((DeviceItem)_cbPlayback.SelectedItem).Index;
        var port        = (int)_nudPort.Value;

        _engine = new AudioEngine();
        _engine.StatusChanged += msg => SafeInvoke(() => AppendLog(msg));
        _engine.Error += msg => SafeInvoke(() =>
        {
            AppendLog(msg, Color.FromArgb(255, 100, 100));
            if (_rbServer.Checked && _connected)
                Task.Delay(300).ContinueWith(_ => SafeInvoke(() => RestartServer()));
            else
                Disconnect();
        });
        _engine.ConfigReceived += cfg => SafeInvoke(() =>
        {
            AppendLog($"Client koos: {cfg.PresetName}", Color.FromArgb(100, 200, 255));
            AppendLog($"Ruis: {(cfg.NoiseSuppress ? "AAN" : "UIT")}  |  {cfg.SampleRate}Hz {cfg.BitsPerSample}bit {cfg.Channels}ch {cfg.BufferMs}ms", Color.FromArgb(100, 200, 255));
            // Client: apply server serial config
            if (_rbClient.Checked)
                ApplySerialConfig(cfg);
        });
        _engine.ClientDisconnected += () =>
        {
            Task.Delay(300).ContinueWith(_ => SafeInvoke(() =>
            { if (_rbServer.Checked && _connected) RestartServer(); }));
        };

        SetConnected(true);

        if (_rbServer.Checked)
        {
            _serverPort = port; _serverCaptureIdx = captureIdx; _serverPlaybackIdx = playbackIdx;
            // Pass serial config to engine so it can be sent to client
            _engine.SerialConfig1 = BuildSerialConfig(1);
            _engine.SerialConfig2 = BuildSerialConfig(2);
            StartSerial();
            await _engine.StartServerAsync(port, captureIdx, playbackIdx);
        }
        else
        {
            var host = _txtHost.Text.Trim();
            if (string.IsNullOrEmpty(host)) { AppendLog("Vul een IP-adres in.", Color.Orange); Disconnect(); return; }
            var preset  = (AudioPreset)(_cbPreset.SelectedItem ?? AudioPresets.Default);
            await _engine.StartClientAsync(host, port, captureIdx, playbackIdx, preset, _chkNoise.Checked);
            // Serial is started inside ApplySerialConfig after config received
        }
    }

    private async void RestartServer()
    {
        if (_restarting) return;
        _restarting = true;
        AppendLog("Client verbroken — wacht op nieuwe verbinding...", Color.FromArgb(220, 180, 80));
        _engine?.Stop(); _engine?.Dispose();
        _engine = new AudioEngine();
        _engine.StatusChanged += msg => SafeInvoke(() => AppendLog(msg));
        _engine.Error += msg => SafeInvoke(() =>
        {
            AppendLog(msg, Color.FromArgb(255, 100, 100));
            if (_connected)
                Task.Delay(300).ContinueWith(_ => SafeInvoke(() => RestartServer()));
        });
        _engine.ConfigReceived += cfg => SafeInvoke(() =>
        {
            AppendLog($"Client koos: {cfg.PresetName}", Color.FromArgb(100, 200, 255));
            AppendLog($"Ruis: {(cfg.NoiseSuppress ? "AAN" : "UIT")}  |  {cfg.SampleRate}Hz {cfg.BitsPerSample}bit {cfg.Channels}ch {cfg.BufferMs}ms", Color.FromArgb(100, 200, 255));
        });
        _engine.ClientDisconnected += () =>
        {
            Task.Delay(300).ContinueWith(_ => SafeInvoke(() =>
            { if (_connected) RestartServer(); }));
        };
        _engine.SerialConfig1 = BuildSerialConfig(1);
        _engine.SerialConfig2 = BuildSerialConfig(2);
        _lblStatus.Text      = "⬤  Wachten op client...";
        _lblStatus.ForeColor = Color.FromArgb(220, 180, 80);
        await Task.Delay(500);
        _restarting = false;
        await _engine.StartServerAsync(_serverPort, _serverCaptureIdx, _serverPlaybackIdx);
    }

    // ── Firewall ─────────────────────────────────────────────────────────────

    private void OnFirewallClick(object? sender, EventArgs e)
    {
        var ports = new List<int> { (int)_nudPort.Value };
        if (_chkPort1.Checked) ports.Add((int)_nudPort1Tcp.Value);
        if (_chkPort2.Checked) ports.Add((int)_nudPort2Tcp.Value);

        AppendLog($"Firewall instellen voor poorten: {string.Join(", ", ports)}...");
        FirewallHelper.EnsureRules(ports, msg => SafeInvoke(() => AppendLog(msg)));
    }

    private SerialPortConfig BuildSerialConfig(int portNum)
    {
        if (portNum == 1)
            return new SerialPortConfig
            {
                Enabled = _chkPort1.Checked,
                ComPort = _cbPort1Com.SelectedItem?.ToString() ?? "",
                Baud    = (int)_nudPort1Baud.Value,
                TcpPort = (int)_nudPort1Tcp.Value,
            };
        else
            return new SerialPortConfig
            {
                Enabled = _chkPort2.Checked,
                ComPort = _cbPort2Com.SelectedItem?.ToString() ?? "",
                Baud    = (int)_nudPort2Baud.Value,
                TcpPort = (int)_nudPort2Tcp.Value,
            };
    }

    private void ApplySerialConfig(BridgeConfig cfg)
    {
        // Apply port 1
        _chkPort1.Checked = cfg.Port1.Enabled;
        if (cfg.Port1.Enabled)
        {
            SelectOrAddCombo(_cbPort1Com, cfg.Port1.ComPort);
            _nudPort1Baud.Value = Math.Clamp(cfg.Port1.Baud, 300, 115200);
            _nudPort1Tcp.Value  = Math.Clamp(cfg.Port1.TcpPort, 1024, 65535);
            AppendLog($"Poort 1: {cfg.Port1.ComPort} {cfg.Port1.Baud} baud TCP {cfg.Port1.TcpPort}", Color.FromArgb(100, 200, 255));
        }
        // Apply port 2
        _chkPort2.Checked = cfg.Port2.Enabled;
        if (cfg.Port2.Enabled)
        {
            SelectOrAddCombo(_cbPort2Com, cfg.Port2.ComPort);
            _nudPort2Baud.Value = Math.Clamp(cfg.Port2.Baud, 300, 115200);
            _nudPort2Tcp.Value  = Math.Clamp(cfg.Port2.TcpPort, 1024, 65535);
            AppendLog($"Poort 2: {cfg.Port2.ComPort} {cfg.Port2.Baud} baud TCP {cfg.Port2.TcpPort}", Color.FromArgb(100, 200, 255));
        }
        UpdateSerialUI();
        // Now start serial with received config
        StartSerial();
    }

    private static void SelectOrAddCombo(ComboBox cb, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        // Find existing
        for (int i = 0; i < cb.Items.Count; i++)
            if (string.Equals(cb.Items[i]?.ToString(), value)) { cb.SelectedIndex = i; return; }
        // Add if not found (server COM port name, just for display)
        cb.Items.Add(value);
        cb.SelectedIndex = cb.Items.Count - 1;
    }

    private void Disconnect()
    {
        _restarting = false;
        StopSerial();
        _engine?.Stop(); _engine?.Dispose(); _engine = null;
        SetConnected(false);
    }

    private void SetConnected(bool connected)
    {
        _connected = connected;
        SafeInvoke(() =>
        {
            _btnConnect.Text      = connected ? "Verbreken" : "Verbinden";
            _btnConnect.BackColor = connected ? Color.FromArgb(180, 40, 40) : Color.FromArgb(0, 120, 212);
            _lblStatus.Text       = connected ? "⬤  Verbonden" : "⬤  Niet verbonden";
            _lblStatus.ForeColor  = connected ? Color.FromArgb(100, 220, 100) : Color.FromArgb(160, 160, 160);
            foreach (Control c in new Control[] { _rbServer, _rbClient, _txtHost, _nudPort, _cbCapture, _cbPlayback, _btnRefresh })
                c.Enabled = !connected;
            _chkPort1.Enabled = !connected;
            _chkPort2.Enabled = !connected;
            UpdateSerialUI();
            _cbPreset.Enabled = _rbClient.Checked && !connected;
            _chkNoise.Enabled = _rbClient.Checked;
            if (!connected) UpdateUI();
        });
    }

    private void AppendLog(string msg, Color? color = null)
    {
        var t = DateTime.Now.ToString("HH:mm:ss");
        _rtbLog.SelectionStart  = _rtbLog.TextLength;
        _rtbLog.SelectionLength = 0;
        _rtbLog.SelectionColor  = color ?? Color.FromArgb(180, 220, 180);
        _rtbLog.AppendText($"[{t}]  {msg}\n");
        _rtbLog.ScrollToCaret();
    }

    private void SafeInvoke(Action a) { if (InvokeRequired) Invoke(a); else a(); }

    protected override void OnFormClosing(FormClosingEventArgs e) { Disconnect(); base.OnFormClosing(e); }

    // ── Control factories ─────────────────────────────────────────────────────

    private GroupBox MakeGroupBox(string title, int x, int y, int w, int h)
    {
        var gb = new GroupBox
        {
            Text = title, Location = new Point(x, y), Size = new Size(w, h),
            ForeColor = Color.FromArgb(160, 200, 255), BackColor = Color.FromArgb(38, 38, 38),
        };
        Controls.Add(gb);
        return gb;
    }

    private RadioButton MakeRadio(string text, int x, int y, int w) => new()
    {
        Text = text, Location = new Point(x, y), Size = new Size(w, 22),
        ForeColor = Color.FromArgb(220, 220, 220), BackColor = Color.Transparent,
    };

    private static void MakeLabel(Control parent, string text, int x, int y, Color? color = null)
    {
        parent.Controls.Add(new Label
        {
            Text = text, Location = new Point(x, y), AutoSize = true,
            ForeColor = color ?? Color.FromArgb(170, 170, 170), BackColor = Color.Transparent,
        });
    }

    private static TextBox MakeTextBox(Control parent, string val, int x, int y, int w)
    {
        var tb = new TextBox
        {
            Text = val, Location = new Point(x, y), Size = new Size(w, 24),
            BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
        };
        parent.Controls.Add(tb);
        return tb;
    }

    private static ComboBox MakeCombo(Control parent, int x, int y, int w)
    {
        var cb = new ComboBox
        {
            Location = new Point(x, y), Size = new Size(w, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.FromArgb(220, 220, 220),
            FlatStyle = FlatStyle.Flat,
        };
        parent.Controls.Add(cb);
        return cb;
    }

    private static NumericUpDown MakeNud(Control parent, int x, int y, int w, int min, int max, int val)
    {
        var n = new NumericUpDown
        {
            Location = new Point(x, y), Size = new Size(w, 23),
            Minimum = min, Maximum = max, Value = Math.Clamp(val, min, max),
            BackColor = Color.FromArgb(50, 50, 50), ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle, Enabled = false,
        };
        parent.Controls.Add(n);
        return n;
    }
}

record DeviceItem(int Index, string Name) { public override string ToString() => Name; }
