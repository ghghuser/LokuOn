using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LokuOn.Audio;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>
/// 出力ウィンドウ(⚙の左のアイコンから開く)。出力ごとの「カード」を並べ、出力先・音量・入力ごとの送りを設定する。
/// 録音とは独立していて、このウィンドウを閉じても出力は止まらない。コンパクト(文字12px前後)。
/// </summary>
public sealed class OutputWindow : Window
{
    private sealed class InputRow
    {
        public int Slot;
        public CheckBox Check = null!;
        public Slider Gain = null!;
        public TextBlock GainText = null!;
        public UIElement Root = null!;
    }

    private sealed class CardView
    {
        public OutputSetting Card = null!;
        public TextBlock StatusText = null!;
        public ComboBox DevCombo = null!;
        public CheckBox OnCheck = null!;
        public ProgressBar Meter = null!;
        public TextBlock ErrorText = null!;
        public Button FoldBtn = null!;
        public StackPanel InputsPanel = null!;
        public readonly List<InputRow> Rows = new();
        public double Level;
    }

    private readonly RecorderController ctl;
    private readonly List<OutputSetting> cards;
    private readonly string[] names;
    private readonly Func<Preset> readPreset;
    private readonly Action changed;
    private readonly Action namesChanged;
    private readonly WrapPanel wrap = new() { Orientation = Orientation.Horizontal };
    private readonly Button addBtn = new() { Content = "＋ 出力を追加", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock emptyText = new() { Text = "出力はありません。「＋ 出力を追加」から始めます。", Opacity = 0.7, Margin = new Thickness(4, 6, 0, 6) };
    private readonly List<CardView> views = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string usedKey = "";
    private int tickCount;

    public OutputWindow(RecorderController ctl, List<OutputSetting> cards, string[] inputNames,
        Func<Preset> readPreset, Action changed, Action namesChanged)
    {
        this.ctl = ctl; this.cards = cards; names = inputNames;
        this.readPreset = readPreset; this.changed = changed; this.namesChanged = namesChanged;

        Title = "LokuOn - 出力";
        Width = 760; Height = 470; MinWidth = 380; MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12;
        ThemeHelper.ApplyWindowMode(this);
        SourceInitialized += (s, e) => ThemeHelper.ApplyWindow(this);

        var root = new StackPanel { Margin = new Thickness(10) };
        root.Children.Add(new TextBlock
        {
            Text = "入力の音を、再生デバイス(スピーカー・ヘッドホン・VB-CABLEのCABLE Inputなど)へ流します。録音とは独立していて、このウィンドウを閉じても出力は止まりません。入力として取り込み中のデバイスは選べません。",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6),
        });
        root.Children.Add(emptyText);
        root.Children.Add(wrap);
        root.Children.Add(addBtn);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

        addBtn.Click += (s, e) => AddCard();
        Loaded += (s, e) => { Build(); timer.Start(); };
        Closed += (s, e) => timer.Stop();
        timer.Tick += (s, e) => OnTick();
    }

    /// <summary>プリセットの切り替えなどで、カードの中身が入れ替わったときに作り直す。</summary>
    public void Rebuild() => Build();

    // ---------------------------------------------------------------- 組み立て

    private string UsedKey(Preset p) => string.Join("|", Enumerable.Range(0, Preset.SourceSlots)
        .Select(i => (string.IsNullOrWhiteSpace(p.Sources[i].DeviceId) ? "-" : p.Sources[i].DeviceName) + ":" + p.InputName(i)));

    private void Build()
    {
        var p = readPreset();
        usedKey = UsedKey(p);
        wrap.Children.Clear();
        views.Clear();
        for (int i = 0; i < cards.Count; i++) wrap.Children.Add(BuildCard(cards[i], p));
        emptyText.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        addBtn.Visibility = cards.Count < Preset.MaxOutputs ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildCard(OutputSetting card, Preset p)
    {
        var v = new CardView { Card = card };
        views.Add(v);

        var border = new Border
        {
            Width = 340, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x80, 0x80, 0x80)),
            CornerRadius = new CornerRadius(4),
        };
        var panel = new StackPanel();
        border.Child = panel;

