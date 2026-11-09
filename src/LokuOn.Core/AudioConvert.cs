using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace LokuOn.Core;

/// <summary>PCMバイト列 → float 変換。</summary>
public static class PcmConverter
{
    /// <summary>変換して dst に書き、サンプル数(チャンネル込み)を返す。</summary>
    public static int ToFloat(byte[] src, int bytes, int bitsPerSample, bool isFloat, float[] dst)
    {
        if (isFloat && bitsPerSample == 32)
        {
            int n = bytes / 4;
            Buffer.BlockCopy(src, 0, dst, 0, n * 4);
            return n;
        }
        if (isFloat && bitsPerSample == 64)
        {
            int n = bytes / 8;
            for (int i = 0; i < n; i++) dst[i] = (float)BitConverter.ToDouble(src, i * 8);
            return n;
        }
        switch (bitsPerSample)
        {
            case 16:
            {
                int n = bytes / 2;
                if (BitConverter.IsLittleEndian)
                {
                    // 1サンプルごとに Span を作らず、まとめて読む(結果は同じ。録音中ずっと動く部分なので軽くする)
                    var s16 = MemoryMarshal.Cast<byte, short>(src.AsSpan(0, n * 2));
                    for (int i = 0; i < s16.Length; i++) dst[i] = s16[i] / 32768f;
                    return n;
                }
                for (int i = 0; i < n; i++) dst[i] = BinaryPrimitives.ReadInt16LittleEndian(src.AsSpan(i * 2)) / 32768f;
                return n;
            }
            case 24:
            {
                int n = bytes / 3;
                for (int i = 0; i < n; i++)
                {
                    int o = i * 3;
                    int v = src[o] | (src[o + 1] << 8) | ((sbyte)src[o + 2] << 16);
                    dst[i] = v / 8388608f;
                }
                return n;
            }
            case 32:
            {
                int n = bytes / 4;
                if (BitConverter.IsLittleEndian)
                {
                    var s32 = MemoryMarshal.Cast<byte, int>(src.AsSpan(0, n * 4));
                    for (int i = 0; i < s32.Length; i++) dst[i] = s32[i] / 2147483648f;
                    return n;
                }
                for (int i = 0; i < n; i++) dst[i] = BinaryPrimitives.ReadInt32LittleEndian(src.AsSpan(i * 4)) / 2147483648f;
                return n;
            }
            case 8:
            {
                for (int i = 0; i < bytes; i++) dst[i] = (src[i] - 128) / 128f;
                return bytes;
            }
            default:
                throw new NotSupportedException($"未対応のビット深度: {bitsPerSample}bit");
        }
    }
}

/// <summary>チャンネル数の変換(1ch/2ch/5.1/7.1 → 1ch または 2ch)。</summary>
public static class ChannelMapper
{
    public static void Convert(float[] src, int frames, int inCh, int outCh, float[] dst)
    {
        if (inCh == outCh) { Array.Copy(src, dst, frames * inCh); return; }
        for (int f = 0; f < frames; f++)
        {
            int s = f * inCh;
            float l, r;
            if (inCh == 1) { l = r = src[s]; }
            else if (inCh < 6) { l = src[s]; r = src[s + 1]; }
            else
            {
                // 5.1 / 7.1: FL FR FC LFE SL SR (BL BR)
                const float c = 0.707f;
                float ll = src[s] + c * src[s + 2] + c * src[s + 4];
                float rr = src[s + 1] + c * src[s + 2] + c * src[s + 5];
                float w = 1 + c + c;
                if (inCh >= 8) { ll += c * src[s + 6]; rr += c * src[s + 7]; w += c; }
                l = ll / w; r = rr / w;
            }
            if (outCh == 1) dst[f] = (l + r) * 0.5f;
            else { dst[f * 2] = l; dst[f * 2 + 1] = r; }
        }
    }
}

/// <summary>
/// デバイス固有の形式(サンプルレート/チャンネル数/ビット深度)の入力を、
/// SourceBuffer の形式に変換して流し込む。1つの入力スレッドから使う。
/// </summary>
public sealed class SourceFeeder
{
    private readonly SourceBuffer target;
    private readonly int inRate, inCh, bits;
    private readonly bool isFloat;
    private readonly StreamResampler resampler;
    private float[] floats = new float[0];
    private float[] mapped = new float[0];
    private readonly List<float> resampled = new();
    private float[] outArr = new float[0];

    public SourceFeeder(SourceBuffer target, int inRate, int inChannels, int bitsPerSample, bool isFloat)
    {
        this.target = target; this.inRate = inRate; inCh = inChannels; bits = bitsPerSample; this.isFloat = isFloat;
        resampler = new StreamResampler(inRate, target.SampleRate, target.Channels);
    }

    public void FeedBytes(byte[] data, int count)
    {
        int bytesPerSample = Math.Max(1, bits / 8);
        int samples = count / bytesPerSample;
        samples -= samples % inCh;
        if (samples <= 0) return;
        if (floats.Length < samples) floats = new float[samples * 2];
        PcmConverter.ToFloat(data, samples * bytesPerSample, bits, isFloat, floats);
        FeedFloats(floats, samples);
    }

    /// <summary>samples=チャンネル込みのサンプル数。</summary>
    public void FeedFloats(float[] data, int samples)
    {
        int frames = samples / inCh;
        if (frames <= 0) return;
        float[] src = data;
        if (inCh != target.Channels)
        {
            if (mapped.Length < frames * target.Channels) mapped = new float[frames * target.Channels * 2];
            ChannelMapper.Convert(data, frames, inCh, target.Channels, mapped);
            src = mapped;
        }
        if (resampler.IsPassThrough) { target.Push(src, 0, frames); return; }
        resampled.Clear();
        resampler.Process(src, frames, resampled);
        int n = resampled.Count;
        if (n == 0) return;
        if (outArr.Length < n) outArr = new float[n * 2];
        resampled.CopyTo(outArr, 0);
        target.Push(outArr, 0, n / target.Channels);
    }
}
