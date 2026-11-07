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
        public RenderProvider(OutputRenderer r, WaveFormat fmt) { this.r = r; ch = fmt.Channels; WaveFormat = fmt; }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            r.Render(buffer, offset, count / ch);
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

    /// <summary>デバイスを開いて再生を始める。失敗したら理由を返す(成功なら null)。</summary>
    public string? Start(IReadOnlyList<SourceBuffer> sources)
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            string id = DeviceId.StartsWith("wasapi-out:", StringComparison.OrdinalIgnoreCase) ? DeviceId.Substring("wasapi-out:".Length) : DeviceId;
            dev = id == "default" || id.Length == 0 ? en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : en.GetDevice(id);
            DeviceName = dev.FriendlyName;
            var mix = dev.AudioClient.MixFormat;
            var fmt = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
            Renderer = new OutputRenderer(sources, fmt.SampleRate, fmt.Channels);
            wo = new WasapiOut(dev, AudioClientShareMode.Shared, true, 60);
            wo.PlaybackStopped += (s, e) =>
            {
                if (e.Exception != null) { LastError = e.Exception.Message; Failed = true; }
            };
            wo.Init(new SampleToWaveProvider(new RenderProvider(Renderer, fmt)));
            wo.Play();
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message; Failed = true;
            Dispose();
            return $"出力デバイスを開けません: {ex.Message}";
        }
    }

    public void Dispose()
    {
        try { wo?.Stop(); } catch { }
        try { wo?.Dispose(); } catch { }
        try { dev?.Dispose(); } catch { }
        wo = null; dev = null;
    }
}
