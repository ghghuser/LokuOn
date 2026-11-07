namespace LokuOn.Core;

/// <summary>
/// 出力デバイス1台ぶんの音づくり。再生デバイスのコールバックから Render が呼ばれ(デバイスのクロックが基準)、
/// 各入力の Tap から音を取り出して、デバイスのサンプルレート・チャンネル数に合わせ、MixBus で足して返す。
/// 入力デバイスと出力デバイスはクロックが別なので、バッファのたまり具合を見て変換比をわずかに調整する。
/// たまりが足りなければ無音を出し、目標までたまってから再開する(途切れても止まらない)。
/// 録音エンジンの 0.3 秒の遅延は経由せず、目標遅延は 50ms 程度。
/// </summary>
public sealed class OutputRenderer
{
    public const double TargetSec = 0.050;      // 目標のたまり
    public const double MaxFillSec = 0.250;     // これを超えたら目標まで捨てる
    private const double Kp = 0.1;              // たまりの差(秒)→変換比の調整
    private const double Ki = 0.01;            // たまりの差の積み上げ(ずれが一定でも目標に戻す)
    private const double MaxTrim = 0.005;       // ±0.5% まで
    private const double EmaAlpha = 0.05;

    private sealed class Chan
    {
        public StreamResampler? Rs;
        public SourceBuffer? Tap;               // Rs を作ったときの Tap(入れ替わったら作り直す)
        public readonly List<float> Pend = new();   // 変換済み(入力と同じチャンネル数、デバイスのレート)
        public int PendPos;                     // Pend の読み出し済みサンプル数
        public bool Primed;
        public double Ema;
        public double Integ;                    // 調整量の積み上げ(クロックのずれの推定)
        public float[] Chunk = new float[4096];
    }

    private readonly IReadOnlyList<SourceBuffer> sources;
    private readonly int devRate, devCh;
    private readonly Chan[] chans;
    private float[][] inputs = new float[0][];
    private float[] mix = new float[0];

    public MixBus Bus { get; }
    public int DeviceRate => devRate;
    public int DeviceChannels => devCh;

    /// <summary>無音から再開するために待った回数など、足りなくなった回数(試験・表示用)。</summary>
    public long Underruns;
    public long Overflows;
    private volatile float peakL, peakR;
    private volatile bool clipped;

    public OutputRenderer(IReadOnlyList<SourceBuffer> sources, int deviceRate, int deviceChannels)
    {
        this.sources = sources;
        devRate = deviceRate;
        devCh = Math.Max(1, deviceChannels);
        chans = new Chan[sources.Count];
        for (int i = 0; i < chans.Length; i++) chans[i] = new Chan();
        Bus = new MixBus(sources.Count);
    }

    /// <summary>前回の読み出し以降のピークとクリップ(メーター用)。</summary>
    public (float L, float R, bool Clipped) ReadPeaks()
    {
        var r = (peakL, peakR, clipped);
        peakL = 0; peakR = 0; clipped = false;
        return r;
    }

    /// <summary>デバイスのコールバックから呼ぶ。dest の先頭から frames フレーム(デバイスのチャンネル数)を書く。</summary>
    public void Render(float[] dest, int destOffset, int frames)
    {
        int n = frames * devCh;
        if (mix.Length < n) mix = new float[n];
        if (inputs.Length != chans.Length) inputs = new float[chans.Length][];
        for (int i = 0; i < chans.Length; i++)
        {
            if (inputs[i] == null || inputs[i].Length < n) inputs[i] = new float[n];
            FillInput(i, frames);
        }
        var st = Bus.Process(inputs, frames, devCh, mix);
        Array.Copy(mix, 0, dest, destOffset, n);
        if (st.PeakL > peakL) peakL = st.PeakL;
        if (st.PeakR > peakR) peakR = st.PeakR;
        if (st.Clipped) clipped = true;
    }

