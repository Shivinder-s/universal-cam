using System;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalCam.VirtualCamera;

/// <summary>
/// Windows 11 22H2+ virtual camera server using Media Foundation.
/// Uses IMFVirtualCamera for immediate device registration without driver signing.
/// </summary>
internal sealed class MfVirtualCameraServer : IVirtualCameraServer
{
    private readonly FrameBuffer _frameBuffer;
    private readonly UCamMediaSource _mediaSource;
    private object? _virtualCamera;  // Untyped for now; will be IMFVirtualCamera once CsWin32 is ready
    private bool _isRunning;
    private bool _disposed;
    private Task? _frameDeliveryTask;
    private CancellationTokenSource? _cts;

    public MfVirtualCameraServer(FrameBuffer frameBuffer)
    {
        _frameBuffer = frameBuffer ?? throw new ArgumentNullException(nameof(frameBuffer));
        _mediaSource = new UCamMediaSource(_frameBuffer);
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            // TODO (Phase 2B): Once CsWin32 generates the P/Invoke stubs:
            // 1. Call MFStartup() to initialize Media Foundation
            // 2. Create an IMFMediaType with NV12 + 1920x1080 @ 30fps
            // 3. Create UCamMediaSource as IMFMediaSource
            // 4. Call MFCreateVirtualCamera with source and register device
            // 5. Call _virtualCamera.Start() to begin publishing frames

            // For now, log that we're ready and start the frame delivery loop
            Console.WriteLine("[MfVirtualCameraServer] Initialized (Windows 11 22H2+)");
            Console.WriteLine("[MfVirtualCameraServer] Note: IMFVirtualCamera integration pending CsWin32 P/Invoke generation");

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _frameDeliveryTask = FrameDeliveryLoop(_cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MfVirtualCameraServer] Initialization failed: {ex.Message}");
            _isRunning = false;
        }
    }

    /// <summary>
    /// Simulates frame delivery until the real IMFMediaSource polling is implemented.
    /// </summary>
    private async Task FrameDeliveryLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                var frame = _mediaSource.GetNextFrame();
                if (frame != null)
                {
                    Console.WriteLine($"[MfVirtualCameraServer] Delivering frame: {frame.Width}×{frame.Height}");
                }

                // Poll every 33ms (~30fps)
                await Task.Delay(33, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when _cts is disposed
        }
    }

    /// <summary>
    /// Updates the virtual camera media type when resolution/fps changes.
    /// </summary>
    public void UpdateMediaType(int width, int height, long fps)
    {
        _mediaSource.UpdateMediaType(width, height, fps);
        // TODO: Once MF is fully integrated, trigger IMFMediaEventGenerator.MediaSample or FormatChanged event
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _isRunning = false;
            _cts?.Cancel();

            if (_frameDeliveryTask != null)
            {
                _frameDeliveryTask.Wait(TimeSpan.FromSeconds(1));
            }

            // TODO: Once MF is integrated, call _virtualCamera.Stop() and _virtualCamera.Remove()

            Console.WriteLine("[MfVirtualCameraServer] Disposed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MfVirtualCameraServer] Dispose error: {ex.Message}");
        }
        finally
        {
            _cts?.Dispose();
            _disposed = true;
        }
    }
}
