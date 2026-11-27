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
        // クリック=メニュー、ダブルクリック=出力ウィンドウ。メニューは、ダブルクリックでないと分かるまで少し(0.3秒)待ってから出す
        var dblTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        dblTimer.Tick += (s, e) => { dblTimer.Stop(); ShowOutputMenu(); };
        BtnOutput.Click += (s, e) =>
        {
            if (dblTimer.IsEnabled) { dblTimer.Stop(); OpenOutputWindow(); }
            else dblTimer.Start();
        };

        // 入力の名前: ダブルクリックまたは右クリックで変更
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            int slot = i;
            foreach (var tb in new[] { InNameLabels[i], GainNameLabels[i] })
            {
                tb.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { RenameInput(slot); e.Handled = true; } };
                var cm = new ContextMenu();
                var mi = new MenuItem { Header = Loc.Get("mout.01") };
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
        if (wasRunning) AddLog(Loc.Get("mout.02"));
        UpdateOutputIndicator();
    }

    /// <summary>入力名を、メイン画面の入力ラベル・ゲインのラベル・メーターに反映する。</summary>
    private void ApplyInputNames()
    {
        var labels = new string[Preset.SourceSlots];
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            string n = string.IsNullOrWhiteSpace(inputNames[i]) ? Loc.Get("mout.03") + (i + 1) : inputNames[i];
            labels[i] = n;
            InNameLabels[i].Text = n; InNameLabels[i].ToolTip = Loc.Get("mout.04");
            GainNameLabels[i].Text = n; GainNameLabels[i].ToolTip = n;
        }
        try { SourceMeter.Configure(labels); UpdateMeterSlots(); } catch { }
    }

    private void RenameInput(int slot)
    {
        string cur = inputNames[slot] ?? "";
        string? t = PromptWindow.Show(this, Loc.Get("mout.05"), Loc.Get("mout.06", slot + 1, OutputSetting.NameMax), cur);
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
                cm.Items.Add(new MenuItem { Header = Loc.Get("mout.07"), IsEnabled = false });
            foreach (var card in outCards)
            {
                var c = card;
                bool hasDev = !string.IsNullOrWhiteSpace(c.DeviceId);
                var mi = new MenuItem
                {
                    Header = $"{c.Name}  →  {(hasDev ? (string.IsNullOrWhiteSpace(c.DeviceName) ? Loc.Get("mout.sel") : c.DeviceName) : Loc.Get("mout.nodev"))}",
                    IsCheckable = true, IsChecked = ctl.IsOutputWanted(c), IsEnabled = hasDev,
                };
                mi.Click += (s, e) => ToggleOutput(c, mi.IsChecked);
                cm.Items.Add(mi);
            }
            cm.Items.Add(new Separator());
            var stop = new MenuItem { Header = Loc.Get("mout.08"), IsEnabled = outCards.Any(c => ctl.IsOutputWanted(c)) };
            stop.Click += (s, e) => StopAllOutputsKeepResume();
            var resume = new MenuItem { Header = Loc.Get("mout.09"), IsEnabled = outCards.Any(c => !string.IsNullOrWhiteSpace(c.DeviceId) && !ctl.IsOutputWanted(c)) };
            resume.Click += (s, e) => ResumeOutputs();
            cm.Items.Add(stop); cm.Items.Add(resume);
            cm.Items.Add(new Separator());
            var open = new MenuItem { Header = Loc.Get("mout.10") };
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
                if (err != null) { AddLog(Loc.Get("mout.11", card.Name, err)); MessageBox.Show(this, err, Loc.Get("mout.12"), MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
        }
        catch (Exception ex) { App.WriteCrashLog("出力切替: " + ex); }
        UpdateOutputIndicator();
    }

    private void StopAllOutputsKeepResume()
    {
        outResume = outCards.Where(c => ctl.IsOutputWanted(c)).ToList();
        foreach (var c in outResume) ctl.StopOutput(c);
        AddLog(Loc.Get("mout.13"));
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
            if (err != null) { errors.Add($"「{c.Name}」: {err}"); AddLog(Loc.Get("mout.11", c.Name, err)); }
        }
        if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), Loc.Get("mout.12"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
        BtnOutput.Content = running ? Loc.Get("mout.14") : "🔊";
        BtnOutput.Width = running ? 82 : 34;
        BtnOutput.Margin = new Thickness(0, 4, 46, 0);
        if (running) BtnOutput.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        else BtnOutput.ClearValue(Control.ForegroundProperty);
        BtnOutput.ToolTip = running ? Loc.Get("mout.15") : Loc.Get("mout.16");
        SetTray(ctl.IsRecording ? Loc.Get("set.37") : null);
    }
}
