namespace LokuOn.Core;

/// <summary>入力1本ぶんのエフェクト設定(イコライザ → エコー → リバーブ の順に通す)。プリセットに保存される。</summary>
public sealed class EffectSetting
{
    // ---- 全体
    /// <summary>すべてバイパス(設定は残したまま、効果だけ切る)。</summary>
    public bool Bypass { get; set; }
    /// <summary>エフェクトをかけた音を録音に入れるか(既定: しない=録音は素の音)。</summary>
    public bool ToRecording { get; set; } = false;
    /// <summary>エフェクトをかけた音を出力に流すか(既定: する)。</summary>
    public bool ToOutput { get; set; } = true;

    // ---- イコライザ
    public bool EqOn { get; set; }
    /// <summary>ローカット(Hz)。0=なし。</summary>
    public double HpfHz { get; set; } = 0;
    public double LowDb { get; set; } = 0;
    public double LowHz { get; set; } = 120;
    public double MidDb { get; set; } = 0;
    public double MidHz { get; set; } = 1000;
    public double MidQ { get; set; } = 1.0;
    public double HighDb { get; set; } = 0;
    public double HighHz { get; set; } = 6000;

    // ---- エコー
    public bool EchoOn { get; set; }
    public double EchoMs { get; set; } = 300;
    public double EchoFeedback { get; set; } = 0.35;
    public double EchoMix { get; set; } = 0.30;

    // ---- リバーブ
    public bool ReverbOn { get; set; }
    public double ReverbRoom { get; set; } = 0.5;
    public double ReverbDamp { get; set; } = 0.5;
    public double ReverbMix { get; set; } = 0.25;

    public const double EqDbMax = 15;

    /// <summary>効果が実際にかかるか(バイパスでなく、どれかがオン)。</summary>
    public bool IsActive => !Bypass && (EqOn || EchoOn || ReverbOn);
    /// <summary>設定が1つでも入っているか(FXボタンの色づけ用。バイパス中でも true)。</summary>
    public bool HasAny => EqOn || EchoOn || ReverbOn;

    public EffectSetting Clone() => (EffectSetting)MemberwiseClone();

    private static double C(double v, double lo, double hi, double def) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;

    public void Normalize()
    {
        HpfHz = HpfHz <= 0 || !double.IsFinite(HpfHz) ? 0 : Math.Clamp(HpfHz, 20, 400);
        LowDb = C(LowDb, -EqDbMax, EqDbMax, 0); LowHz = C(LowHz, 40, 400, 120);
        MidDb = C(MidDb, -EqDbMax, EqDbMax, 0); MidHz = C(MidHz, 200, 5000, 1000); MidQ = C(MidQ, 0.3, 4, 1.0);
        HighDb = C(HighDb, -EqDbMax, EqDbMax, 0); HighHz = C(HighHz, 2000, 12000, 6000);
        EchoMs = C(EchoMs, 20, 1000, 300); EchoFeedback = C(EchoFeedback, 0, 0.9, 0.35); EchoMix = C(EchoMix, 0, 1, 0.3);
        ReverbRoom = C(ReverbRoom, 0, 1, 0.5); ReverbDamp = C(ReverbDamp, 0, 1, 0.5); ReverbMix = C(ReverbMix, 0, 1, 0.25);
    }

    /// <summary>設定が初期値のままか(iniに書かなくてよいか)。</summary>
    public bool IsDefault => SameSound(new EffectSetting()) && ToRecording == false && ToOutput == true;

    public void WriteTo(IniSection s, string prefix, bool comment)
    {
        string? C(string t) => comment ? t : null;
        s.Set(prefix + "Bypass", Bypass, C("入力ごとのエフェクト。Bypass=すべてバイパス / ToRecording=録音に反映 / ToOutput=出力に反映"));
        s.Set(prefix + "ToRecording", ToRecording); s.Set(prefix + "ToOutput", ToOutput);
        s.Set(prefix + "EqOn", EqOn); s.Set(prefix + "HpfHz", HpfHz, C("ローカット(Hz、0=なし)"));
        s.Set(prefix + "LowDb", LowDb); s.Set(prefix + "LowHz", LowHz);
        s.Set(prefix + "MidDb", MidDb); s.Set(prefix + "MidHz", MidHz); s.Set(prefix + "MidQ", MidQ);
        s.Set(prefix + "HighDb", HighDb); s.Set(prefix + "HighHz", HighHz);
        s.Set(prefix + "EchoOn", EchoOn); s.Set(prefix + "EchoMs", EchoMs); s.Set(prefix + "EchoFeedback", EchoFeedback); s.Set(prefix + "EchoMix", EchoMix);
        s.Set(prefix + "ReverbOn", ReverbOn); s.Set(prefix + "ReverbRoom", ReverbRoom); s.Set(prefix + "ReverbDamp", ReverbDamp); s.Set(prefix + "ReverbMix", ReverbMix);
    }

