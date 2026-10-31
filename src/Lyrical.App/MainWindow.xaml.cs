using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Lyrical;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly MediaSessionService _media = new();
    private readonly LyricsService _lyrics = new();
    private CancellationTokenSource? _lyricsCts;
    private bool _mediaStarted;
    private bool _fullscreen;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => _media.Dispose();
    }

    private async Task InitAsync()
    {
        Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0E, 0x10, 0x20);
        await Web.EnsureCoreWebView2Async();
        var core = Web.CoreWebView2;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        core.SetVirtualHostNameToFolderMapping(
            "Lyrical.local", Path.Combine(AppContext.BaseDirectory, "web"),
            CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;
        core.Navigate("https://Lyrical.local/index.html");
    }

    // ---- page -> app ----
    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "ready":
                    if (!_mediaStarted)
                    {
                        _mediaStarted = true;
                        _media.TrackChanged += OnTrack;
                        _media.ClockChanged += (pos, playing) =>
                            Post(new { type = "clock", positionMs = pos, playing, rate = 1.0 });
                        await _media.StartAsync();
                    }
                    else
                    {
                        _media.Resend(); // page reloaded
                    }
                    break;
                case "toggleFullscreen": ToggleFullscreen(); break;
                case "togglePin": Topmost = !Topmost; break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Web message failed: {ex}");
        }
    }

    // ---- app -> page ----
    private void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, Json);
        Dispatcher.BeginInvoke(() => Web.CoreWebView2?.PostWebMessageAsJson(json));
    }

    private void OnTrack(TrackInfo? track)
    {
        _lyricsCts?.Cancel();
        _lyricsCts = new CancellationTokenSource();
        var ct = _lyricsCts.Token;

        if (track == null)
        {
            Post(new { type = "idle" });
            return;
        }

        // Repair broken media-session metadata once, so the visible track
        // details and both lyrics providers receive the same clean values.
        track = TrackNormalizer.CleanMetadata(track);

        Post(new { type = "track", title = track.Title, artist = track.Artist, album = track.Album, durationMs = track.DurationMs, artDataUrl = track.ArtworkDataUrl });

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _lyrics.GetAsync(track, ct);
                if (ct.IsCancellationRequested) return;
                Post(new { type = "lyrics", lrc = result?.Synced, plain = !string.IsNullOrEmpty(result?.Plain) });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lyrics lookup failed: {ex.Message}");
                if (!ct.IsCancellationRequested) Post(new { type = "lyrics", lrc = (string?)null, plain = false });
            }
        });
    }

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        if (_fullscreen)
        {
            WindowState = WindowState.Normal; // re-maximise so the window covers the taskbar
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
        }
    }
}
