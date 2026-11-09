using LokuOn.Core;

/// <summary>ノイズゲートのテスト。</summary>
static class GateTests
{
    const int SR = 48000;

    public static void Run(CheckFn check, Action<string> section)
    {
        section("ノイズゲート");
        var st = new EffectSetting { GateOn = true, GateThresholdDb = -40, GateReleaseMs = 100, GateReductionDb = 40 };
        var chain = new EffectChain(SR, 2); chain.Update(st);
        // 0〜1秒: 小さいノイズ(-60dB) / 1〜2秒: 大きい音(-12dB) / 2〜3秒: ノイズ / 3〜4秒: 閾値と閉じる閾値の間(-42dB)の音
        double Amp(long t) => t < SR ? 0.001 : t < 2 * SR ? 0.25 : t < 3 * SR ? 0.001 : 0.0079;
        var outPeak = new double[40];   // 0.1秒ごとの出力ピーク
        var buf = new float[480 * 2];
        for (int blk = 0; blk < 400; blk++)
        {
            for (int f = 0; f < 480; f++)
            {
                long t = blk * 480L + f; float v = (float)(Amp(t) * Math.Sin(2 * Math.PI * 440 * t / SR));
                buf[f * 2] = v; buf[f * 2 + 1] = v;
            }
            chain.Process(buf, 480);
            double pk = 0; for (int j = 0; j < buf.Length; j++) pk = Math.Max(pk, Math.Abs(buf[j]));
            int slot = blk / 10; if (slot < 40) outPeak[slot] = Math.Max(outPeak[slot], pk);
        }
        // 最初は開いた状態で始まり、ノイズだけが続くと閉じて -40dB 下がる(0.001 → 0.00001)
        check(outPeak[8] < 0.001 * 0.03, "ノイズだけが続くと閉じて、下げる量(40dB)だけ小さくなる", $"{20 * Math.Log10(outPeak[8] / 0.001):F1} dB");
        check(Math.Abs(outPeak[15] - 0.25) < 0.003, "大きい音が来ると開いて、音量はそのまま通る", $"{outPeak[15]:F4}");
        check(outPeak[10] > 0.2, "音が来てすぐ(0〜0.1秒)に、もう開いている(頭が切れない)", $"{outPeak[10]:F3}");
        check(outPeak[28] < 0.001 * 0.03, "大きい音のあとのノイズも、また閉じる", $"{outPeak[28]:F6}");

        // 開いたあとは、開く閾値より少し低い音(ヒステリシスの範囲内)では閉じない
        {
            var c2 = new EffectChain(SR, 1); c2.Update(new EffectSetting { GateOn = true, GateThresholdDb = -40, GateReleaseMs = 100, GateReductionDb = 40 });
            double min = 1; var b = new float[480];
            for (int blk = 0; blk < 300; blk++)
            {
                for (int f = 0; f < 480; f++)
                {
                    long t = blk * 480L + f;
                    double a = blk < 50 ? 0.2 : 0.0079;    // 最初に大きい音で開き、その後は -42dB(閉じる閾値 -44dB より上)
                    b[f] = (float)(a * Math.Sin(2 * Math.PI * 440 * t / SR));
                }
                c2.Process(b, 480);
                if (blk > 100) for (int f = 0; f < 480; f++) min = Math.Min(min, Math.Abs(b[f]) / 0.0079 + (Math.Abs(b[f]) > 0 ? 0 : 0));
            }
            double peakLate = 0; for (int f = 0; f < 480; f++) peakLate = Math.Max(peakLate, Math.Abs(b[f]));
            check(Math.Abs(peakLate - 0.0079) < 0.0002, "いったん開いたら、閾値より少し低い音では閉じない(バタつき防止)", $"{peakLate:F5}");
        }

        // 左右連動(片側だけ大きくても、両側が同じ量で開閉する)
        {
            var c3 = new EffectChain(SR, 2); c3.Update(st);
            var b = new float[480 * 2]; double err = 0;
            for (int blk = 0; blk < 200; blk++)
            {
                for (int f = 0; f < 480; f++)
                {
                    long t = blk * 480L + f; float s = (float)Math.Sin(2 * Math.PI * 440 * t / SR);
                    b[f * 2] = (blk < 100 ? 0.002f : 0.2f) * s; b[f * 2 + 1] = 0.001f * s;
                }
                var orig = (float[])b.Clone(); c3.Process(b, 480);
                if (blk > 120) for (int f = 0; f < 480; f++) if (Math.Abs(orig[f * 2]) > 0.05) err = Math.Max(err, Math.Abs(b[f * 2 + 1] / orig[f * 2 + 1] - b[f * 2] / orig[f * 2]));
            }
            check(err < 0.01, "ノイズゲートは左右連動で開閉する", $"{err:F4}");
        }

        // オフ・バイパスは音を変えない / 保存復元 / 範囲外
        {
            var src = new float[960]; for (int i = 0; i < src.Length; i++) src[i] = (float)Math.Sin(i * 0.05) * 0.0005f;
            foreach (var s2 in new[] { new EffectSetting(), new EffectSetting { GateOn = true, Bypass = true } })
            {
                var c = new EffectChain(SR, 2); c.Update(s2); var b = (float[])src.Clone(); c.Process(b, 480);
                check(b.SequenceEqual(src), s2.Bypass ? "ゲートもバイパス中は音を変えない(ビット一致)" : "ゲートがオフなら音を変えない(ビット一致)");
            }
            var tmp = Path.Combine(Path.GetTempPath(), "lokuon-gate-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            var p = new Preset(); p.Effects[1] = new EffectSetting { GateOn = true, GateThresholdDb = -52, GateReleaseMs = 350, GateReductionDb = 25 };
            string ini = Path.Combine(tmp, "g.ini"); SettingsStore.Save(ini, new AppSettings { Current = p });
            var r = SettingsStore.Load(ini).Current.Effects[1];
            check(r.GateOn && r.SameSound(p.Effects[1]) && r.GateThresholdDb == -52 && r.GateReleaseMs == 350 && r.GateReductionDb == 25, "ノイズゲートの設定がiniに保存・復元される");
            PresetStore.Save(tmp, new Preset { Name = "gt", Effects = p.Effects });
            check(PresetStore.LoadAll(tmp, out _).First(x => x.Name == "gt").Effects[1].SameSound(p.Effects[1]), "ノイズゲートの設定がプリセットのJSONに入る");
            check(new EffectSetting { GateOn = true }.HasAny && !new EffectSetting { GateOn = true, Bypass = true }.IsActive, "ゲートだけでも「かかっている」扱い(バイパス中を除く)");
            var bad = new EffectSetting { GateThresholdDb = 5, GateReleaseMs = 1, GateReductionDb = 500 }; bad.Normalize();
            check(bad.GateThresholdDb == -10 && bad.GateReleaseMs == 20 && bad.GateReductionDb == 80, "ゲートの範囲外の値は範囲内に直される");
        }
    }
}
