namespace LokuOn.Core;

/// <summary>
/// 出力デバイス1台ぶんの「最終ミックス」にかけるエフェクト(全体音量のあとに、ローカット → グラフィックEQ → パラメトリックEQ → リミッターの順)。
/// 入力ごとのエフェクトとは別で、その出力にだけかかる。プリセットの出力カードに入る。
/// </summary>
public sealed class OutputFxSetting
{
    /// <summary>すべてバイパス(設定は残したまま、効果だけ切る)。</summary>
    public bool Bypass { get; set; }

    public bool EqOn { get; set; }
    /// <summary>ローカット(Hz)。0=なし、20〜400。</summary>
    public double HpfHz { get; set; } = 0;
    public double[] Bands { get; set; } = new double[EqBands.Count];
    public bool ParamOn { get; set; }
    public ParamBand[] Params { get; set; } = ParamBand.Defaults();

    public bool LimiterOn { get; set; }
    /// <summary>リミッターの上限(dB。0=クリップ寸前)。この値を超える音は、先読みして滑らかに下げる。</summary>
    public double CeilingDb { get; set; } = -1.0;
    /// <summary>リミッターが効いたあと、元の音量に戻るまでの時間(ミリ秒)。</summary>
    public double ReleaseMs { get; set; } = 150;

    public const double CeilingMinDb = -24, CeilingMaxDb = 0, ReleaseMinMs = 20, ReleaseMaxMs = 1000;

    public bool HasAny => EqOn || HpfHz >= 20 || ParamOn || LimiterOn;
    public bool IsActive => !Bypass && HasAny;
    public bool IsDefault => SameSound(new OutputFxSetting());

    public OutputFxSetting Clone()
    {
        var c = (OutputFxSetting)MemberwiseClone();
        c.Bands = (double[])(Bands ?? new double[EqBands.Count]).Clone();
        c.Params = (Params ?? ParamBand.Defaults()).Select(p => p.Clone()).ToArray();
        return c;
    }

    private static double C(double v, double lo, double hi, double def) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;

    public void Normalize()
    {
        HpfHz = double.IsFinite(HpfHz) && HpfHz >= 20 ? Math.Clamp(HpfHz, 20, 400) : 0;
        var bd = new double[EqBands.Count];
        for (int i = 0; i < bd.Length; i++) bd[i] = Bands != null && i < Bands.Length ? C(Bands[i], -EqBands.MaxDb, EqBands.MaxDb, 0) : 0;
        Bands = bd;
        var pd = ParamBand.Defaults();
        for (int i = 0; i < pd.Length; i++)
            if (Params != null && i < Params.Length && Params[i] != null) { pd[i] = Params[i]; pd[i].Normalize(i); }
        Params = pd;
        CeilingDb = C(CeilingDb, CeilingMinDb, CeilingMaxDb, -1.0);
        ReleaseMs = C(ReleaseMs, ReleaseMinMs, ReleaseMaxMs, 150);
    }

    public bool SameSound(OutputFxSetting o)
    {
        if (Bypass != o.Bypass || EqOn != o.EqOn || HpfHz != o.HpfHz || ParamOn != o.ParamOn || LimiterOn != o.LimiterOn || CeilingDb != o.CeilingDb || ReleaseMs != o.ReleaseMs) return false;
        for (int i = 0; i < EqBands.Count; i++) if ((Bands?[i] ?? 0) != (o.Bands?[i] ?? 0)) return false;
        for (int i = 0; i < ParamBand.Count; i++) if (!Params[i].Same(o.Params[i])) return false;
        return true;
    }

    /// <summary>EQの部分だけを、入力用のエフェクトチェーンに渡せる形にする。</summary>
    public EffectSetting ToEqSetting() => new()
    {
        EqOn = EqOn, HpfHz = HpfHz, Bands = (double[])Bands.Clone(), ParamOn = ParamOn, Params = Params.Select(p => p.Clone()).ToArray(), ToOutput = true,
    };

