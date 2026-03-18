using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UniversalCam.Audio;
using UniversalCam.Core;
using UniversalCam.Core.Audio;
using UniversalCam.Core.Protocol;
using UniversalCam.Core.Transport;
using UniversalCam.Core.Video;
using UniversalCam.VirtualCamera;

namespace UniversalCam.Views;

// ── Value converters ──────────────────────────────────────────────────────────

public sealed class BoolToHighlightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C))
            : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToAccentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))   // iOS blue
            : new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x4A));  // iOS tertiary

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// ── Camera view-model ─────────────────────────────────────────────────────────

public sealed class CameraViewModel
{
    public string Id          { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
    public string Position    { get; set; } = string.Empty;
    public string CameraType  { get; set; } = string.Empty;
    public double ZoomFactor  { get; set; } = 1.0;
    public bool   IsSelected  { get; set; }

    public string DisplayName
    {
        get
        {
            string z = ZoomFactor.ToString("0.#");
            return (CameraType, Position) switch
            {
                ("ultra_wide", _)    => $"Ultra Wide {z}×",
                ("telephoto",  _)    => $"Telephoto {z}×",
                ("true_depth", _)    => $"Selfie {z}×",
                (_,         "front") => $"Selfie {z}×",
                _                    => $"Wide {z}×",
            };
        }
    }
}

// ── MainWindow ────────────────────────────────────────────────────────────────

public partial class MainWindow : Window
{
    private ConnectionManager? _cm;
    private H264Decoder? _decoder;
    private AacDecoder? _aacDecoder;
    private AudioPlayer? _audioPlayer;
    private VirtualCameraSession? _vCamSession;

    private int _frameWidth;
    private int _frameHeight;
    private WriteableBitmap? _bitmap;
    private int _rotation;

    // ── Render-level drop guard: skip Dispatcher if a frame is already queued ──
    private int _pendingFrame;

    // ── Decode-level drop guard: always decode only the newest received frame ──
    private volatile MediaFrame? _latestVideoFrame;
    private int _decoderRunning;

    private string _currentCameraId = string.Empty;

    // Debounce Configure sends: WiFi+USB hellos arrive within ~50 ms of each other,
    // causing two Connected events and two Configure messages. Cancel the first and
    // let the second win so we send exactly one Configure on the active transport.
    private CancellationTokenSource? _configCts;

    private readonly DispatcherTimer _vuTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _pingTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _decoder = new H264Decoder();
        _decoder.FrameDecoded += OnFrameDecoded;

        _vCamSession = new VirtualCameraSession();

        _aacDecoder  = new AacDecoder();
        _audioPlayer = new AudioPlayer();
        _aacDecoder.PcmDecoded += (_, pcm) => _audioPlayer.Feed(pcm);

        _vuTimer.Tick += (_, _) =>
        {
            if (_audioPlayer is not null && pbVolume.IsEnabled)
                pbVolume.Value = _audioPlayer.CurrentRms;
        };
        _vuTimer.Start();

        _pingTimer.Tick += (_, _) => _ = _cm?.PingAsync();

        _cm = new ConnectionManager();
        _cm.StateChanged    += OnStateChanged;
        _cm.FrameReceived   += OnMediaFrame;
        _cm.CamerasReceived += OnCamerasReceived;
        _cm.LatencyUpdated  += OnLatencyUpdated;
        _cm.CameraSwitched  += (_, _) => _decoder?.Reset();

        _decoder.FrameDecoded += _vCamSession.OnFrameDecoded;
        _cm.StateChanged      += _vCamSession.OnConnectionStateChanged;

        txtIpHint.Text = GetLocalIpHint();
        UpdateStatus(TransportState.Idle, TransportType.None);

        try   { await _cm.StartAsync(); }
        catch (Exception ex) { txtWaiting.Text = $"Startup error: {ex.Message}"; }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _pingTimer.Stop();
        _vuTimer.Stop();
        _configCts?.Cancel();

        // Stop feeding new frames and wait for any in-flight drain task to exit
        // before disposing the FFmpeg codec context — without this, avcodec_receive_frame
        // can be called on a freed AVCodecContext → AccessViolationException.
        _latestVideoFrame = null;
        while (System.Threading.Interlocked.CompareExchange(ref _decoderRunning, 0, 0) != 0)
            await Task.Delay(5);

        if (_cm is not null)
        {
            await _cm.StopStreamAsync();
            await _cm.DisposeAsync();
            _cm = null;
        }

        _vCamSession?.Dispose();
        _decoder?.Dispose();
        _aacDecoder?.Dispose();
        _audioPlayer?.Dispose();
    }

