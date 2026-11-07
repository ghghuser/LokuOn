using LokuOn.Core;

/// <summary>出力デバイス用の音づくり(OutputRenderer)のテスト。時計のずれを仮想の時間で再現する。</summary>
static class OutputTests
{
    sealed class Sim
    {
        public long Underruns, Overflows; public double MinFill = 1e9, MaxFill; public double MaxJump; public int Glitches;
        public double RmsErr; public double FreqRatio;
    }

    /// <summary>入力(srcRate, srcCh)を ppm だけ速く/遅く送り、デバイス(devRate)が 480 フレームずつ引く。</summary>
    static Sim Simulate(int srcRate, int srcCh, int devRate, int devCh, double drift, double seconds, double warm = 3.0,
        Func<double, bool>? producerOn = null)
    {
        var src = new SourceBuffer(srcRate, srcCh, 2.0);
        src.Tap = new SourceBuffer(srcRate, srcCh, 1.0);
        var rend = new OutputRenderer(new[] { src }, devRate, devCh);
        double freq = 1000, amp = 0.5;
        long produced = 0; int chunk = srcRate / 100;
        var pbuf = new float[chunk * srcCh];
        var outBuf = new float[480 * devCh];
        var sim = new Sim();
        double t = 0; int block = 480;
        float prev = 0; bool havePrev = false;
        long blocks = (long)(seconds * devRate / block);
        long warmBlocks = (long)(warm * devRate / block);
        double errSum = 0; long errN = 0;
        for (long b = 0; b < blocks; b++)
        {
            t = (double)b * block / devRate;
            // 送り側: 現在時刻までに作られた分を 10ms ずつ Push
            double target = t * srcRate * (1 + drift);
            while (produced + chunk <= target)
            {
                bool on = producerOn == null || producerOn(produced / (double)srcRate);
                if (on)
                {
                    for (int f = 0; f < chunk; f++)
                    {
                        float v = (float)(amp * Math.Sin(2 * Math.PI * freq * (produced + f) / srcRate));
                        for (int c = 0; c < srcCh; c++) pbuf[f * srcCh + c] = v;
                    }
                    src.Tap.Push(pbuf, 0, chunk);
                }
                produced += chunk;
            }
            long u0 = rend.Underruns;
            rend.Render(outBuf, 0, block);
            if (b >= warmBlocks)
            {
                double fill = src.Tap.AvailableFrames / (double)srcRate;
                sim.MinFill = Math.Min(sim.MinFill, fill); sim.MaxFill = Math.Max(sim.MaxFill, fill);
                if (rend.Underruns > u0) sim.Glitches++;
                for (int f = 0; f < block; f++)
                {
                    float v = outBuf[f * devCh];
                    if (havePrev) { double j = Math.Abs(v - prev); if (j > sim.MaxJump) sim.MaxJump = j; }
                    prev = v; havePrev = true;
                }
            }
        }
        sim.Underruns = rend.Underruns; sim.Overflows = rend.Overflows;
        return sim;
    }

