using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;
using UniversalCam.Core.Protocol;

namespace UniversalCam.Core.Video;

/// <summary>
/// Decodes H.264 Annex B NAL units (received from the iPhone) into BGRA32 frames
/// using FFmpeg (Sdcb.FFmpeg) via the libavcodec H.264 software decoder.
///
/// Thread safety: Feed() must be called from a single thread.
/// FrameDecoded events fire synchronously on that thread.
/// </summary>
public sealed class H264Decoder : IDisposable
{
    public event EventHandler<DecodedFrame>? FrameDecoded;

    private FfmpegDecoder? _decoder;
    private bool           _initialized;

    /// <summary>
    /// Feed one H.264 Annex B <see cref="MediaFrame"/>. Zero or more
    /// <see cref="FrameDecoded"/> events may fire synchronously.
    /// </summary>
    public void Feed(MediaFrame frame)
    {
        if (!frame.IsVideo) return;

        if (!_initialized)
        {
            try
            {
                _decoder = new FfmpegDecoder();
                _decoder.FrameDecoded += (_, f) => FrameDecoded?.Invoke(this, f);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[H264Decoder] Init failed: {ex.Message}");
            }
            _initialized = true;
        }

        _decoder?.Decode(frame.Payload, frame.Header.PtsUs, frame.Header.IsKeyframe);
    }

    /// <summary>Flush decoder state on camera switch; waits for next keyframe before emitting frames.</summary>
    public void Reset() => _decoder?.Reset();

    public void Dispose()
    {
        _decoder?.Dispose();
        _decoder = null;
    }
}

/// <summary>A decoded video frame in BGRA32 format, ready for WPF WriteableBitmap rendering.</summary>
public sealed class DecodedFrame
{
    public int    Width  { get; }
    public int    Height { get; }
    public long   PtsUs  { get; }

