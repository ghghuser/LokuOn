using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LokuOn.Audio;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>出力(カード)と入力名の、メイン画面側のつなぎ。カードの設定はプリセットの一部。</summary>
public partial class MainWindow
{
    private readonly List<OutputSetting> outCards = new();
    private readonly string[] inputNames = new string[Preset.SourceSlots] { "", "", "", "" };
    private OutputWindow? outWindow;
    private DispatcherTimer? outTimer;
    private bool outRunningShown;
    private List<OutputSetting> outResume = new();   // 「すべて停止」で止めた出力(「すべて再開」で戻す)

    private TextBlock[] InNameLabels => new[] { InName1, InName2, InName3, InName4 };
    private TextBlock[] GainNameLabels => new[] { GName1, GName2, GName3, GName4 };

    private void InitOutputUi()
    {
        BtnOutput.Click += (s, e) => ShowOutputMenu();

        // 入力の名前: ダブルクリックまたは右クリックで変更
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            int slot = i;
            foreach (var tb in new[] { InNameLabels[i], GainNameLabels[i] })
            {
                tb.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { RenameInput(slot); e.Handled = true; } };
                var cm = new ContextMenu();
                var mi = new MenuItem { Header = "名前を変更..." };
                mi.Click += (s, e) => RenameInput(slot);
                cm.Items.Add(mi);
                tb.ContextMenu = cm;
            }
        }

        outTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        outTimer.Tick += (s, e) => UpdateOutputIndicator();
        outTimer.Start();
    }

    /// <summary>プリセットの出力・入力名を画面の状態に取り込む。動いていた出力は止める(カードが入れ替わるため)。</summary>
    private void ApplyOutputsFromPreset(Preset p)
    {
        bool wasRunning = ctl.AnyOutputRunning;
        ctl.StopAllOutputs();
        outCards.Clear();
        outCards.AddRange(p.Outputs.Select(o => o.Clone()));
        for (int i = 0; i < Preset.SourceSlots; i++) inputNames[i] = p.InputNames[i] ?? "";
        ApplyInputNames();
        outWindow?.Rebuild();
        if (wasRunning) AddLog("プリセットを切り替えたので、出力を止めました");
        UpdateOutputIndicator();
    }

    /// <summary>入力名を、メイン画面の入力ラベル・ゲインのラベル・メーターに反映する。</summary>
    private void ApplyInputNames()
    {
        var labels = new string[Preset.SourceSlots];
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            string n = string.IsNullOrWhiteSpace(inputNames[i]) ? "入力" + (i + 1) : inputNames[i];
            labels[i] = n;
            InNameLabels[i].Text = n; InNameLabels[i].ToolTip = "ダブルクリックまたは右クリックで名前を変更";
            GainNameLabels[i].Text = n; GainNameLabels[i].ToolTip = n;
        }
        try { SourceMeter.Configure(labels); UpdateMeterSlots(); } catch { }
    }

    private void RenameInput(int slot)
    {
        string cur = inputNames[slot] ?? "";
        string? t = PromptWindow.Show(this, "入力の名前", $"入力{slot + 1}の名前(中身を覚えるための目印です。{OutputSetting.NameMax}文字まで。空欄で「入力{slot + 1}」に戻ります)", cur);
        if (t == null) return;
        t = t.Trim();
        if (t.Length > OutputSetting.NameMax) t = t.Substring(0, OutputSetting.NameMax);
        inputNames[slot] = t;
        ApplyInputNames();
        MarkDirty();
    }

    /// <summary>🔊 を押したときのメニュー: 出力ごとのオン/オフ、すべて停止/再開、出力ウィンドウを開く。</summary>
    private void ShowOutputMenu()
    {
        try
        {
            var cm = new ContextMenu { PlacementTarget = BtnOutput, Placement = PlacementMode.Bottom };
            if (outCards.Count == 0)
                cm.Items.Add(new MenuItem { Header = "(出力がありません。出力ウィンドウで追加します)", IsEnabled = false });
            foreach (var card in outCards)
            {
                var c = card;
                bool hasDev = !string.IsNullOrWhiteSpace(c.DeviceId);
                var mi = new MenuItem
                {
                    Header = $"{c.Name}  →  {(hasDev ? (string.IsNullOrWhiteSpace(c.DeviceName) ? "(選択済み)" : c.DeviceName) : "出力先が未選択")}",
                    IsCheckable = true, IsChecked = ctl.IsOutputWanted(c), IsEnabled = hasDev,
                };
                mi.Click += (s, e) => ToggleOutput(c, mi.IsChecked);
                cm.Items.Add(mi);
            }
            cm.Items.Add(new Separator());
            var stop = new MenuItem { Header = "すべて停止", IsEnabled = outCards.Any(c => ctl.IsOutputWanted(c)) };
            stop.Click += (s, e) => StopAllOutputsKeepResume();
            var resume = new MenuItem { Header = "すべて再開", IsEnabled = outCards.Any(c => !string.IsNullOrWhiteSpace(c.DeviceId) && !ctl.IsOutputWanted(c)) };
            resume.Click += (s, e) => ResumeOutputs();
            cm.Items.Add(stop); cm.Items.Add(resume);
            cm.Items.Add(new Separator());
            var open = new MenuItem { Header = "出力ウィンドウを開く..." };
            open.Click += (s, e) => OpenOutputWindow();
            cm.Items.Add(open);
            cm.IsOpen = true;
        }
        catch (Exception ex) { App.WriteCrashLog("出力メニュー: " + ex); OpenOutputWindow(); }
    }

    private void ToggleOutput(OutputSetting card, bool on)
    {
        try
        {
            if (!on) { ctl.StopOutput(card); outResume.Remove(card); }
            else
            {
                string? err = ctl.StartOutput(card, ReadPreset());
                if (err != null) { AddLog($"出力「{card.Name}」: {err}"); MessageBox.Show(this, err, "LokuOn - 出力", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }
        catch (Exception ex) { App.WriteCrashLog("出力切替: " + ex); }
        UpdateOutputIndicator();
    }

    private void StopAllOutputsKeepResume()
    {
        outResume = outCards.Where(c => ctl.IsOutputWanted(c)).ToList();
        foreach (var c in outResume) ctl.StopOutput(c);
        AddLog("出力をすべて停止しました");
        UpdateOutputIndicator();
    }

    /// <summary>「すべて停止」で止めたものを戻す。覚えていなければ、出力先を選んである出力すべてを始める。</summary>
    private void ResumeOutputs()
    {
        var targets = outResume.Where(outCards.Contains).ToList();
        if (targets.Count == 0) targets = outCards.Where(c => !string.IsNullOrWhiteSpace(c.DeviceId)).ToList();
        outResume.Clear();
        var errors = new List<string>();
        foreach (var c in targets)
        {
            if (ctl.IsOutputWanted(c)) continue;
            string? err = ctl.StartOutput(c, ReadPreset());
            if (err != null) { errors.Add($"「{c.Name}」: {err}"); AddLog($"出力「{c.Name}」: {err}"); }
        }
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), "LokuOn - 出力", MessageBoxButton.OK, MessageBoxImage.Warning);
        UpdateOutputIndicator();
    }

    private void OpenOutputWindow()
    {
        try
        {
            if (outWindow != null) { if (outWindow.WindowState == WindowState.Minimized) outWindow.WindowState = WindowState.Normal; outWindow.Activate(); return; }
            var w = new OutputWindow(ctl, outCards, inputNames, ReadPreset, MarkDirty, () => { ApplyInputNames(); MarkDirty(); }) { Owner = this };
            w.Closed += (s, e) => { if (ReferenceEquals(outWindow, w)) outWindow = null; };
            outWindow = w;
            w.Show();
        }
        catch (Exception ex) { App.WriteCrashLog("出力ウィンドウ: " + ex); outWindow = null; }
    }

    /// <summary>メイン画面のボタン(🔊)に「出力中」を出す。</summary>
    private void UpdateOutputIndicator()
    {
        bool running = ctl.AnyOutputRunning;
        if (running == outRunningShown) return;
        outRunningShown = running;
        BtnOutput.Content = running ? "🔊 出力中" : "🔊";
        BtnOutput.Width = running ? 82 : 34;
        BtnOutput.Margin = new Thickness(0, 4, 46, 0);
        if (running) BtnOutput.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        else BtnOutput.ClearValue(Control.ForegroundProperty);
        BtnOutput.ToolTip = running ? "出力中です。クリックで、出力ごとのオン/オフ・すべて停止/再開・出力ウィンドウを開く" : "出力: クリックで、出力ごとのオン/オフ・すべて再開・出力ウィンドウを開く(録音とは独立)";
        SetTray(ctl.IsRecording ? "録音中" : null);
    }
}