    public void WriteTo(IniSection s, string prefix, bool comment)
    {
        string? C(string t) => comment ? t : null;
        s.Set(prefix + "Bypass", Bypass, C("この出力の最終ミックスのエフェクト(ローカット→グラフィックEQ→パラメトリックEQ→リミッター)。Bypass=すべてバイパス"));
        s.Set(prefix + "EqOn", EqOn);
        s.Set(prefix + "HpfHz", HpfHz, C("ローカット(Hz、0=なし)"));
        s.Set(prefix + "EqBands", string.Join(",", Bands.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))));
        s.Set(prefix + "ParamOn", ParamOn);
        s.Set(prefix + "ParamBands", string.Join(",", Params.Select(b => b.ToText())));
        s.Set(prefix + "LimiterOn", LimiterOn);
        s.Set(prefix + "CeilingDb", CeilingDb, C("リミッターの上限(dB、-24〜0)"));
        s.Set(prefix + "ReleaseMs", ReleaseMs, C("リミッターの戻り時間(ミリ秒、20〜1000)"));
    }

    public static OutputFxSetting ReadFrom(IniSection s, string prefix)
    {
        var d = new OutputFxSetting();
        var e = new OutputFxSetting
        {
            Bypass = s.GetBool(prefix + "Bypass", false), EqOn = s.GetBool(prefix + "EqOn", false), HpfHz = s.GetDouble(prefix + "HpfHz", 0),
            ParamOn = s.GetBool(prefix + "ParamOn", false), LimiterOn = s.GetBool(prefix + "LimiterOn", false),
            CeilingDb = s.GetDouble(prefix + "CeilingDb", d.CeilingDb), ReleaseMs = s.GetDouble(prefix + "ReleaseMs", d.ReleaseMs),
        };
        var parts = s.GetString(prefix + "EqBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < EqBands.Count; i++)
            e.Bands[i] = i < parts.Length && double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        var pp = s.GetString(prefix + "ParamBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < e.Params.Length && i < pp.Length; i++) ParamBand.TryParse(pp[i], e.Params[i]);
        e.Normalize();
        return e;
    }
}

/// <summary>
/// 先読み式のリミッター。1.5ms 先読みして、上限を超えそうな音を滑らかに下げる(上限を超えないことを保証する)。
/// 左右(全チャンネル)は連動して下げるので、音の定位が動かない。遅延は先読みぶん(1.5ms)。
/// </summary>
public sealed class Limiter
{
    private readonly int ch, n, sr;            // n = 先読みのフレーム数
    private readonly float[] delay;             // 遅らせた音(インターリーブ、n フレーム)
    private readonly float[] rBuf, mBuf;        // 戻り付きの必要ゲイン / その窓内の最小値(n+1 個のリング)
    private int dpos, wpos;
    private float r = 1f;
    private float ceiling = 0.89f, relCoef = 0.001f;

    public int LatencyFrames => n;

    public Limiter(int sampleRate, int channels)
    {
        sr = sampleRate;
        ch = Math.Max(1, channels);
        n = Math.Max(1, (int)Math.Round(0.0015 * sampleRate));
        delay = new float[n * ch];
        rBuf = new float[n + 1]; mBuf = new float[n + 1];
        Reset();
    }

    public void Set(double ceilingDb, double releaseMs)
    {
        ceiling = (float)Math.Pow(10.0, ceilingDb / 20.0);
        relCoef = (float)(1.0 - Math.Exp(-1.0 / (Math.Max(1.0, releaseMs) * 0.001 * sr)));
    }

    public void Reset()
    {
        Array.Clear(delay); Array.Fill(rBuf, 1f); Array.Fill(mBuf, 1f); dpos = 0; wpos = 0; r = 1f;
    }

    /// <summary>
    /// 音 x[t] が来るたびに、必要なゲイン gr[t] を求め、戻り付きの r[t] (r ≤ gr)にする。
    /// M[t] = 直近 n+1 個の r の最小値、G = 直近 n+1 個の M の平均。
    /// n フレーム前の音 x[t-n] に G をかける。G の平均に入る M はすべて x[t-n] を含む窓なので、
    /// それぞれ gr[t-n] 以下であり、したがって出力は上限を超えない。
    /// </summary>
    public void Process(float[] buf, int frames)
    {
        int n1 = n + 1;
        for (int f = 0; f < frames; f++)
        {
            int b = f * ch;
            float p = 0;
            for (int c = 0; c < ch; c++) { float a = buf[b + c]; if (a < 0) a = -a; if (a > p) p = a; }
            float gr = p > ceiling ? ceiling / p : 1f;
            float rec = r + relCoef * (1f - r);
            r = gr < rec ? gr : rec;
            rBuf[wpos] = r;
            float m = 1f;
            for (int k = 0; k < n1; k++) { float v = rBuf[k]; if (v < m) m = v; }
            mBuf[wpos] = m;
            double sum = 0;
            for (int k = 0; k < n1; k++) sum += mBuf[k];
            float g = (float)(sum / n1);
            int d = dpos * ch;
            for (int c = 0; c < ch; c++)
            {
                float old = delay[d + c];
                delay[d + c] = buf[b + c];
                buf[b + c] = old * g;
            }
            if (++dpos >= n) dpos = 0;
            if (++wpos >= n1) wpos = 0;
        }
    }
}

/// <summary>出力1台ぶんの最終ミックス用エフェクト。設定は別スレッドから Update で渡してよい。何もかけない設定のときは配列に触れない。</summary>
public sealed class OutputFxChain
{
    private readonly EffectChain eq;
    private readonly Limiter lim;
    private volatile OutputFxSetting snap = new();
    private bool limWasOn;

    public OutputFxChain(int sampleRate, int channels)
    {
        eq = new EffectChain(sampleRate, channels);
        lim = new Limiter(sampleRate, channels);
    }

    public bool IsActive => snap.IsActive;

    public void Update(OutputFxSetting s)
    {
        var c = s.Clone(); c.Normalize();
        eq.Update(c.IsActive ? c.ToEqSetting() : new EffectSetting());
        lim.Set(c.CeilingDb, c.ReleaseMs);
        snap = c;
    }

    public void Process(float[] buf, int frames)
    {
        var s = snap;
        if (!s.IsActive) { limWasOn = false; return; }
        if (s.EqOn || s.HpfHz >= 20 || s.ParamOn) eq.Process(buf, frames);
        if (s.LimiterOn)
        {
            if (!limWasOn) { lim.Reset(); limWasOn = true; }
            lim.Process(buf, frames);
        }
        else limWasOn = false;
    }
}