        // 見出し: 名前 / 状態 / 削除
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameBox = new TextBox { Text = card.Name, MaxLength = OutputSetting.NameMax, FontWeight = FontWeights.SemiBold, FontSize = 13, MinHeight = 24, ToolTip = "名前(自由に変えられます)" };
        nameBox.LostFocus += (s, e) =>
        {
            string t = nameBox.Text.Trim();
            if (t.Length == 0) t = OutputSetting.DefaultName(Math.Max(0, cards.IndexOf(card)));
            nameBox.Text = t;
            if (card.Name != t) { card.Name = t; changed(); }
        };
        v.StatusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), Opacity = 0.85, MaxWidth = 130, TextTrimming = TextTrimming.CharacterEllipsis };
        var del = new Button { Content = "✕", Width = 26, Height = 24, Padding = new Thickness(0), ToolTip = "この出力を削除" };
        del.Click += (s, e) => DeleteCard(card);
        Grid.SetColumn(v.StatusText, 1); Grid.SetColumn(del, 2);
        head.Children.Add(nameBox); head.Children.Add(v.StatusText); head.Children.Add(del);
        panel.Children.Add(head);

        // 出力先
        v.DevCombo = new ComboBox { Margin = new Thickness(0, 6, 0, 0) };
        FillDevices(v, p);
        v.DevCombo.SelectionChanged += (s, e) => OnDeviceChanged(v);
        panel.Children.Add(v.DevCombo);

        // スイッチ・全体音量・メーター
        var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        v.OnCheck = new CheckBox { Content = "出力する", VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsChecked = ctl.IsOutputWanted(card) };
        v.OnCheck.Click += (s, e) => OnToggle(v);
        var vol = MakeSlider(card.MasterGainDb);
        var volText = new TextBlock { Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        volText.Text = DbText(card.MasterGainDb);
        vol.ValueChanged += (s, e) =>
        {
            card.MasterGainDb = Math.Round(vol.Value, 1); volText.Text = DbText(card.MasterGainDb);
            ctl.ApplyOutputLive(card); changed();
        };
        vol.MouseDoubleClick += (s, e) => vol.Value = 0;
        vol.ToolTip = "この出力の全体音量(ダブルクリックで0dB)";
        Grid.SetColumn(vol, 1); Grid.SetColumn(volText, 2);
        row.Children.Add(v.OnCheck); row.Children.Add(vol); row.Children.Add(volText);
        panel.Children.Add(row);

        v.Meter = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 4, 0, 0), Value = 0 };
        panel.Children.Add(v.Meter);

        v.ErrorText = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(v.ErrorText);

        // 入力の一覧(畳める)
        v.FoldBtn = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 2), Padding = new Thickness(6, 2, 6, 2), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        v.FoldBtn.Click += (s, e) => { card.Collapsed = !card.Collapsed; UpdateFold(v); changed(); };
        panel.Children.Add(v.FoldBtn);
        v.InputsPanel = new StackPanel();
        for (int slot = 0; slot < Preset.SourceSlots; slot++)
        {
            if (string.IsNullOrWhiteSpace(p.Sources[slot].DeviceId)) continue;
            v.Rows.Add(BuildInputRow(v, slot, p));
            v.InputsPanel.Children.Add(v.Rows[^1].Root);
        }
        if (v.Rows.Count == 0)
            v.InputsPanel.Children.Add(new TextBlock { Text = "入力デバイスが選ばれていません(メイン画面で選びます)", Opacity = 0.7, Margin = new Thickness(6, 2, 0, 2) });
        panel.Children.Add(v.InputsPanel);
        UpdateFold(v);
        return border;
    }

    private InputRow BuildInputRow(CardView v, int slot, Preset p)
    {
        var card = v.Card;
        var r = new InputRow { Slot = slot };
        var g = new Grid { Height = 30 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        r.Check = new CheckBox { VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, IsChecked = card.SendOn[slot] };
        var nameText = new TextBlock
        {
            Text = p.InputName(slot), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 76, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0), ToolTip = p.Sources[slot].DeviceName + "\n(ダブルクリックで名前を変更)",
        };
        nameText.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { RenameInput(slot); e.Handled = true; } };
        var first = new StackPanel { Orientation = Orientation.Horizontal };
        first.Children.Add(r.Check); first.Children.Add(nameText);
        r.Check.Click += (s, e) =>
        {
            card.SendOn[slot] = r.Check.IsChecked == true;
            ctl.ApplyOutputLive(card); UpdateFold(v); changed();
        };
        r.Gain = MakeSlider(card.SendGainDb[slot]);
        r.GainText = new TextBlock { Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = DbText(card.SendGainDb[slot]) };
        r.Gain.ValueChanged += (s, e) =>
        {
            card.SendGainDb[slot] = Math.Round(r.Gain.Value, 1); r.GainText.Text = DbText(card.SendGainDb[slot]);
            ctl.ApplyOutputLive(card); changed();
        };
        r.Gain.MouseDoubleClick += (s, e) => r.Gain.Value = 0;
        r.Gain.ToolTip = "この入力をこの出力へ送る音量(ダブルクリックで0dB)";
        Grid.SetColumn(r.Gain, 1); Grid.SetColumn(r.GainText, 2);
        g.Children.Add(first); g.Children.Add(r.Gain); g.Children.Add(r.GainText);
        r.Root = g;
        return r;
    }

    private static Slider MakeSlider(double value) => new()
    {
        Minimum = Preset.GainMinDb, Maximum = Preset.GainMaxDb, SmallChange = 1, LargeChange = 3,
        Value = Math.Clamp(value, Preset.GainMinDb, Preset.GainMaxDb), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0),
    };

    private static string DbText(double db) => (db >= 0 ? "+" : "") + db.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB";

    private void UpdateFold(CardView v)
    {
        var card = v.Card;
        int total = v.Rows.Count, on = v.Rows.Count(r => card.SendOn[r.Slot]);
        v.InputsPanel.Visibility = card.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        v.FoldBtn.Content = (card.Collapsed ? "▸ " : "▾ ") + (card.Collapsed ? $"入力 {on}/{total} を送信中" : $"入力(送る入力にチェック {on}/{total})");
    }

    // ---------------------------------------------------------------- 出力先

    private void FillDevices(CardView v, Preset p)
    {
        var card = v.Card;
        var list = new List<DeviceInfo> { new() { Id = "", Kind = DeviceKind.WasapiOutput, Display = "(未選択)" } };
        foreach (var d in DeviceCatalog.EnumerateOutputs())
        {
            string? loop = RecorderController.CheckLoop(p, d.Id);
            list.Add(loop == null ? d : new DeviceInfo { Id = d.Id, Kind = d.Kind, Display = d.Display + "  (入力と同じ=選べません)" });
        }
        if (card.DeviceId.Length > 0 && !list.Any(d => d.Id == card.DeviceId))
            list.Add(new DeviceInfo { Id = card.DeviceId, Kind = DeviceKind.WasapiOutput, Display = (card.DeviceName.Length > 0 ? card.DeviceName : card.DeviceId) + "  (見つかりません)" });
        v.DevCombo.ItemsSource = list;
        v.DevCombo.SelectedItem = list.FirstOrDefault(d => d.Id == card.DeviceId) ?? list[0];
    }

    private void OnDeviceChanged(CardView v)
    {
        if (v.DevCombo.SelectedItem is not DeviceInfo d) return;
        var card = v.Card;
        if (d.Id == card.DeviceId) return;
        var p = readPreset();
        string? loop = d.Id.Length > 0 ? RecorderController.CheckLoop(p, d.Id) : null;
        if (loop != null)
        {
            SetError(v, loop);
            var back = (v.DevCombo.ItemsSource as List<DeviceInfo>)?.FirstOrDefault(x => x.Id == card.DeviceId);
            Dispatcher.BeginInvoke(new Action(() => { if (back != null) v.DevCombo.SelectedItem = back; }));
            return;
        }
        SetError(v, null);
        bool wasOn = ctl.IsOutputWanted(card);
        if (wasOn) ctl.StopOutput(card);
        card.DeviceId = d.Id;
        string n = d.Display.Trim();
        if (n.EndsWith(DeviceCatalog.DefaultMark)) n = n.Substring(0, n.Length - DeviceCatalog.DefaultMark.Length).TrimEnd();
        card.DeviceName = d.Id.Length == 0 ? "" : n;
        changed();
        if (wasOn)
        {
            if (d.Id.Length == 0) { v.OnCheck.IsChecked = false; return; }
            string? err = ctl.StartOutput(card, p);
            if (err != null) { v.OnCheck.IsChecked = false; SetError(v, err); }
        }
    }

    private void OnToggle(CardView v)
    {
        var card = v.Card;
        try
        {
            SetError(v, null);
            if (v.OnCheck.IsChecked == true)
            {
                string? err = ctl.StartOutput(card, readPreset());
                if (err != null) { v.OnCheck.IsChecked = false; SetError(v, err); }
            }
            else ctl.StopOutput(card);
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("出力: " + ex);
            v.OnCheck.IsChecked = false;
            SetError(v, "出力でエラー: " + ex.Message);
        }
    }

    private static void SetError(CardView v, string? msg)
    {
        v.ErrorText.Text = msg ?? "";
        v.ErrorText.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------- 追加・削除・名前

    private void AddCard()
    {
        if (cards.Count >= Preset.MaxOutputs) return;
        int n = 0;
        while (cards.Any(c => c.Name == OutputSetting.DefaultName(n))) n++;
        cards.Add(new OutputSetting { Name = OutputSetting.DefaultName(n) });   // 出力先は未選択・停止
        changed();
        Build();
    }

    private void DeleteCard(OutputSetting card)
    {
        string extra = ctl.IsOutputWanted(card) ? "\n(出力中です。止めてから削除します)" : "";
        var r = MessageBox.Show(this, $"出力「{card.Name}」を削除しますか?{extra}\n\n保存済みのプリセットは、上書き保存するまで変わりません。",
            "LokuOn - 出力の削除", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        ctl.ForgetOutput(card);
        cards.Remove(card);
        changed();
        Build();
    }

    private void RenameInput(int slot)
    {
        string? t = PromptWindow.Show(this, "入力の名前", $"入力{slot + 1}の名前(中身を覚えるための目印です。{OutputSetting.NameMax}文字まで。空欄で「入力{slot + 1}」に戻ります)", names[slot] ?? "");
        if (t == null) return;
        t = t.Trim();
        if (t.Length > OutputSetting.NameMax) t = t.Substring(0, OutputSetting.NameMax);
        names[slot] = t;
        namesChanged();
        Build();
    }

    // ---------------------------------------------------------------- 定期更新

    private void OnTick()
    {
        tickCount++;
        try
        {
            if (tickCount % 10 == 0)
            {
                var p = readPreset();
                if (UsedKey(p) != usedKey) { Build(); return; }       // 入力の選び直し・名前変更に追従
            }
            foreach (var v in views)
            {
                var card = v.Card;
                bool wanted = ctl.IsOutputWanted(card), running = ctl.IsOutputRunning(card);
                string st = ctl.OutputStatus(card);
                v.StatusText.Text = !wanted ? "停止" : running ? "● 出力中" : (st.Length > 0 ? st : "接続中…");
                v.StatusText.ToolTip = st + (running ? "\n[診断] " + ctl.OutputDiagnostics(card) : "");
                if (v.OnCheck.IsChecked != wanted) v.OnCheck.IsChecked = wanted;
                var (l, r, clip) = ctl.ReadOutputPeaks(card);
                double pk = Math.Max(l, r);
                double db = pk > 0.00003 ? 20 * Math.Log10(pk) : -90;
                double target = Math.Clamp((db + 60) / 60, 0, 1);
                v.Level = target > v.Level ? target : Math.Max(target, v.Level - 0.05);
                v.Meter.Value = running ? v.Level : 0;
                v.Meter.Foreground = clip ? Brushes.IndianRed : SystemColors.HighlightBrush;
            }
        }
        catch (Exception ex) { App.WriteCrashLog("出力ウィンドウ: " + ex); }
    }
}