    public static EffectSetting ReadFrom(IniSection s, string prefix)
    {
        var d = new EffectSetting();
        var e = new EffectSetting
        {
            Bypass = s.GetBool(prefix + "Bypass", d.Bypass), ToRecording = s.GetBool(prefix + "ToRecording", d.ToRecording), ToOutput = s.GetBool(prefix + "ToOutput", d.ToOutput),
            EqOn = s.GetBool(prefix + "EqOn", d.EqOn), HpfHz = s.GetDouble(prefix + "HpfHz", d.HpfHz),
            LowDb = s.GetDouble(prefix + "LowDb", d.LowDb), LowHz = s.GetDouble(prefix + "LowHz", d.LowHz),
            MidDb = s.GetDouble(prefix + "MidDb", d.MidDb), MidHz = s.GetDouble(prefix + "MidHz", d.MidHz), MidQ = s.GetDouble(prefix + "MidQ", d.MidQ),
            HighDb = s.GetDouble(prefix + "HighDb", d.HighDb), HighHz = s.GetDouble(prefix + "HighHz", d.HighHz),
            EchoOn = s.GetBool(prefix + "EchoOn", d.EchoOn), EchoMs = s.GetDouble(prefix + "EchoMs", d.EchoMs),
            EchoFeedback = s.GetDouble(prefix + "EchoFeedback", d.EchoFeedback), EchoMix = s.GetDouble(prefix + "EchoMix", d.EchoMix),
            ReverbOn = s.GetBool(prefix + "ReverbOn", d.ReverbOn), ReverbRoom = s.GetDouble(prefix + "ReverbRoom", d.ReverbRoom),
            ReverbDamp = s.GetDouble(prefix + "ReverbDamp", d.ReverbDamp), ReverbMix = s.GetDouble(prefix + "ReverbMix", d.ReverbMix),
        };
        e.Normalize();
        return e;
    }

    /// <summary>効果の中身(オン/オフと数値)が同じか。録音・出力への反映先の設定は比べない。</summary>
    public bool SameSound(EffectSetting o) =>
        Bypass == o.Bypass && EqOn == o.EqOn && HpfHz == o.HpfHz && LowDb == o.LowDb && LowHz == o.LowHz && MidDb == o.MidDb && MidHz == o.MidHz
        && MidQ == o.MidQ && HighDb == o.HighDb && HighHz == o.HighHz && EchoOn == o.EchoOn && EchoMs == o.EchoMs && EchoFeedback == o.EchoFeedback
        && EchoMix == o.EchoMix && ReverbOn == o.ReverbOn && ReverbRoom == o.ReverbRoom && ReverbDamp == o.ReverbDamp && ReverbMix == o.ReverbMix;
}

/// <summary>
/// 入力1本ぶんのエフェクトを実際にかける処理(インターリーブのfloatを、その場で書き換える)。
/// 設定は別スレッド(画面)から Update で渡してよい。何もかけない設定のときは、配列に一切触れない(ビット一致)。
/// </summary>
public sealed class EffectChain
{
    private readonly int sr, ch;
    private volatile EffectSetting snap = new();
    private EffectSetting? applied;

    // --- イコライザ(BiQuad)
    private sealed class Bq
    {
        public double B0 = 1, B1, B2, A1, A2;
        public bool On;
        public double[] Z1 = Array.Empty<double>(), Z2 = Array.Empty<double>();
    }
    private readonly Bq[] bands = { new(), new(), new(), new() };   // ローカット, 低域, 中域, 高域
    private bool eqWasOn;

    // --- エコー
    private float[][]? echo;
    private int echoLen, echoPos, echoDelay;
    private float echoFb, echoMix;
    private bool echoOn, echoWasOn;

    // --- リバーブ(Freeverb)
    private sealed class Comb { public float[] Buf = Array.Empty<float>(); public int Pos; public float Store; }
    private sealed class Allpass { public float[] Buf = Array.Empty<float>(); public int Pos; }
    private Comb[][]? combs;          // [ch][8]
    private Allpass[][]? allpasses;   // [ch][4]
    private float revFeedback, revDamp, revMix;
    private bool revOn, revWasOn;
    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
    private const int StereoSpread = 23;

