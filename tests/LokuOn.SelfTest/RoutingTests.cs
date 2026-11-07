using LokuOn.Core;

/// <summary>ミキサー(MixBus)のテスト。録音のミックスが、これまでの計算とビット単位で同じことを確かめる。</summary>
static class RoutingTests
{
    /// <summary>これまで RecorderEngine.ProcessChunk の中にあった計算(置き換え前の写し)。</summary>
    static MixStats OldMix(float[][] srcBuf, int n, int ch, float gm, float[] all, float[] mono)
    {
        int samples = n * ch;
        Array.Clear(all, 0, samples);
        for (int i = 0; i < srcBuf.Length; i++)
        {
            var b = srcBuf[i];
            for (int j = 0; j < samples; j++) all[j] += b[j];
        }
        float pl = 0, pr = 0; bool clip = false;
        for (int f = 0; f < n; f++)
        {
            float l = all[f * ch] * gm;
            float r = ch > 1 ? all[f * ch + 1] * gm : l;
            if (l > 1f || l < -1f) { clip = true; l = l > 0 ? 1f : -1f; }
            if (r > 1f || r < -1f) { clip = true; r = r > 0 ? 1f : -1f; }
            all[f * ch] = l; if (ch > 1) all[f * ch + 1] = r;
            mono[f] = ch > 1 ? (l + r) * 0.5f : l;
            float al = l < 0 ? -l : l, ar = r < 0 ? -r : r;
            if (al > pl) pl = al; if (ar > pr) pr = ar;
        }
        return new MixStats { PeakL = pl, PeakR = pr, Clipped = clip };
    }

