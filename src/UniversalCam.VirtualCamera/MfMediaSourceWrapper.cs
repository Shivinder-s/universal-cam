using System;

namespace UniversalCam.VirtualCamera;

/// <summary>
/// Media Foundation media source wrapper for Windows 11 virtual camera.
/// Adapts our FrameBuffer to provide frames in the format expected by IMFMediaSource.
/// This class will implement IMFMediaSource once CsWin32 generates the P/Invoke.
///
/// PHASE 2B: Uncomment the interface implementation and implement the methods below
/// once CsWin32 generates Windows.Win32.Media.MediaFoundation.IMFMediaSource P/Invoke.
/// </summary>
internal sealed class MfMediaSourceWrapper
{
    private readonly FrameBuffer _frameBuffer;
    private readonly MfMediaStream _mediaStream;
    private int _width = 1920;
    private int _height = 1080;
    private long _fps = 30;
    private bool _disposed;

    public int Width => _width;
    public int Height => _height;
    public long FramesPerSecond => _fps;

    public MfMediaSourceWrapper(FrameBuffer frameBuffer)
    {
        _frameBuffer = frameBuffer ?? throw new ArgumentNullException(nameof(frameBuffer));
        _mediaStream = new MfMediaStream(0, this);
    }

    /// <summary>
    /// Updates the expected frame resolution and frame rate.
    /// Called when the iPhone sends a configure message.
    /// </summary>
    public void UpdateMediaType(int width, int height, long fps)
    {
        _width = width;
        _height = height;
        _fps = fps;
        Console.WriteLine($"[MfMediaSourceWrapper] Media type updated: {width}×{height} @ {fps}fps");

        // Notify stream of format change
        _mediaStream?.NotifyFormatChanged(width, height, fps);
    }

    /// <summary>
    /// Gets the next frame from the buffer, or returns null if unavailable.
    /// </summary>
    public NV12Frame? GetNextFrame()
    {
        if (_frameBuffer.TryDequeue(out var frame))
            return frame;
        return null;
    }

    /// <summary>
    /// Clears all pending frames from the buffer.
    /// Called on disconnect or media type change.
    /// </summary>
    public void ClearFrames()
    {
        _frameBuffer.Clear();
    }

    #region IMFMediaSource Method Stubs (Phase 2B)
    // PHASE 2B (CsWin32 Integration):
    // Uncomment interface implementation below once CsWin32 generates P/Invoke stubs
    // from NativeMethods.txt. Then implement the following methods:

    /// <summary>
    /// Gets the media source characteristics.
    /// Returns: MFMEDIASOURCE_CAN_SEEK | MFMEDIASOURCE_CAN_PAUSE
    /// </summary>
    public void GetCharacteristics()
    {
        // TODO: Return uint with capability flags once P/Invoke available
        // const uint MFMEDIASOURCE_CAN_SEEK = 0x00000001;
        // const uint MFMEDIASOURCE_CAN_PAUSE = 0x00000002;
        // const uint MFMEDIASOURCE_IS_LIVE = 0x00000004;
        // return MFMEDIASOURCE_IS_LIVE;  // Live source, no seeking
    }

    /// <summary>
    /// Gets source-level attributes (empty for now).
    /// </summary>
    public void GetSourceAttributes()
    {
        // TODO: Return IMFAttributes collection once P/Invoke available
        // Create a new attributes object and return it
    }

    /// <summary>
    /// Gets the number of streams (always 1 for video capture).
    /// </summary>
    public int GetStreamCount()
    {
        return 1;
    }

    /// <summary>
    /// Gets the media stream by index.
    /// </summary>
    public MfMediaStream? GetStreamByIndex(int index)
    {
        if (index == 0)
            return _mediaStream;
        return null;
    }

    /// <summary>
    /// Called when Media Foundation starts playback.
    /// </summary>
    public void Start()
    {
        _mediaStream?.Start();
        Console.WriteLine("[MfMediaSourceWrapper] Started");
    }

    /// <summary>
    /// Called when Media Foundation pauses playback.
    /// </summary>
    public void Pause()
    {
        _mediaStream?.Pause();
        Console.WriteLine("[MfMediaSourceWrapper] Paused");
    }

    /// <summary>
    /// Called when Media Foundation stops playback.
    /// </summary>
    public void Stop()
    {
        _mediaStream?.Stop();
        Console.WriteLine("[MfMediaSourceWrapper] Stopped");
    }
    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _mediaStream?.Dispose();
        _frameBuffer.Clear();
        _disposed = true;
    }
}
