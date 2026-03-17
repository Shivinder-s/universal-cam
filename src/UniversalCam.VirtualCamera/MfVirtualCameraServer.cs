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

    /// <summary>
    /// Value of MF_VERSION constant used with MFStartup.
    /// </summary>
    private const uint MF_VERSION = 0x00020070;  // MF_SDK_VERSION for Windows 11

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
            if (!InitializeMediaFoundation())
            {
                Console.WriteLine("[MfVirtualCameraServer] MF initialization deferred (CsWin32 P/Invoke pending)");
            }

            // Step 2: Create and register virtual camera
            if (!CreateVirtualCamera())
            {
                Console.WriteLine("[MfVirtualCameraServer] Virtual camera creation deferred (CsWin32 P/Invoke pending)");
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
    ///
    /// PHASE 2B: Uncomment the actual P/Invoke call when CsWin32 generates stubs.
    /// This method will call MFStartup with the appropriate flags and version.
    /// </summary>
    private bool InitializeMediaFoundation()
    {
        try
        {
            // TODO (Phase 2B - CsWin32 Integration):
            // Uncomment when NativeMethods.txt-generated stubs are available:
            //
            // using Windows.Win32.Media.MediaFoundation;
            //
            // const uint MFSTARTUP_LITE = 0x00000000;
            // HResult hr = MediaFoundation.MFStartup(MF_VERSION, MFSTARTUP_LITE);
            // if (hr < 0)
            // {
            //     Console.WriteLine($"[MfVirtualCameraServer] MFStartup failed: 0x{hr:X}");
            //     return false;
            // }
            //
            // _mfStartupCalled = true;
            // Console.WriteLine("[MfVirtualCameraServer] MFStartup() succeeded");
            // return true;

            // For now, log placeholder
            Console.WriteLine("[MfVirtualCameraServer] MFStartup: CsWin32 integration pending");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MfVirtualCameraServer] MFStartup failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Create and register the virtual camera device.
    ///
    /// PHASE 2B: Uncomment the actual P/Invoke calls when CsWin32 generates stubs.
    /// This method will:
    /// 1. Create an IMFMediaType with NV12 format
    /// 2. Call MFCreateVirtualCamera to register the device
    /// 3. Start the virtual camera
    /// </summary>
    private bool CreateVirtualCamera()
    {
        try
        {
            // TODO (Phase 2B - CsWin32 Integration):
            // Uncomment when CsWin32 provides IMFVirtualCamera, IMFMediaType P/Invoke:
            //
            // using Windows.Win32.Media.MediaFoundation;
            // using Windows.Win32.Media.MediaFoundation.Mpeg2;
            //
            // Step 1: Create and configure media type
            // var mediaType = MediaFoundation.MFCreateMediaType();
            // if (mediaType == null)
            // {
            //     Console.WriteLine("[MfVirtualCameraServer] MFCreateMediaType failed");
            //     return false;
            // }
            //
            // mediaType.SetUINT32(Windows.Win32.Media.MediaFoundation.MF_MT_MAJOR_TYPE,
            //                     (uint)MediaType.Video);
            // mediaType.SetUINT32(Windows.Win32.Media.MediaFoundation.MF_MT_SUBTYPE,
            //                     (uint)Mpeg2VideoFormat.NV12);
            // mediaType.SetUINT32(Windows.Win32.Media.MediaFoundation.MF_MT_FRAME_SIZE,
            //                     ((uint)_mediaSourceWrapper.Width << 16) | (uint)_mediaSourceWrapper.Height);
            // mediaType.SetUINT32(Windows.Win32.Media.MediaFoundation.MF_MT_FRAME_RATE,
            //                     (uint)_mediaSourceWrapper.FramesPerSecond);
            // mediaType.SetUINT32(Windows.Win32.Media.MediaFoundation.MF_MT_MEDIA_TYPE_FLAGS,
            //                     0);
            //
            // Step 2: Create virtual camera
            // var hr = MediaFoundation.MFCreateVirtualCamera(
            //     MFCameraDeviceType.Synth,
            //     0,
            //     "UniversalCam",
            //     (ushort)_mediaSourceWrapper.Width,
            //     (ushort)_mediaSourceWrapper.Height,
            //     mediaType,
            //     _mediaSourceWrapper,
            //     out var vCam);
            //
            // if (hr < 0 || vCam == null)
            // {
            //     Console.WriteLine($"[MfVirtualCameraServer] MFCreateVirtualCamera failed: 0x{hr:X}");
            //     return false;
            // }
            //
            // Step 3: Start the virtual camera
            // hr = vCam.Start();
            // if (hr < 0)
            // {
            //     Console.WriteLine($"[MfVirtualCameraServer] vCam.Start() failed: 0x{hr:X}");
            //     vCam.Dispose();
            //     return false;
            // }
            //
            // _virtualCamera = vCam;
            // Console.WriteLine("[MfVirtualCameraServer] Virtual camera registered and started");
            // return true;

            // For now, log placeholder
            Console.WriteLine("[MfVirtualCameraServer] Virtual camera creation: CsWin32 integration pending");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MfVirtualCameraServer] Virtual camera creation failed: {ex.Message}");
            return false;
        }
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
    ///
    /// PHASE 2B: Signal format change events to consumers once MF is integrated.
    /// </summary>
    public void UpdateMediaType(int width, int height, long fps)
    {
        try
        {
            _mediaSourceWrapper.UpdateMediaType(width, height, fps);
            Console.WriteLine($"[MfVirtualCameraServer] Media type updated: {width}×{height} @ {fps}fps");

            // TODO (Phase 2B): Once MF is integrated, signal format change event:
            // if (_virtualCamera is IMFVirtualCamera vCam)
            // {
            //     // Notify consumers of format change
            //     _mediaSourceWrapper.GetStreamByIndex(0)?.NotifyFormatChanged(width, height, fps);
            // }
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
            // if (_virtualCamera is IMFVirtualCamera vCam)
            // {
            //     vCam.Stop();
            //     vCam.Dispose();
            //     _virtualCamera = null;
            // }
            //
            // if (_mfStartupCalled)
            // {
            //     MediaFoundation.MFShutdown();
            //     _mfStartupCalled = false;
            // }

            _mediaSourceWrapper?.Dispose();
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
