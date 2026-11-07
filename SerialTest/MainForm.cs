using System.IO.Ports;
using NAudio.Wave;

namespace SerialTest;

/// <summary>
/// Two-PC serial port test tool.
///
/// PC 1 (Server): opens local COM port, waits for data, sends echo back
/// PC 2 (Client): opens local COM port, sends test data, checks echo
///
/// The connection between the two COM ports can be anything:
/// RadioBridge tunnel, com0com pair, real cable, etc.
/// This tool only tests whether data arrives correctly.
/// </summary>
public class MainForm : Form
{
    // ── Serial tab ────────────────────────────────────────────────────────────
    private RadioButton   _rbServer    = new();
    private RadioButton   _rbClient    = new();
    private ComboBox      _cbPort      = new();
    private NumericUpDown _nudBaud     = new();
    private NumericUpDown _nudBytes    = new();
    private NumericUpDown _nudRounds   = new();
    private Button        _btnTest     = new();
    private Button        _btnStop     = new();
    private RichTextBox   _rtbSerial   = new();

    // ── Audio tab ─────────────────────────────────────────────────────────────
    private ComboBox      _cbAudioOut  = new();
    private ComboBox      _cbAudioIn   = new();
    private NumericUpDown _nudFreq     = new();
    private NumericUpDown _nudDuration = new();
    private TrackBar      _trkVolume   = new();
    private Label         _lblVolume   = new();
    private Button        _btnAudioTest = new();
    private Button        _btnAudioStop = new();
    private RichTextBox   _rtbAudio    = new();
    private Panel         _pnlMeter    = new();
    private Label         _lblLevel    = new();

    private RadioButton   _rbAudioInitiator = new();
    private RadioButton   _rbAudioLoopback  = new();
    private CancellationTokenSource _serialCts = new();
    private CancellationTokenSource _audioCts  = new();

