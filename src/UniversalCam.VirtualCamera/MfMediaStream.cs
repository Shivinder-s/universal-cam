using System;
using System.Collections.Generic;

namespace UniversalCam.VirtualCamera;

/// <summary>
/// Media Foundation media stream implementation for virtual camera.
/// Implements IMFMediaStream to deliver NV12 video frames to Media Foundation.
///
/// PHASE 2B NOTE: Once CsWin32 generates Windows.Win32.Media.MediaFoundation stubs,
/// update the type signatures below to use the actual COM interfaces.
/// </summary>
internal sealed class MfMediaStream
{
    private readonly MfMediaSourceWrapper _mediaSource;
    private readonly Queue<object> _eventQueue = new();
    private object? _mediaEventGenerator;  // IMFMediaEventGenerator once CsWin32 P/Invoke available
    private int _streamId;
    private bool _isActive = true;
    private bool _disposed;

    public MfMediaStream(int streamId, MfMediaSourceWrapper mediaSource)
    {
        _streamId = streamId;
        _mediaSource = mediaSource ?? throw new ArgumentNullException(nameof(mediaSource));
    }

    /// <summary>
    /// Gets the next media sample from the frame buffer.
    /// In real implementation, wraps NV12Frame in IMFSample.
    /// </summary>
    public NV12Frame? RequestSample()
    {
        if (!_isActive || _disposed)
            return null;

        var frame = _mediaSource.GetNextFrame();
        if (frame != null)
        {
            // TODO (Phase 2B): Once CsWin32 generates IMFSample P/Invoke:
            // 1. Create IMFSample via MFCreateSample()
            // 2. Create IMFMediaBuffer via MFCreateMemoryBuffer(frame.Data.Length)
            // 3. Copy NV12 data into buffer
            // 4. Set buffer length
            // 5. Add buffer to sample
            // 6. Set sample timestamp (frame.PtsUs * 10)
            // 7. Return sample
            //
            // Example (pseudocode once P/Invoke ready):
            // var sample = MFCreateSample();
            // var buffer = MFCreateMemoryBuffer((uint)frame.Data.Length);
            // buffer.Lock(out var ptr, out var maxLen, out var curLen);
            // Marshal.Copy(frame.Data, 0, ptr, frame.Data.Length);
            // buffer.Unlock();
            // buffer.SetCurrentLength((uint)frame.Data.Length);
            // sample.AddBuffer(buffer);
            // sample.SetSampleTime(frame.PtsUs * 10);
            // sample.SetSampleDuration(333333);  // 30fps = 333ms / 10000
            // return sample;
        }

        return null;
    }

    /// <summary>
    /// Called when Media Foundation requests format change notification.
    /// Signals MEStreamFormatChanged event to consumers.
    /// </summary>
    public void NotifyFormatChanged(int width, int height, long fps)
    {
        if (_disposed)
            return;

        // TODO (Phase 2B): Once CsWin32 generates IMFMediaEventGenerator P/Invoke:
        // 1. Create media type with new dimensions and fps
        // 2. Queue MEStreamFormatChanged event with media type
        // 3. Fire event via _mediaEventGenerator
        //
        // Example (pseudocode):
        // var mediaType = MFCreateMediaType();
        // mediaType.SetUINT32(MF_MT_FRAME_WIDTH, (uint)width);
        // mediaType.SetUINT32(MF_MT_FRAME_HEIGHT, (uint)height);
        // mediaType.SetUINT32(MF_MT_FRAME_RATE, (uint)fps);
        // mediaType.SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
        //
        // _mediaEventGenerator.QueueEvent(
        //     MEStreamFormatChanged,
        //     GUID_NULL,
        //     S_OK,
        //     mediaType);

        Console.WriteLine($"[MfMediaStream] Format changed: {width}×{height} @ {fps}fps");
    }

    /// <summary>
    /// Pauses stream (stops delivering samples).
    /// </summary>
    public void Pause()
    {
        _isActive = false;
        Console.WriteLine($"[MfMediaStream] Paused");
    }

    /// <summary>
    /// Resumes stream (resumes sample delivery).
    /// </summary>
    public void Start()
    {
        if (_disposed)
            return;

        _isActive = true;
        Console.WriteLine($"[MfMediaStream] Started");
    }

    /// <summary>
    /// Stops stream and cleans up resources.
    /// </summary>
    public void Stop()
    {
        _isActive = false;
        Console.WriteLine($"[MfMediaStream] Stopped");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // TODO (Phase 2B): Release COM references
        // _mediaEventGenerator = null;

        _disposed = true;
    }
}

/// <summary>
/// Helper class for queuing Media Foundation events.
/// Placeholder for event management once IMFMediaEventGenerator is available.
/// </summary>
internal sealed class MfMediaEvent
{
    public enum MediaEventType
    {
        // Standard MF event types
        MEStreamFormatChanged = 6,    // Stream format changed
        MEStreamStopped = 7,           // Stream stopped
        MEStreamStarted = 8,           // Stream started
    }

    public MediaEventType EventType { get; set; }
    public object? EventData { get; set; }
}
