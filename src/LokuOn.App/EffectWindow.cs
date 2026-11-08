using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private readonly CheckBox bypassCheck = new() { Content = "すべてバイパス(効果を一時的に切る。設定は残る)", Margin = new Thickness(0, 6, 0, 2) };
    private readonly CheckBox toRecCheck = new() { Content = "エフェクトを録音に反映する(オフ=録音は素の音のまま)", Margin = new Thickness(0, 2, 0, 0) };
    private readonly CheckBox toOutCheck = new() { Content = "エフェクトを出力に反映する", Margin = new Thickness(0, 2, 0, 0) };
    private readonly List<Action> refreshers = new();
    private readonly Button pasteBtn = new() { Content = "貼り付け", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
    private bool loading;
    private int slot;

    public int Slot => slot;

    public EffectWindow(EffectSetting[] fx, Func<Preset> readPreset, Action changed, int initialSlot)
    {
        this.fx = fx; this.readPreset = readPreset; this.changed = changed;
        Title = "LokuOn - エフェクト";
        Width = 440; SizeToContent = SizeToContent.Height; MaxHeight = 760;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12;
        ThemeHelper.ApplyWindowMode(this);
        SourceInitialized += (s, e) => ThemeHelper.ApplyWindow(this);

        var root = new StackPanel { Margin = new Thickness(12) };

        // 入力の切り替え・コピー/貼り付け
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copyBtn = new Button { Content = "コピー", Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        copyBtn.Click += (s, e) => { clipboard = Cur.Clone(); UpdatePaste(); };
        pasteBtn.Click += (s, e) => PasteSound();
        Grid.SetColumn(copyBtn, 1); Grid.SetColumn(pasteBtn, 2);
        top.Children.Add(slotBox); top.Children.Add(copyBtn); top.Children.Add(pasteBtn);
        root.Children.Add(top);
        slotBox.SelectionChanged += (s, e) => { if (loading || slotBox.SelectedIndex < 0) return; slot = slotBox.SelectedIndex; Reload(); };

        root.Children.Add(bypassCheck);
        bypassCheck.Click += (s, e) => Edit(c => c.Bypass = bypassCheck.IsChecked == true);

        // イコライザ
        var eq = Section(root, "イコライザ", c => c.EqOn, (c, v) => c.EqOn = v);
        AddSlider(eq, "ローカット", 0, 400, 5, c => c.HpfHz, (c, v) => c.HpfHz = v < 20 ? 0 : v, v => v < 20 ? "なし" : $"{v:0} Hz", 0);
        AddSlider(eq, "低域", -15, 15, 0.5, c => c.LowDb, (c, v) => c.LowDb = v, Db, 0);
        AddSlider(eq, "　低域の位置", 40, 400, 5, c => c.LowHz, (c, v) => c.LowHz = v, v => $"{v:0} Hz", 120);
        AddSlider(eq, "中域", -15, 15, 0.5, c => c.MidDb, (c, v) => c.MidDb = v, Db, 0);
        AddSlider(eq, "　中域の位置", 200, 5000, 50, c => c.MidHz, (c, v) => c.MidHz = v, v => $"{v:0} Hz", 1000);
        AddSlider(eq, "　中域の幅(Q)", 0.3, 4, 0.1, c => c.MidQ, (c, v) => c.MidQ = v, v => v.ToString("0.0", CultureInfo.InvariantCulture), 1.0);
        AddSlider(eq, "高域", -15, 15, 0.5, c => c.HighDb, (c, v) => c.HighDb = v, Db, 0);
        AddSlider(eq, "　高域の位置", 2000, 12000, 100, c => c.HighHz, (c, v) => c.HighHz = v, v => $"{v:0} Hz", 6000);

        // エコー
        var echo = Section(root, "エコー", c => c.EchoOn, (c, v) => c.EchoOn = v);
        AddSlider(echo, "間隔", 20, 1000, 10, c => c.EchoMs, (c, v) => c.EchoMs = v, v => $"{v:0} ms", 300);
        AddSlider(echo, "くり返し", 0, 0.9, 0.05, c => c.EchoFeedback, (c, v) => c.EchoFeedback = v, Pct, 0.35);
        AddSlider(echo, "量", 0, 1, 0.05, c => c.EchoMix, (c, v) => c.EchoMix = v, Pct, 0.3);

        // リバーブ
        var rev = Section(root, "リバーブ", c => c.ReverbOn, (c, v) => c.ReverbOn = v);
        AddSlider(rev, "部屋の広さ", 0, 1, 0.05, c => c.ReverbRoom, (c, v) => c.ReverbRoom = v, Pct, 0.5);
        AddSlider(rev, "こもり", 0, 1, 0.05, c => c.ReverbDamp, (c, v) => c.ReverbDamp = v, Pct, 0.5);
        AddSlider(rev, "量", 0, 1, 0.05, c => c.ReverbMix, (c, v) => c.ReverbMix = v, Pct, 0.25);

        // 反映先
        root.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 6) });
        root.Children.Add(toRecCheck); root.Children.Add(toOutCheck);
        toRecCheck.Click += (s, e) => Edit(c => c.ToRecording = toRecCheck.IsChecked == true);
        toOutCheck.Click += (s, e) => Edit(c => c.ToOutput = toOutCheck.IsChecked == true);
        root.Children.Add(new TextBlock
        {
            Text = "かける順番: イコライザ → エコー → リバーブ。無音での自動分割・自動開始は、エフェクトをかける前の音で判定します。スライダーはダブルクリックで初期値に戻ります。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.65, Margin = new Thickness(0, 6, 0, 6),
        });
        var reset = new Button { Content = "この入力を初期値に戻す", Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
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
            UpdatePaste();
        }
        finally { loading = false; }
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
        // 見出しの入力名に付く「かかっています」の表示だけ更新する
        UpdateSlotLabels();
    }

    private void UpdateSlotLabels()
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
            slotBox.ItemsSource = items; slotBox.SelectedIndex = slot;
        }
        finally { loading = false; }
    }

    private StackPanel Section(StackPanel root, string title, Func<EffectSetting, bool> get, Action<EffectSetting, bool> set)
    {
        var box = new GroupBox { Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(6, 4, 6, 4) };
        var cb = new CheckBox { Content = title, FontWeight = FontWeights.SemiBold };
        box.Header = cb;
        var panel = new StackPanel();
        box.Content = panel;
        cb.Click += (s, e) => { Edit(c => set(c, cb.IsChecked == true)); panel.Opacity = cb.IsChecked == true ? 1.0 : 0.55; };
        refreshers.Add(() => { cb.IsChecked = get(Cur); panel.Opacity = get(Cur) ? 1.0 : 0.55; });
        root.Children.Add(box);
        return panel;
    }

    private void AddSlider(StackPanel panel, string label, double min, double max, double step,
        Func<EffectSetting, double> get, Action<EffectSetting, double> set, Func<double, string> fmt, double def)
    {
        var g = new Grid { Height = 26 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
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
