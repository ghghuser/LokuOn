using LokuOn.Core;

/// <summary>エフェクト(イコライザ・エコー・リバーブ)の計算のテスト。</summary>
static class EffectTests
{
    const int SR = 48000;

    static double ToneGainDb(EffectSetting s, double hz, int ch = 2)
    {
        var chain = new EffectChain(SR, ch); chain.Update(s);
        int total = SR, block = 480; var buf = new float[block * ch];
        double sumIn = 0, sumOut = 0; long n0 = 0; int count = 0;
        for (int off = 0; off < total; off += block)
        {
            for (int f = 0; f < block; f++)
                for (int c = 0; c < ch; c++) buf[f * ch + c] = (float)(0.1 * Math.Sin(2 * Math.PI * hz * (off + f) / SR));
            var inCopy = (float[])buf.Clone();
            chain.Process(buf, block);
            if (off >= SR / 2) { for (int j = 0; j < buf.Length; j++) { sumIn += inCopy[j] * inCopy[j]; sumOut += buf[j] * buf[j]; } }
            n0++; count++;
        }
        return 10 * Math.Log10(sumOut / sumIn);
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("エフェクト(イコライザ・エコー・リバーブ)");

        // 何もかけない/バイパスは、配列に触れない(ビット一致)
        {
            var src = new float[960]; var rnd = new Random(1); for (int i = 0; i < src.Length; i++) src[i] = (float)(rnd.NextDouble() * 2 - 1);
            foreach (var s in new[] { new EffectSetting(), new EffectSetting { EqOn = true, LowDb = 9, EchoOn = true, ReverbOn = true, Bypass = true } })
            {
                var chain = new EffectChain(SR, 2); chain.Update(s);
                var b = (float[])src.Clone(); chain.Process(b, 480);
                check(b.SequenceEqual(src), "効果なし/バイパスは素通し(ビット一致)");
            }
            check(!new EffectSetting().IsActive && new EffectSetting { EqOn = true, Bypass = true }.HasAny && !new EffectSetting { EqOn = true, Bypass = true }.IsActive, "IsActive/HasAny");
        }

        // イコライザ
        {
            double g0 = ToneGainDb(new EffectSetting { EqOn = true }, 1000);
            check(Math.Abs(g0) < 0.05, "EQオンでも設定が0dBなら変わらない", $"{g0:F2}");
            double lo = ToneGainDb(new EffectSetting { EqOn = true, LowDb = 12, LowHz = 120 }, 40);
            double loHi = ToneGainDb(new EffectSetting { EqOn = true, LowDb = 12, LowHz = 120 }, 4000);
            check(lo > 9 && lo < 13 && Math.Abs(loHi) < 0.5, "低域+12dB: 低い音が上がり、高い音は変わらない", $"{lo:F1} / {loHi:F2}");
            double hi = ToneGainDb(new EffectSetting { EqOn = true, HighDb = -12, HighHz = 6000 }, 12000);
            double hiLo = ToneGainDb(new EffectSetting { EqOn = true, HighDb = -12, HighHz = 6000 }, 300);
            check(hi < -9 && hi > -13 && Math.Abs(hiLo) < 0.5, "高域-12dB", $"{hi:F1} / {hiLo:F2}");
            double pk = ToneGainDb(new EffectSetting { EqOn = true, MidDb = 9, MidHz = 1000, MidQ = 1 }, 1000);
            double pkOff = ToneGainDb(new EffectSetting { EqOn = true, MidDb = 9, MidHz = 1000, MidQ = 1 }, 100);
            check(Math.Abs(pk - 9) < 0.5 && Math.Abs(pkOff) < 1.5, "中域+9dB(中心で+9、離れると変わらない)", $"{pk:F2} / {pkOff:F2}");
            double h50 = ToneGainDb(new EffectSetting { EqOn = true, HpfHz = 200 }, 50);
            double h2k = ToneGainDb(new EffectSetting { EqOn = true, HpfHz = 200 }, 2000);
            check(h50 < -20 && Math.Abs(h2k) < 0.3, "ローカット200Hz: 50Hzは大きく減り、2kHzは変わらない", $"{h50:F1} / {h2k:F2}");
            double mono = ToneGainDb(new EffectSetting { EqOn = true, MidDb = 9 }, 1000, 1);
            check(Math.Abs(mono - 9) < 0.5, "モノラルでも同じ", $"{mono:F2}");
            double rate = ToneGainDb(new EffectSetting { EqOn = true, LowDb = 12 }, 40);
            // 96kHz でも同様に動く
            var chain = new EffectChain(96000, 2); chain.Update(new EffectSetting { EqOn = true, HighDb = -12, HighHz = 6000 });
            var buf = new float[960 * 2]; double si = 0, so = 0;
            for (int off = 0; off < 96000; off += 960)
            {
                for (int f = 0; f < 960; f++) { float v = (float)(0.1 * Math.Sin(2 * Math.PI * 20000 * (off + f) / 96000.0)); buf[f * 2] = v; buf[f * 2 + 1] = v; }
                var cp = (float[])buf.Clone(); chain.Process(buf, 960);
                if (off > 48000) for (int j = 0; j < buf.Length; j++) { si += cp[j] * cp[j]; so += buf[j] * buf[j]; }
            }
            check(10 * Math.Log10(so / si) < -9, "96kHzでも高域カットが効く", $"{10 * Math.Log10(so / si):F1}");
        }

        // エコー
        {
            var chain = new EffectChain(SR, 2);
            chain.Update(new EffectSetting { EchoOn = true, EchoMs = 100, EchoFeedback = 0.5, EchoMix = 0.5 });
            int frames = SR; var buf = new float[frames * 2]; buf[0] = 1f; buf[1] = 1f;
            for (int off = 0; off < frames; off += 480) { var seg = new float[960]; Array.Copy(buf, off * 2, seg, 0, 960); chain.Process(seg, 480); Array.Copy(seg, 0, buf, off * 2, 960); }
            int d = 4800;
            check(Math.Abs(buf[0] - 1f) < 1e-6f, "エコー: 元の音はそのまま");
            check(Math.Abs(buf[d * 2] - 0.5f) < 1e-4f && Math.Abs(buf[(d * 2) * 2] - 0.25f) < 1e-4f, "エコー: 100ms後に0.5倍、さらに200ms後に0.25倍(フィードバック)", $"{buf[d * 2]} {buf[d * 4]}");
            check(buf[(d - 1) * 2] == 0f && buf[(d + 1) * 2] == 0f, "エコー: 間は無音");
        }

        // リバーブ
        {
            var chain = new EffectChain(SR, 2);
            chain.Update(new EffectSetting { ReverbOn = true, ReverbRoom = 0.7, ReverbDamp = 0.4, ReverbMix = 0.5 });
            int frames = SR * 6; var buf = new float[frames * 2]; buf[0] = 0.5f; buf[1] = 0.5f;
            for (int off = 0; off < frames; off += 480) { var seg = new float[960]; Array.Copy(buf, off * 2, seg, 0, 960); chain.Process(seg, 480); Array.Copy(seg, 0, buf, off * 2, 960); }
            double E(double a, double b) { double e = 0; for (int i = (int)(a * SR) * 2; i < (int)(b * SR) * 2; i++) e += buf[i] * buf[i]; return e; }
            check(Math.Abs(buf[0] - 0.5f) < 0.01f, "リバーブ: 最初の音は元のまま(残響は遅れて来る)", $"{buf[0]}");
            check(E(0.05, 0.5) > 1e-6, "リバーブ: 残響が出る");
            check(E(4.0, 6.0) < E(0.5, 1.5) * 0.2, "リバーブ: 時間とともに減衰する");
            check(buf.All(float.IsFinite), "リバーブ: 値がおかしくならない");
            // 大きな雑音を長く入れても発散しない
            var c2 = new EffectChain(SR, 2); c2.Update(new EffectSetting { EqOn = true, LowDb = 15, HighDb = 15, MidDb = 15, EchoOn = true, EchoFeedback = 0.9, EchoMix = 1, ReverbOn = true, ReverbRoom = 1, ReverbDamp = 0, ReverbMix = 1 });
            var rnd = new Random(3); var nb = new float[960]; float mx = 0; bool fin = true;
            for (int k = 0; k < 1000; k++)
            {
                for (int j = 0; j < nb.Length; j++) nb[j] = (float)(rnd.NextDouble() * 2 - 1);
                c2.Process(nb, 480);
                foreach (var v in nb) { if (!float.IsFinite(v)) fin = false; mx = Math.Max(mx, Math.Abs(v)); }
            }
            check(fin && mx < 200, "最大設定の雑音10秒でも発散しない", $"max={mx:F1}");
        }

        // 設定の変更を途中で反映できる / オフ→オンで残響が残らない
        {
            var chain = new EffectChain(SR, 2);
            chain.Update(new EffectSetting { EchoOn = true, EchoMs = 50, EchoMix = 1, EchoFeedback = 0.8 });
            var b = new float[960]; for (int i = 0; i < b.Length; i++) b[i] = 0.5f;
            for (int k = 0; k < 20; k++) chain.Process(b, 480);
            chain.Update(new EffectSetting());
            chain.Process(b, 480);
            chain.Update(new EffectSetting { EchoOn = true, EchoMs = 50, EchoMix = 1, EchoFeedback = 0.8 });
            var z = new float[960]; chain.Process(z, 480);
            check(z.All(v => v == 0f), "オフにしてからオンに戻すと、前のエコーが残らない");
        }

        // プリセット(ini・JSON)への保存と旧プリセット
        {
            var tmp = Path.Combine(Path.GetTempPath(), "lokuon-fx-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            var p = new Preset();
            p.Effects[1] = new EffectSetting { EqOn = true, HpfHz = 100, LowDb = 3.5, MidDb = -4, MidHz = 2500, MidQ = 2, EchoOn = true, EchoMs = 420, EchoFeedback = 0.5, EchoMix = 0.4, ReverbOn = true, ReverbRoom = 0.8, ToRecording = true, ToOutput = false, Bypass = true };
            string ini = Path.Combine(tmp, "a.ini");
            SettingsStore.Save(ini, new AppSettings { Current = p });
            string text = File.ReadAllText(ini);
            var r = SettingsStore.Load(ini).Current.Effects;
            check(r[1].SameSound(p.Effects[1]) && r[1].ToRecording && !r[1].ToOutput, "エフェクト設定がiniに保存・復元される");
            check(r[0].IsDefault && r[2].IsDefault && !text.Contains("Fx1EqOn"), "初期値のままの入力はiniに書かない");
            PresetStore.Save(tmp, new Preset { Name = "fx", Effects = p.Effects });
            var lp = PresetStore.LoadAll(tmp, out _).First(x => x.Name == "fx");
            check(lp.Effects[1].SameSound(p.Effects[1]) && lp.Effects[0].IsDefault, "エフェクト設定がプリセットのJSONに入る");
            var cl = lp.Clone(); cl.Effects[1].LowDb = -9;
            check(lp.Effects[1].LowDb == 3.5, "Cloneはエフェクトも別物にコピーする");
            File.WriteAllText(Path.Combine(tmp, "旧.json"), "{ \"Format\": \"Mp3\" }");
            var oldp = PresetStore.LoadAll(tmp, out _).First(x => x.Name == "旧");
            check(oldp.Effects.Length == 4 && oldp.Effects.All(e => e.IsDefault), "エフェクトの項目がない旧プリセットは「かけない」で読める");
        }

        // 出力への組み込み: 出力に反映する設定のときだけかかる
        {
            foreach (bool toOut in new[] { true, false })
            {
                var src = new SourceBuffer(48000, 2, 1.0); var tap = new SourceBuffer(48000, 2, 1.0); src.AddTap(tap);
                var rend = new OutputRenderer(new[] { src }, new SourceBuffer?[] { tap }, 48000, 2);
                rend.SetEffects(new EffectSetting?[] { new EffectSetting { EchoOn = true, EchoMs = 100, EchoMix = 1, EchoFeedback = 0, ToOutput = toOut } });
                var d = new float[480 * 2]; for (int i = 0; i < d.Length; i++) d[i] = 0.3f;
                var o = new float[480 * 2]; float tail = 0;
                // 0.2秒の音(20ブロック)を入れ、そのあとは無音。元の音が終わったあとの区間に、エコー(100ms=10ブロック遅れ)が出るかを見る
                for (int k = 0; k < 10; k++) src.Push(d, 0, 480);
                for (int k = 0; k < 60; k++)
                {
                    if (k == 10) Array.Clear(d, 0, d.Length);
                    src.Push(d, 0, 480);
                    rend.Render(o, 0, 480);
                    if (k >= 24 && k <= 28) foreach (var v in o) tail = Math.Max(tail, Math.Abs(v));
                }
                check(toOut ? tail > 0.05f : tail < 0.001f, toOut ? "出力に反映: エコーが出力にかかる" : "出力に反映しない: 素の音のまま(エコーなし)", $"tail={tail}");
            }
        }

        // 設定の保存・正規化
        {
            var e = new EffectSetting { EqOn = true, HpfHz = 5000, LowDb = 99, MidQ = double.NaN, EchoOn = true, EchoMs = 5, EchoFeedback = 5, ReverbOn = true, ReverbMix = -1, ToRecording = true, ToOutput = false };
            e.Normalize();
            check(e.HpfHz == 400 && e.LowDb == 15 && e.MidQ == 1.0 && e.EchoMs == 20 && e.EchoFeedback == 0.9 && e.ReverbMix == 0, "範囲外の値は使える値に直る");
            var d = new EffectSetting();
            check(!d.ToRecording && d.ToOutput, "既定: 録音は素の音(エフェクトなし)、出力には反映");
        }
    }
}
