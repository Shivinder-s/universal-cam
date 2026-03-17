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

    public FfmpegDecoder()
    {
        var codec = Codec.FindDecoderById(Sdcb.FFmpeg.Raw.AVCodecID.H264);
        _codecCtx = new CodecContext(codec);
        _codecCtx.Open(codec);
        _yuvFrame = new Frame();
    }

    public unsafe void Decode(byte[] annexBNalUnits, long ptsUs, bool isKeyframe)
    {
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
            if ((int)result < 0) break; // skip other errors silently

            EmitFrame(ptsUs);
            _yuvFrame.Unref();
        }
    }

    private unsafe void EmitFrame(long ptsUs)
    {
        int w = _yuvFrame.Width;
        int h = _yuvFrame.Height;
        if (w <= 0 || h <= 0) return;

        byte[] output = new byte[w * h * 4];

        // YUV420p → BGRA32 in-place (BT.601 full-range coefficients)
        byte* yPlane = (byte*)_yuvFrame.Data[0];
        byte* uPlane = (byte*)_yuvFrame.Data[1];
        byte* vPlane = (byte*)_yuvFrame.Data[2];
        int   yStride = _yuvFrame.Linesize[0];
        int   uStride = _yuvFrame.Linesize[1];

        fixed (byte* dst = output)
        {
            for (int row = 0; row < h; row++)
            {
                int uvRow = row >> 1;
                byte* yRow  = yPlane + row   * yStride;
                byte* uRow  = uPlane + uvRow * uStride;
                byte* vRow  = vPlane + uvRow * uStride;
                byte* dstRow = dst + row * w * 4;

                for (int col = 0; col < w; col++)
                {
                    int Y = yRow[col];
                    int U = uRow[col >> 1] - 128;
                    int V = vRow[col >> 1] - 128;

                    int R = Clamp(Y + (V * 45941 >> 15));
                    int G = Clamp(Y - (U * 11277 >> 15) - (V * 23401 >> 15));
                    int B = Clamp(Y + (U * 58065 >> 15));

                    int o = col * 4;
                    dstRow[o + 0] = (byte)B;
                    dstRow[o + 1] = (byte)G;
                    dstRow[o + 2] = (byte)R;
                    dstRow[o + 3] = 0xFF;
                }
            }
        }

        FrameDecoded?.Invoke(this, new DecodedFrame(w, h, ptsUs, output));
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

    public void Dispose()
    {
        _yuvFrame.Dispose();
        _codecCtx.Dispose();
    }
}
