using System;

namespace UniversalCam.VirtualCamera;

/// <summary>
/// Media Foundation media source wrapper for Windows 11 virtual camera.
/// Adapts our FrameBuffer to provide frames in the format expected by IMFMediaSource.
/// This class will eventually implement IMFMediaSource once CsWin32 generates the P/Invoke.
/// </summary>
internal sealed class MfMediaSourceWrapper
{
    private readonly FrameBuffer _frameBuffer;
    private int _width = 1920;
    private int _height = 1080;
    private long _fps = 30;

    public int Width => _width;
    public int Height => _height;
    public long FramesPerSecond => _fps;

    public MfMediaSourceWrapper(FrameBuffer frameBuffer)
    {
        _frameBuffer = frameBuffer ?? throw new ArgumentNullException(nameof(frameBuffer));
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
}
