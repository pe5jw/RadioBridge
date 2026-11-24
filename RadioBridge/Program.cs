using Avalonia;

namespace RadioBridge;

internal enum StartMode { Server, Client }

internal static class Program
{
    /// <summary>Gezet door --startserver / --startclient: direct verbinden met de opgeslagen instellingen.</summary>
    public static StartMode? AutoStart { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        bool Has(string a) => args.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase));

        if (Has("--help") || Has("-h") || Has("/?"))
        {
            Console.WriteLine("""
                RadioBridge — audio + serieel over netwerk

                Gebruik: RadioBridge [opties]

                  --startserver   start direct als server met de laatst opgeslagen instellingen
                  --startclient   start direct als client met de laatst opgeslagen instellingen
                  --headless      zonder venster draaien (alleen samen met --startserver of --startclient);
                                  logt naar de console, stoppen met Ctrl+C of systemctl stop
                  --help          deze uitleg

                Instellingen worden bewaard in:
                """ + "  " + AppSettings.FilePath);
            return 0;
        }

        bool startServer = Has("--startserver");
        bool startClient = Has("--startclient");
        bool headless    = Has("--headless");

        foreach (var a in args.Where(a => a.StartsWith('-') &&
                     !new[] { "--startserver", "--startclient", "--headless" }.Contains(a, StringComparer.OrdinalIgnoreCase)))
            Console.Error.WriteLine($"Onbekende optie genegeerd: {a}  (zie --help)");

        if (startServer && startClient)
        {
            Console.Error.WriteLine("Kies --startserver óf --startclient, niet allebei.");
            return 1;
        }
        if (headless && !startServer && !startClient)
        {
            Console.Error.WriteLine("--headless kan alleen samen met --startserver of --startclient.");
            return 1;
        }

        if (headless)
            return Headless.Run(server: startServer);

        AutoStart = startServer ? StartMode.Server : startClient ? StartMode.Client : null;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace();
}
