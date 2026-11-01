using LokuOn.Core;
using NAudio.Wave;

namespace LokuOn.Audio;

/// <summary>
/// 録音ファイルの簡易プレイヤー。0.5〜2倍の速度でも音程が変わらない(WSOLA方式)。
/// 画面スレッド(UI)から呼ぶこと。
/// </summary>
public sealed class FilePlayer : IDisposable
{
    private readonly object gate = new();
    private AudioFileReader? reader;
    private WaveOutEvent? output;
    private WsolaStretcher? stretch;
    private bool userStopped;
    private double speed = 1.0;
    private float volume = 1f;

    /// <summary>最後まで再生して止まった。</summary>
    public event Action? Ended;
    /// <summary>再生できなかった(理由つき)。</summary>
    public event Action<string>? Failed;

    public bool IsOpen => reader != null;
    public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Duration => reader?.TotalTime ?? TimeSpan.Zero;

    public double Volume
    {
        get => volume;
        set { volume = (float)Math.Clamp(value, 0, 1); if (reader != null) reader.Volume = volume; }
    }

    /// <summary>再生速度(0.5〜2.0)。再生中でも変えられる。</summary>
    public double SpeedRatio
    {
        get => speed;
        set { speed = Math.Clamp(value, 0.5, 2.0); lock (gate) { if (stretch != null) stretch.Tempo = speed; } }
    }

    public TimeSpan Position
    {
        get
        {
            lock (gate)
            {
                if (stretch == null) return TimeSpan.Zero;
                // 出力側に溜まっている分(約0.2秒)を差し引く
                double s = stretch.PositionSeconds - 0.2 * speed;
                return TimeSpan.FromSeconds(Math.Max(0, s));
            }
        }
        set => Seek(value);
    }

    /// <summary>ファイルを開く(再生は始めない)。開けなければ例外。</summary>
    public void Open(string path)
    {
        Close();
        var r = new AudioFileReader(path) { Volume = volume };
        var fmt = r.WaveFormat;
        var st = new WsolaStretcher((buf, off, cnt) => r.Read(buf, off, cnt), fmt.SampleRate, fmt.Channels) { Tempo = speed };
        st.Reset(0);
        var o = new WaveOutEvent { DesiredLatency = 150 };
        o.Init(new FloatToWave(new StretchProvider(this, st, WaveFormat.CreateIeeeFloatWaveFormat(fmt.SampleRate, fmt.Channels))));
        o.PlaybackStopped += OnStopped;
        lock (gate) { reader = r; stretch = st; output = o; }
        userStopped = false;
    }

    public void Play()
    {
        var o = output; if (o == null) return;
        userStopped = false;
        o.Play();
    }

    public void Pause()
    {
        var o = output; if (o == null) return;
        userStopped = true;
        o.Pause();
    }

    /// <summary>止めて先頭に戻す。</summary>
    public void Stop()
    {
        var o = output; if (o == null) return;
        userStopped = true;
        try { o.Stop(); } catch { }
        Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan t)
    {
        lock (gate)
        {
            if (reader == null || stretch == null) return;
            double sec = Math.Clamp(t.TotalSeconds, 0, Math.Max(0, reader.TotalTime.TotalSeconds));
            reader.CurrentTime = TimeSpan.FromSeconds(sec);
            stretch.Reset((long)(sec * reader.WaveFormat.SampleRate));
        }
    }

    /// <summary>ファイルを閉じる(名前変更・削除の前など)。</summary>
    public void Close()
    {
        WaveOutEvent? o; AudioFileReader? r;
        lock (gate) { o = output; r = reader; output = null; reader = null; stretch = null; }
        userStopped = true;
        try { if (o != null) { o.PlaybackStopped -= OnStopped; o.Stop(); o.Dispose(); } } catch { }
        try { r?.Dispose(); } catch { }
    }

    public void Dispose() => Close();

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null) { Failed?.Invoke(e.Exception.Message); return; }
        if (!userStopped) Ended?.Invoke();
    }

    /// <summary>float の音声を、出力デバイスに渡すバイト列にする。</summary>
    private sealed class FloatToWave : IWaveProvider
    {
        private readonly ISampleProvider src;
        private float[] tmp = new float[8192];
        public WaveFormat WaveFormat => src.WaveFormat;
        public FloatToWave(ISampleProvider src) { this.src = src; }
        public int Read(byte[] buffer, int offset, int count)
        {
            int n = count / 4;
            if (tmp.Length < n) tmp = new float[n];
            int r = src.Read(tmp, 0, n);
            Buffer.BlockCopy(tmp, 0, buffer, offset, r * 4);
            return r * 4;
        }
    }

    private sealed class StretchProvider : ISampleProvider
    {
        private readonly FilePlayer owner;
        private readonly WsolaStretcher st;
        public WaveFormat WaveFormat { get; }
        public StretchProvider(FilePlayer owner, WsolaStretcher st, WaveFormat fmt) { this.owner = owner; this.st = st; WaveFormat = fmt; }
        public int Read(float[] buffer, int offset, int count)
        {
            lock (owner.gate) return st.Read(buffer, offset, count);
        }
    }
}