    public static void Run(CheckFn check, Action<string> section)
    {
        section("出力(クロックのずれの補正・変換)");

        // 1) タップの複製
        {
            var s = new SourceBuffer(48000, 2, 1.0);
            var data = new float[480 * 2]; for (int i = 0; i < data.Length; i++) data[i] = i;
            s.Push(data, 0, 480);
            check(s.AvailableFrames == 480, "タップなし: 従来どおり");
            s.Tap = new SourceBuffer(48000, 2, 1.0);
            s.Push(data, 0, 480);
            check(s.AvailableFrames == 960 && s.Tap.AvailableFrames == 480, "タップあり: 両方に入る");
            var r = new float[480 * 2]; s.Tap.Read(r, 0, 480);
            check(r[5] == 5 && r[959] == 959, "タップの中身が同じ");
            s.Tap = null; s.Push(data, 0, 480);
            check(s.AvailableFrames == 1440, "タップを外すと元どおり");
        }

        // 2) ずれの補正(同じレート・違うレート、速い/遅い)
        foreach (var (inR, devR, d, name) in new[]
        {
            (48000, 48000, 0.0, "ずれなし 48k→48k"),
            (48000, 48000, 0.0005, "入力が+500ppm 48k→48k"),
            (48000, 48000, -0.0005, "入力が-500ppm 48k→48k"),
            (48000, 44100, 0.003, "入力が+0.3% 48k→44.1k"),
            (44100, 48000, -0.003, "入力が-0.3% 44.1k→48k"),
        })
        {
            var sm = Simulate(inR, 2, devR, 2, d, 60);
            string det = $"underrun={sm.Underruns} overflow={sm.Overflows} fill={sm.MinFill * 1000:F0}-{sm.MaxFill * 1000:F0}ms jump={sm.MaxJump:F3}";
            check(sm.Glitches == 0 && sm.Overflows == 0, name + ": 60秒、途切れなし", det);
            check(sm.MinFill > 0.003 && sm.MaxFill < 0.2, name + ": たまりが範囲内", det);
            check(sm.MaxJump < 0.2, name + ": 波形が連続(プチノイズなし)", det);
        }

        // 3) 長時間(5分)
        {
            var sm = Simulate(48000, 1, 44100, 2, 0.0005, 300);
            string det = $"underrun={sm.Underruns} overflow={sm.Overflows} fill={sm.MinFill * 1000:F0}-{sm.MaxFill * 1000:F0}ms jump={sm.MaxJump:F3}";
            check(sm.Glitches == 0 && sm.Overflows == 0 && sm.MaxJump < 0.2, "5分(モノラル→ステレオ、+500ppm)", det);
        }

        // 4) 入力が止まって再開(ループバックの無音)
        {
            var sm = Simulate(48000, 2, 48000, 2, 0.0, 30, warm: 3, producerOn: t => t < 10 || t > 15);
            check(sm.Underruns >= 1, "入力が止まると無音になる(止まらない)", $"underrun={sm.Underruns}");
            // 再開後に鳴ること
            var src = new SourceBuffer(48000, 2, 2.0); src.Tap = new SourceBuffer(48000, 2, 1.0);
            var rend = new OutputRenderer(new[] { src }, 48000, 2);
            var o = new float[480 * 2];
            rend.Render(o, 0, 480);
            check(o.All(x => x == 0f), "データがないときは無音");
            var d = new float[480 * 2]; for (int i = 0; i < d.Length; i++) d[i] = 0.3f;
            for (int k = 0; k < 10; k++) src.Tap.Push(d, 0, 480);
            float peak = 0; for (int k = 0; k < 6; k++) { rend.Render(o, 0, 480); peak = Math.Max(peak, o.Max()); }
            check(peak > 0.25f, "たまれば鳴り始める", $"peak={peak}");
        }

        // 5) ミックス・音量・送りのオンオフ・チャンネル変換
        {
            var a = new SourceBuffer(48000, 1, 1.0); var b = new SourceBuffer(48000, 2, 1.0);
            // モノラルとステレオは同じ Renderer に混ぜない(プリセット内は同じ形式)ので別々に確認する
            a.Tap = new SourceBuffer(48000, 1, 1.0);
            var rend = new OutputRenderer(new[] { a }, 48000, 2);
            var m = new float[4800]; for (int i = 0; i < m.Length; i++) m[i] = 0.25f;
            var o = new float[480 * 2];
            // 目標までためて、助走を済ませる
            for (int k = 0; k < 20; k++) a.Tap.Push(m, 0, 480);
            rend.Render(o, 0, 480);
            for (int k = 0; k < 4; k++) { a.Tap.Push(m, 0, 480); rend.Render(o, 0, 480); }
            check(Math.Abs(o[400] - 0.25f) < 0.01f && Math.Abs(o[401] - 0.25f) < 0.01f, "モノラル→左右に複製");
            a.GainDb = 6.0206f;
            for (int k = 0; k < 3; k++) { a.Tap.Push(m, 0, 480); rend.Render(o, 0, 480); }
            check(Math.Abs(o[400] - 0.5f) < 0.02f, "入力ゲイン+6dBで2倍", $"{o[400]}");
            rend.Bus.MasterGain = 0.5f;
            for (int k = 0; k < 3; k++) { a.Tap.Push(m, 0, 480); rend.Render(o, 0, 480); }
            check(Math.Abs(o[400] - 0.25f) < 0.02f, "全体音量0.5", $"{o[400]}");
            rend.Bus.MasterGain = 1f; a.GainDb = 40f;
            for (int k = 0; k < 3; k++) { a.Tap.Push(m, 0, 480); rend.Render(o, 0, 480); }
            check(o.Max() <= 1f && rend.ReadPeaks().Clipped, "大きすぎる音は±1に切り詰めてクリップを記録");
            a.GainDb = 0f; rend.Bus.SetSend(0, false);
            a.Tap.Push(m, 0, 480); rend.Render(o, 0, 480);
            check(o.All(x => x == 0f) && a.Tap.AvailableFrames == 0, "送りオフ: 無音でためない");

            // ステレオ→モノラル、ステレオ→6ch
            var st = new SourceBuffer(48000, 2, 1.0); st.Tap = new SourceBuffer(48000, 2, 1.0);
            var sd = new float[480 * 2]; for (int i = 0; i < 480; i++) { sd[i * 2] = 0.2f; sd[i * 2 + 1] = 0.4f; }
            var r1 = new OutputRenderer(new[] { st }, 48000, 1);
            var r6 = new OutputRenderer(new[] { st }, 48000, 6);
            var o1 = new float[480]; var o6 = new float[480 * 6];
            for (int k = 0; k < 20; k++) st.Tap.Push(sd, 0, 480);
            r1.Render(o1, 0, 480); r1.Render(o1, 0, 480);
            check(Math.Abs(o1[300] - 0.3f) < 0.01f, "ステレオ→モノラルは平均", $"{o1[300]}");
            st.Tap.Clear(); for (int k = 0; k < 20; k++) st.Tap.Push(sd, 0, 480);
            r6.Render(o6, 0, 480); r6.Render(o6, 0, 480);
            check(Math.Abs(o6[300 * 6] - 0.2f) < 0.01f && Math.Abs(o6[300 * 6 + 1] - 0.4f) < 0.01f && o6[300 * 6 + 2] == 0f, "ステレオ→6ch: 余りは無音");
        }

        // 5b) 複数の出力(それぞれ専用のタップ)と、送りの違い
        {
            var src = new SourceBuffer(48000, 2, 1.0);
            var tA = new SourceBuffer(48000, 2, 1.0); var tB = new SourceBuffer(48000, 2, 1.0);
            src.AddTap(tA); src.AddTap(tB);
            var rA = new OutputRenderer(new[] { src }, new SourceBuffer?[] { tA }, 48000, 2);
            var rB = new OutputRenderer(new[] { src }, new SourceBuffer?[] { tB }, 48000, 2);
            rB.Bus.SetSend(0, true, 0.5f);          // 出力Bは半分の音量で送る
            var d = new float[480 * 2]; for (int i = 0; i < d.Length; i++) d[i] = 0.4f;
            var oa = new float[480 * 2]; var ob = new float[480 * 2];
            for (int k = 0; k < 20; k++) src.Push(d, 0, 480);
            for (int k = 0; k < 5; k++) { rA.Render(oa, 0, 480); rB.Render(ob, 0, 480); src.Push(d, 0, 480); }
            check(src.AvailableFrames > 0 && tA.PushedFrames == tB.PushedFrames, "1つの入力が2つの出力に同じだけ複製される");
            check(Math.Abs(oa[600] - 0.4f) < 0.01f && Math.Abs(ob[600] - 0.2f) < 0.01f, "出力ごとに送りの音量が違う", $"{oa[600]} {ob[600]}");
            rA.Bus.SetSend(0, false);
            rA.Render(oa, 0, 480); rB.Render(ob, 0, 480);
            check(oa.All(x => x == 0f) && ob.Any(x => x != 0f), "片方の送りをオフにしても、もう片方は鳴る");
            src.RemoveTap(tA);
            long before = tA.PushedFrames; src.Push(d, 0, 480);
            check(tA.PushedFrames == before && tB.PushedFrames > before, "タップを外すとそこへは複製されない");
        }

        // 6) 録音への影響がないこと: 既存の Read 側は従来どおり
        {
            var s = new SourceBuffer(48000, 2, 1.0);
            s.Tap = new SourceBuffer(48000, 2, 0.01);     // 小さいタップ(あふれても)
            var d = new float[48000 * 2];
            s.Push(d, 0, 48000);
            check(s.AvailableFrames == 48000 && s.DroppedFrames == 0, "タップがあふれても本体は影響なし");
        }
    }
}