    // ── Transport events ──────────────────────────────────────────────────────

    private void OnStateChanged(object? sender, TransportState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var type = _cm?.ActiveTransport ?? TransportType.None;
            UpdateStatus(state, type);

            if (state == TransportState.Connected)
            {
                _rotation = 0;
                imgRotation.Angle = 0;
                txtDeviceName.Text = _cm?.DeviceName ?? "iPhone";
                txtWaiting.Text   = "Connected — waiting for stream…";
                btnMute.IsEnabled = true;
                pbVolume.IsEnabled = true;
                _pingTimer.Start();
                _configCts?.Cancel();
                _configCts = new CancellationTokenSource();
                var cts = _configCts;
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(120, cts.Token); }
                    catch (OperationCanceledException) { return; }
                    await Dispatcher.InvokeAsync(() => _ = SendConfigureAndStartAsync());
                });
            }
            else if (state is TransportState.Disconnected or TransportState.Idle)
            {
                _pingTimer.Stop();
                txtDeviceName.Text = "No device";
                pnlNoStream.Visibility    = Visibility.Visible;
                pnlVideoInfo.Visibility   = Visibility.Collapsed;
                imgPreview.Source         = null;
                icBackCameras.ItemsSource  = null;
                icFrontCameras.ItemsSource = null;
                pnlBack.Visibility   = Visibility.Collapsed;
                pnlFront.Visibility  = Visibility.Collapsed;
                btnMute.IsEnabled    = false;
                pbVolume.IsEnabled   = false;
                pbVolume.Value       = 0;
                txtInfo.Text         = string.Empty;
                txtLatency.Text      = string.Empty;
                txtInfoSep.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void OnMediaFrame(object? sender, MediaFrame frame)
    {
        if (frame.IsVideo)
        {
            // Overwrite with newest frame — the decode task will pick it up.
            // Older frames that haven't been decoded yet are silently replaced,
            // ensuring we always decode the freshest data even during bursts.
            _latestVideoFrame = frame;
            if (System.Threading.Interlocked.CompareExchange(ref _decoderRunning, 1, 0) == 0)
                System.Threading.Tasks.Task.Run(DrainVideoFrames);
        }
        else if (frame.IsAudio)
        {
            _aacDecoder?.Decode(frame.Payload, frame.Header.PtsUs);
        }
    }

    /// Runs on a thread-pool thread. Serialised by _decoderRunning so H264Decoder
    /// is always called from a single thread at a time (its documented requirement).
    private void DrainVideoFrames()
    {
        while (true)
        {
            var frame = _latestVideoFrame;
            _latestVideoFrame = null;
            if (frame == null) break;
            _decoder?.Feed(frame);
        }
        System.Threading.Interlocked.Exchange(ref _decoderRunning, 0);
        // Lost-wakeup guard: a new frame may have arrived between the while-exit
        // and the flag release. Schedule a new drain task rather than recursing.
        if (_latestVideoFrame != null &&
            System.Threading.Interlocked.CompareExchange(ref _decoderRunning, 1, 0) == 0)
            System.Threading.Tasks.Task.Run(DrainVideoFrames);
    }

    private void OnCamerasReceived(object? sender, AvailableCameras msg)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _currentCameraId = msg.CurrentCameraId;

            var vms = msg.Cameras
                .Select(c => new CameraViewModel
                {
                    Id         = c.Id,
                    Name       = c.Name,
                    Position   = c.Position,
                    CameraType = c.Type,
                    ZoomFactor = c.ZoomFactor,
                    IsSelected = c.Id == msg.CurrentCameraId
                })
                .ToList();

            var back  = vms.Where(c => c.Position == "back").ToList();
            var front = vms.Where(c => c.Position is "front" or "unspecified").ToList();

            icBackCameras.ItemsSource  = back;
            icFrontCameras.ItemsSource = front;
            pnlBack.Visibility  = back.Count  > 0 ? Visibility.Visible : Visibility.Collapsed;
            pnlFront.Visibility = front.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_cm is not null && _cm.DeviceName is { Length: > 0 } name)
                txtDeviceName.Text = name;
        });
    }

    private void OnLatencyUpdated(object? sender, int ms)
    {
        Dispatcher.BeginInvoke(() =>
        {
            txtLatency.Text = $"~{ms} ms";
            txtInfoSep.Visibility = txtInfo.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // ── Decoder output (background thread) ────────────────────────────────────

    private void OnFrameDecoded(object? sender, DecodedFrame frame)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _pendingFrame, 1, 0) != 0)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            System.Threading.Interlocked.Exchange(ref _pendingFrame, 0);

            if (frame.Width <= 1) return;

            pnlNoStream.Visibility  = Visibility.Collapsed;
            pnlVideoInfo.Visibility = Visibility.Visible;

            if (_bitmap is null || _frameWidth != frame.Width || _frameHeight != frame.Height)
            {
                _frameWidth  = frame.Width;
                _frameHeight = frame.Height;
                _bitmap = new WriteableBitmap(
                    frame.Width, frame.Height, 96, 96,
                    PixelFormats.Bgra32, null);
                imgPreview.Source = _bitmap;
                txtInfo.Text = $"{frame.Width}×{frame.Height}";
                txtInfoSep.Visibility = txtLatency.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            _bitmap.Lock();
            try
            {
                Marshal.Copy(frame.Data, 0, _bitmap.BackBuffer, frame.Data.Length);
                _bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, frame.Width, frame.Height));
            }
            finally
            {
                _bitmap.Unlock();
            }
        });
    }

    // ── Button / control handlers ─────────────────────────────────────────────

    private async void OnCameraRowClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || _cm is null) return;
        await _cm.SwitchCameraAsync(id);
        await Task.Delay(300);
        await _cm.ListCamerasAsync();
    }

    private void OnQualityChanged(object sender, RoutedEventArgs e)
    {
        if (_cm?.State == TransportState.Connected || _cm?.State == (TransportState)4)
            _ = SendConfigureAndStartAsync();
    }

    private void OnRotateClicked(object sender, RoutedEventArgs e)
    {
        _rotation = (_rotation + 90) % 360;
        imgRotation.Angle = _rotation;
    }

    private void OnMuteClicked(object sender, RoutedEventArgs e)
    {
        if (_audioPlayer is null) return;
        _audioPlayer.IsMuted = btnMute.IsChecked ?? false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendConfigureAndStartAsync()
    {
        if (_cm is null) return;

        var (res, fps, bitrate) = true switch
        {
            _ when rb720p60.IsChecked  == true => ("1280x720",  60,  7_000_000),
            _ when rb1080p60.IsChecked == true => ("1920x1080", 60, 16_000_000),
            _ when rb4K.IsChecked      == true => ("3840x2160", 30, 25_000_000),
            _ when rb4K60.IsChecked    == true => ("3840x2160", 60, 50_000_000),
            _                                   => ("1920x1080", 30,  8_000_000),
        };

        await _cm.ConfigureAsync(res, fps, "h264", bitrate);
    }

    private static string GetLocalIpHint()
    {
        try
        {
            var ip = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .FirstOrDefault();
            return ip is null ? string.Empty : $"PC: {ip}";
        }
        catch { return string.Empty; }
    }

    private void UpdateStatus(TransportState state, TransportType type)
    {
        (ellStatus.Fill, txtStatus.Text, txtStatus.Foreground) = state switch
        {
            TransportState.Idle         => (new SolidColorBrush(Color.FromRgb(0x48,0x48,0x4A)), "Idle",          new SolidColorBrush(Color.FromRgb(0x63,0x63,0x66))),
            TransportState.Listening    => (new SolidColorBrush(Color.FromRgb(0x0A,0x84,0xFF)), "Searching…",    new SolidColorBrush(Color.FromRgb(0x0A,0x84,0xFF))),
            TransportState.Connected    => (new SolidColorBrush(Color.FromRgb(0x30,0xD1,0x58)), "Connected",     new SolidColorBrush(Color.FromRgb(0x30,0xD1,0x58))),
            TransportState.Disconnected => (new SolidColorBrush(Color.FromRgb(0xFF,0x9F,0x0A)), "Disconnected",  new SolidColorBrush(Color.FromRgb(0xFF,0x9F,0x0A))),
            TransportState.Error        => (new SolidColorBrush(Color.FromRgb(0xFF,0x45,0x3A)), "Error",         new SolidColorBrush(Color.FromRgb(0xFF,0x45,0x3A))),
            _                           => (new SolidColorBrush(Color.FromRgb(0x48,0x48,0x4A)), state.ToString(), new SolidColorBrush(Color.FromRgb(0x63,0x63,0x66))),
        };

        if (type == TransportType.None)
        {
            badgeTransport.Visibility = Visibility.Collapsed;
        }
        else
        {
            badgeTransport.Visibility = Visibility.Visible;
            badgeTransport.Background = type == TransportType.USB
                ? (Brush)Application.Current.Resources["UsbBadgeBrush"]
                : (Brush)Application.Current.Resources["WifiBadgeBrush"];
            txtTransport.Text = type.ToString();
        }
    }
}