    public EffectChain(int sampleRate, int channels)
    {
        sr = sampleRate; ch = Math.Max(1, channels);
    }

    /// <summary>設定を渡す(コピーして持つ。次の Process から反映)。</summary>
    public void Update(EffectSetting s)
    {
        var c = s.Clone(); c.Normalize();
        snap = c;
    }

    /// <summary>いま何かがかかる設定か(かからなければ Process を呼ばなくてよい)。</summary>
    public bool IsActive => snap.IsActive;

    public void Process(float[] buf, int frames)
    {
        var s = snap;
        if (!s.IsActive) { if (applied != null && applied.IsActive) { applied = s; ResetStates(); } return; }
        if (!ReferenceEquals(s, applied)) { Rebuild(s); applied = s; }

        if (eqOnNow) ProcessEq(buf, frames);
        if (echoOn) ProcessEcho(buf, frames);
        if (revOn) ProcessReverb(buf, frames);
    }

    private bool eqOnNow;

    private void ResetStates()
    {
        foreach (var b in bands) { Array.Clear(b.Z1); Array.Clear(b.Z2); }
        echo = null; combs = null; allpasses = null; echoWasOn = revWasOn = eqWasOn = false;
    }

    // ------------------------------------------------------------ 設定の反映

    private void Rebuild(EffectSetting s)
    {
        // イコライザ
        eqOnNow = s.EqOn;
        if (s.EqOn)
        {
            if (!eqWasOn) { foreach (var b in bands) { b.Z1 = new double[ch]; b.Z2 = new double[ch]; } }
            SetBand(bands[0], s.HpfHz > 0, () => Hpf(s.HpfHz));
            SetBand(bands[1], Math.Abs(s.LowDb) > 0.01, () => LowShelf(s.LowHz, s.LowDb));
            SetBand(bands[2], Math.Abs(s.MidDb) > 0.01, () => Peak(s.MidHz, s.MidQ, s.MidDb));
            SetBand(bands[3], Math.Abs(s.HighDb) > 0.01, () => HighShelf(s.HighHz, s.HighDb));
        }
        eqWasOn = s.EqOn;

        // エコー
        echoOn = s.EchoOn;
        if (s.EchoOn)
        {
            echoDelay = Math.Max(1, (int)(s.EchoMs * 0.001 * sr));
            if (echo == null || !echoWasOn)
            {
                echoLen = sr + 2;      // 最大1秒
                echo = new float[ch][];
                for (int c = 0; c < ch; c++) echo[c] = new float[echoLen];
                echoPos = 0;
            }
            echoFb = (float)s.EchoFeedback; echoMix = (float)s.EchoMix;
        }
        echoWasOn = s.EchoOn;

        // リバーブ
        revOn = s.ReverbOn;
        if (s.ReverbOn)
        {
            if (combs == null || !revWasOn)
            {
                double k = sr / 44100.0;
                combs = new Comb[ch][]; allpasses = new Allpass[ch][];
                for (int c = 0; c < ch; c++)
                {
                    int spread = (c % 2) * StereoSpread;
                    combs[c] = CombTuning.Select(t => new Comb { Buf = new float[Math.Max(1, (int)((t + spread) * k))] }).ToArray();
                    allpasses[c] = AllpassTuning.Select(t => new Allpass { Buf = new float[Math.Max(1, (int)((t + spread) * k))] }).ToArray();
                }
            }
            revFeedback = (float)(0.7 + 0.28 * s.ReverbRoom);
            revDamp = (float)(0.4 * s.ReverbDamp);
            revMix = (float)s.ReverbMix;
        }
        revWasOn = s.ReverbOn;
    }

    private static void SetBand(Bq b, bool on, Func<(double, double, double, double, double)> make)
    {
        b.On = on;
        if (!on) return;
        var (b0, b1, b2, a1, a2) = make();
        b.B0 = b0; b.B1 = b1; b.B2 = b2; b.A1 = a1; b.A2 = a2;
    }

    // RBJ Audio EQ Cookbook。戻り値は a0 で割った (b0, b1, b2, a1, a2)
    private (double, double, double, double, double) Norm(double b0, double b1, double b2, double a0, double a1, double a2)
        => (b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);

