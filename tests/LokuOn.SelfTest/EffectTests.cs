using LokuOn.Core;

/// <summary>エフェクト(イコライザ・エコー・リバーブ)の計算のテスト。</summary>
static class EffectTestsExt { }

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

    static EffectSetting WithFx(EffectSetting e) { e.EchoOn = true; e.ReverbOn = true; e.Bypass = true; return e; }

    static EffectSetting Normalized(EffectSetting e) { e.Normalize(); return e; }

    static EffectSetting Eq(params (int Band, double Db)[] bs)
    {
        var e = new EffectSetting { EqOn = true };
        foreach (var (b, db) in bs) e.Bands[b] = db;
        return e;
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("エフェクト(イコライザ・エコー・リバーブ)");

        // 何もかけない/バイパスは、配列に触れない(ビット一致)
        {
            var src = new float[960]; var rnd = new Random(1); for (int i = 0; i < src.Length; i++) src[i] = (float)(rnd.NextDouble() * 2 - 1);
            foreach (var s in new[] { new EffectSetting(), WithFx(Eq((1, 9))) })
            {
                var chain = new EffectChain(SR, 2); chain.Update(s);
                var b = (float[])src.Clone(); chain.Process(b, 480);
                check(b.SequenceEqual(src), "効果なし/バイパスは素通し(ビット一致)");
            }
            check(!new EffectSetting().IsActive && new EffectSetting { EqOn = true, Bypass = true }.HasAny && !new EffectSetting { EqOn = true, Bypass = true }.IsActive, "IsActive/HasAny");
        }

        // イコライザ(10バンド)
        {
            double g0 = ToneGainDb(Eq(), 1000);
            check(Math.Abs(g0) < 0.05, "EQオンでも全帯域0dBなら変わらない", $"{g0:F2}");
            double b1k = ToneGainDb(Eq((5, 9)), 1000);
            double b1kFar = ToneGainDb(Eq((5, 9)), 100);
            double b1kFar2 = ToneGainDb(Eq((5, 9)), 10000);
            check(Math.Abs(b1k - 9) < 0.5 && Math.Abs(b1kFar) < 1.0 && Math.Abs(b1kFar2) < 1.0, "1kHz帯域+9dB: 中心で+9、遠い音はほぼ変わらない", $"{b1k:F2} / {b1kFar:F2} / {b1kFar2:F2}");
            double b31 = ToneGainDb(Eq((0, 12)), 31.25);
            check(Math.Abs(b31 - 12) < 1.0, "31Hz帯域+12dB", $"{b31:F2}");
            double b16 = ToneGainDb(Eq((9, -12)), 16000);
            check(Math.Abs(b16 + 12) < 1.0, "16kHz帯域-12dB", $"{b16:F2}");
            // 複数帯域: 画面のカーブ(計算)と実際の音が一致する
            var multi = Eq((1, 6), (3, -4), (6, 5), (8, -7));
            foreach (double hz in new[] { 62.5, 150, 250, 1000, 2000, 5000, 8000 })
            {
                double meas = ToneGainDb(multi, hz), calc = EqMath.ResponseDb(multi.Bands, hz);
                check(Math.Abs(meas - calc) < 0.4, $"カーブ表示の計算が実際の音と一致 {hz}Hz", $"{meas:F2} vs {calc:F2}");
            }
            double mono = ToneGainDb(Eq((5, 9)), 1000, 1);
            check(Math.Abs(mono - 9) < 0.5, "モノラルでも同じ", $"{mono:F2}");
            // 96kHz でも同様に動く
            var chain = new EffectChain(96000, 2); chain.Update(Eq((9, -12)));
            var buf = new float[960 * 2]; double si = 0, so = 0;
            for (int off = 0; off < 96000; off += 960)
            {
                for (int f = 0; f < 960; f++) { float v = (float)(0.1 * Math.Sin(2 * Math.PI * 16000 * (off + f) / 96000.0)); buf[f * 2] = v; buf[f * 2 + 1] = v; }
                var cp = (float[])buf.Clone(); chain.Process(buf, 960);
                if (off > 48000) for (int j = 0; j < buf.Length; j++) { si += cp[j] * cp[j]; so += buf[j] * buf[j]; }
            }
            check(Math.Abs(10 * Math.Log10(so / si) + 12) < 1.0, "96kHzでも16kHz帯域が効く", $"{10 * Math.Log10(so / si):F1}");
            // ローカット
            double h50 = ToneGainDb(new EffectSetting { EqOn = true, HpfHz = 200 }, 50);
            double h200 = ToneGainDb(new EffectSetting { EqOn = true, HpfHz = 200 }, 200);
            double h2k = ToneGainDb(new EffectSetting { EqOn = true, HpfHz = 200 }, 2000);
            check(h50 < -20 && Math.Abs(h200 + 3) < 1.0 && Math.Abs(h2k) < 0.3, "ローカット200Hz: 50Hzは大きく減り、200Hzは-3dB、2kHzは変わらない", $"{h50:F1} / {h200:F1} / {h2k:F2}");
            var hm = new EffectSetting { EqOn = true, HpfHz = 120, Bands = new double[] { 0, 0, 3, 0, 0, 4, 0, 0, 0, -3 } };
            foreach (double hz in new[] { 60.0, 120, 300, 1000, 12000 })
            {
                double meas = ToneGainDb(hm, hz), calc = EqMath.ResponseDb(hm.Bands, hz, 48000, 120);
                check(Math.Abs(meas - calc) < 0.4, $"ローカットを含むカーブの計算が実際の音と一致 {hz}Hz", $"{meas:F2} vs {calc:F2}");
            }
            check(new EffectSetting { EqOn = true, HpfHz = 5 }.Clone() is var cn && Normalized(cn).HpfHz == 0 && Normalized(new EffectSetting { HpfHz = 9999 }).HpfHz == 400, "ローカットの範囲: 20未満=なし、最大400");
            // 帯域の設定の途中変更
            var live = new EffectChain(SR, 2); live.Update(Eq((5, 6)));
            var lb = new float[960]; for (int i = 0; i < lb.Length; i++) lb[i] = 0.1f;
            for (int k = 0; k < 10; k++) live.Process(lb, 480);
            live.Update(Eq((5, -6)));
            for (int k = 0; k < 10; k++) live.Process(lb, 480);
            check(lb.All(float.IsFinite), "帯域の設定を途中で変えても壊れない");
            // 初期設定の一覧
            check(EqBands.Presets.All(p => p.Db.Length == EqBands.Count && p.Db.All(v => Math.Abs(v) <= EqBands.MaxDb)) && EqBands.Presets[0].Db.All(v => v == 0), "EQの初期設定の一覧は範囲内で、先頭はフラット");
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
            var c2 = new EffectChain(SR, 2); c2.Update(new EffectSetting { EqOn = true, Bands = new double[] { 12, 12, 12, 12, 12, 12, 12, 12, 12, 12 }, EchoOn = true, EchoFeedback = 0.9, EchoMix = 1, ReverbOn = true, ReverbRoom = 1, ReverbDamp = 0, ReverbMix = 1 });
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
            p.Effects[1] = new EffectSetting { EqOn = true, HpfHz = 85, Bands = new double[] { 3.5, 0, -4, 1.5, 0, 0, 2, 0, -6, 12 }, EchoOn = true, EchoMs = 420, EchoFeedback = 0.5, EchoMix = 0.4, ReverbOn = true, ReverbRoom = 0.8, ToRecording = true, ToOutput = false, Bypass = true };
            string ini = Path.Combine(tmp, "a.ini");
            SettingsStore.Save(ini, new AppSettings { Current = p });
            string text = File.ReadAllText(ini);
            var r = SettingsStore.Load(ini).Current.Effects;
            check(r[1].HpfHz == 85 && r[1].SameSound(p.Effects[1]) && r[1].ToRecording && !r[1].ToOutput, "エフェクト設定がiniに保存・復元される");
            check(r[0].IsDefault && r[2].IsDefault && !text.Contains("Fx1EqOn"), "初期値のままの入力はiniに書かない");
            PresetStore.Save(tmp, new Preset { Name = "fx", Effects = p.Effects });
            var lp = PresetStore.LoadAll(tmp, out _).First(x => x.Name == "fx");
            check(lp.Effects[1].SameSound(p.Effects[1]) && lp.Effects[0].IsDefault, "エフェクト設定がプリセットのJSONに入る");
            var cl = lp.Clone(); cl.Effects[1].Bands[0] = -9;
            check(lp.Effects[1].Bands[0] == 3.5, "Cloneはエフェクトも別物にコピーする");
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
            var e = new EffectSetting { EqOn = true, Bands = new double[] { 99, -99, double.NaN, 1 }, EchoOn = true, EchoMs = 5, EchoFeedback = 5, ReverbOn = true, ReverbMix = -1, ToRecording = true, ToOutput = false };
            e.Normalize();
            check(e.Bands.Length == 10 && e.Bands[0] == 12 && e.Bands[1] == -12 && e.Bands[2] == 0 && e.Bands[3] == 1 && e.Bands[9] == 0 && e.EchoMs == 20 && e.EchoFeedback == 0.9 && e.ReverbMix == 0, "範囲外の値は使える値に直る");
            var d = new EffectSetting();
            check(!d.ToRecording && d.ToOutput, "既定: 録音は素の音(エフェクトなし)、出力には反映");
        }
    }
}
