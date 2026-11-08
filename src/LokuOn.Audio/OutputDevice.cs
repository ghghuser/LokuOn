using LokuOn.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LokuOn.Audio;

/// <summary>
/// 再生デバイス1台への出力(WASAPI共有モード・イベント駆動)。音づくりは Core の OutputRenderer が行い、
/// ここはデバイスを開いてコールバックから Render を呼ぶだけ。録音には触れない(失敗しても例外は外に出さない)。
/// </summary>
public sealed class OutputDevice : IDisposable
{
    /// <summary>
    /// デバイスが答えた形式(MixFormat)そのままの形で音を渡す。Bluetoothなどは、形式が少しでも違うと
    /// 「Value does not fall within the expected range」で開けないことがあるため、float 以外(16/24/32bit整数)にも対応する。
    /// </summary>
    private sealed class RenderProvider : IWaveProvider
    {
        private readonly OutputRenderer r;
        private readonly int ch, bits;
        private readonly bool isFloat;
        private float[] scratch = new float[0];
        public RenderProvider(OutputRenderer r, WaveFormat fmt)
        {
            this.r = r; ch = fmt.Channels; WaveFormat = fmt; bits = fmt.BitsPerSample;
            isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat
                || (fmt is WaveFormatExtensible ex && ex.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        }
        public WaveFormat WaveFormat { get; }
        public static bool Supports(WaveFormat f)
            => f.Encoding == WaveFormatEncoding.IeeeFloat || f.Encoding == WaveFormatEncoding.Pcm || f is WaveFormatExtensible;

        public int Read(byte[] buffer, int offset, int count)
        {
            int bytesPer = bits / 8, frameBytes = bytesPer * ch;
            int frames = count / frameBytes;
            int samples = frames * ch;
            if (scratch.Length < samples) scratch = new float[samples];
            r.Render(scratch, 0, frames);
            if (isFloat && bits == 32) { Buffer.BlockCopy(scratch, 0, buffer, offset, samples * 4); }
            else
            {
                int o = offset;
                for (int i = 0; i < samples; i++)
                {
                    float v = scratch[i]; if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    switch (bits)
                    {
                        case 16: { short x = (short)Math.Round(v * 32767f); buffer[o++] = (byte)x; buffer[o++] = (byte)(x >> 8); break; }
                        case 24: { int x = (int)Math.Round(v * 8388607f); buffer[o++] = (byte)x; buffer[o++] = (byte)(x >> 8); buffer[o++] = (byte)(x >> 16); break; }
                        default: { int x = (int)Math.Round(v * 2147483647.0); buffer[o++] = (byte)x; buffer[o++] = (byte)(x >> 8); buffer[o++] = (byte)(x >> 16); buffer[o++] = (byte)(x >> 24); break; }
                    }
                }
            }
            int used = frames * frameBytes;
            for (int i = used; i < count; i++) buffer[offset + i] = 0;
            return count;
        }
    }

    private WasapiOut? wo;
    private MMDevice? dev;
    public OutputRenderer? Renderer { get; private set; }
    public string DeviceId { get; }
    public string DeviceName { get; private set; } = "";
    public volatile bool Failed;
    public string LastError { get; private set; } = "";

    public OutputDevice(string deviceId) { DeviceId = deviceId; }

    /// <summary>
    /// デバイスを開いて再生を始める。失敗したら理由を返す(成功なら null)。
    /// Bluetoothなどは短いバッファやイベント駆動を受け付けないことがあるので、設定を変えながら順に試す。
    /// </summary>
    public string? Start(IReadOnlyList<SourceBuffer> sources, IReadOnlyList<SourceBuffer?> taps)
    {
        // (イベント駆動, バッファms)
        var attempts = new (bool Event, int Ms)[] { (true, 60), (true, 120), (true, 250), (false, 120), (false, 300) };
        string last = "", fmtText = "不明";
        foreach (var (ev, ms) in attempts)
        {
            MMDevice? d = null; WasapiOut? w = null;
            try
            {
                using var en = new MMDeviceEnumerator();
                string id = DeviceId.StartsWith("wasapi-out:", StringComparison.OrdinalIgnoreCase) ? DeviceId.Substring("wasapi-out:".Length) : DeviceId;
                d = id == "default" || id.Length == 0 ? en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : en.GetDevice(id);
                DeviceName = d.FriendlyName;
                var mix = d.AudioClient.MixFormat;
                fmtText = $"{mix.SampleRate}Hz {mix.Channels}ch {mix.BitsPerSample}bit {mix.Encoding}";
                var fmt = RenderProvider.Supports(mix) && (mix.BitsPerSample is 16 or 24 or 32)
                    ? mix : WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
                var r = new OutputRenderer(sources, taps, fmt.SampleRate, fmt.Channels);
                w = new WasapiOut(d, AudioClientShareMode.Shared, ev, ms);
                w.PlaybackStopped += (s, e) =>
                {
                    if (e.Exception != null) { LastError = e.Exception.Message; Failed = true; }
                };
                w.Init(new RenderProvider(r, fmt));
                w.Play();
                Renderer = r; wo = w; dev = d;
                Failed = false; LastError = "";
                return null;
            }
            catch (Exception ex)
            {
                last = $"{ex.Message} (0x{ex.HResult:X8} / デバイスの形式: {fmtText} / {(ev ? "イベント駆動" : "ポーリング")}・バッファ{ms}ms)";
                try { w?.Dispose(); } catch { }
                try { d?.Dispose(); } catch { }
            }
        }
        LastError = last; Failed = true;
        return $"出力デバイスを開けません: {last}";
    }

    public void Dispose()
    {
        try { wo?.Stop(); } catch { }
        try { wo?.Dispose(); } catch { }
        try { dev?.Dispose(); } catch { }
        wo = null; dev = null;
    }
}
