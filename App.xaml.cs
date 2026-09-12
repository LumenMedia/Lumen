using Microsoft.UI.Xaml;
using Lumen.Services;
using Lumen.ViewModels;

namespace Lumen;

public partial class App : Application
{
    public static JellyfinClient Jellyfin { get; } = new();
    public static LrcLibClient LrcLib { get; } = new();
    public static AppSettings Settings { get; } = new();
    public static DownloadManager Downloads { get; } = new();
    public static MusicPlaybackService MusicPlayback { get; } = new();
    public static LyricsAutoSyncService LyricsAutoSync { get; } = new();
    public static MainViewModel MainViewModel { get; } = new(Jellyfin, Settings);

    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        if (MainWindow is Lumen.MainWindow mainWindow)
            mainWindow.SetLaunchArgument(args.Arguments);
        MainWindow.Activate();
    }
}
