using LokuOn.Core;

/// <summary>出力ごとのエフェクト(ローカット・EQ・リミッター)のテスト。</summary>
static class OutputFxTests
{
    const int SR = 48000;

    static double Db(double lin) => 20 * Math.Log10(lin);

    public static void Run(CheckFn check, Action<string> section)
    {
        section("出力ごとのエフェクト(最終ミックスのEQ・リミッター)");

        // リミッター: 上限を超えない・小さい音は変えない・左右連動・遅延
        {
            double ceilDb = -6; double ceil = Math.Pow(10, ceilDb / 20);
            var fx = new OutputFxChain(SR, 2);
            fx.Update(new OutputFxSetting { LimiterOn = true, CeilingDb = ceilDb, ReleaseMs = 100 });
            var buf = new float[480 * 2]; double peak = 0, quietPeak = 0; double ratioErr = 0;
            for (int blk = 0; blk < 300; blk++)
            {
                for (int f = 0; f < 480; f++)
                {
                    long t = blk * 480L + f;
                    // 0〜1秒: 振幅1.8(L)と0.9(R)の大きな音 / 1〜2秒: 小さい音 0.1 / 2〜3秒: 突然のバースト
                    double amp = t < SR ? 1.8 : t < 2 * SR ? 0.1 : (t % 4800 < 24 ? 3.0 : 0.05);
                    double s = Math.Sin(2 * Math.PI * 440 * t / SR);
                    buf[f * 2] = (float)(amp * s); buf[f * 2 + 1] = (float)(amp * 0.5 * s);
                }
                fx.Process(buf, 480);
                for (int f = 0; f < 480; f++)
                {
                    long t = blk * 480L + f;
                    double l = Math.Abs(buf[f * 2]), r = Math.Abs(buf[f * 2 + 1]);
                    if (t > 200) peak = Math.Max(peak, Math.Max(l, r));
                    if (t >= SR + 24000 && t < 2 * SR) quietPeak = Math.Max(quietPeak, l);
                    if (l > 0.05) ratioErr = Math.Max(ratioErr, Math.Abs(r / l - 0.5));
                }
            }
            check(peak <= ceil * 1.001, $"リミッター: 振幅1.8・3.0の音でも上限({ceilDb}dB)を超えない", $"最大 {Db(peak):F2} dB");
            check(Math.Abs(quietPeak - 0.1) < 0.002, "リミッター: 上限より小さい音は(戻り終われば)変わらない", $"{quietPeak:F4}");
            check(ratioErr < 0.002, "リミッター: 左右は連動して下がる(定位が動かない)", $"{ratioErr:F4}");
        }
        {
            // 遅延: インパルスが先読みぶん(1.5ms)遅れて出る
            var lim = new Limiter(SR, 1);
            lim.Set(0, 100);
            var b = new float[480]; b[0] = 0.5f;
            lim.Process(b, 480);
            int idx = Array.FindIndex(b, v => v != 0);
            check(idx == lim.LatencyFrames && Math.Abs(b[idx] - 0.5f) < 1e-6, "リミッター: 先読みぶんだけ遅れ、上限以下の音はそのまま", $"位置 {idx} / 遅延 {lim.LatencyFrames} / 値 {b[idx]}");
            // 戻り: 大きな音のあと、設定時間で元の音量に戻る
            var fx = new OutputFxChain(SR, 1); fx.Update(new OutputFxSetting { LimiterOn = true, CeilingDb = -12, ReleaseMs = 200 });
            var loud = Enumerable.Repeat(1.0f, 2400).ToArray(); fx.Process(loud, 2400);
            double gAt(int afterFrames)
            {
                var q = Enumerable.Repeat(0.1f, afterFrames).ToArray(); fx.Process(q, afterFrames); return q[^1] / 0.1;
            }
            double g1 = gAt(480);            // 10ms後: まだ下がっている
            double g2 = gAt(48000);          // 1秒後(約5倍の時間): ほぼ戻る
            check(g1 < 0.7 && g2 > 0.98, "リミッター: 大きな音のあと、戻り時間をかけて元の音量に戻る", $"10ms後 {g1:F2} / 約1秒後 {g2:F3}");
        }

        // 何もかけない・バイパスのときは配列に触れない(ビット一致)
        {
            var src = new float[960]; for (int i = 0; i < src.Length; i++) src[i] = (float)Math.Sin(i * 0.1) * 1.7f;
            foreach (var st in new[] { new OutputFxSetting(), new OutputFxSetting { Bypass = true, EqOn = true, HpfHz = 300, LimiterOn = true, Bands = new double[] { 12, 12, 12, 12, 12, 12, 12, 12, 12, 12 } } })
            {
                var fx = new OutputFxChain(SR, 2); fx.Update(st);
                var b = (float[])src.Clone(); fx.Process(b, 480);
                check(b.SequenceEqual(src), st.Bypass ? "バイパス中は音を一切変えない(ビット一致)" : "初期設定(何もしない)は音を一切変えない(ビット一致)");
            }
        }

        // EQ(ローカット・グラフィック・パラメトリック)も出力用にかかる
        {
            double Gain(OutputFxSetting s, double hz)
            {
                var fx = new OutputFxChain(SR, 2); fx.Update(s);
                var b = new float[480 * 2]; double si = 0, so = 0;
                for (int blk = 0; blk < 100; blk++)
                {
                    for (int f = 0; f < 480; f++) { float v = (float)(0.1 * Math.Sin(2 * Math.PI * hz * (blk * 480 + f) / SR)); b[f * 2] = v; b[f * 2 + 1] = v; }
                    var cp = (float[])b.Clone(); fx.Process(b, 480);
                    if (blk >= 50) for (int j = 0; j < b.Length; j++) { si += cp[j] * cp[j]; so += b[j] * b[j]; }
                }
                return 10 * Math.Log10(so / si);
            }
            var bands = new double[EqBands.Count]; bands[5] = 9;
            check(Math.Abs(Gain(new OutputFxSetting { EqOn = true, Bands = bands }, 1000) - 9) < 0.6, "出力のグラフィックEQが効く(1kHz +9dB)");
            check(Gain(new OutputFxSetting { HpfHz = 200 }, 50) < -20 && Math.Abs(Gain(new OutputFxSetting { HpfHz = 200 }, 2000)) < 0.3, "出力のローカットが効く");
            var pr = new OutputFxSetting { ParamOn = true }; pr.Params[2].Freq = 3000; pr.Params[2].Db = -9; pr.Params[2].Q = 4;
            check(Math.Abs(Gain(pr, 3000) + 9) < 0.6 && Math.Abs(Gain(pr, 600)) < 1.0, "出力のパラメトリックEQが効く(3kHz -9dB)");
        }

        // 出力デバイスの音づくりに組み込まれている: 全体音量(+12dB)のあと、切り詰めの前にかかる
        {
            double RunPeak(OutputFxSetting? fxs, out bool clipped)
            {
                var src = new SourceBuffer(48000, 2, 1.0); var tap = new SourceBuffer(48000, 2, 1.0); src.AddTap(tap);
                var r = new OutputRenderer(new[] { src }, new SourceBuffer?[] { tap }, 48000, 2);
                r.Bus.MasterGain = 4f;           // +12dB
                if (fxs != null) r.SetMasterFx(fxs);
                var d = new float[480 * 2]; var o = new float[480 * 2]; double peak = 0; clipped = false; long t = 0;
                for (int k = 0; k < 20; k++) PushSine(src, d, ref t);
                long t2 = t;
                for (int k = 0; k < 60; k++)
                {
                    PushSine(src, d, ref t2);
                    r.Render(o, 0, 480);
                    var pk = r.ReadPeaks(); if (k > 20) { peak = Math.Max(peak, Math.Max(pk.L, pk.R)); clipped |= pk.Clipped; }
                }
                return peak;
            }
            double pNo = RunPeak(null, out bool cNo);
            double pLim = RunPeak(new OutputFxSetting { LimiterOn = true, CeilingDb = -3 }, out bool cLim);
            check(pNo > 0.999 && cNo, "(参考)エフェクトなしで+12dBにすると切り詰められる", $"{pNo:F3}");
            check(pLim <= Math.Pow(10, -3 / 20.0) * 1.002 && !cLim && pLim > 0.6, "出力のリミッターが全体音量のあとにかかり、切り詰めずに上限へ収める", $"最大 {Db(pLim):F2} dB / 切り詰め {cLim}");
        }

        // 設定の保存・復元(ini / JSON)・旧プリセット・Clone
        {
            var tmp = Path.Combine(Path.GetTempPath(), "lokuon-ofx-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            var p = new Preset();
            p.Outputs.Add(new OutputSetting { Name = "第2" });
            p.Outputs[1].Fx = new OutputFxSetting { EqOn = true, HpfHz = 80, LimiterOn = true, CeilingDb = -2.5, ReleaseMs = 300, Bypass = true, ParamOn = true };
            p.Outputs[1].Fx.Bands[3] = -4; p.Outputs[1].Fx.Params[1].Freq = 1500; p.Outputs[1].Fx.Params[1].Db = 5;
            string ini = Path.Combine(tmp, "a.ini");
            SettingsStore.Save(ini, new AppSettings { Current = p });
            string text = File.ReadAllText(ini);
            var r = SettingsStore.Load(ini).Current.Outputs;
            check(r.Count == 2 && r[1].Fx.SameSound(p.Outputs[1].Fx) && r[1].Fx.CeilingDb == -2.5, "出力のエフェクト設定がiniに保存・復元される");
            check(r[0].Fx.IsDefault && !text.Contains("Output1FxEqOn"), "初期設定のままの出力はiniに書かない");
            PresetStore.Save(tmp, new Preset { Name = "ofx", Outputs = p.Outputs });
            var lp = PresetStore.LoadAll(tmp, out _).First(x => x.Name == "ofx");
            check(lp.Outputs[1].Fx.SameSound(p.Outputs[1].Fx) && lp.Outputs[0].Fx.IsDefault, "出力のエフェクト設定がプリセットのJSONに入る");
            var cl = lp.Clone(); cl.Outputs[1].Fx.Bands[3] = 7;
            check(lp.Outputs[1].Fx.Bands[3] == -4, "Cloneは出力のエフェクトも別物にコピーする");
            File.WriteAllText(Path.Combine(tmp, "旧.json"), "{ \"Format\": \"Mp3\", \"Outputs\": [ { \"Name\": \"古い\" } ] }");
            var oldp = PresetStore.LoadAll(tmp, out _).First(x => x.Name == "旧");
            check(oldp.Outputs.Count == 1 && oldp.Outputs[0].Fx != null && oldp.Outputs[0].Fx.IsDefault, "エフェクトの項目がない旧プリセットの出力は「かけない」で読める");
            var bad = new OutputFxSetting { CeilingDb = 99, ReleaseMs = -5, HpfHz = 9999 }; bad.Normalize();
            check(bad.CeilingDb == 0 && bad.ReleaseMs == 20 && bad.HpfHz == 400, "範囲外の値は範囲内に直される");
        }
    }

    static void PushSine(SourceBuffer src, float[] d, ref long t)
    {
        for (int f = 0; f < 480; f++) { float v = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * (t + f) / 48000.0)); d[f * 2] = v; d[f * 2 + 1] = v; }
        t += 480;
        src.Push(d, 0, 480);
    }
}
