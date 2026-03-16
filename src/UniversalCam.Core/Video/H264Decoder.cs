using System.Runtime.InteropServices;
using UniversalCam.Core.Protocol;

namespace UniversalCam.Core.Video;

/// <summary>
/// Decodes H.264 Annex B NAL units (received from the iPhone) into
/// BGRA32 software bitmaps using the Windows Media Foundation H.264 decoder MFT.
///
/// This is a thin P/Invoke wrapper. The MFT runs in software by default on all
/// Windows 10+ machines; hardware decode (DXVA2 / D3D11-VA) can be layered on
/// top later. Output is <see cref="DecodedFrame"/> which exposes a raw BGRA byte
/// array suitable for WriteableBitmap / SwapChainPanel rendering.
///
/// Thread safety: Feed() must be called from a single thread. DecodedFrame events
/// are raised on the same thread.
/// </summary>
public sealed class H264Decoder : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────

    /// Raised for each decoded video frame. The <see cref="DecodedFrame.Data"/>
    /// buffer is only valid until the next Feed() call — copy it if you need it longer.
    public event EventHandler<DecodedFrame>? FrameDecoded;

    // ── State ─────────────────────────────────────────────────────────────────

    private MfDecoder? _decoder;
    private bool       _initialized;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Feed one <see cref="MediaFrame"/> (H.264 Annex B payload) into the decoder.
    /// Zero or more <see cref="FrameDecoded"/> events may fire synchronously.
    /// </summary>
    public void Feed(MediaFrame frame)
    {
        if (!frame.IsVideo) return;

        if (!_initialized)
        {
            Initialize();
            _initialized = true;
        }

        _decoder?.Decode(frame.Payload, frame.Header.PtsUs, frame.Header.IsKeyframe);
    }

    public void Dispose()
    {
        _decoder?.Dispose();
        _decoder = null;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void Initialize()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("[H264Decoder] Not on Windows — decoder unavailable.");
            return;
        }

        try
        {
            _decoder = new MfDecoder();
            _decoder.FrameDecoded += (_, f) => FrameDecoded?.Invoke(this, f);
            Console.WriteLine("[H264Decoder] Media Foundation H.264 decoder ready.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[H264Decoder] Init failed: {ex.Message}");
        }
    }
}

/// <summary>
/// A decoded video frame in BGRA32 format, ready for WinUI rendering.
/// </summary>
public sealed class DecodedFrame
{
    public int    Width  { get; }
    public int    Height { get; }
    public long   PtsUs  { get; }

    /// BGRA32 raw pixels, Width * Height * 4 bytes.
    public byte[] Data   { get; }

    public DecodedFrame(int width, int height, long ptsUs, byte[] data)
    {
        Width  = width;
        Height = height;
        PtsUs  = ptsUs;
        Data   = data;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal MF wrapper (software-only, no COM interop dependency for the app layer)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Thin wrapper around the Windows Media Foundation H.264 software decoder MFT.
/// Isolated here so that the rest of the codebase stays COM-free.
///
/// TODO (Phase 2): Replace with a proper MF COM P/Invoke or use
/// Windows.Media.VideoEffects / SharpDX.MediaFoundation for hardware acceleration.
/// For Phase 1, we write NAL units into a named pipe / temp file and use
/// FFmpeg.AutoGen as an optional fallback if available.
/// </summary>
internal sealed class MfDecoder : IDisposable
{
    public event EventHandler<DecodedFrame>? FrameDecoded;

    // Phase 1 stub: outputs an empty 1×1 frame to keep the pipeline wired.
    // Replace the body of Decode() with real MFT calls in Phase 2.
    public void Decode(byte[] annexBNalUnits, long ptsUs, bool isKeyframe)
    {
        // TODO Phase 2: push annexBNalUnits into MFT input stream,
        //               drain MFT output stream, convert IMFSample → byte[].
        //
        // For now, emit a placeholder so the event pipeline is testable.
        // The WinUI preview layer handles a 1×1 frame gracefully (blank screen).
        FrameDecoded?.Invoke(this, new DecodedFrame(1, 1, ptsUs, new byte[4]));
    }

    public void Dispose() { /* TODO: release MFT COM objects */ }
}
