using LokuOn.Core;

/// <summary>録音開始・終了のとき、開いている入力を開き直さずに引き継げるかの判定と、バッファの扱いのテスト。</summary>
static class ReuseTests
{
    static Preset P(params (int slot, string dev)[] src)
    {
        var p = new Preset { SampleRate = 48000, Channels = 2 };
        foreach (var (slot, dev) in src) p.Sources[slot].DeviceId = dev;
        return p;
    }

    static SourceReuse.OpenSource O(int slot, string dev, int sr = 48000, int ch = 2, bool conn = true) => new(slot, dev, sr, ch, conn);

    public static void Run(CheckFn check, Action<string> section)
    {
        section("録音開始・終了での入力の引き継ぎ(出力を途切れさせない)");
        var open = new List<SourceReuse.OpenSource> { O(0, "wasapi-loop:A"), O(2, "wasapi-cap:B") };
        check(SourceReuse.CanReuse(open, P((0, "wasapi-loop:A"), (2, "wasapi-cap:B"))), "同じ入力・同じ形式なら引き継げる");
        check(!SourceReuse.CanReuse(open, P((0, "wasapi-loop:A"))), "入力が減っていたら開き直す");
        check(!SourceReuse.CanReuse(open, P((0, "wasapi-loop:A"), (2, "wasapi-cap:B"), (3, "wasapi-cap:C"))), "入力が増えていたら開き直す");
        check(!SourceReuse.CanReuse(open, P((0, "wasapi-loop:A"), (2, "wasapi-cap:X"))), "デバイスが変わっていたら開き直す");
        check(!SourceReuse.CanReuse(open, P((0, "wasapi-loop:A"), (1, "wasapi-cap:B"))), "枠が違えば開き直す");
        var p44 = P((0, "wasapi-loop:A"), (2, "wasapi-cap:B")); p44.SampleRate = 44100;
        check(!SourceReuse.CanReuse(open, p44), "サンプルレートが違えば開き直す");
        var p1 = P((0, "wasapi-loop:A"), (2, "wasapi-cap:B")); p1.Channels = 1;
        check(!SourceReuse.CanReuse(open, p1), "チャンネル数が違えば開き直す");
        var dis = new List<SourceReuse.OpenSource> { O(0, "wasapi-loop:A"), O(2, "wasapi-cap:B", conn: false) };
        check(!SourceReuse.CanReuse(dis, P((0, "wasapi-loop:A"), (2, "wasapi-cap:B"))), "つながっていない入力があれば、従来どおり開き直す");
        check(!SourceReuse.CanReuse(new List<SourceReuse.OpenSource>(), P((0, "wasapi-loop:A"))), "何も開いていなければ開く(引き継ぎではない)");
        check(!SourceReuse.CanReuse(open, P()), "入力が1つも選ばれていなければ引き継がない");

        // 引き継ぐときはバッファを空にする: 古い音は録音の先頭に入らず、出力用のタップには影響しない
        var src = new SourceBuffer(48000, 2, 4.0); var tap = new SourceBuffer(48000, 2, 1.0); src.AddTap(tap);
        var old = new float[4800 * 2]; Array.Fill(old, 0.8f);       // 録音前にたまっていた古い音(0.1秒ぶん)
        src.Push(old, 0, 4800);
        check(src.AvailableFrames == 4800 && tap.AvailableFrames == 4800, "(前提)古い音が本体とタップの両方にたまっている");
        src.Clear();
        check(src.AvailableFrames == 0, "引き継ぎ時のClearで、本体のバッファの古い音が消える");
        check(tap.AvailableFrames == 4800, "出力用のタップはそのまま(出力は途切れない)");
        var fresh = new float[480 * 2]; Array.Fill(fresh, 0.1f);
        src.Push(fresh, 0, 480);
        var rd = new float[480 * 2]; int n = src.Read(rd, 0, 480);
        check(n == 480 && rd.All(v => v == 0.1f), "Clearのあとに入った新しい音だけが、録音側から読める(先頭に古い音が混ざらない)");
    }
}
