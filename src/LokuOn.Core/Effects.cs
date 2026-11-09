namespace LokuOn.Core;

/// <summary>入力1本ぶんのエフェクト設定(ノイズゲート → ローカット/イコライザ → エコー → リバーブ の順に通す)。プリセットに保存される。</summary>
public sealed class EffectSetting
{
    // ---- 全体
    /// <summary>すべてバイパス(設定は残したまま、効果だけ切る)。</summary>
    public bool Bypass { get; set; }
    /// <summary>エフェクトをかけた音を録音に入れるか(既定: しない=録音は素の音)。</summary>
    public bool ToRecording { get; set; } = false;
    /// <summary>エフェクトをかけた音を出力に流すか(既定: する)。</summary>
    public bool ToOutput { get; set; } = true;

    // ---- ノイズゲート(いちばん最初にかかる。小さい音=ノイズを自動で下げ、声などの大きい音のときだけ通す)
    public bool GateOn { get; set; }
    /// <summary>この音量(dB)を超えると開く。-70〜-10。</summary>
    public double GateThresholdDb { get; set; } = -45;
    /// <summary>音が小さくなってから、閉じていく速さ(ミリ秒)。20〜1000。</summary>
    public double GateReleaseMs { get; set; } = 200;
    /// <summary>閉じたときに下げる量(dB)。6〜80。大きいほど無音に近くなる。</summary>
    public double GateReductionDb { get; set; } = 40;

    // ---- イコライザ(10バンドのグラフィックEQ。31Hz〜16kHz、各±12dB)
    public bool EqOn { get; set; }
    /// <summary>ローカット(Hz)。この周波数より低い音を削る(12dB/oct)。0=なし、20〜400。イコライザの前にかかる。</summary>
    public double HpfHz { get; set; } = 0;
    /// <summary>10個の帯域のゲイン(dB)。EqBands.Freqs の順。</summary>
    public double[] Bands { get; set; } = new double[EqBands.Count];

    // ---- パラメトリックEQ(4バンド。位置(周波数)・ゲイン・幅(Q)を自由に決める。グラフィックEQの後にかかる)
    public bool ParamOn { get; set; }
    public ParamBand[] Params { get; set; } = ParamBand.Defaults();

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


    /// <summary>効果が実際にかかるか(バイパスでなく、どれかがオン)。</summary>
    public bool IsActive => !Bypass && HasAny;
    /// <summary>設定が1つでも入っているか(FXボタンの色づけ用。バイパス中でも true)。</summary>
    public bool HasAny => GateOn || EqOn || HpfHz >= 20 || ParamOn || EchoOn || ReverbOn;