    /// BGRA32 pixels: Width * Height * 4 bytes.
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
// FFmpeg H.264 decoder (libavcodec via Sdcb.FFmpeg)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class FfmpegDecoder : IDisposable
{
    public event EventHandler<DecodedFrame>? FrameDecoded;

    private readonly CodecContext _codecCtx;
    private readonly Frame        _yuvFrame;

    private nint _swsCtx;            // SwsContext* stored as nint (0 = null)
    private int  _swsW, _swsH;
    private int  _swsFmt   = -1;
    private int  _swsDispW;          // display width after SAR correction
    private bool _waitingForKeyframe = true; // drop P-frames until a keyframe arrives
    private int  _consecutiveErrors  = 0;

    /// <summary>
    /// Called when the iOS camera changes. Flushes the FFmpeg decoder state so stale
    /// reference frames from the previous session don't corrupt the new stream.
    /// </summary>
    public unsafe void Reset()
    {
        _waitingForKeyframe = true;
        _consecutiveErrors  = 0;
        ffmpeg.avcodec_flush_buffers(_codecCtx);
    }

    public FfmpegDecoder()
    {
        var codec = Codec.FindDecoderById(Sdcb.FFmpeg.Raw.AVCodecID.H264);
        _codecCtx = new CodecContext(codec);
        _codecCtx.Open(codec);
        _yuvFrame = new Frame();
    }

    public unsafe void Decode(byte[] annexBNalUnits, long ptsUs, bool isKeyframe)
    {
        // Don't feed P-frames before the first keyframe — they'll decode to garbage
        // and can crash sws_scale via null plane pointers.
        if (_waitingForKeyframe)
        {
            if (!isKeyframe) return;
            _waitingForKeyframe = false;
        }

        using var packet = new Packet();
        AVPacket* raw = packet;

        // Allocate an FFmpeg-managed buffer so av_packet_free can safely release it.
        if (ffmpeg.av_new_packet(raw, annexBNalUnits.Length) < 0) return;
        Marshal.Copy(annexBNalUnits, 0, (nint)raw->data, annexBNalUnits.Length);
        raw->pts = ptsUs;

        try { _codecCtx.SendPacket(packet); }
        catch { return; } // skip corrupt/invalid input silently

        while (true)
        {
            var result = _codecCtx.ReceiveFrame(_yuvFrame);
            if (result == CodecResult.Again || result == CodecResult.EOF) break;
            if ((int)result < 0)
            {
                // Too many consecutive decode errors → flush and wait for next keyframe.
                // This prevents the decoder from producing garbage frames with invalid
                // strides or data pointers that crash sws_scale.
                if (++_consecutiveErrors >= 10)
                {
                    ffmpeg.avcodec_flush_buffers(_codecCtx);
                    _waitingForKeyframe = true;
                    _consecutiveErrors  = 0;
                }
                break;
            }
            _consecutiveErrors = 0;
            EmitFrame(ptsUs);
            _yuvFrame.Unref();
        }
    }

    private unsafe void EmitFrame(long ptsUs)
    {
        int w   = _yuvFrame.Width;
        int h   = _yuvFrame.Height;
        int fmt = _yuvFrame.Format;
        if (w <= 0 || h <= 0) return;

        // Apply SAR correction: H.264 VUI may declare non-square pixels.
        // sws_getContext does NOT auto-correct SAR when src/dst dims are identical,
        // so compute the display width explicitly and scale the output accordingly.
        AVRational sar = _yuvFrame.SampleAspectRatio;
        int dispW = (sar.Num > 0 && sar.Den > 0 && sar.Num != sar.Den)
            ? (int)Math.Round((double)w * sar.Num / sar.Den)
            : w;

        // Lazily create/recreate sws context on dimension or format change.
        // sws_scale handles BT.709 color matrix for HD content.
        if (_swsCtx == 0 || _swsW != w || _swsH != h || _swsFmt != fmt)
        {
            if (_swsCtx != 0) ffmpeg.sws_freeContext((SwsContext*)_swsCtx);
            var ctx = ffmpeg.sws_getContext(
                w, h, (AVPixelFormat)fmt,
                dispW, h, AVPixelFormat.Bgra,
                (int)SWS.Bilinear, null, null, null);
            _swsCtx   = (nint)ctx;
            _swsW     = w;
            _swsH     = h;
            _swsFmt   = fmt;
            _swsDispW = dispW;
        }

        // Guard against corrupted frames — sws_scale will SIGSEGV if any of these are bad.
        if (_swsCtx == 0) return;
        if ((byte*)_yuvFrame.Data[0] == null) return;
        if (_yuvFrame.Linesize[0] <= 0) return; // invalid stride → garbage/crash

        byte[] output = new byte[_swsDispW * h * 4];

        // Sdcb.FFmpeg's sws_scale overload takes byte*[] / int[] (managed arrays).
        // P/Invoke pins managed arrays for the duration of the native call, so GC
        // cannot move them while sws_scale is writing — no CLR corruption risk.
        fixed (byte* dst = output)
        {
            var srcSlice  = new byte*[8];
            var srcStride = new int[8];
            srcSlice[0]  = (byte*)_yuvFrame.Data[0];
            srcSlice[1]  = (byte*)_yuvFrame.Data[1];
            srcSlice[2]  = (byte*)_yuvFrame.Data[2];
            srcStride[0] = _yuvFrame.Linesize[0];
            srcStride[1] = _yuvFrame.Linesize[1];
            srcStride[2] = _yuvFrame.Linesize[2];

            var dstSlice  = new byte*[8];
            var dstStride = new int[8];
            dstSlice[0]  = dst;
            dstStride[0] = _swsDispW * 4;

            ffmpeg.sws_scale((SwsContext*)_swsCtx, srcSlice, srcStride, 0, h, dstSlice, dstStride);
        }

        FrameDecoded?.Invoke(this, new DecodedFrame(_swsDispW, h, ptsUs, output));
    }

    public unsafe void Dispose()
    {
        if (_swsCtx != 0) { ffmpeg.sws_freeContext((SwsContext*)_swsCtx); _swsCtx = 0; }
        _yuvFrame.Dispose();
        _codecCtx.Dispose();
    }
}
