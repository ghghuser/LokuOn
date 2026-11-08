using LokuOn.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LokuOn.Audio;

/// <summary>
/// 再生デバイス1台への出力(WASAPI共有モード・イベント駆動)。音づくりは Core の OutputRenderer が行い、
/// ここはデバイスを開いてコールバックから Render を呼ぶだけ。録音には触れない(失敗しても例外は外に出さない)。
/// </summary>
public sealed class OutputDevice : IDisposable
{
    private sealed class RenderProvider : ISampleProvider
    {
        private readonly OutputRenderer r;
        private readonly int ch;
        private float[] scratch = new float[0];
        public RenderProvider(OutputRenderer r, WaveFormat fmt) { this.r = r; ch = fmt.Channels; WaveFormat = fmt; }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            // NAudio の SampleToWaveProvider は byte[] を float[] として渡してくるため、Array.Copy/Clear は使えない(型の不一致になる)。
            // 自前の配列に作ってから、1サンプルずつ書き写す。
            if (scratch.Length < count) scratch = new float[count];
            r.Render(scratch, 0, count / ch);
            for (int i = 0; i < count; i++) buffer[offset + i] = scratch[i];
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
        string last = "";
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
                var fmt = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
                var r = new OutputRenderer(sources, taps, fmt.SampleRate, fmt.Channels);
                w = new WasapiOut(d, AudioClientShareMode.Shared, ev, ms);
                w.PlaybackStopped += (s, e) =>
                {
                    if (e.Exception != null) { LastError = e.Exception.Message; Failed = true; }
                };
                w.Init(new SampleToWaveProvider(new RenderProvider(r, fmt)));
                w.Play();
                Renderer = r; wo = w; dev = d;
                Failed = false; LastError = "";
                return null;
            }
            catch (Exception ex)
            {
                last = $"{ex.Message} (0x{ex.HResult:X8}, {(ev ? "イベント" : "ポーリング")} {ms}ms)";
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