    public EffectSetting Clone()
    {
        var c = (EffectSetting)MemberwiseClone();
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
        GateThresholdDb = C(GateThresholdDb, -70, -10, -45); GateReleaseMs = C(GateReleaseMs, 20, 1000, 200); GateReductionDb = C(GateReductionDb, 6, 80, 40);
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
        s.Set(prefix + "GateOn", GateOn, C("ノイズゲート(閾値dB-70〜-10 / 戻りms / 下げる量dB)")); s.Set(prefix + "GateThresholdDb", GateThresholdDb); s.Set(prefix + "GateReleaseMs", GateReleaseMs); s.Set(prefix + "GateReductionDb", GateReductionDb);
        s.Set(prefix + "EqOn", EqOn);
        s.Set(prefix + "HpfHz", HpfHz, C("ローカット(Hz、0=なし)"));
        s.Set(prefix + "EqBands", string.Join(",", (Bands ?? new double[EqBands.Count]).Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))),
            C("イコライザ 31/62/125/250/500/1k/2k/4k/8k/16kHz のゲイン(dB、±12)"));
        s.Set(prefix + "ParamOn", ParamOn);
        s.Set(prefix + "ParamBands", string.Join(",", (Params ?? ParamBand.Defaults()).Select(b => b.ToText())),
            C("パラメトリックEQ 4バンドの 周波数Hz:ゲインdB:幅(Q)"));
        s.Set(prefix + "EchoOn", EchoOn); s.Set(prefix + "EchoMs", EchoMs); s.Set(prefix + "EchoFeedback", EchoFeedback); s.Set(prefix + "EchoMix", EchoMix);
        s.Set(prefix + "ReverbOn", ReverbOn); s.Set(prefix + "ReverbRoom", ReverbRoom); s.Set(prefix + "ReverbDamp", ReverbDamp); s.Set(prefix + "ReverbMix", ReverbMix);
    }

    public static EffectSetting ReadFrom(IniSection s, string prefix)
    {
        var d = new EffectSetting();
        var e = new EffectSetting
        {
            Bypass = s.GetBool(prefix + "Bypass", d.Bypass), ToRecording = s.GetBool(prefix + "ToRecording", d.ToRecording), ToOutput = s.GetBool(prefix + "ToOutput", d.ToOutput),
            GateOn = s.GetBool(prefix + "GateOn", d.GateOn), GateThresholdDb = s.GetDouble(prefix + "GateThresholdDb", d.GateThresholdDb),
            GateReleaseMs = s.GetDouble(prefix + "GateReleaseMs", d.GateReleaseMs), GateReductionDb = s.GetDouble(prefix + "GateReductionDb", d.GateReductionDb),
            EqOn = s.GetBool(prefix + "EqOn", d.EqOn), ParamOn = s.GetBool(prefix + "ParamOn", d.ParamOn), HpfHz = s.GetDouble(prefix + "HpfHz", d.HpfHz),
            EchoOn = s.GetBool(prefix + "EchoOn", d.EchoOn), EchoMs = s.GetDouble(prefix + "EchoMs", d.EchoMs),
            EchoFeedback = s.GetDouble(prefix + "EchoFeedback", d.EchoFeedback), EchoMix = s.GetDouble(prefix + "EchoMix", d.EchoMix),
            ReverbOn = s.GetBool(prefix + "ReverbOn", d.ReverbOn), ReverbRoom = s.GetDouble(prefix + "ReverbRoom", d.ReverbRoom),
            ReverbDamp = s.GetDouble(prefix + "ReverbDamp", d.ReverbDamp), ReverbMix = s.GetDouble(prefix + "ReverbMix", d.ReverbMix),
        };
        var parts = s.GetString(prefix + "EqBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < EqBands.Count; i++)
            e.Bands[i] = i < parts.Length && double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        var pp = s.GetString(prefix + "ParamBands").Split(',', StringSplitOptions.TrimEntries);
        for (int i = 0; i < e.Params.Length && i < pp.Length; i++) ParamBand.TryParse(pp[i], e.Params[i]);
        e.Normalize();
        return e;
    }

    private bool SameBands(EffectSetting o)
    {
        if (Bands == null || o.Bands == null) return Bands == o.Bands;
        if (Bands.Length != o.Bands.Length) return false;
        for (int i = 0; i < Bands.Length; i++) if (Bands[i] != o.Bands[i]) return false;
        return true;
    }

    private bool SameParams(EffectSetting o)
    {
        if (Params == null || o.Params == null) return Params == o.Params;
        if (Params.Length != o.Params.Length) return false;
        for (int i = 0; i < Params.Length; i++) if (!Params[i].Same(o.Params[i])) return false;
        return true;
    }

    /// <summary>効果の中身(オン/オフと数値)が同じか。録音・出力への反映先の設定は比べない。</summary>
    public bool SameSound(EffectSetting o) =>
        Bypass == o.Bypass && GateOn == o.GateOn && GateThresholdDb == o.GateThresholdDb && GateReleaseMs == o.GateReleaseMs && GateReductionDb == o.GateReductionDb && EqOn == o.EqOn && HpfHz == o.HpfHz && SameBands(o) && ParamOn == o.ParamOn && SameParams(o) && EchoOn == o.EchoOn && EchoMs == o.EchoMs && EchoFeedback == o.EchoFeedback
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

    // --- イコライザ(10バンド。各帯域はオクターブ幅のピーキングBiQuad)
    private sealed class Bq
    {
        public double B0 = 1, B1, B2, A1, A2;
        public bool On;
        public double[] Z1 = Array.Empty<double>(), Z2 = Array.Empty<double>();
    }
    private readonly Bq[] bands = Enumerable.Range(0, EqBands.Count).Select(_ => new Bq()).ToArray();
    private readonly Bq hpfBq = new();
    private readonly Bq[] paramBq = Enumerable.Range(0, ParamBand.Count).Select(_ => new Bq()).ToArray();
    private readonly Bq[] eqAll;    // ローカット → 10バンド → パラメトリック の順
    private bool eqWasOn;

    // --- ノイズゲート
    private bool gateOn, gateWasOn, gateOpen = true;
    private float gateOpenLin, gateCloseLin, gateFloor = 0.01f, gateG = 1f, gateRel = 0.001f, gateAtk = 0.1f;
    private int gateHold, gateHoldFrames;

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
        eqAll = new[] { hpfBq }.Concat(bands).Concat(paramBq).ToArray();
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

        if (gateOn) ProcessGate(buf, frames);
        if (eqOnNow) ProcessEq(buf, frames);
        if (echoOn) ProcessEcho(buf, frames);
        if (revOn) ProcessReverb(buf, frames);
    }

    private bool eqOnNow;

    /// <summary>
    /// ノイズゲート。全チャンネルの最大の大きさで開閉を決め(左右連動)、開く: 3ms、閉じる: 設定の戻り時間でゲインを滑らかに動かす。
    /// 開く閾値より4dB低い値を下回り、さらに60ms続いたら閉じる(バタつき防止)。
    /// </summary>
    private void ProcessGate(float[] buf, int frames)
    {
        for (int f = 0, b = 0; f < frames; f++, b += ch)
        {
            float lv = 0;
            for (int c = 0; c < ch; c++) { float a = buf[b + c]; if (a < 0) a = -a; if (a > lv) lv = a; }
            if (lv >= gateOpenLin) { gateOpen = true; gateHold = gateHoldFrames; }
            else if (gateOpen)
            {
                if (lv >= gateCloseLin) gateHold = gateHoldFrames;
                else if (--gateHold <= 0) gateOpen = false;
            }
            if (gateOpen) gateG += (1f - gateG) * gateAtk;
            else { gateG += (gateFloor - gateG) * gateRel; if (gateG < gateFloor) gateG = gateFloor; }
            if (gateG < 1f) for (int c = 0; c < ch; c++) buf[b + c] *= gateG;
        }
    }

    private void ResetStates()
    {
        foreach (var b in eqAll) { Array.Clear(b.Z1); Array.Clear(b.Z2); }
        echo = null; combs = null; allpasses = null; echoWasOn = revWasOn = eqWasOn = false; gateWasOn = false; gateOpen = true; gateG = 1f;
    }

    // ------------------------------------------------------------ 設定の反映

    private void Rebuild(EffectSetting s)
    {
        // ノイズゲート
        gateOn = s.GateOn;
        if (s.GateOn)
        {
            gateOpenLin = (float)Math.Pow(10, s.GateThresholdDb / 20.0);
            gateCloseLin = (float)Math.Pow(10, (s.GateThresholdDb - 4) / 20.0);
            gateFloor = (float)Math.Pow(10, -s.GateReductionDb / 20.0);
            gateRel = (float)(1.0 - Math.Exp(-1.0 / (s.GateReleaseMs * 0.001 * sr / 4.6)));   // 戻り時間でおよそ -40dB 下がる速さ
            gateAtk = (float)(1.0 - Math.Exp(-1.0 / (0.003 * sr / 4.6)));
            gateHoldFrames = (int)(0.06 * sr);
            if (!gateWasOn) { gateOpen = true; gateG = 1f; gateHold = gateHoldFrames; }
        }
        gateWasOn = s.GateOn;

        // イコライザ(ローカット / グラフィック / パラメトリックは、それぞれ独立にオン・オフできる)
        bool hpfOn = s.HpfHz >= 20;
        eqOnNow = s.EqOn || hpfOn || s.ParamOn;
        if (eqOnNow)
        {
            if (!eqWasOn) { foreach (var b in eqAll) { b.Z1 = new double[ch]; b.Z2 = new double[ch]; } }
            hpfBq.On = hpfOn;
            if (hpfOn)
            {
                var (h0, h1, h2, ha1, ha2) = EqMath.HighPass(sr, s.HpfHz);
                hpfBq.B0 = h0; hpfBq.B1 = h1; hpfBq.B2 = h2; hpfBq.A1 = ha1; hpfBq.A2 = ha2;
            }
            for (int i = 0; i < bands.Length; i++)
            {
                double db = s.Bands[i];
                bands[i].On = s.EqOn && Math.Abs(db) > 0.01;
                if (bands[i].On)
                {
                    var (b0, b1, b2, a1, a2) = EqMath.Peaking(sr, EqBands.Freqs[i], EqBands.Q, db);
                    bands[i].B0 = b0; bands[i].B1 = b1; bands[i].B2 = b2; bands[i].A1 = a1; bands[i].A2 = a2;
                }
            }
            for (int i = 0; i < paramBq.Length; i++)
            {
                var pb = s.Params[i];
                paramBq[i].On = s.ParamOn && Math.Abs(pb.Db) > 0.01;
                if (paramBq[i].On)
                {
                    var (b0, b1, b2, a1, a2) = EqMath.Peaking(sr, pb.Freq, pb.Q, pb.Db);
                    paramBq[i].B0 = b0; paramBq[i].B1 = b1; paramBq[i].B2 = b2; paramBq[i].A1 = a1; paramBq[i].A2 = a2;
                }
            }
        }
        eqWasOn = eqOnNow;

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

    // ------------------------------------------------------------ 処理

    private void ProcessEq(float[] buf, int frames)
    {
        foreach (var b in eqAll)
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

/// <summary>パラメトリックEQの1バンド(ピーキング)。Db=0 なら効果なし。</summary>
public sealed class ParamBand
{
    public const int Count = 4;
    public static readonly string[] Names = { "低域", "中低域", "中高域", "高域" };
    public static readonly double[] DefaultFreqs = { 100, 500, 2000, 8000 };
    public const double MaxDb = 12, MinQ = 0.3, MaxQ = 8, DefaultQ = 1.0, MinHz = 20, MaxHz = 20000;

    public double Freq { get; set; } = 1000;
    public double Db { get; set; }
    public double Q { get; set; } = DefaultQ;

    public static ParamBand[] Defaults() => Enumerable.Range(0, Count).Select(i => new ParamBand { Freq = DefaultFreqs[i] }).ToArray();
    public ParamBand Clone() => new() { Freq = Freq, Db = Db, Q = Q };
    public bool Same(ParamBand o) => Freq == o.Freq && Db == o.Db && Q == o.Q;

    public void Normalize(int i)
    {
        Freq = double.IsFinite(Freq) ? Math.Clamp(Freq, MinHz, MaxHz) : DefaultFreqs[i];
        Db = double.IsFinite(Db) ? Math.Clamp(Db, -MaxDb, MaxDb) : 0;
        Q = double.IsFinite(Q) ? Math.Clamp(Q, MinQ, MaxQ) : DefaultQ;
    }

    public string ToText()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return Freq.ToString("0.#", ci) + ":" + Db.ToString("0.#", ci) + ":" + Q.ToString("0.##", ci);
    }

    public static void TryParse(string t, ParamBand into)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var a = t.Split(':', StringSplitOptions.TrimEntries);
        if (a.Length >= 1 && double.TryParse(a[0], System.Globalization.NumberStyles.Float, ci, out var f)) into.Freq = f;
        if (a.Length >= 2 && double.TryParse(a[1], System.Globalization.NumberStyles.Float, ci, out var d)) into.Db = d;
        if (a.Length >= 3 && double.TryParse(a[2], System.Globalization.NumberStyles.Float, ci, out var q)) into.Q = q;
    }
}

