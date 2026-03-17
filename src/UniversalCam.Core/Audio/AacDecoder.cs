using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Utils;

namespace UniversalCam.Core.Audio;

/// <summary>
/// Decodes raw AAC-LC frames (received from the iPhone) to S16 interleaved PCM
/// using FFmpeg libavcodec.
///
/// Input:  raw AAC-LC bytes at 44100 Hz stereo 128 kbps (~1024 samples/frame)
/// Output: <see cref="PcmFrame"/> with S16 interleaved PCM via <see cref="PcmDecoded"/>
///
/// Thread safety: Decode() must be called from a single thread.
/// </summary>
public sealed class AacDecoder : IDisposable
{
    public event EventHandler<PcmFrame>? PcmDecoded;

    private readonly CodecContext _codecCtx;
    private readonly Frame        _pcmFrame;

    public AacDecoder()
    {
        var codec = Codec.FindDecoderById(AVCodecID.Aac);
        _codecCtx = new CodecContext(codec);
        _codecCtx.Open(codec);
        _pcmFrame = new Frame();
    }

    public unsafe void Decode(byte[] aacData, long ptsUs)
    {
        using var packet = new Packet();
        AVPacket* raw = packet;

        if (ffmpeg.av_new_packet(raw, aacData.Length) < 0) return;
        Marshal.Copy(aacData, 0, (nint)raw->data, aacData.Length);
        raw->pts = ptsUs;

        try { _codecCtx.SendPacket(packet); }
        catch { return; }

        while (true)
        {
            var result = _codecCtx.ReceiveFrame(_pcmFrame);
            if (result == CodecResult.Again || result == CodecResult.EOF) break;
            if ((int)result < 0) break;

            EmitPcm(ptsUs);
            _pcmFrame.Unref();
        }
    }

    /// <summary>
    /// Convert FLTP (float planar, the native AAC decoder output format) to
    /// S16 interleaved stereo — pure C# unsafe, no swresample dependency.
    /// </summary>
    private unsafe void EmitPcm(long ptsUs)
    {
        int sampleRate = _pcmFrame.SampleRate;
        int nbSamples  = _pcmFrame.NbSamples;
        if (sampleRate <= 0 || nbSamples <= 0) return;

        // FLTP: each channel in its own plane as float[nbSamples]
        float* left  = (float*)_pcmFrame.Data[0];
        float* right = (float*)_pcmFrame.Data[1];

        // S16 interleaved: [L0 R0 L1 R1 …] — 2 channels × 2 bytes × nbSamples
        byte[] output = new byte[nbSamples * 2 * 2];

        fixed (byte* dst = output)
        {
            short* s = (short*)dst;
            for (int i = 0; i < nbSamples; i++)
            {
                s[i * 2 + 0] = FloatToS16(left[i]);
                s[i * 2 + 1] = FloatToS16(right[i]);
            }
        }

        PcmDecoded?.Invoke(this, new PcmFrame(output, sampleRate, 2, ptsUs));
    }

    private static short FloatToS16(float f)
    {
        int v = (int)(f * 32767f);
        return (short)(v < -32768 ? -32768 : v > 32767 ? 32767 : v);
    }

    public void Dispose()
    {
        _pcmFrame.Dispose();
        _codecCtx.Dispose();
    }
}

/// <summary>S16 interleaved PCM audio ready for WASAPI playback.</summary>
public sealed class PcmFrame
{
    /// S16 little-endian interleaved: [L0 R0 L1 R1 …]
    public byte[] Data     { get; }
    public int    Rate     { get; }
    public int    Channels { get; }
    public long   PtsUs    { get; }

    public PcmFrame(byte[] data, int rate, int channels, long ptsUs)
    {
        Data     = data;
        Rate     = rate;
        Channels = channels;
        PtsUs    = ptsUs;
    }
}
