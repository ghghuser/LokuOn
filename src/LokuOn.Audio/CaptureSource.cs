using NAudio.CoreAudioApi;
using NAudio.Wave;
using LokuOn.Core;

namespace LokuOn.Audio;

/// <summary>
/// 1つの入力デバイス(WASAPI入力 / WASAPIループバック=PC内部音 / ASIO)から音を受け取り、
/// SourceBuffer へ流し込む。切断されたら Connected=false にし、Poll() で復帰を試みる。
/// Start / Poll / Dispose は UI スレッド(STA)から呼ぶこと(ASIOドライバがCOMのため)。
/// </summary>
public sealed class CaptureSource : IDisposable
{
    private readonly string deviceId;
    private readonly SourceBuffer target;
    private readonly object gate = new();
    private WasapiCapture? wasapi;
    private AsioOut? asio;
    private string? currentEndpointId;      // 「既定」追従用
    private volatile bool disposed;
    private float[] asioBuf = new float[0];

    public string DisplayName { get; }
    public string LastError { get; private set; } = "";

    public CaptureSource(string deviceId, string displayName, SourceBuffer target)
    {
        this.deviceId = deviceId;
        DisplayName = displayName;
        this.target = target;
        target.Label = displayName;
        target.Connected = false;
    }

    private bool IsAsio => DeviceCatalog.IsAsio(deviceId);
    private bool IsLoopback => deviceId.StartsWith("wasapi-loop:", StringComparison.OrdinalIgnoreCase);
    private bool FollowsDefault => deviceId.EndsWith(":default", StringComparison.OrdinalIgnoreCase);
    private string EndpointPart => deviceId.Substring(deviceId.IndexOf(':') + 1);

    /// <summary>接続を試みる。失敗しても例外は出さず、LastError に理由を入れる(Poll で再試行される)。</summary>
    public bool Start()
    {
        lock (gate)
        {
            if (disposed) return false;
            try
            {
                if (IsAsio) StartAsio(); else StartWasapi();
                target.Connected = true;
                LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                StopInternal();
                target.Connected = false;
                return false;
            }
        }
    }

    /// <summary>1秒ごとなどに呼ぶ。切断中なら再接続、「既定のデバイス」なら既定が変わったか確認する。</summary>
    public void Poll()
    {
        if (disposed) return;
        bool needRestart;
        lock (gate)
        {
            if (!target.Connected) needRestart = true;
            else if (!IsAsio && FollowsDefault)
            {
                needRestart = false;
                try
                {
                    using var en = new MMDeviceEnumerator();
                    using var d = en.GetDefaultAudioEndpoint(IsLoopback ? DataFlow.Render : DataFlow.Capture, Role.Multimedia);
                    if (currentEndpointId != null && d.ID != currentEndpointId) needRestart = true;
                }
                catch { needRestart = true; }
            }
            else needRestart = false;
        }
        if (needRestart)
        {
            lock (gate) StopInternal();
            Start();
        }
    }

    // ---------------------------------------------------------------- WASAPI

    private void StartWasapi()
    {
        using var en = new MMDeviceEnumerator();
        var flow = IsLoopback ? DataFlow.Render : DataFlow.Capture;
        using var dev = FollowsDefault ? en.GetDefaultAudioEndpoint(flow, Role.Multimedia) : en.GetDevice(EndpointPart);
        currentEndpointId = dev.ID;
        if (dev.State != DeviceState.Active) throw new InvalidOperationException("デバイスが有効ではありません");

        WasapiCapture cap = IsLoopback ? new WasapiLoopbackCapture(dev) : new WasapiCapture(dev);
        var wf = cap.WaveFormat;
        var feeder = new SourceFeeder(target, wf.SampleRate, wf.Channels, wf.BitsPerSample, wf.Encoding == WaveFormatEncoding.IeeeFloat);
        cap.DataAvailable += (s, e) =>
        {
            if (disposed || e.BytesRecorded <= 0) return;
            try { feeder.FeedBytes(e.Buffer, e.BytesRecorded); } catch { }
        };
        cap.RecordingStopped += (s, e) =>
        {
            // 録音を止めたのが自分でなければ(デバイスが外れた等)、切断として扱う
            lock (gate)
            {
                if (ReferenceEquals(wasapi, s)) { target.Connected = false; LastError = e.Exception?.Message ?? "デバイスが停止しました"; }
            }
        };
        wasapi = cap;
        cap.StartRecording();
    }

    // ---------------------------------------------------------------- ASIO

    private void StartAsio()
    {
        string rest = EndpointPart;                    // ドライバ名|先頭チャンネル
        int bar = rest.LastIndexOf('|');
        string driver = bar >= 0 ? rest.Substring(0, bar) : rest;
        int offset = bar >= 0 && int.TryParse(rest.Substring(bar + 1), out var o) ? o : 0;

        var a = new AsioOut(driver);
        try
        {
            a.InputChannelOffset = offset;
            if (!a.IsSampleRateSupported(target.SampleRate))
                throw new InvalidOperationException($"ASIOドライバが {target.SampleRate}Hz に対応していません(ドライバ側の設定を確認してください)");
            const int channels = 2;
            a.InitRecordAndPlayback(null, channels, target.SampleRate);
            var feeder = new SourceFeeder(target, target.SampleRate, channels, 32, true);
            a.AudioAvailable += (s, e) =>
            {
                if (disposed) return;
                try
                {
                    int need = e.SamplesPerBuffer * e.InputBuffers.Length;
                    if (asioBuf.Length < need) asioBuf = new float[need];
                    int n = e.GetAsInterleavedSamples(asioBuf);
                    if (e.InputBuffers.Length == channels) feeder.FeedFloats(asioBuf, n);
                }
                catch { }
            };
            a.DriverResetRequest += (s, e) =>
            {
                lock (gate) { if (ReferenceEquals(asio, s)) { target.Connected = false; LastError = "ASIOドライバがリセットを要求しました"; } }
            };
            a.Play();
            asio = a;
        }
        catch
        {
            try { a.Dispose(); } catch { }
            throw;
        }
    }

    // ---------------------------------------------------------------- 停止

    private void StopInternal()
    {
        var w = wasapi; wasapi = null;
        if (w != null)
        {
            try { w.StopRecording(); } catch { }
            try { w.Dispose(); } catch { }
        }
        var a = asio; asio = null;
        if (a != null)
        {
            try { a.Stop(); } catch { }
            try { a.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        disposed = true;
        lock (gate) { StopInternal(); target.Connected = false; }
    }
}
