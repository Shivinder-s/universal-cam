using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniversalCam.Core;
using UniversalCam.Core.Protocol;
using UniversalCam.Core.Transport;
using UniversalCam.Core.Video;

namespace UniversalCam.Views;

public partial class MainWindow : Window
{
    private ConnectionManager? _cm;
    private H264Decoder?       _decoder;

    private int              _frameWidth;
    private int              _frameHeight;
    private WriteableBitmap? _bitmap;

    public MainWindow()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Closed   += OnClosed;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _decoder = new H264Decoder();
        _decoder.FrameDecoded += OnFrameDecoded;

        _cm = new ConnectionManager();
        _cm.StateChanged     += OnStateChanged;
        _cm.FrameReceived    += OnMediaFrame;
        _cm.CamerasReceived  += OnCamerasReceived;

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
        if (_cm is not null)
        {
            await _cm.StopStreamAsync();
            await _cm.DisposeAsync();
            _cm = null;
        }
        _decoder?.Dispose();
        _decoder = null;
    }

    // ── Transport events (background thread) ─────────────────────────────────

    private void OnStateChanged(object? sender, TransportState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var type = _cm?.ActiveTransport ?? TransportType.None;
            UpdateStatus(state, type);

            if (state == TransportState.Connected)
            {
                txtWaiting.Text   = "Connected — waiting for stream…";
                btnStop.IsEnabled = true;
                _ = SendConfigureAndStartAsync();
            }
            else if (state is TransportState.Disconnected or TransportState.Idle)
            {
                pnlNoStream.Visibility = Visibility.Visible;
                imgPreview.Source      = null;
                btnFlip.IsEnabled      = false;
                btnStop.IsEnabled      = false;
                txtInfo.Text           = string.Empty;
            }
        });
    }

    private void OnMediaFrame(object? sender, MediaFrame frame)
    {
        if (frame.IsVideo)
            _decoder?.Feed(frame);
        // Audio: TODO route to WASAPI output
    }

    private void OnCamerasReceived(object? sender, AvailableCameras msg)
    {
        Dispatcher.BeginInvoke(() =>
        {
            btnFlip.IsEnabled = msg.Cameras.Count > 1;
        });
    }

    // ── Decoder output (background thread) ────────────────────────────────────

    private void OnFrameDecoded(object? sender, DecodedFrame frame)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (frame.Width <= 1) return; // Phase 1 stub frame

            pnlNoStream.Visibility = Visibility.Collapsed;

            // Re-create bitmap only when resolution changes
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

            // Write BGRA pixels directly into the back buffer
            _bitmap.Lock();
            Marshal.Copy(frame.Data, 0, _bitmap.BackBuffer, frame.Data.Length);
            _bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, frame.Width, frame.Height));
            _bitmap.Unlock();
        });
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private async void OnFlipClicked(object sender, RoutedEventArgs e)
    {
        if (_cm is null) return;
        await _cm.ListCamerasAsync();
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        if (_cm is null) return;
        await _cm.StopStreamAsync();
        btnStop.IsEnabled = false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SendConfigureAndStartAsync()
    {
        if (_cm is null) return;
        await _cm.ConfigureAsync("1080p", 30, "h264", 8_000_000);
    }

    private void UpdateStatus(TransportState state, TransportType type)
    {
        (ellStatus.Fill, txtStatus.Text) = state switch
        {
            TransportState.Idle         => (Brushes.Gray,        "Idle"),
            TransportState.Listening    => (Brushes.DodgerBlue,  "Waiting for iPhone…"),
            TransportState.Connected    => (Brushes.LimeGreen,   "Connected"),
            TransportState.Disconnected => (Brushes.Orange,      "Disconnected"),
            TransportState.Error        => (Brushes.OrangeRed,   "Error"),
            _                           => (Brushes.Gray,        state.ToString()),
        };

        if (type == TransportType.None)
        {
            badgeTransport.Visibility = Visibility.Collapsed;
        }
        else
        {
            badgeTransport.Visibility  = Visibility.Visible;
            badgeTransport.Background  = type == TransportType.USB
                ? (Brush)Application.Current.Resources["UsbBadgeBrush"]
                : (Brush)Application.Current.Resources["WifiBadgeBrush"];
            txtTransport.Text = type.ToString();
        }
    }
}
