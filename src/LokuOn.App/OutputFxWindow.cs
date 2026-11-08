using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>
/// 出力1台ぶんの「最終ミックス」のエフェクト(ローカット → グラフィックEQ → パラメトリックEQ → リミッター)。
/// 出力カードの「FX」ボタンから開く。変更はすぐ、その出力にだけ反映される。入力ごとのエフェクトとは別。
/// </summary>
public sealed class OutputFxWindow : Window
{
    private readonly OutputSetting card;
    private readonly Action apply;
    private readonly CheckBox bypassCheck = new() { Content = "すべてバイパス(効果を一時的に切る。設定は残る)", Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock summary = new() { Margin = new Thickness(0, 3, 0, 0), Opacity = 0.85, FontWeight = FontWeights.SemiBold };
    private readonly ComboBox presetBox = new() { MinWidth = 180 };
    private readonly Controls.GraphicEqControl eqView = new();
    private readonly List<Action> refreshers = new();
    private CheckBox? eqCheck, paramCheck;
    private bool loading;

    private OutputFxSetting Fx => card.Fx;

    public OutputSetting Card => card;

    /// <param name="apply">設定が変わったとき(その出力へ反映し、プリセットを変更済みにする)。</param>
    public OutputFxWindow(OutputSetting card, Action apply)
    {
        this.card = card; this.apply = apply;
        card.Fx ??= new OutputFxSetting();
        Width = 620; SizeToContent = SizeToContent.Height; MaxHeight = 900;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12.5;
        ThemeHelper.ApplyWindowMode(this);
        SourceInitialized += (s, e) => ThemeHelper.ApplyWindow(this);
        UpdateTitle();

        var root = new StackPanel { Margin = new Thickness(8, 5, 8, 5) };
        root.Children.Add(new TextBlock
        {
            Text = "この出力に流れる最終ミックス(全体音量のあと)にだけかかります。入力ごとのエフェクトとは別です。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11.5,
        });
        root.Children.Add(summary);
        root.Children.Add(bypassCheck);
        bypassCheck.Click += (s, e) => Edit(f => f.Bypass = bypassCheck.IsChecked == true);

        // グラフィックEQ(曲線はローカット・パラメトリックも含めた全体。常に見える)
        var g = Section(root, "グラフィックEQ(曲線はローカット・パラメトリックも含めた全体)", f => f.EqOn, (f, v) => f.EqOn = v, cb => eqCheck = cb, false);
        var top = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var pr in EqBands.Presets) presetBox.Items.Add(pr.Name);
        presetBox.SelectionChanged += (s, e) =>
        {
            if (loading || presetBox.SelectedIndex < 0) return;
            var d = EqBands.Presets[presetBox.SelectedIndex].Db;
            Edit(f => { f.Bands = (double[])d.Clone(); f.EqOn = presetBox.SelectedIndex > 0 || f.EqOn; });
            eqView.SetBands(Fx.Bands);
        };
        var reset = new Button { Content = "リセット", Padding = new Thickness(12, 1, 12, 1), Margin = new Thickness(6, 0, 0, 0) };
        reset.Click += (s, e) => { Edit(f => f.Bands = new double[EqBands.Count]); eqView.SetBands(Fx.Bands); loading = true; presetBox.SelectedIndex = 0; loading = false; };
        Grid.SetColumn(reset, 1);
        top.Children.Add(presetBox); top.Children.Add(reset);
        g.Children.Add(top); g.Children.Add(eqView);
        eqView.BandsChanged += () =>
        {
            if (loading) return;
            Edit(f => { f.Bands = eqView.GetBands(); if (f.Bands.Any(v => v != 0)) f.EqOn = true; });
            loading = true; presetBox.SelectedIndex = -1; loading = false;
        };
        refreshers.Add(() => { eqView.SetBands(Fx.Bands); presetBox.SelectedIndex = MatchPreset(Fx.Bands); });

        // ローカット
        var lc = Section(root, "ローカット(低い音を削る。風・振動・こもりの低音に)", null, null, null);
        AddSlider(lc, "カットする周波数", 0, 400, 5, f => f.HpfHz, (f, v) => f.HpfHz = v < 20 ? 0 : v, v => v < 20 ? "なし" : $"{v:0} Hz", 0);

        BuildParametric(root);

        // リミッター
        var lim = Section(root, "リミッター(音の大きすぎる部分を、割れないように自動で下げる)", f => f.LimiterOn, (f, v) => f.LimiterOn = v);
        AddSlider(lim, "上限", OutputFxSetting.CeilingMinDb, OutputFxSetting.CeilingMaxDb, 0.5, f => f.CeilingDb, (f, v) => f.CeilingDb = v, v => v.ToString("0.0", CultureInfo.InvariantCulture) + " dB", -1.0);
        AddSlider(lim, "戻り時間", OutputFxSetting.ReleaseMinMs, OutputFxSetting.ReleaseMaxMs, 10, f => f.ReleaseMs, (f, v) => f.ReleaseMs = v, v => $"{v:0} ms", 150);

        root.Children.Add(new TextBlock
        {
            Text = "かける順番: 全体音量 → ローカット → グラフィックEQ → パラメトリックEQ → リミッター。リミッターは1.5ミリ秒先読みするので、この出力だけわずかに遅れます(録音には影響しません)。スライダーはダブルクリックで初期値に戻ります。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11.5, Margin = new Thickness(0, 4, 0, 4),
        });
        var all = new Button { Content = "この出力のエフェクトを初期値に戻す", Padding = new Thickness(10, 1, 10, 1), HorizontalAlignment = HorizontalAlignment.Left };
        all.Click += (s, e) => { card.Fx = new OutputFxSetting(); apply(); Reload(); };
        root.Children.Add(all);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        Loaded += (s, e) => Reload();
    }

