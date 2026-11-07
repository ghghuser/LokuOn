using System.Windows;
using System.Windows.Controls;
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

    private TextBlock[] InNameLabels => new[] { InName1, InName2, InName3, InName4 };
    private TextBlock[] GainNameLabels => new[] { GName1, GName2, GName3, GName4 };

    private void InitOutputUi()
    {
        BtnOutput.Click += (s, e) => OpenOutputWindow();

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
        BtnOutput.ToolTip = running ? "出力中です。クリックで出力ウィンドウを開く" : "出力: 入力の音をスピーカーやVB-CABLEなどの再生デバイスへ流します(録音とは独立)";
        SetTray(ctl.IsRecording ? "録音中" : null);
    }
}