    private (double, double, double, double, double) Hpf(double f)
    {
        double w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071);
        return Norm((1 + cs) / 2, -(1 + cs), (1 + cs) / 2, 1 + al, -2 * cs, 1 - al);
    }

    private (double, double, double, double, double) Peak(double f, double q, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
        return Norm(1 + al * A, -2 * cs, 1 - al * A, 1 + al / A, -2 * cs, 1 - al / A);
    }

    private (double, double, double, double, double) LowShelf(double f, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), sn = Math.Sin(w);
        double beta = 2 * Math.Sqrt(A) * (sn / (2 * Math.Sqrt(0.5)));   // S=1 相当(Q=0.7071)
        return Norm(A * ((A + 1) - (A - 1) * cs + beta), 2 * A * ((A - 1) - (A + 1) * cs), A * ((A + 1) - (A - 1) * cs - beta),
                    (A + 1) + (A - 1) * cs + beta, -2 * ((A - 1) + (A + 1) * cs), (A + 1) + (A - 1) * cs - beta);
    }

    private (double, double, double, double, double) HighShelf(double f, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), sn = Math.Sin(w);
        double beta = 2 * Math.Sqrt(A) * (sn / (2 * Math.Sqrt(0.5)));
        return Norm(A * ((A + 1) + (A - 1) * cs + beta), -2 * A * ((A - 1) + (A + 1) * cs), A * ((A + 1) + (A - 1) * cs - beta),
                    (A + 1) - (A - 1) * cs + beta, 2 * ((A - 1) - (A + 1) * cs), (A + 1) - (A - 1) * cs - beta);
    }

    // ------------------------------------------------------------ 処理

    private void ProcessEq(float[] buf, int frames)
    {
        foreach (var b in bands)
        {
            if (!b.On) continue;
            double b0 = b.B0, b1 = b.B1, b2 = b.B2, a1 = b.A1, a2 = b.A2;
            for (int c = 0; c < ch; c++)
            {
                double z1 = b.Z1[c], z2 = b.Z2[c];
                for (int f = 0, i = c; f < frames; f++, i += ch)
                {
                    double x = buf[i];
                    double y = b0 * x + z1;
                    z1 = b1 * x - a1 * y + z2;
                    z2 = b2 * x - a2 * y;
                    buf[i] = (float)y;
                }
                if (Math.Abs(z1) < 1e-20) z1 = 0;
                if (Math.Abs(z2) < 1e-20) z2 = 0;
                b.Z1[c] = z1; b.Z2[c] = z2;
            }
        }
    }

    private void ProcessEcho(float[] buf, int frames)
    {
        var e = echo!;
        int pos0 = echoPos;
        for (int c = 0; c < ch; c++)
        {
            var line = e[c];
            int pos = pos0;
            for (int f = 0, i = c; f < frames; f++, i += ch)
            {
                int rp = pos - echoDelay; if (rp < 0) rp += echoLen;
                float d = line[rp];
                float x = buf[i];
                line[pos] = x + d * echoFb;
                buf[i] = x + d * echoMix;
                if (++pos >= echoLen) pos = 0;
            }
        }
        echoPos = (pos0 + frames) % echoLen;
    }

    private void ProcessReverb(float[] buf, int frames)
    {
        float fb = revFeedback, d1 = revDamp, d2 = 1f - revDamp, mix = revMix;
        for (int c = 0; c < ch; c++)
        {
            var cb = combs![c]; var ap = allpasses![c];
            for (int f = 0, i = c; f < frames; f++, i += ch)
            {
                float x = buf[i], inp = x * 0.015f, sum = 0;
                for (int k = 0; k < cb.Length; k++)
                {
                    var cm = cb[k];
                    float o = cm.Buf[cm.Pos];
                    cm.Store = o * d2 + cm.Store * d1;
                    if (cm.Store < 1e-20f && cm.Store > -1e-20f) cm.Store = 0;
                    cm.Buf[cm.Pos] = inp + cm.Store * fb;
                    if (++cm.Pos >= cm.Buf.Length) cm.Pos = 0;
                    sum += o;
                }
                for (int k = 0; k < ap.Length; k++)
                {
                    var a = ap[k];
                    float bo = a.Buf[a.Pos];
                    float o = -sum + bo;
                    a.Buf[a.Pos] = sum + bo * 0.5f;
                    if (++a.Pos >= a.Buf.Length) a.Pos = 0;
                    sum = o;
                }
                buf[i] = x + sum * 3f * mix;
            }
        }
    }
}