    public MainForm()
    {
        BuildUI();
        PopulatePorts();
        PopulateAudioDevices();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UI
    // ═══════════════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        Text            = "RadioBridge — Verbindingstester";
        Size            = new Size(660, 680);
        MinimumSize     = new Size(560, 560);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor       = Color.FromArgb(30, 30, 30);
        ForeColor       = Color.FromArgb(220, 220, 220);
        Font            = new Font("Segoe UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.DrawItem += (s, e) =>
        {
            var tab = tabs.TabPages[e.Index];
            using var brush = new SolidBrush(e.Index == tabs.SelectedIndex
                ? Color.FromArgb(50, 50, 70) : Color.FromArgb(38, 38, 38));
            e.Graphics.FillRectangle(brush, e.Bounds);
            e.Graphics.DrawString(tab.Text, Font, Brushes.LightGray, e.Bounds.X + 8, e.Bounds.Y + 4);
        };
        Controls.Add(tabs);

        var pgSerial = new TabPage("  🔌  Seriële poort test  ")
            { BackColor = Color.FromArgb(28, 28, 28), Padding = new Padding(8) };
        var pgAudio = new TabPage("  🔊  Audio loopback test  ")
            { BackColor = Color.FromArgb(28, 28, 28), Padding = new Padding(8) };

        tabs.TabPages.Add(pgSerial);
        tabs.TabPages.Add(pgAudio);

        BuildSerialTab(pgSerial);
        BuildAudioTab(pgAudio);
    }

    private void BuildSerialTab(TabPage page)
    {
        int y = 4;

        // Info
        var pnlInfo = GB(page, "Hoe werkt de test", 0, y, 72);
        L(pnlInfo, "Start SERVER op de pc bij de radio. Start CLIENT op de bedienings-pc.", 8, 18);
        L(pnlInfo, "Elke pc gebruikt zijn eigen lokale COM poort.", 8, 36);
        L(pnlInfo, "De verbinding ertussen (RadioBridge tunnel, kabel, etc.) is voor deze tool niet van belang.", 8, 54, Color.FromArgb(120,120,120));
        y += 84;

        // Modus
        var pnlMode = GB(page, "Modus", 0, y, 64);
        _rbServer = new RadioButton
        {
            Text = "Server  (wacht op data, stuurt echo terug)",
            Location = new Point(12, 22), Size = new Size(300, 22),
            ForeColor = Color.FromArgb(220,220,220), BackColor = Color.Transparent,
            Checked = true,
        };
        _rbClient = new RadioButton
        {
            Text = "Client  (stuurt data, controleert echo)",
            Location = new Point(320, 22), Size = new Size(280, 22),
            ForeColor = Color.FromArgb(220,220,220), BackColor = Color.Transparent,
        };
        pnlMode.Controls.AddRange(new Control[] { _rbServer, _rbClient });
        y += 76;

        // COM poort
        var pnlPort = GB(page, "Lokale COM poort", 0, y, 64);
        L(pnlPort, "COM poort:", 8, 24);
        _cbPort = CB(pnlPort, 80, 21, 130);
        L(pnlPort, "Baud:", 224, 24);
        _nudBaud = NUD(pnlPort, 262, 21, 90, 300, 115200, 9600);
        var btnRefresh = SBtn(pnlPort, "↺  Verversen", 366, 21);
        btnRefresh.Click += (s, e) => PopulatePorts();
        y += 76;

        // Test opties (alleen voor client)
        var pnlOpts = GB(page, "Test opties  (client)", 0, y, 64);
        L(pnlOpts, "Bytes per ronde:", 8, 24);
        _nudBytes = NUD(pnlOpts, 120, 21, 72, 1, 4096, 64);
        L(pnlOpts, "Rondes:", 206, 24);
        _nudRounds = NUD(pnlOpts, 258, 21, 72, 1, 1000, 10);
        L(pnlOpts, "(server hoeft opties niet in te stellen)", 344, 24, Color.FromArgb(110,110,110));
        y += 76;

        // Knoppen
        var pnlBtns = new Panel
        {
            Location = new Point(0, y), Height = 36, BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        page.Controls.Add(pnlBtns);
        page.Resize += (s, e) => pnlBtns.Width = page.Width - 16;

        _btnTest = new Button
        {
            Text = "▶  Start", Location = new Point(0, 0), Size = new Size(100, 34),
            BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        _btnTest.FlatAppearance.BorderSize = 0;
        _btnTest.Click += (s, e) => _ = RunSerialTest();

        _btnStop = new Button
        {
            Text = "⏹  Stop", Location = new Point(112, 0), Size = new Size(100, 34),
            BackColor = Color.FromArgb(160, 40, 40), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Enabled = false,
        };
        _btnStop.FlatAppearance.BorderSize = 0;
        _btnStop.Click += (s, e) => _serialCts.Cancel();

        var btnClear = new Button
        {
            Text = "Log wissen", Location = new Point(224, 0), Size = new Size(100, 34),
            BackColor = Color.FromArgb(55,55,55), ForeColor = Color.FromArgb(200,200,200),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        };
        btnClear.FlatAppearance.BorderSize = 0;
        btnClear.Click += (s, e) => _rtbSerial.Clear();

        pnlBtns.Controls.AddRange(new Control[] { _btnTest, _btnStop, btnClear });
        y += 46;

        // Log
        _rtbSerial = new RichTextBox
        {
            Location = new Point(0, y),
            BackColor = Color.FromArgb(18,18,18), ForeColor = Color.FromArgb(200,200,200),
            Font = new Font("Consolas", 8.5f), ReadOnly = true,
            BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        page.Controls.Add(_rtbSerial);
        page.Resize += (s, e) => _rtbSerial.Size = new Size(page.Width - 16, page.Height - y - 8);
    }

    private void BuildAudioTab(TabPage page)
    {
        int y = 4;

        // Info
        var pnlInfo = GB(page, "Hoe werkt de test", 0, y, 72);
        L(pnlInfo, "Initiator (Client of Server): speelt testtoon af via uitgang → verzendt via RadioBridge.", 8, 16);
        L(pnlInfo, "Loopback (andere kant): ontvangt audio via ingang → stuurt direct terug via uitgang.", 8, 34);
        L(pnlInfo, "Initiator hoort zichzelf terug. Optie: spreek via microfoon en hoor jezelf terug.", 8, 52, Color.FromArgb(120,120,120));
        y += 84;

        // Modus
        var pnlMode = GB(page, "Modus", 0, y, 64);
        _rbAudioInitiator = new RadioButton
        {
            Text = "Initiator  (speelt af, hoort terug)",
            Location = new Point(12, 22), Size = new Size(260, 22),
            ForeColor = Color.FromArgb(220,220,220), BackColor = Color.Transparent,
            Checked = true,
        };
        _rbAudioLoopback = new RadioButton
        {
            Text = "Loopback  (ontvangt, stuurt terug)",
            Location = new Point(280, 22), Size = new Size(260, 22),
            ForeColor = Color.FromArgb(220,220,220), BackColor = Color.Transparent,
        };
        _rbAudioInitiator.CheckedChanged += (s, e) => UpdateAudioUI();
        _rbAudioLoopback.CheckedChanged  += (s, e) => UpdateAudioUI();
        pnlMode.Controls.AddRange(new Control[] { _rbAudioInitiator, _rbAudioLoopback });
        y += 68;

        // Audio apparaten
        var pnlDev = GB(page, "Audio apparaten", 0, y, 158);
        L(pnlDev, "Uitgang  (testtoon / microfoon naar):", 8, 16, Color.FromArgb(160,200,255));
        _cbAudioOut = CB(pnlDev, 8, 36, 500);
        L(pnlDev, "Ingang  (ontvangen / terugluisteren op):", 8, 82, Color.FromArgb(160,200,255));
        _cbAudioIn = CB(pnlDev, 8, 102, 500);
        var btnRefA = SBtn(pnlDev, "↺  Apparaten", 8, 134);
        btnRefA.Click += (s, e) => PopulateAudioDevices();
        page.Resize += (s, e) =>
        {
            int cw = Math.Max(200, page.Width - 32);
            _cbAudioOut.Width = cw;
            _cbAudioIn.Width  = cw;
        };
        y += 172;

        // Test opties
        var pnlOpts = GB(page, "Test opties  (initiator)", 0, y, 96);
        L(pnlOpts, "Testtoon (Hz):", 8, 18);
        _nudFreq = NUD(pnlOpts, 110, 15, 86, 0, 10000, 1000);
        L(pnlOpts, "(0 = microfoon)", 204, 18, Color.FromArgb(110,110,110));
        L(pnlOpts, "Duur (s):", 8, 60);
        _nudDuration = NUD(pnlOpts, 70, 57, 64, 1, 60, 5);
        L(pnlOpts, "Volume:", 150, 60);
        _trkVolume = new TrackBar
        {
            Location = new Point(210, 50), Size = new Size(200, 30),
            Minimum = 0, Maximum = 100, Value = 80,
            TickFrequency = 10, BackColor = Color.FromArgb(38,38,38),
        };
        _lblVolume = new Label
        {
            Text = "80%", Location = new Point(418, 60), Size = new Size(44, 18),
            ForeColor = Color.FromArgb(220,220,220), BackColor = Color.Transparent,
        };
        _trkVolume.ValueChanged += (s, e) => _lblVolume.Text = $"{_trkVolume.Value}%";
        pnlOpts.Controls.AddRange(new Control[] { _trkVolume, _lblVolume });
        y += 108;

        // Meter
        var pnlMeter = GB(page, "Signaalniveau ingang", 0, y, 52);
        _pnlMeter = new Panel
        {
            Location = new Point(8, 18), Size = new Size(480, 22),
            BackColor = Color.FromArgb(20,20,20),
        };
        _lblLevel = new Label
        {
            Text = "---", Location = new Point(496, 20), AutoSize = true,
            ForeColor = Color.FromArgb(180,180,180), BackColor = Color.Transparent,
        };
        pnlMeter.Controls.AddRange(new Control[] { _pnlMeter, _lblLevel });
        y += 64;

        // Knoppen
        var pnlBtns = new Panel
        {
            Location = new Point(0, y), Height = 36, BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        page.Controls.Add(pnlBtns);
        page.Resize += (s, e) => pnlBtns.Width = page.Width - 16;

        _btnAudioTest = new Button
        {
            Text = "▶  Start", Location = new Point(0, 0), Size = new Size(100, 34),
            BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
        };
        _btnAudioTest.FlatAppearance.BorderSize = 0;
        _btnAudioTest.Click += (s, e) => _ = RunAudioTest();

        _btnAudioStop = new Button
        {
            Text = "⏹  Stop", Location = new Point(112, 0), Size = new Size(100, 34),
            BackColor = Color.FromArgb(160, 40, 40), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Enabled = false,
        };
        _btnAudioStop.FlatAppearance.BorderSize = 0;
        _btnAudioStop.Click += (s, e) => _audioCts.Cancel();

        var btnClearA = new Button
        {
            Text = "Log wissen", Location = new Point(224, 0), Size = new Size(100, 34),
            BackColor = Color.FromArgb(55,55,55), ForeColor = Color.FromArgb(200,200,200),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
        };
        btnClearA.FlatAppearance.BorderSize = 0;
        btnClearA.Click += (s, e) => _rtbAudio.Clear();

        pnlBtns.Controls.AddRange(new Control[] { _btnAudioTest, _btnAudioStop, btnClearA });
        y += 46;

        _rtbAudio = new RichTextBox
        {
            Location = new Point(0, y),
            BackColor = Color.FromArgb(18,18,18), ForeColor = Color.FromArgb(200,200,200),
            Font = new Font("Consolas", 8.5f), ReadOnly = true,
            BorderStyle = BorderStyle.None, ScrollBars = RichTextBoxScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        page.Controls.Add(_rtbAudio);
        page.Resize += (s, e) => _rtbAudio.Size = new Size(page.Width - 16, page.Height - y - 8);
        UpdateAudioUI();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SERIAL TEST
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task RunSerialTest()
    {
        _serialCts = new CancellationTokenSource();
        var ct = _serialCts.Token;

        _btnTest.Enabled = false;
        _btnStop.Enabled = true;
        _rbServer.Enabled = false;
        _rbClient.Enabled = false;

        string comPort = _cbPort.SelectedItem?.ToString() ?? "";
        int baud = (int)_nudBaud.Value;
        bool isServer = _rbServer.Checked;

        SLog("═══════════════════════════════════════════════");
        SLog($"Seriële poort test  —  {DateTime.Now:HH:mm:ss}");
        SLog($"Modus  : {(isServer ? "SERVER (wacht op data)" : "CLIENT (stuurt data)")}");
        SLog($"Poort  : {comPort}  {baud} baud");
        if (!isServer)
            SLog($"Opties : {_nudBytes.Value} bytes/ronde, {_nudRounds.Value} rondes");
        SLog("═══════════════════════════════════════════════");
        SLog("");

        SerialPort? sp = null;
        try
        {
            // Open port
            SStep("1", $"COM poort {comPort} openen op {baud} baud");
            sp = new SerialPort(comPort, baud)
            {
                ReadTimeout  = 3000,
                WriteTimeout = 2000,
                DtrEnable    = true,
                RtsEnable    = true,
            };
            sp.Open();
            SPass($"Poort {comPort} open");

            if (isServer)
                await RunServer(sp, ct);
            else
                await RunClient(sp, (int)_nudBytes.Value, (int)_nudRounds.Value, ct);
        }
        catch (OperationCanceledException) { SLog("⏹  Test afgebroken.", Color.FromArgb(220, 130, 60)); }
        catch (Exception ex) { SFail($"Fout: {ex.Message}"); }
        finally
        {
            try { sp?.Close(); sp?.Dispose(); } catch { }
        }

        _btnStop.Enabled = false;
        _btnTest.Enabled = true;
        _rbServer.Enabled = true;
        _rbClient.Enabled = true;
    }

    // ── Server: wacht op data, stuurt echo terug ──────────────────────────────

    private async Task RunServer(SerialPort sp, CancellationToken ct)
    {
        SStep("2", "Wachten op data van client...");
        SLog("     (start nu de CLIENT op de andere pc)");

        int totalReceived = 0;
        int rounds = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait for data
                await Task.Run(() =>
                {
                    while (sp.BytesToRead == 0 && !ct.IsCancellationRequested)
                        Thread.Sleep(10);
                }, ct);

                if (ct.IsCancellationRequested) break;

                // Read all available bytes
                int avail = sp.BytesToRead;
                var buf = new byte[avail];
                sp.Read(buf, 0, avail);
                totalReceived += avail;
                rounds++;

                // Echo back
                sp.Write(buf, 0, buf.Length);
                SInfo($"Ronde {rounds}: ontvangen {avail} bytes → echo teruggestuurd");

                // Check for signal lines
                if (rounds == 1)
                {
                    SInfo($"CTS: {(sp.CtsHolding ? "HIGH" : "LOW")}  " +
                          $"DSR: {(sp.DsrHolding ? "HIGH" : "LOW")}  " +
                          $"DCD: {(sp.CDHolding  ? "HIGH" : "LOW")}");
                }
            }
            catch (TimeoutException) { /* geen data, blijf wachten */ }
            catch (OperationCanceledException) { break; }
        }

        SLog("");
        SPass($"Server gestopt — {rounds} rondes verwerkt, {totalReceived} bytes ontvangen");
    }

    // ── Client: stuurt data, controleert echo ─────────────────────────────────

    private async Task RunClient(SerialPort sp, int bytes, int rounds, CancellationToken ct)
    {
        SStep("2", $"Data sturen en echo controleren ({bytes} bytes × {rounds} rondes)");
        SLog("     (zorg dat de SERVER al wacht op de andere pc)");
        SLog("");

        // Give server time to get ready
        await Task.Delay(800, ct);

        // Clear any stale data in buffers before we start
        sp.DiscardInBuffer();
        sp.DiscardOutBuffer();

        var rng = new Random();
        int ok = 0, errors = 0;

        for (int r = 1; r <= rounds && !ct.IsCancellationRequested; r++)
        {
            // Generate random data
            var data = new byte[bytes];
            rng.NextBytes(data);

            // Clear input buffer before sending to avoid reading old data
            sp.DiscardInBuffer();

            // Send and wait until all bytes are transmitted
            sp.Write(data, 0, data.Length);

            // Small delay to allow data to travel through the tunnel and echo back
            // Adjust based on baudrate: at 9600 baud, 64 bytes takes ~67ms
            int travelMs = (int)((bytes * 10.0 / sp.BaudRate) * 1000) + 100;
            await Task.Delay(travelMs, ct);

            // Read echo
            var echo = new byte[bytes];
            int read = 0;
            var deadline = DateTime.Now.AddMilliseconds(3000);
            while (read < bytes && DateTime.Now < deadline && !ct.IsCancellationRequested)
            {
                if (sp.BytesToRead > 0)
                    read += sp.Read(echo, read, bytes - read);
                else
                    await Task.Delay(5, ct);
            }

            if (read < bytes)
            {
                errors++;
                SWarn($"Ronde {r}: timeout — verwacht {bytes} bytes, ontvangen {read}");
            }
            else if (!data.SequenceEqual(echo))
            {
                errors++;
                // Find first difference
                int diff = 0;
                for (int i = 0; i < bytes; i++) if (data[i] != echo[i]) { diff = i; break; }
                SWarn($"Ronde {r}: data verschil op byte {diff}  " +
                      $"verstuurd: 0x{data[diff]:X2}  ontvangen: 0x{echo[diff]:X2}");
            }
            else
            {
                ok++;
                if (r == 1 || r % 5 == 0 || r == rounds)
                    SInfo($"Ronde {r}/{rounds}  ✓{ok}  ✗{errors}  ({bytes * r} bytes totaal)");
            }
        }

        SLog("");
        if (errors == 0)
            SPass($"Alle {ok} rondes correct  —  {ok * bytes} bytes foutloos overgedragen");
        else
            SFail($"{errors} rondes met fouten van {rounds} totaal");

        // Signal line check
        SStep("3", "Signaallijnen controleren");
        SInfo($"RTS: {(sp.RtsEnable  ? "HIGH" : "LOW")}  " +
              $"CTS: {(sp.CtsHolding ? "HIGH" : "LOW")}  " +
              $"DTR: {(sp.DtrEnable  ? "HIGH" : "LOW")}  " +
              $"DSR: {(sp.DsrHolding ? "HIGH" : "LOW")}  " +
              $"DCD: {(sp.CDHolding  ? "HIGH" : "LOW")}");

        if (sp.CtsHolding) SPass("CTS HIGH — hardware handshaking actief");
        else               SWarn("CTS LOW  — hardware handshaking niet actief (normaal bij software hs)");
        if (sp.DsrHolding) SPass("DSR HIGH — verbinding bevestigd");
        else               SWarn("DSR LOW  — geen DSR signaal");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AUDIO TEST
    // ═══════════════════════════════════════════════════════════════════════════

    private void UpdateAudioUI()
    {
        bool isInitiator = _rbAudioInitiator.Checked;
        _nudFreq.Enabled     = isInitiator;
        _nudDuration.Enabled = isInitiator;
        _trkVolume.Enabled   = isInitiator;
    }

    private async Task RunAudioTest()
    {
        _audioCts = new CancellationTokenSource();
        var ct = _audioCts.Token;

        _btnAudioTest.Enabled     = false;
        _btnAudioStop.Enabled     = true;
        _rbAudioInitiator.Enabled = false;
        _rbAudioLoopback.Enabled  = false;

        int outIdx = (_cbAudioOut.SelectedItem as AudioDevice)?.Index ?? 0;
        int inIdx  = (_cbAudioIn.SelectedItem  as AudioDevice)?.Index ?? 0;
        bool isInitiator = _rbAudioInitiator.Checked;

        ALog("═══════════════════════════════════════════════");
        ALog($"Audio test  —  {DateTime.Now:HH:mm:ss}");
        ALog($"Modus   : {(isInitiator ? "INITIATOR (speelt af, hoort terug)" : "LOOPBACK  (ontvangt, stuurt terug)")}");
        ALog($"Uitgang : {_cbAudioOut.Text}");
        ALog($"Ingang  : {_cbAudioIn.Text}");
        ALog("═══════════════════════════════════════════════");
        ALog("");

        if (isInitiator)
            await RunAudioInitiator(outIdx, inIdx, ct);
        else
            await RunAudioLoopback(outIdx, inIdx, ct);

        _btnAudioStop.Enabled     = false;
        _btnAudioTest.Enabled     = true;
        _rbAudioInitiator.Enabled = true;
        _rbAudioLoopback.Enabled  = true;
    }

    // ── Initiator: speelt af, luistert terug, meet niveau ────────────────────

    private async Task RunAudioInitiator(int outIdx, int inIdx, CancellationToken ct)
    {
        int freq   = (int)_nudFreq.Value;
        int durSec = (int)_nudDuration.Value;
        float vol  = _trkVolume.Value / 100f;
        bool useMic = freq == 0;

        const int sampleRate = 44100;
        var fmt = new WaveFormat(sampleRate, 16, 1);

        WaveOutEvent?        waveOut   = null;
        WaveInEvent?         waveIn    = null;
        WaveInEvent?         micIn     = null;
        BufferedWaveProvider? micBuf   = null;

        float maxLevel = 0;

        try
        {
            AStep("1", "Apparaten openen");
            waveIn = new WaveInEvent { DeviceNumber = inIdx, WaveFormat = fmt, BufferMilliseconds = 50 };
            waveIn.DataAvailable += (s, e) =>
            {
                double sum = 0;
                for (int i = 0; i < e.BytesRecorded - 1; i += 2)
                {
                    short s16 = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    sum += (double)s16 * s16;
                }
                float rms = (float)Math.Sqrt(sum / (e.BytesRecorded / 2)) / 32768f;
                if (rms > maxLevel) maxLevel = rms;
                UpdateMeter(rms);
            };

            waveOut = new WaveOutEvent { DeviceNumber = outIdx, DesiredLatency = 100 };

            if (useMic)
            {
                // Microphone mode: capture mic and play to output
                ALog($"Modus: microfoon  (ingang als bron, duur: {durSec}s)");
                micBuf = new BufferedWaveProvider(fmt)
                {
                    BufferDuration = TimeSpan.FromSeconds(5),
                    DiscardOnBufferOverflow = true,
                };
                micIn = new WaveInEvent { DeviceNumber = outIdx, WaveFormat = fmt, BufferMilliseconds = 50 };
                micIn.DataAvailable += (s, e) => micBuf.AddSamples(e.Buffer, 0, e.BytesRecorded);
                waveOut.Init(micBuf);
            }
            else
            {
                ALog($"Modus: testtoon {freq} Hz  {durSec}s  volume {_trkVolume.Value}%");
                var sine = GenerateSine(freq, vol, sampleRate, durSec * 1000);
                waveOut.Init(new RawSourceWaveStream(new MemoryStream(sine), fmt));
            }
            APass("1", "Apparaten open");

            AStep("2", useMic ? "Microfoon doorsturen en terugluisteren..." : $"Testtoon afspelen en terugluisteren...");
            ALog("     (zorg dat LOOPBACK kant al actief is op de andere pc)");

            waveIn.StartRecording();
            if (useMic) micIn?.StartRecording();
            waveOut.Play();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var endTime = DateTime.Now.AddSeconds(durSec);
            while (DateTime.Now < endTime && !ct.IsCancellationRequested)
            {
                await Task.Delay(300, ct).ConfigureAwait(false);
                AInfo($"  {sw.Elapsed.TotalSeconds:0.0}s  max niveau terug: {maxLevel * 100:0.0}%");
            }

            waveIn.StopRecording();
            micIn?.StopRecording();
            waveOut.Stop();

            AStep("3", "Resultaat");
            if (maxLevel > 0.01f)
            {
                APass("3", $"Signaal ontvangen — max niveau: {maxLevel * 100:0.0}%");
                if      (maxLevel > 0.3f)  ALog("  ✅ Sterk signaal — tunnel werkt uitstekend", Color.FromArgb(100,220,100));
                else if (maxLevel > 0.05f) ALog("  ✅ Signaal aanwezig — tunnel werkt", Color.FromArgb(160,220,100));
                else                        ALog("  ⚠  Zwak signaal — controleer volume", Color.FromArgb(220,180,60));
            }
            else
            {
                AFail("3", "Geen signaal terug ontvangen");
                ALog("  Controleer: is de LOOPBACK kant actief op de andere pc?", Color.FromArgb(160,160,160));
                ALog("  Controleer: klopt de uitgang/ingang selectie?", Color.FromArgb(160,160,160));
            }
        }
        catch (OperationCanceledException) { ALog("⏹  Gestopt.", Color.FromArgb(220,130,60)); }
        catch (Exception ex) { AFail("", $"Fout: {ex.Message}"); }
        finally
        {
            UpdateMeter(0);
            try { waveOut?.Dispose(); } catch { }
            try { waveIn?.Dispose();  } catch { }
            try { micIn?.Dispose();   } catch { }
        }
    }

    // ── Loopback: ontvangt audio, stuurt direct terug ─────────────────────────

    private async Task RunAudioLoopback(int outIdx, int inIdx, CancellationToken ct)
    {
        const int sampleRate = 44100;
        var fmt = new WaveFormat(sampleRate, 16, 1);

        WaveOutEvent?         waveOut = null;
        WaveInEvent?          waveIn  = null;
        BufferedWaveProvider? buf     = null;

        try
        {
            AStep("1", "Apparaten openen");
            buf = new BufferedWaveProvider(fmt)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
            };

            waveIn = new WaveInEvent { DeviceNumber = inIdx, WaveFormat = fmt, BufferMilliseconds = 50 };
            waveIn.DataAvailable += (s, e) =>
            {
                buf.AddSamples(e.Buffer, 0, e.BytesRecorded);
                // Meter
                double sum = 0;
                for (int i = 0; i < e.BytesRecorded - 1; i += 2)
                {
                    short s16 = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    sum += (double)s16 * s16;
                }
                float rms = (float)Math.Sqrt(sum / (e.BytesRecorded / 2)) / 32768f;
                UpdateMeter(rms);
            };

            waveOut = new WaveOutEvent { DeviceNumber = outIdx, DesiredLatency = 100 };
            waveOut.Init(buf);
            APass("1", "Apparaten open");

            AStep("2", "Loopback actief — audio van ingang → uitgang...");
            ALog("     (start nu de INITIATOR op de andere pc)");
            ALog("     (druk Stop om te stoppen)");

            waveIn.StartRecording();
            waveOut.Play();

            // Run until cancelled
            while (!ct.IsCancellationRequested)
                await Task.Delay(500, ct).ConfigureAwait(false);

            waveIn.StopRecording();
            waveOut.Stop();
            ALog("⏹  Loopback gestopt.", Color.FromArgb(220,130,60));
        }
        catch (OperationCanceledException)
        {
            ALog("⏹  Loopback gestopt.", Color.FromArgb(220,130,60));
        }
        catch (Exception ex) { AFail("", $"Fout: {ex.Message}"); }
        finally
        {
            UpdateMeter(0);
            try { waveOut?.Dispose(); } catch { }
            try { waveIn?.Dispose();  } catch { }
        }
    }

    private static byte[] GenerateSine(int freq, float amp, int sr, int ms)
    {
        int n = sr * ms / 1000;
        var b = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short s = (short)(Math.Sin(2 * Math.PI * freq * i / sr) * amp * 32767);
            b[i * 2]     = (byte)(s & 0xFF);
            b[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return b;
    }

    private void UpdateMeter(float level)
    {
        if (InvokeRequired) { Invoke(() => UpdateMeter(level)); return; }
        int w = (int)(_pnlMeter.Width * Math.Min(level * 2, 1.0));
        Color c = level > 0.7f ? Color.FromArgb(220,60,60)
                : level > 0.3f ? Color.FromArgb(220,180,40)
                : Color.FromArgb(60,180,60);
        using var g = _pnlMeter.CreateGraphics();
        g.FillRectangle(new SolidBrush(Color.FromArgb(20,20,20)), 0, 0, _pnlMeter.Width, _pnlMeter.Height);
        if (w > 0) g.FillRectangle(new SolidBrush(c), 0, 0, w, _pnlMeter.Height);
        _lblLevel.Text = level > 0.001f ? $"{level*100:0.0}%" : "---";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // POPULATE
    // ═══════════════════════════════════════════════════════════════════════════

    private void PopulatePorts()
    {
        var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        var sel = _cbPort.SelectedItem?.ToString();
        _cbPort.Items.Clear();
        foreach (var p in ports) _cbPort.Items.Add(p);
        if (sel != null && _cbPort.Items.Contains(sel)) _cbPort.SelectedItem = sel;
        else if (_cbPort.Items.Count > 0) _cbPort.SelectedIndex = 0;
        SLog($"Gevonden: {ports.Length} COM poort(en): {string.Join(", ", ports)}");
    }

    private void PopulateAudioDevices()
    {
        var selOut = _cbAudioOut.SelectedItem?.ToString();
        var selIn  = _cbAudioIn.SelectedItem?.ToString();
        _cbAudioOut.Items.Clear();
        for (int i = 0; i < WaveOut.DeviceCount; i++)
            _cbAudioOut.Items.Add(new AudioDevice(i, WaveOut.GetCapabilities(i).ProductName));
        _cbAudioIn.Items.Clear();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            _cbAudioIn.Items.Add(new AudioDevice(i, WaveInEvent.GetCapabilities(i).ProductName));

        RestoreSel(_cbAudioOut, selOut);
        RestoreSel(_cbAudioIn,  selIn);
        ALog($"Gevonden: {_cbAudioOut.Items.Count} uitgang(en), {_cbAudioIn.Items.Count} ingang(en)");
    }

    private static void RestoreSel(ComboBox cb, string? prev)
    {
        if (prev != null)
            for (int i = 0; i < cb.Items.Count; i++)
                if (string.Equals(cb.Items[i]?.ToString(), prev)) { cb.SelectedIndex = i; return; }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private static void Swap(ComboBox a, ComboBox b)
    {
        var t = a.SelectedItem; a.SelectedItem = b.SelectedItem; b.SelectedItem = t;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LOG
    // ═══════════════════════════════════════════════════════════════════════════

    private void SStep(string n, string t) => SLog($"── Stap {n}: {t}", Color.FromArgb(160,200,255));
    private void SPass(string t)  => SLog($"  ✅ {t}", Color.FromArgb(100,220,100));
    private void SFail(string t)  => SLog($"  ❌ {t}", Color.FromArgb(220,80,80));
    private void SWarn(string t)  => SLog($"  ⚠  {t}", Color.FromArgb(220,180,60));
    private void SInfo(string t)  => SLog($"     {t}", Color.FromArgb(160,160,160));
    private void SLog(string t, Color? c = null) => Append(_rtbSerial, t, c ?? Color.FromArgb(130,130,130));

    private void AStep(string n, string t) => ALog($"── Stap {n}: {t}", Color.FromArgb(160,200,255));
    private void APass(string n, string t) => ALog($"  ✅ {t}", Color.FromArgb(100,220,100));
    private void AFail(string n, string t) => ALog($"  ❌ {t}", Color.FromArgb(220,80,80));
    private void AInfo(string t)           => ALog($"     {t}", Color.FromArgb(160,160,160));
    private void ALog(string t, Color? c = null) => Append(_rtbAudio, t, c ?? Color.FromArgb(130,130,130));

    private void Append(RichTextBox rtb, string text, Color color)
    {
        if (InvokeRequired) { Invoke(() => Append(rtb, text, color)); return; }
        rtb.SelectionStart = rtb.TextLength; rtb.SelectionLength = 0;
        rtb.SelectionColor = color;
        rtb.AppendText(text + "\n");
        rtb.ScrollToCaret();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CONTROL FACTORIES
    // ═══════════════════════════════════════════════════════════════════════════

    private GroupBox GB(Control parent, string title, int x, int y, int h)
    {
        var gb = new GroupBox
        {
            Text = title, Location = new Point(x, y),
            Size = new Size(parent.Width - x - 16, h),
            ForeColor = Color.FromArgb(160,200,255),
            BackColor = Color.FromArgb(38,38,38),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        parent.Controls.Add(gb);
        parent.Resize += (s, e) => gb.Width = parent.Width - x - 16;
        return gb;
    }

    private static void L(Control p, string t, int x, int y, Color? c = null) =>
        p.Controls.Add(new Label { Text=t, Location=new Point(x,y), AutoSize=true,
            ForeColor=c??Color.FromArgb(170,170,170), BackColor=Color.Transparent });

    private static ComboBox CB(Control p, int x, int y, int w)
    {
        var cb = new ComboBox { Location=new Point(x,y), Size=new Size(w,23),
            DropDownStyle=ComboBoxStyle.DropDownList,
            BackColor=Color.FromArgb(50,50,50), ForeColor=Color.FromArgb(220,220,220),
            FlatStyle=FlatStyle.Flat };
        p.Controls.Add(cb); return cb;
    }

    private static NumericUpDown NUD(Control p, int x, int y, int w, int min, int max, int val)
    {
        var n = new NumericUpDown { Location=new Point(x,y), Size=new Size(w,23),
            Minimum=min, Maximum=max, Value=Math.Clamp(val,min,max),
            BackColor=Color.FromArgb(50,50,50), ForeColor=Color.FromArgb(220,220,220),
            BorderStyle=BorderStyle.FixedSingle };
        p.Controls.Add(n); return n;
    }

    private static Button SBtn(Control p, string t, int x, int y)
    {
        var b = new Button { Text=t, Location=new Point(x,y), Size=new Size(100,24),
            BackColor=Color.FromArgb(60,60,60), ForeColor=Color.FromArgb(200,200,200),
            FlatStyle=FlatStyle.Flat, Cursor=Cursors.Hand };
        b.FlatAppearance.BorderSize=0; p.Controls.Add(b); return b;
    }
}

record AudioDevice(int Index, string Name) { public override string ToString() => Name; }
