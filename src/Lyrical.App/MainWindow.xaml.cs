using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Lyrical;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly MediaSessionService _media = new();
    private readonly LyricsService _lyrics = new();
    private CancellationTokenSource? _lyricsCts;
    private bool _mediaStarted;
    private bool _fullscreen;
    private readonly string _backgroundDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lyrical", "backgrounds");

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
                    await PostCustomBackgroundAsync();
                    break;
                case "toggleFullscreen": ToggleFullscreen(); break;
                case "setPresentation": SetFullscreen(doc.RootElement.GetProperty("presentation").GetString() == "fullscreen"); break;
                case "togglePin": Topmost = !Topmost; break;
                case "setChromeColor": SetChromeColor(doc.RootElement.GetProperty("color").GetString()); break;
                case "chooseBackgroundImage": await ChooseCustomBackgroundAsync(); break;
                case "requestBackgroundImage": await PostCustomBackgroundAsync(); break;
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
        SetFullscreen(!_fullscreen);
    }

    private void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        if (fullscreen)
        {
            WindowState = WindowState.Normal; // re-maximise so the window covers the taskbar
            ResizeMode = ResizeMode.NoResize;
            ChromeRow.Height = new GridLength(0);
            Chrome.CaptionHeight = 0;
            WindowState = WindowState.Maximized;
        }
        else
        {
            ResizeMode = ResizeMode.CanResize;
            ChromeRow.Height = new GridLength(38);
            Chrome.CaptionHeight = 38;
            WindowState = WindowState.Normal;
        }
    }

    private void SetChromeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            ChromeBar.Background = new SolidColorBrush(color);
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
            var foreground = new SolidColorBrush(luminance > .48 ? Colors.Black : Colors.White);
            ChromeTitle.Foreground = foreground;
            MinimizeButton.Foreground = foreground;
            MaximizeButton.Foreground = foreground;
        }
        catch (FormatException) { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task ChooseCustomBackgroundAsync()
    {
        // WebView2 can keep focus when the request originates from a renderer
        // button. Activate the WPF owner before showing the native picker, and
        // temporarily release Topmost so the dialog cannot be obscured by the
        // always-on-top presentation mode.
        var wasTopmost = Topmost;
        try
        {
            Topmost = false;
            Activate();
            Focus();

            var dialog = new OpenFileDialog
            {
                Title = "Choose a Lyrical background image",
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) != true) return;

        try
        {
            const long maxBackgroundBytes = 12 * 1024 * 1024;
            var source = new FileInfo(dialog.FileName);
            if (source.Length > maxBackgroundBytes)
            {
                MessageBox.Show(this, "Choose an image smaller than 12 MB.", "Lyrical", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var extension = source.Extension.ToLowerInvariant();
            if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp" and not ".bmp") return;

            Directory.CreateDirectory(_backgroundDirectory);
            var name = $"{Guid.NewGuid():N}{extension}";
            File.Copy(source.FullName, Path.Combine(_backgroundDirectory, name));
            await File.WriteAllTextAsync(Path.Combine(_backgroundDirectory, "current.txt"), name);
            await PostCustomBackgroundAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Background image save failed: {ex}");
            MessageBox.Show(this, "Lyrical could not save that background image.", "Lyrical", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        }
        finally
        {
            Topmost = wasTopmost;
            if (wasTopmost) Activate();
        }
    }

    private async Task PostCustomBackgroundAsync()
    {
        try
        {
            var currentPath = Path.Combine(_backgroundDirectory, "current.txt");
            if (!File.Exists(currentPath))
            {
                Post(new { type = "backgroundImage", dataUrl = (string?)null });
                return;
            }

            var name = Path.GetFileName((await File.ReadAllTextAsync(currentPath)).Trim());
            var path = Path.Combine(_backgroundDirectory, name);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length > 12 * 1024 * 1024)
            {
                Post(new { type = "backgroundImage", dataUrl = (string?)null });
                return;
            }

            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };
            var bytes = await File.ReadAllBytesAsync(path);
            Post(new { type = "backgroundImage", dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}" });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Background image load failed: {ex.Message}");
            Post(new { type = "backgroundImage", dataUrl = (string?)null });
        }
    }
}