    private void FillInput(int i, int frames)
    {
        var buf = inputs[i];
        int n = frames * devCh;
        var src = sources[i];
        var c = chans[i];
        var tap = src.Tap;
        if (tap == null || !Bus.IsSendOn(i))
        {
            Array.Clear(buf, 0, n);
            if (tap != null) tap.Clear();       // 送らない間はためない
            c.Primed = false; c.Pend.Clear(); c.PendPos = 0;
            return;
        }
        int sc = tap.Channels;
        if (c.Tap != tap || c.Rs == null)
        {
            c.Tap = tap;
            c.Rs = new StreamResampler(tap.SampleRate, devRate, sc, allowTrim: true);
            c.Pend.Clear(); c.PendPos = 0; c.Primed = false; c.Integ = 0;
        }

        double inRate = tap.SampleRate;
        double fillSec = tap.AvailableFrames / inRate + (c.Pend.Count - c.PendPos) / (double)sc / devRate;
        if (fillSec > MaxFillSec)
        {
            tap.DiscardTo((int)(TargetSec * inRate));
            c.Pend.Clear(); c.PendPos = 0;
            Interlocked.Increment(ref Overflows);
            fillSec = tap.AvailableFrames / inRate;
        }
        if (!c.Primed)
        {
            if (fillSec < TargetSec) { Array.Clear(buf, 0, n); return; }
            c.Primed = true; c.Ema = fillSec;
        }
        c.Ema += EmaAlpha * (fillSec - c.Ema);
        c.Integ = Math.Clamp(c.Integ + Ki * (c.Ema - TargetSec) * frames / devRate, -MaxTrim, MaxTrim);
        c.Rs.Trim = Math.Clamp(Kp * (c.Ema - TargetSec) + c.Integ, -MaxTrim, MaxTrim);

        // 必要なぶんだけ変換して Pend にためる
        int pendFrames = (c.Pend.Count - c.PendPos) / sc;
        double ratio = inRate / devRate;
        while (pendFrames < frames)
        {
            int avail = tap.AvailableFrames;
            if (avail <= 0) break;
            int want = (int)Math.Ceiling((frames - pendFrames) * ratio * 1.01) + 2;
            int take = Math.Min(Math.Min(want, avail), c.Chunk.Length / sc);
            int got = tap.Read(c.Chunk, 0, take);
            if (got <= 0) break;
            c.Rs.Process(c.Chunk, got, c.Pend);
            pendFrames = (c.Pend.Count - c.PendPos) / sc;
        }

        int use = Math.Min(frames, pendFrames);
        float g = (float)Math.Pow(10.0, src.GainDb / 20.0);
        Convert(c.Pend, c.PendPos, use, sc, buf, g);
        c.PendPos += use * sc;
        if (use < frames)
        {
            Array.Clear(buf, use * devCh, (frames - use) * devCh);
            c.Primed = false;                    // 足りなくなった: たまり直すまで無音
            Interlocked.Increment(ref Underruns);
        }
        if (c.PendPos > 0 && c.PendPos >= c.Pend.Count) { c.Pend.Clear(); c.PendPos = 0; }
        else if (c.PendPos > 16384) { c.Pend.RemoveRange(0, c.PendPos); c.PendPos = 0; }
    }

    /// <summary>入力のチャンネル数からデバイスのチャンネル数へ(モノラルは左右に複製、ステレオ→モノラルは平均、余りは0)。</summary>
    private void Convert(List<float> src, int pos, int frames, int sc, float[] dst, float g)
    {
        int dc = devCh;
        for (int f = 0; f < frames; f++)
        {
            int s = pos + f * sc, d = f * dc;
            if (sc == dc) { for (int k = 0; k < dc; k++) dst[d + k] = src[s + k] * g; }
            else if (sc == 1)
            {
                float v = src[s] * g;
                dst[d] = v; if (dc > 1) dst[d + 1] = v;
                for (int k = 2; k < dc; k++) dst[d + k] = 0f;
            }
            else if (dc == 1) dst[d] = (src[s] + src[s + 1]) * 0.5f * g;
            else
            {
                int m = Math.Min(sc, dc);
                for (int k = 0; k < m; k++) dst[d + k] = src[s + k] * g;
                for (int k = m; k < dc; k++) dst[d + k] = 0f;
            }
        }
    }
}
