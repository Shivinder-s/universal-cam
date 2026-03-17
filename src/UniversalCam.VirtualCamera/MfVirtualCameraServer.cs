using System;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalCam.VirtualCamera;

/// <summary>
/// Windows 11 22H2+ virtual camera server using Media Foundation.
/// Uses IMFVirtualCamera for immediate device registration without driver signing.
/// This implementation is structured to integrate with CsWin32-generated P/Invoke stubs.
/// </summary>
internal sealed class MfVirtualCameraServer : IVirtualCameraServer
{
    private readonly FrameBuffer _frameBuffer;
    private readonly MfMediaSourceWrapper _mediaSourceWrapper;
    private object? _virtualCamera;  // IMFVirtualCamera once CsWin32 P/Invoke is available
#pragma warning disable CS0169 // Field used in Phase 2B when CsWin32 generates P/Invoke
#pragma warning restore CS0169
    private bool _isRunning;
#pragma warning disable CS0414 // Field used in Phase 2B when CsWin32 generates P/Invoke
    private bool _mfStartupCalled;
#pragma warning restore CS0414
    private bool _disposed;
    private Task? _frameDeliveryTask;
    private CancellationTokenSource? _cts;

    public MfVirtualCameraServer(FrameBuffer frameBuffer)
    {
        _frameBuffer = frameBuffer ?? throw new ArgumentNullException(nameof(frameBuffer));
        _mediaSourceWrapper = new MfMediaSourceWrapper(_frameBuffer);
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            Console.WriteLine("[MfVirtualCameraServer] Initializing (Windows 11 22H2+)");

            // Step 1: Initialize Media Foundation
            try
            {
                InitializeMediaFoundation();
                _mfStartupCalled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MfVirtualCameraServer] MF init failed (CsWin32 integration pending): {ex.Message}");
                // Continue with simulation mode for now
            }

            // Step 2: Create and register virtual camera
            try
            {
                CreateVirtualCamera();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MfVirtualCameraServer] Virtual camera creation failed: {ex.Message}");
                // Fall back to frame simulation
            }

            // Step 3: Start async frame delivery loop
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _frameDeliveryTask = FrameDeliveryLoop(_cts.Token);

            Console.WriteLine("[MfVirtualCameraServer] Initialization complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MfVirtualCameraServer] Initialization failed: {ex.Message}");
            _isRunning = false;
        }
    }

    /// <summary>
    /// Initialize Media Foundation runtime.
    /// Once CsWin32 generates P/Invoke for MFStartup, uncomment the real call.
    /// </summary>
    private void InitializeMediaFoundation()
    {
        // TODO (Phase 2B - CsWin32 Integration):
        // Uncomment when NativeMethods.txt-generated stubs are available:
        //
        // const uint MFSTARTUP_LITE = 0x00000000;
        // HResult hr = Windows.Win32.Media.MediaFoundation.MFStartup(
        //     Windows.Win32.Media.MediaFoundation.MF_VERSION,
        //     MFSTARTUP_LITE);
        // if (hr < 0)
        //     throw new InvalidOperationException($"MFStartup failed: 0x{hr:X}");
        //
        Console.WriteLine("[MfVirtualCameraServer] MFStartup: placeholder (CsWin32 integration pending)");
    }

    /// <summary>
    /// Create and register the virtual camera device.
    /// TODO: Integrate with IMFVirtualCamera once CsWin32 P/Invoke is available.
    /// </summary>
    private void CreateVirtualCamera()
    {
        // TODO (Phase 2B - CsWin32 Integration):
        // Uncomment and integrate when CsWin32 provides IMFVirtualCamera P/Invoke:
        //
        // var mediaType = Windows.Win32.Media.MediaFoundation.MFCreateMediaType();
        // mediaType.SetUINT32(MF_MT_FRAME_RATE, 30);
        // mediaType.SetUINT32(MF_MT_FRAME_SIZE, (uint)(_mediaSourceWrapper.Width << 16 | _mediaSourceWrapper.Height));
        // mediaType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
        //
        // _virtualCamera = Windows.Win32.Media.MediaFoundation.MFCreateVirtualCamera(
        //     Windows.Win32.Media.MediaFoundation.MFCameraDeviceType.MF_CAMERA_TYPE_SYNTHETIC,
        //     "UniversalCam",
        //     _mediaSourceWrapper);
        //
        // ((IMFVirtualCamera)_virtualCamera).Start();
        //
        Console.WriteLine("[MfVirtualCameraServer] Virtual camera creation: placeholder (CsWin32 integration pending)");
    }

    /// <summary>
    /// Delivers frames from the frame buffer.
    /// Runs async at 30fps while the server is active.
    /// </summary>
    private async Task FrameDeliveryLoop(CancellationToken ct)
    {
        int frameCount = 0;
        try
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                var frame = _mediaSourceWrapper.GetNextFrame();
                if (frame != null)
                {
                    frameCount++;
                    if (frameCount % 30 == 0)  // Log every 30 frames (~1 second)
                    {
                        Console.WriteLine($"[MfVirtualCameraServer] Frame delivery active: {frame.Width}×{frame.Height}");
                    }
                }

                // Poll every 33ms (~30fps)
                await Task.Delay(33, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        finally
        {
            Console.WriteLine($"[MfVirtualCameraServer] Frame delivery loop ended ({frameCount} frames processed)");
        }
    }

    /// <summary>
    /// Updates the virtual camera media type when resolution/fps changes.
    /// Propagates changes to the media source wrapper.
    /// </summary>
    public void UpdateMediaType(int width, int height, long fps)
    {
        try
        {
            _mediaSourceWrapper.UpdateMediaType(width, height, fps);
            Console.WriteLine($"[MfVirtualCameraServer] Media type updated: {width}×{height} @ {fps}fps");

            // TODO (Phase 2B): Once MF is integrated, signal format change event:
            // _virtualCamera?.NotifyFormatChange(...);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MfVirtualCameraServer] UpdateMediaType error: {ex.Message}");
        }
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
                _frameDeliveryTask.Wait(TimeSpan.FromSeconds(2));
            }

            // TODO (Phase 2B): Clean up MF resources:
            // if (_virtualCamera != null)
            // {
            //     ((IMFVirtualCamera)_virtualCamera).Stop();
            //     ((IMFVirtualCamera)_virtualCamera).Remove();
            // }
            // if (_mfStartupCalled)
            //     Windows.Win32.Media.MediaFoundation.MFShutdown();

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