    /// <summary>出力の名前が変わったときなど。</summary>
    public void UpdateTitle() => Title = $"LokuOn - 出力のエフェクト「{card.Name}」";

    public void Reload()
    {
        loading = true;
        try
        {
            card.Fx ??= new OutputFxSetting();
            bypassCheck.IsChecked = Fx.Bypass;
            foreach (var r in refreshers) r();
            eqView.SetHpf(Fx.HpfHz); eqView.SetParams(Fx.ParamOn ? Fx.Params : null); eqView.Dimmed = !Fx.EqOn;
            UpdateSummary();
        }
        finally { loading = false; }
        UpdateTitle();
    }

    private void UpdateSummary()
    {
        var l = new List<string>();
        if (Fx.HpfHz >= 20) l.Add("ローカット");
        if (Fx.EqOn) l.Add("グラフィック");
        if (Fx.ParamOn) l.Add("パラメトリック");
        if (Fx.LimiterOn) l.Add("リミッター");
        summary.Text = l.Count == 0 ? "かかっているもの: なし" : (Fx.Bypass ? "○ バイパス中(設定は残っています): " : "● かかっているもの: ") + string.Join("・", l);
    }

    private void Edit(Action<OutputFxSetting> change)
    {
        if (loading) return;
        change(Fx); Fx.Normalize();
        apply();
        eqView.SetHpf(Fx.HpfHz); eqView.SetParams(Fx.ParamOn ? Fx.Params : null); eqView.Dimmed = !Fx.EqOn;
        if (eqCheck != null) eqCheck.IsChecked = Fx.EqOn;
        paramCheckRefresh?.Invoke();
        UpdateSummary();
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

    private Action? paramCheckRefresh;

    private void BuildParametric(StackPanel parent)
    {
        var panel = Section(parent, "パラメトリックEQ(気になる周波数をピンポイントで上げ下げ)", f => f.ParamOn, (f, v) => f.ParamOn = v, x => paramCheck = x);
        paramCheckRefresh = () => { paramCheck!.IsChecked = Fx.ParamOn; panel.Opacity = Fx.ParamOn ? 1.0 : 0.55; };
        var head = new Grid { Margin = new Thickness(0, 0, 0, 1), Opacity = 0.7 };
        ParamCols(head);
        AddHead(head, "周波数", 1); AddHead(head, "ゲイン", 3); AddHead(head, "幅(大きいほど狭い)", 5);
        panel.Children.Add(head);
        for (int i = 0; i < ParamBand.Count; i++)
        {
            int bi = i;
            var row = new Grid { Height = 24 };
            ParamCols(row);
            row.Children.Add(new TextBlock { Text = ParamBand.Names[i], VerticalAlignment = VerticalAlignment.Center });
            ParamSlider(row, 1, 0, 120, 1, f => 12 * Math.Log2(f.Params[bi].Freq / ParamBand.MinHz), (f, v) => f.Params[bi].Freq = ParamBand.MinHz * Math.Pow(2, v / 12),
                v => { double hz = ParamBand.MinHz * Math.Pow(2, v / 12); return hz >= 1000 ? $"{hz / 1000:0.##} kHz" : $"{hz:0} Hz"; }, 12 * Math.Log2(ParamBand.DefaultFreqs[i] / ParamBand.MinHz));
            ParamSlider(row, 3, -ParamBand.MaxDb, ParamBand.MaxDb, 0.5, f => f.Params[bi].Db, (f, v) => { f.Params[bi].Db = v; if (Math.Abs(v) > 0.01) f.ParamOn = true; },
                v => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + " dB", 0);
            ParamSlider(row, 5, ParamBand.MinQ, ParamBand.MaxQ, 0.1, f => f.Params[bi].Q, (f, v) => f.Params[bi].Q = v, v => "Q " + v.ToString("0.0", CultureInfo.InvariantCulture), ParamBand.DefaultQ);
            panel.Children.Add(row);
        }
        var rs = new Button { Content = "パラメトリックを初期値に戻す", Padding = new Thickness(10, 2, 10, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 0) };
        rs.Click += (s, e) => { Edit(f => f.Params = ParamBand.Defaults()); Reload(); };
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

    private void ParamSlider(Grid g, int col, double min, double max, double step, Func<OutputFxSetting, double> get, Action<OutputFxSetting, double> set, Func<double, string> fmt, double def)
    {
        var sl = new Slider { Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var val = new TextBlock { TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        Grid.SetColumn(sl, col); Grid.SetColumn(val, col + 1);
        g.Children.Add(sl); g.Children.Add(val);
        sl.ValueChanged += (s, e) =>
        {
            val.Text = fmt(sl.Value);
            if (loading) return;
            Edit(f => set(f, Math.Round(sl.Value / step) * step));
        };
        sl.MouseDoubleClick += (s, e) => sl.Value = Math.Clamp(def, min, max);
        refreshers.Add(() => { sl.Value = Math.Clamp(get(Fx), min, max); val.Text = fmt(sl.Value); });
    }

    private StackPanel Section(StackPanel root, string title, Func<OutputFxSetting, bool>? get, Action<OutputFxSetting, bool>? set, Action<CheckBox>? grab = null, bool dim = true)
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
        cb.Click += (s, e) => { Edit(f => set(f, cb.IsChecked == true)); if (dim) panel.Opacity = cb.IsChecked == true ? 1.0 : 0.55; };
        refreshers.Add(() => { cb.IsChecked = get(Fx); if (dim) panel.Opacity = get(Fx) ? 1.0 : 0.55; });
        return panel;
    }

    private void AddSlider(StackPanel panel, string label, double min, double max, double step,
        Func<OutputFxSetting, double> get, Action<OutputFxSetting, double> set, Func<double, string> fmt, double def)
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
            Edit(f => set(f, Math.Round(sl.Value / step) * step));
        };
        sl.MouseDoubleClick += (s, e) => sl.Value = Math.Clamp(def, min, max);
        refreshers.Add(() => { sl.Value = Math.Clamp(get(Fx), min, max); val.Text = fmt(sl.Value); });
    }
}
