using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>
/// 入力ごとのエフェクト(イコライザ → エコー → リバーブ)の小さなパネル。1枚だけ開き、上の入力の切り替えで中身が入れ替わる。
/// 変更はすぐ録音・出力に反映される(changed を呼ぶ)。
/// </summary>
public sealed class EffectWindow : Window
{
    private static EffectSetting? clipboard;

    private readonly EffectSetting[] fx;
    private readonly Func<Preset> readPreset;
    private readonly Action changed;
    private readonly ComboBox slotBox = new();
    private readonly CheckBox bypassCheck = new() { Content = "すべてバイパス(効果を一時的に切る。設定は残る)", Margin = new Thickness(0, 3, 0, 0) };
    private readonly CheckBox toRecCheck = new() { Content = "録音に反映(オフ=録音は素の音)" };
    private readonly CheckBox toOutCheck = new() { Content = "出力に反映" };
    private readonly List<Action> refreshers = new();
    private readonly Button pasteBtn = new() { Content = "貼り付け", Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox presetBox = new() { MinWidth = 180 };
    private readonly Controls.GraphicEqControl eqView = new();
    private CheckBox? eqCheck;
    private readonly TabItem eqTab = new(), gateTab = new(), fxTab = new();
    private bool loading;
    private int slot;

    public int Slot => slot;

    public EffectWindow(EffectSetting[] fx, Func<Preset> readPreset, Action changed, int initialSlot)
    {
        this.fx = fx; this.readPreset = readPreset; this.changed = changed;
        Title = "LokuOn - エフェクト";
        Width = 620; SizeToContent = SizeToContent.Height; MaxHeight = 900;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12.5;
        ThemeHelper.ApplyWindowMode(this);
        SourceInitialized += (s, e) => ThemeHelper.ApplyWindow(this);

        var root = new StackPanel { Margin = new Thickness(8, 5, 8, 5) };

        // 入力の切り替え・コピー/貼り付け
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copyBtn = new Button { Content = "コピー", Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(6, 0, 0, 0) };
        copyBtn.Click += (s, e) => { clipboard = Cur.Clone(); UpdatePaste(); };
        pasteBtn.Click += (s, e) => PasteSound();
        Grid.SetColumn(copyBtn, 1); Grid.SetColumn(pasteBtn, 2);
        top.Children.Add(slotBox); top.Children.Add(copyBtn); top.Children.Add(pasteBtn);
        root.Children.Add(top);
        slotBox.SelectionChanged += (s, e) => { if (loading || slotBox.SelectedIndex < 0) return; slot = slotBox.SelectedIndex; Reload(); };

        root.Children.Add(bypassCheck);
        bypassCheck.Click += (s, e) => Edit(c => c.Bypass = bypassCheck.IsChecked == true);

        // 上のタブ: イコライザ(グラフィック → ローカット → パラメトリック) / エコー・リバーブ
        var mainTabs = new TabControl { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(2, 2, 2, 2) };
        var eqPanel = new StackPanel();
        var fxPanel = new StackPanel();
        var gatePanel = new StackPanel();
        eqTab.Content = eqPanel; gateTab.Content = gatePanel; fxTab.Content = fxPanel;
        mainTabs.Items.Add(eqTab); mainTabs.Items.Add(gateTab); mainTabs.Items.Add(fxTab);
        root.Children.Add(mainTabs);

        // グラフィックEQ(曲線は常に見える。ローカット・パラメトリックの効果もこの曲線に合成される)
        var gPanel = Section(eqPanel, "グラフィックEQ(曲線はローカット・パラメトリックも含めた全体)", c => c.EqOn, (c, v) => c.EqOn = v, cb => eqCheck = cb, false);
        var eqTop = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        eqTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        eqTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var pr in EqBands.Presets) presetBox.Items.Add(pr.Name);
        presetBox.SelectionChanged += (s, e) =>
        {
            if (loading || presetBox.SelectedIndex < 0) return;
            var d = EqBands.Presets[presetBox.SelectedIndex].Db;
            Edit(c => { c.Bands = (double[])d.Clone(); c.EqOn = presetBox.SelectedIndex > 0 || c.EqOn; });
            eqView.SetBands(Cur.Bands);
            RefreshEqSwitch();
        };
        var eqReset = new Button { Content = "リセット", Padding = new Thickness(12, 1, 12, 1), Margin = new Thickness(6, 0, 0, 0) };
        eqReset.Click += (s, e) => { Edit(c => c.Bands = new double[EqBands.Count]); eqView.SetBands(Cur.Bands); loading = true; presetBox.SelectedIndex = 0; loading = false; };
        Grid.SetColumn(eqReset, 1);
        eqTop.Children.Add(presetBox); eqTop.Children.Add(eqReset);
        gPanel.Children.Add(eqTop);
        gPanel.Children.Add(eqView);
        eqView.BandsChanged += () =>
        {
            if (loading) return;
            Edit(c => { c.Bands = eqView.GetBands(); if (c.Bands.Any(v => v != 0)) c.EqOn = true; });
            loading = true; presetBox.SelectedIndex = -1; loading = false;
            RefreshEqSwitch();
        };
        // 曲線は常に見せる(グラフィックがオフのときは、帯域を除いたローカット・パラメトリックだけの曲線になる)
        refreshers.Add(() => { eqView.SetBands(Cur.Bands); eqView.Dimmed = !Cur.EqOn; presetBox.SelectedIndex = MatchPreset(Cur.Bands); });

        // ローカット(0=なし)
        var lc = Section(eqPanel, "ローカット(低い音を削る。風・振動・こもりの低音に)", null, null, null);
        AddSlider(lc, "カットする周波数", 0, 400, 5, c => c.HpfHz, (c, v) => c.HpfHz = v < 20 ? 0 : v,
            v => v < 20 ? "なし" : $"{v:0} Hz", 0);
        refreshers.Add(() => eqView.SetHpf(Cur.HpfHz));

        BuildParametric(eqPanel);

        // ノイズゲート(いちばん最初にかかる)
        var gate = Section(gatePanel, "ノイズゲート(小さい音=ノイズを自動で下げる)", c => c.GateOn, (c, v) => c.GateOn = v);
        AddSlider(gate, "しきい値", -70, -10, 1, c => c.GateThresholdDb, (c, v) => c.GateThresholdDb = v, v => $"{v:0} dB", -45);
        AddSlider(gate, "戻り時間", 20, 1000, 10, c => c.GateReleaseMs, (c, v) => c.GateReleaseMs = v, v => $"{v:0} ms", 200);
        AddSlider(gate, "下げる量", 6, 80, 1, c => c.GateReductionDb, (c, v) => c.GateReductionDb = v, v => $"{v:0} dB", 40);
        gatePanel.Children.Add(new TextBlock
        {
            Text = "しきい値より大きい音のときだけ通し、小さい音(ハム・息・部屋のノイズ)のときは「下げる量」だけ下げます。しきい値は、ノイズだけのときのメーターの位置より少し上に合わせます。声の語尾が切れるときは、しきい値を下げるか戻り時間を長くします。ノイズゲートは、かける順番でいちばん最初(EQより前)です。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(2, 6, 2, 2),
        });

        // エコー
        var echo = Section(fxPanel, "エコー", c => c.EchoOn, (c, v) => c.EchoOn = v);
        AddSlider(echo, "間隔", 20, 1000, 10, c => c.EchoMs, (c, v) => c.EchoMs = v, v => $"{v:0} ms", 300);
        AddSlider(echo, "くり返し", 0, 0.9, 0.05, c => c.EchoFeedback, (c, v) => c.EchoFeedback = v, Pct, 0.35);
        AddSlider(echo, "量", 0, 1, 0.05, c => c.EchoMix, (c, v) => c.EchoMix = v, Pct, 0.3);

        // リバーブ
        var rev = Section(fxPanel, "リバーブ", c => c.ReverbOn, (c, v) => c.ReverbOn = v);
        AddSlider(rev, "部屋の広さ", 0, 1, 0.05, c => c.ReverbRoom, (c, v) => c.ReverbRoom = v, Pct, 0.5);
        AddSlider(rev, "こもり", 0, 1, 0.05, c => c.ReverbDamp, (c, v) => c.ReverbDamp = v, Pct, 0.5);
        AddSlider(rev, "量", 0, 1, 0.05, c => c.ReverbMix, (c, v) => c.ReverbMix = v, Pct, 0.25);

        // 反映先
        root.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 3) });
        var dest = new StackPanel { Orientation = Orientation.Horizontal };
        toOutCheck.Margin = new Thickness(18, 0, 0, 0);
        dest.Children.Add(toRecCheck); dest.Children.Add(toOutCheck);
        root.Children.Add(dest);
        toRecCheck.Click += (s, e) => Edit(c => c.ToRecording = toRecCheck.IsChecked == true);
        toOutCheck.Click += (s, e) => Edit(c => c.ToOutput = toOutCheck.IsChecked == true);
        root.Children.Add(new TextBlock
        {
            Text = "かける順番: ローカット → グラフィックEQ → パラメトリックEQ → エコー → リバーブ。無音での自動分割・自動開始は、エフェクトをかける前の音で判定します。スライダーはダブルクリックで初期値に戻ります。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 4),
        });
        var reset = new Button { Content = "この入力を初期値に戻す", Padding = new Thickness(10, 1, 10, 1), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (s, e) =>
        {
            var d = new EffectSetting { ToRecording = Cur.ToRecording, ToOutput = Cur.ToOutput };
            fx[slot] = d; changed(); Reload();
        };
        root.Children.Add(reset);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        slot = Math.Clamp(initialSlot, 0, Preset.SourceSlots - 1);
        Loaded += (s, e) => Reload();
    }

    private EffectSetting Cur => fx[slot];

    /// <summary>パラメトリックEQ(4バンド)。見出しのチェックで有効/無効。グラフィックの後にかかる。</summary>
    private void BuildParametric(StackPanel parent)
    {
        CheckBox? cb = null;
        var panel = Section(parent, "パラメトリックEQ(気になる周波数をピンポイントで上げ下げ)", c => c.ParamOn, (c, v) => c.ParamOn = v, x => cb = x);
        paramCheckRefresh = () => { cb!.IsChecked = Cur.ParamOn; panel.Opacity = Cur.ParamOn ? 1.0 : 0.55; };
        refreshers.Add(() => eqView.SetParams(Cur.ParamOn ? Cur.Params : null));

        var head = new Grid { Margin = new Thickness(0, 0, 0, 1), Opacity = 0.7 };
        ParamCols(head);
        AddHead(head, "周波数", 1); AddHead(head, "ゲイン", 3); AddHead(head, "幅(大きいほど狭い)", 5);
        panel.Children.Add(head);
        for (int i = 0; i < ParamBand.Count; i++)
        {
            int bi = i;
            var g = new Grid { Height = 24 };
            ParamCols(g);
            g.Children.Add(new TextBlock { Text = ParamBand.Names[i], VerticalAlignment = VerticalAlignment.Center });
            // 周波数は 20Hz から半音(1/12オクターブ)刻み。0〜120 で 20Hz〜約20kHz
            ParamSlider(g, 1, 0, 120, 1, c => 12 * Math.Log2(c.Params[bi].Freq / ParamBand.MinHz), (c, v) => c.Params[bi].Freq = ParamBand.MinHz * Math.Pow(2, v / 12),
                v => { double f = ParamBand.MinHz * Math.Pow(2, v / 12); return f >= 1000 ? $"{f / 1000:0.##} kHz" : $"{f:0} Hz"; }, 12 * Math.Log2(ParamBand.DefaultFreqs[i] / ParamBand.MinHz));
            ParamSlider(g, 3, -ParamBand.MaxDb, ParamBand.MaxDb, 0.5, c => c.Params[bi].Db, (c, v) => { c.Params[bi].Db = v; if (Math.Abs(v) > 0.01) c.ParamOn = true; }, Db, 0);
            ParamSlider(g, 5, ParamBand.MinQ, ParamBand.MaxQ, 0.1, c => c.Params[bi].Q, (c, v) => c.Params[bi].Q = v, v => "Q " + v.ToString("0.0", CultureInfo.InvariantCulture), ParamBand.DefaultQ);
            panel.Children.Add(g);
        }
        var rs = new Button { Content = "パラメトリックを初期値に戻す", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
        rs.Click += (s, e) => { Edit(c => c.Params = ParamBand.Defaults()); Reload(); };
        panel.Children.Add(rs);
    }

    private static void ParamCols(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        for (int k = 0; k < 3; k++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        }
    }

    private static void AddHead(Grid g, string t, int col)
    {
        var tb = new TextBlock { Text = t, FontSize = 11.5 }; Grid.SetColumn(tb, col); g.Children.Add(tb);
    }

    private void ParamSlider(Grid g, int col, double min, double max, double step, Func<EffectSetting, double> get, Action<EffectSetting, double> set, Func<double, string> fmt, double def)
    {
        var sl = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var val = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        Grid.SetColumn(sl, col); Grid.SetColumn(val, col + 1);
        g.Children.Add(sl); g.Children.Add(val);
        sl.ValueChanged += (s, e) =>
        {
            val.Text = fmt(sl.Value);
            if (loading) return;
            Edit(c => set(c, Math.Round(sl.Value / step) * step));
            if (paramCheckRefresh != null) paramCheckRefresh();
        };
        sl.MouseDoubleClick += (s, e) => sl.Value = Math.Clamp(def, min, max);
        refreshers.Add(() => { sl.Value = Math.Clamp(get(Cur), min, max); val.Text = fmt(sl.Value); });
    }

    private Action? paramCheckRefresh;

    private static string Db(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
    private static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    /// <summary>入力を切り替える(メイン画面から別の入力のFXを開き直したとき)。</summary>
    public void ShowSlot(int s) { slot = Math.Clamp(s, 0, Preset.SourceSlots - 1); Reload(); }

    /// <summary>プリセットの切り替えなどで、設定の中身が入れ替わったとき。</summary>
    public void Reload()
    {
        loading = true;
        try
        {
            var p = readPreset();
            var items = new List<string>();
            for (int i = 0; i < Preset.SourceSlots; i++)
            {
                string tail = string.IsNullOrWhiteSpace(p.Sources[i].DeviceId) ? "(デバイス未選択)" : "";
                string mark = fx[i].IsActive ? "  ● かかっています" : fx[i].HasAny ? "  (バイパス中)" : "";
                items.Add($"入力{i + 1}: {p.InputName(i)} {tail}{mark}");
            }
            slotBox.ItemsSource = items;
            slotBox.SelectedIndex = slot;
            bypassCheck.IsChecked = Cur.Bypass;
            toRecCheck.IsChecked = Cur.ToRecording;
            toOutCheck.IsChecked = Cur.ToOutput;
            foreach (var r in refreshers) r();
            UpdateTabLabels();
            UpdatePaste();
        }
        finally { loading = false; }
    }

    /// <summary>つまみを動かして自動でEQをオンにしたとき、見出しのチェックと表示の薄さを合わせる。</summary>
    private void RefreshEqSwitch()
    {
        if (eqCheck != null) eqCheck.IsChecked = Cur.EqOn;
        eqView.Dimmed = !Cur.EqOn;
    }

    private static int MatchPreset(double[] b)
    {
        for (int i = 0; i < EqBands.Presets.Length; i++)
        {
            var d = EqBands.Presets[i].Db; bool same = true;
            for (int k = 0; k < EqBands.Count && same; k++) same = Math.Abs(b[k] - d[k]) < 0.01;
            if (same) return i;
        }
        return -1;
    }

    private void UpdatePaste() => pasteBtn.IsEnabled = clipboard != null;

    private void PasteSound()
    {
        if (clipboard == null) return;
        var c = clipboard.Clone(); c.ToRecording = Cur.ToRecording; c.ToOutput = Cur.ToOutput;
        fx[slot] = c; changed(); Reload();
    }

    private void Edit(Action<EffectSetting> apply)
    {
        if (loading) return;
        apply(Cur); Cur.Normalize();
        changed();
        if (eqCheck != null) { eqView.SetHpf(Cur.HpfHz); eqView.SetParams(Cur.ParamOn ? Cur.Params : null); RefreshEqSwitch(); }
        // 見出しの入力名に付く「かかっています」の表示だけ更新する
        UpdateSlotLabels();
    }

    /// <summary>タブ見出しに、そのタブの中でいまかかっているものを出す(例: イコライザ ● グラフィック・ローカット)。</summary>
    private void UpdateTabLabels()
    {
        var c = Cur;
        string Join(params (bool on, string name)[] a) { var l = a.Where(x => x.on).Select(x => x.name).ToList(); return l.Count == 0 ? "" : (c.Bypass ? "  ○ バイパス中: " : "  ● ") + string.Join("・", l); }
        eqTab.Header = "イコライザ" + Join((c.EqOn, "グラフィック"), (c.HpfHz >= 20, "ローカット"), (c.ParamOn, "パラメトリック"));
        gateTab.Header = "ノイズゲート" + Join((c.GateOn, "オン"));
        fxTab.Header = "エコー・リバーブ" + Join((c.EchoOn, "エコー"), (c.ReverbOn, "リバーブ"));
    }

    private void UpdateSlotLabels()
    {
        UpdateTabLabels();
        loading = true;
        try
        {
            var p = readPreset();
            var items = new List<string>();
            for (int i = 0; i < Preset.SourceSlots; i++)
            {
                string tail = string.IsNullOrWhiteSpace(p.Sources[i].DeviceId) ? "(デバイス未選択)" : "";
                string mark = fx[i].IsActive ? "  ● かかっています" : fx[i].HasAny ? "  (バイパス中)" : "";
                items.Add($"入力{i + 1}: {p.InputName(i)} {tail}{mark}");
            }
            slotBox.ItemsSource = items; slotBox.SelectedIndex = slot;
        }
        finally { loading = false; }
    }

    private StackPanel Section(StackPanel root, string title, Func<EffectSetting, bool>? get, Action<EffectSetting, bool>? set, Action<CheckBox>? grab = null, bool dim = true)
    {
        var box = new GroupBox { Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(5, 1, 5, 2) };
        var panel = new StackPanel();
        box.Content = panel;
        root.Children.Add(box);
        if (get == null || set == null)
        {
            box.Header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold };
            return panel;
        }
        var cb = new CheckBox { Content = title, FontWeight = FontWeights.SemiBold };
        box.Header = cb;
        grab?.Invoke(cb);
        cb.Click += (s, e) => { Edit(c => set(c, cb.IsChecked == true)); if (dim) panel.Opacity = cb.IsChecked == true ? 1.0 : 0.55; if (ReferenceEquals(cb, eqCheck)) eqView.Dimmed = !Cur.EqOn; };
        refreshers.Add(() => { cb.IsChecked = get(Cur); if (dim) panel.Opacity = get(Cur) ? 1.0 : 0.55; });
        return panel;
    }

    private void AddSlider(StackPanel panel, string label, double min, double max, double step,
        Func<EffectSetting, double> get, Action<EffectSetting, double> set, Func<double, string> fmt, double def)
    {
        var g = new Grid { Height = 24 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var sl = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        var val = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sl, 1); Grid.SetColumn(val, 2);
        g.Children.Add(name); g.Children.Add(sl); g.Children.Add(val);
        panel.Children.Add(g);
        sl.ValueChanged += (s, e) =>
        {
            val.Text = fmt(sl.Value);
            if (loading) return;
            Edit(c => set(c, Math.Round(sl.Value / step) * step));
        };
        sl.MouseDoubleClick += (s, e) => sl.Value = Math.Clamp(def, min, max);
        refreshers.Add(() => { sl.Value = Math.Clamp(get(Cur), min, max); val.Text = fmt(sl.Value); });
    }
}
