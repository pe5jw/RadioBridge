using System.Text.Json;

namespace RadioBridge;

/// <summary>
/// Laatst gebruikte instellingen, bewaard als JSON zodat de app na een herstart
/// met dezelfde instellingen opent.
///   Windows: %APPDATA%\RadioBridge\settings.json
///   Linux  : ~/.config/RadioBridge/settings.json
/// </summary>
public sealed class AppSettings
{
    public bool   IsServer       { get; set; } = true;
    public string Host           { get; set; } = "127.0.0.1";
    public int    Port           { get; set; } = 5000;

    // Audio — apparaten op naam, want indexen verschuiven als er iets wordt in/uitgeplugd
    public string CaptureDevice  { get; set; } = "";
    public string PlaybackDevice { get; set; } = "";
    public double CaptureVolume  { get; set; } = 100;
    public double PlaybackVolume { get; set; } = 100;
    public string Preset         { get; set; } = "";
    public bool   NoiseFilter    { get; set; }

    // Server: seriële poorten
    public SerialSetting Serial1 { get; set; } = new() { TcpPort = 5001 };
    public SerialSetting Serial2 { get; set; } = new() { TcpPort = 5002 };

    // Client: com0com "inside" poorten (leeg = niet gebruiken)
    public string VirtualCom1    { get; set; } = "";
    public string VirtualCom2    { get; set; } = "";

    // Testtoon
    public double ToneFrequency  { get; set; } = 1000;
    public double ToneVolume     { get; set; } = 25;

    public sealed class SerialSetting
    {
        public bool   Enabled { get; set; }
        public string ComPort { get; set; } = "";
        public int    Baud    { get; set; } = 9600;
        public int    TcpPort { get; set; }
    }

    // ── Opslaan / laden ───────────────────────────────────────────────────────

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RadioBridge", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new();
        }
        catch { /* kapot bestand → standaardinstellingen */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Eerst naar tijdelijk bestand, dan vervangen: geen half bestand bij stroomuitval
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* opslaan is best-effort */ }
    }
}