    static bool SameBits(float[] a, float[] b, int count)
    {
        for (int i = 0; i < count; i++)
            if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i])) return false;
        return true;
    }

    static float[][] RandomInputs(Random rnd, int inputs, int n, int ch, float amp)
    {
        var a = new float[inputs][];
        for (int i = 0; i < inputs; i++)
        {
            a[i] = new float[n * ch];
            for (int j = 0; j < a[i].Length; j++) a[i][j] = (float)((rnd.NextDouble() * 2 - 1) * amp);
        }
        return a;
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("ミキサー(録音のミックスが従来と同じ)");
        var rnd = new Random(20261107);
        int mismatches = 0, cases = 0;
        foreach (int ch in new[] { 1, 2, 3 })
            foreach (int inputs in new[] { 1, 2, 3, 4 })
                foreach (float amp in new[] { 0.1f, 0.6f, 1.5f })        // 1.5=足すと±1を超える(切り詰めの確認)
                    foreach (float gmDb in new[] { -6f, 0f, 6f })
                    {
                        int n = 480;
                        var inp = RandomInputs(rnd, inputs, n, ch, amp);
                        float gm = (float)Math.Pow(10.0, gmDb / 20.0);
                        var allOld = new float[n * ch]; var monoOld = new float[n];
                        var allNew = new float[n * ch]; var monoNew = new float[n];
                        var so = OldMix(inp, n, ch, gm, allOld, monoOld);
                        var bus = new MixBus(inputs) { MasterGain = gm };
                        var sn = bus.Process(inp, n, ch, allNew, monoNew);
                        cases++;
                        bool same = SameBits(allOld, allNew, n * ch) && SameBits(monoOld, monoNew, n)
                            && BitConverter.SingleToInt32Bits(so.PeakL) == BitConverter.SingleToInt32Bits(sn.PeakL)
                            && BitConverter.SingleToInt32Bits(so.PeakR) == BitConverter.SingleToInt32Bits(sn.PeakR)
                            && so.Clipped == sn.Clipped;
                        if (!same) mismatches++;
                    }
        check(mismatches == 0, $"従来の計算とビット単位で一致({cases}通り: チャンネル1〜3×入力1〜4×音量×全体ゲイン)", $"不一致 {mismatches}");

        section("ミキサー(送りのオンオフと音量)");
        {
            int n = 8, ch = 2;
            var a = new float[n * ch]; var b = new float[n * ch];
            for (int j = 0; j < a.Length; j++) { a[j] = 0.2f; b[j] = 0.1f; }
            var inp = new[] { a, b };
            var dest = new float[n * ch];

            var bus = new MixBus(2);
            bus.Process(inp, n, ch, dest);
            check(Math.Abs(dest[0] - 0.3f) < 1e-6f, "2入力をそのまま足す");

            bus.SetSend(1, false);
            bus.Process(inp, n, ch, dest);
            check(dest[0] == 0.2f, "送りをオフにした入力は入らない");
            check(!bus.IsSendOn(1) && bus.IsSendOn(0), "送りの状態を読める");

            bus.SetSend(1, true, 0.5f);
            bus.Process(inp, n, ch, dest);
            check(Math.Abs(dest[0] - 0.25f) < 1e-6f, "送りの音量(0.5倍)");

            bus.SetSend(1, true, 0f);
            bus.Process(inp, n, ch, dest);
            check(dest[0] == 0.2f, "送りの音量0は入らない");

            bus.SetSend(7, true, 1f);   // 範囲外は無視する
            bus.SetSend(-1, true, 1f);
            check(bus.InputCount == 2 && !bus.IsSendOn(7), "範囲外の入力は無視する");

            var none = new MixBus(2, allOn: false);
            var st = none.Process(inp, n, ch, dest);
            check(dest.All(x => x == 0f) && st.PeakL == 0 && st.PeakR == 0 && !st.Clipped, "すべてオフなら無音");
        }

        section("ミキサー(全体音量・切り詰め・複数バス)");
        {
            int n = 4, ch = 2;
            var loud = new float[n * ch]; for (int j = 0; j < loud.Length; j++) loud[j] = 0.8f;
            var inp = new[] { loud, loud };
            var dest = new float[n * ch];
            var bus = new MixBus(2);
            var st = bus.Process(inp, n, ch, dest);
            check(st.Clipped && dest.All(x => x == 1f), "足して±1を超えたら切り詰めて、クリップを記録する");
            check(st.PeakL == 1f && st.PeakR == 1f, "切り詰めた後のピーク");

            bus.MasterGain = 0.25f;
            st = bus.Process(inp, n, ch, dest);
            check(!st.Clipped && Math.Abs(dest[0] - 0.4f) < 1e-6f, "全体音量を下げれば切り詰めなし");

            // 同じ入力から、送りの違う2本のバスを同時に作る(出力ごとのミックスの基本)
            var a = new float[n * ch]; var b = new float[n * ch];
            for (int j = 0; j < a.Length; j++) { a[j] = 0.1f; b[j] = 0.2f; }
            var self = new MixBus(2); var other = new MixBus(2);
            other.SetSend(1, false);
            var d1 = new float[n * ch]; var d2 = new float[n * ch];
            self.Process(new[] { a, b }, n, ch, d1);
            other.Process(new[] { a, b }, n, ch, d2);
            check(Math.Abs(d1[0] - 0.3f) < 1e-6f && d2[0] == 0.1f, "同じ入力から、送りの違う2本のバスを作れる");
            check(a[0] == 0.1f && b[0] == 0.2f, "入力のバッファは書き換えない");
        }

        section("ミキサー(モノラルの波形用出力)");
        {
            int n = 4, ch = 2;
            var a = new float[n * ch];
            for (int f = 0; f < n; f++) { a[f * 2] = 0.2f; a[f * 2 + 1] = 0.6f; }
            var dest = new float[n * ch]; var mono = new float[n];
            new MixBus(1).Process(new[] { a }, n, ch, dest, mono);
            check(Math.Abs(mono[0] - 0.4f) < 1e-6f, "(L+R)/2 を書く");
            var m1 = new float[n]; var d1 = new float[n];
            var a1 = new float[n]; for (int f = 0; f < n; f++) a1[f] = 0.3f;
            new MixBus(1).Process(new[] { a1 }, n, 1, d1, m1);
            check(m1[0] == 0.3f, "モノラルはそのまま");
        }
    }
}
