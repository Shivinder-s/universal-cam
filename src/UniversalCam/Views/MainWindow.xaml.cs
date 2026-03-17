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

namespace UniversalCam.Views;

// ── Value converters ──────────────────────────────────────────────────────────

public sealed class BoolToHighlightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42))
            : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToAccentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x51, 0x2B, 0xD4))
            : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// ── Camera view-model ─────────────────────────────────────────────────────────

public sealed class CameraViewModel
{
    public string Id          { get; set; } = string.Empty;
    public string Name        { get; set; } = string.Empty;
    public string Position    { get; set; } = string.Empty;
    public bool   IsSelected  { get; set; }

    /// Adds a lens-type emoji prefix for visual clarity in the sidebar.
    public string DisplayName => Name;
}

// ── MainWindow ────────────────────────────────────────────────────────────────

public partial class MainWindow : Window
{
    private ConnectionManager? _cm;
    private H264Decoder?       _decoder;
    private AacDecoder?        _aacDecoder;
    private AudioPlayer?       _audioPlayer;

    private int              _frameWidth;
    private int              _frameHeight;
    private WriteableBitmap? _bitmap;
    private int              _rotation; // 0 / 90 / 180 / 270

    private string _currentCameraId = string.Empty;

    private readonly DispatcherTimer _vuTimer = new()
        { Interval = TimeSpan.FromMilliseconds(100) };

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Video
        _decoder = new H264Decoder();
        _decoder.FrameDecoded += OnFrameDecoded;

        // Audio
        _aacDecoder  = new AacDecoder();
        _audioPlayer = new AudioPlayer();
        _aacDecoder.PcmDecoded += (_, pcm) => _audioPlayer.Feed(pcm);

        // VU meter timer
        _vuTimer.Tick += (_, _) =>
        {
            if (_audioPlayer is not null && pbVolume.IsEnabled)
                pbVolume.Value = _audioPlayer.CurrentRms;
        };
        _vuTimer.Start();

        // Connection
        _cm = new ConnectionManager();
        _cm.StateChanged    += OnStateChanged;
        _cm.FrameReceived   += OnMediaFrame;
        _cm.CamerasReceived += OnCamerasReceived;

        txtIpHint.Text = GetLocalIpHint();
        UpdateStatus(TransportState.Idle, TransportType.None);

        try
        {
            await _cm.StartAsync();
        }
        catch (Exception ex)
        {
            txtWaiting.Text = $"Startup error: {ex.Message}";
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _vuTimer.Stop();

        if (_cm is not null)
        {
            await _cm.StopStreamAsync();
            await _cm.DisposeAsync();
            _cm = null;
        }

        _decoder?.Dispose();
        _decoder = null;

        _aacDecoder?.Dispose();
        _aacDecoder = null;

        _audioPlayer?.Dispose();
        _audioPlayer = null;
    }

    // ── Transport events (background thread) ──────────────────────────────────

    private void OnStateChanged(object? sender, TransportState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var type = _cm?.ActiveTransport ?? TransportType.None;
            UpdateStatus(state, type);

            if (state == TransportState.Connected)
            {
                txtDeviceName.Text = _cm?.DeviceName ?? "iPhone";
                txtWaiting.Text    = "Connected — waiting for stream…";
                btnStop.IsEnabled  = true;
                btnMute.IsEnabled  = true;
                pbVolume.IsEnabled = true;
                _ = SendConfigureAndStartAsync();
            }
            else if (state is TransportState.Disconnected or TransportState.Idle)
            {
                txtDeviceName.Text     = "No device";
                pnlNoStream.Visibility = Visibility.Visible;
                imgPreview.Source      = null;
                icCameras.ItemsSource  = null;
                btnStop.IsEnabled      = false;
                btnMute.IsEnabled      = false;
                pbVolume.IsEnabled     = false;
                pbVolume.Value         = 0;
                txtInfo.Text           = string.Empty;
                txtLatency.Text        = string.Empty;
            }
        });
    }

    private void OnMediaFrame(object? sender, MediaFrame frame)
    {
        if (frame.IsVideo)
            _decoder?.Feed(frame);
        else if (frame.IsAudio)
            _aacDecoder?.Decode(frame.Payload, frame.Header.PtsUs);
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
                    IsSelected = c.Id == msg.CurrentCameraId
                })
                .ToList();

            icCameras.ItemsSource = vms;

            // Update device name if ConnectionManager has it
            if (_cm is not null && _cm.DeviceName is { Length: > 0 } name)
                txtDeviceName.Text = name;
        });
    }

    // ── Decoder output (background thread) ────────────────────────────────────

    private void OnFrameDecoded(object? sender, DecodedFrame frame)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (frame.Width <= 1) return;

            pnlNoStream.Visibility = Visibility.Collapsed;

            if (_bitmap is null || _frameWidth != frame.Width || _frameHeight != frame.Height)
            {
                _frameWidth  = frame.Width;
                _frameHeight = frame.Height;
                _bitmap = new WriteableBitmap(
                    frame.Width, frame.Height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null);
                imgPreview.Source = _bitmap;
                txtInfo.Text = $"{frame.Width}×{frame.Height}";
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
        // Only reconfigure when already connected
        if (_cm?.State == TransportState.Connected || _cm?.State == (TransportState)4 /* streaming */)
            _ = SendConfigureAndStartAsync();
    }

    private void OnRotateClicked(object sender, RoutedEventArgs e)
    {
        _rotation = (_rotation + 90) % 360;
        imgRotation.Angle = _rotation;
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        if (_cm is null) return;
        await _cm.StopStreamAsync();
        btnStop.IsEnabled = false;
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
        bool is4K  = rb4K.IsChecked == true;
        string res = is4K ? "3840x2160" : "1920x1080";
        int  rate  = is4K ? 25_000_000  : 8_000_000;
        await _cm.ConfigureAsync(res, 30, "h264", rate);
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
        (ellStatus.Fill, txtStatus.Text) = state switch
        {
            TransportState.Idle         => (Brushes.Gray,       "Idle"),
            TransportState.Listening    => (Brushes.DodgerBlue, "Searching…"),
            TransportState.Connected    => (Brushes.LimeGreen,  "Connected"),
            TransportState.Disconnected => (Brushes.Orange,     "Disconnected"),
            TransportState.Error        => (Brushes.OrangeRed,  "Error"),
            _                           => (Brushes.Gray,       state.ToString()),
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