/// <summary>グラフィックEQの帯域(31Hz〜16kHz、オクターブ間隔)。</summary>
public static class EqBands
{
    public const int Count = 10;
    public const double MaxDb = 12;
    public const double Q = 1.41;   // 約1オクターブ幅
    public static readonly double[] Freqs = { 31.25, 62.5, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public static readonly string[] Labels = { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    /// <summary>よく使う設定(名前, 10帯域のdB)。先頭は「フラット」。</summary>
    public static readonly (string Name, double[] Db)[] Presets =
    {
        ("フラット", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        ("低音を強く", new double[] { 6, 6, 5, 3, 1, 0, 0, 0, 0, 0 }),
        ("高音を強く", new double[] { 0, 0, 0, 0, 0, 1, 2, 4, 5, 6 }),
        ("声を聞きやすく", new double[] { -6, -5, -3, -1, 1, 3, 4, 3, 1, 0 }),
        ("音楽/映画鑑賞向け", new double[] { 4, 5, 2, -1, -2, -3, -1, 3, 5, 5 }),
        ("ロック", new double[] { 4, 3, 2, 0, -2, -1, 1, 3, 4, 4 }),
        ("ポップ", new double[] { -1, 2, 3, 3, 2, 0, -1, -1, 1, 1 }),
        ("クラシック", new double[] { 0, 0, 0, 0, 0, 0, -2, -3, -3, -4 }),
        ("こもりを取る", new double[] { 0, 0, -2, -4, -3, 0, 1, 2, 2, 1 }),
        ("ノイズ(シャー音)を抑える", new double[] { 0, 0, 0, 0, 0, 0, -1, -3, -6, -9 }),
    };
}

/// <summary>BiQuad係数と周波数特性の計算(RBJ Audio EQ Cookbook)。</summary>
public static class EqMath
{
    /// <summary>ピーキングEQの係数 (b0, b1, b2, a1, a2)(a0で割ってある)。</summary>
    public static (double, double, double, double, double) Peaking(int sr, double f, double q, double db)
    {
        double A = Math.Pow(10, db / 40), w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
        double a0 = 1 + al / A;
        return ((1 + al * A) / a0, -2 * cs / a0, (1 - al * A) / a0, -2 * cs / a0, (1 - al / A) / a0);
    }

    /// <summary>ローカット(2次のバターワース相当)の係数 (b0, b1, b2, a1, a2)。</summary>
    public static (double, double, double, double, double) HighPass(int sr, double f)
    {
        double w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cs = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071);
        double a0 = 1 + al;
        return ((1 + cs) / 2 / a0, -(1 + cs) / a0, (1 + cs) / 2 / a0, -2 * cs / a0, (1 - al) / a0);
    }

    private static double MagDb(double b0, double b1, double b2, double a1, double a2, double c1, double c2)
    {
        double num = b0 * b0 + b1 * b1 + b2 * b2 + 2 * (b0 * b1 + b1 * b2) * c1 + 2 * b0 * b2 * c2;
        double den = 1 + a1 * a1 + a2 * a2 + 2 * (a1 + a1 * a2) * c1 + 2 * a2 * c2;
        return 10 * Math.Log10(Math.Max(1e-12, num / den));
    }

    /// <summary>ローカットと10バンドを全部通したときの、周波数 hz での音量変化(dB)。画面のカーブ表示用。hpfHz は 0=なし。</summary>
    public static double ResponseDb(double[] bands, double hz, int sr = 48000, double hpfHz = 0, ParamBand[]? param = null)
    {
        double total = 0, w = 2 * Math.PI * hz / sr, c1 = Math.Cos(w), c2 = Math.Cos(2 * w);
        if (hpfHz >= 20) { var (h0, h1, h2, ha1, ha2) = HighPass(sr, hpfHz); total += MagDb(h0, h1, h2, ha1, ha2, c1, c2); }
        for (int i = 0; i < EqBands.Count && i < bands.Length; i++)
        {
            if (Math.Abs(bands[i]) <= 0.01) continue;
            var (b0, b1, b2, a1, a2) = Peaking(sr, EqBands.Freqs[i], EqBands.Q, bands[i]);
            double num = b0 * b0 + b1 * b1 + b2 * b2 + 2 * (b0 * b1 + b1 * b2) * c1 + 2 * b0 * b2 * c2;
            double den = 1 + a1 * a1 + a2 * a2 + 2 * (a1 + a1 * a2) * c1 + 2 * a2 * c2;
            total += 10 * Math.Log10(Math.Max(1e-12, num / den));
        }
        if (param != null)
            foreach (var pb in param)
            {
                if (Math.Abs(pb.Db) <= 0.01) continue;
                var (b0, b1, b2, a1, a2) = Peaking(sr, pb.Freq, pb.Q, pb.Db);
                total += MagDb(b0, b1, b2, a1, a2, c1, c2);
            }
        return total;
    }
}
