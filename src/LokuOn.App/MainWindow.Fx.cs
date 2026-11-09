using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>入力ごとのエフェクトの、メイン画面側のつなぎ(FXボタン・メーターのダブルクリック・右クリック)。</summary>
public partial class MainWindow
{
    private readonly EffectSetting[] fxSettings = { new(), new(), new(), new() };
    private EffectWindow? fxWindow;

    private Button[] FxButtons => new[] { FxBtn1, FxBtn2, FxBtn3, FxBtn4 };

    private void InitFxUi()
    {
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            int slot = i;
            FxButtons[i].Click += (s, e) => OpenEffectWindow(slot);
            FxButtons[i].ContextMenu = BuildFxMenu(slot);
        }
        SourceMeter.BarDoubleClicked += i => { if (i >= 0 && i < Preset.SourceSlots) OpenEffectWindow(i); };
        SourceMeter.BarRightClicked += i =>
        {
            if (i < 0 || i >= Preset.SourceSlots) return;
            var m = BuildFxMenu(i); m.PlacementTarget = SourceMeter; m.IsOpen = true;
        };
    }

    private ContextMenu BuildFxMenu(int slot)
    {
        var cm = new ContextMenu();
        var open = new MenuItem { Header = "エフェクトを開く..." };
        open.Click += (s, e) => OpenEffectWindow(slot);
        var bypass = new MenuItem { Header = "エフェクトをバイパス(一時的に切る)", IsCheckable = true };
        bypass.Click += (s, e) => { fxSettings[slot].Bypass = bypass.IsChecked; OnFxChanged(); fxWindow?.Reload(); };
        var rename = new MenuItem { Header = "入力の名前を変更..." };
        rename.Click += (s, e) => RenameInput(slot);
        cm.Opened += (s, e) => { bypass.IsChecked = fxSettings[slot].Bypass; bypass.IsEnabled = fxSettings[slot].HasAny; };
        cm.Items.Add(open); cm.Items.Add(bypass); cm.Items.Add(new Separator()); cm.Items.Add(rename);
        return cm;
    }

    private void OpenEffectWindow(int slot)
    {
        try
        {
            if (fxWindow != null)
            {
                if (fxWindow.WindowState == WindowState.Minimized) fxWindow.WindowState = WindowState.Normal;
                fxWindow.ShowSlot(slot); fxWindow.Activate(); return;
            }
            var w = new EffectWindow(fxSettings, ReadPreset, OnFxChanged, slot) { Owner = this };
            w.Closed += (s, e) => { if (ReferenceEquals(fxWindow, w)) fxWindow = null; };
            fxWindow = w;
            w.Show();
        }
        catch (Exception ex) { App.WriteCrashLog("エフェクト: " + ex); fxWindow = null; }
    }

    /// <summary>エフェクトの設定が変わった: 録音・出力に反映し、プリセットを変更済みにする。</summary>
    private void OnFxChanged()
    {
        try { ctl.SetEffects(fxSettings); } catch (Exception ex) { App.WriteCrashLog("エフェクト反映: " + ex); }
        UpdateFxButtons();
        MarkDirty();
    }

    private void ApplyFxFromPreset(Preset p)
    {
        for (int i = 0; i < Preset.SourceSlots; i++) fxSettings[i] = p.Effects[i].Clone();
        try { ctl.SetEffects(fxSettings); } catch { }
        UpdateFxButtons();
        fxWindow?.Reload();
    }

    /// <summary>FXボタンの色: かかっている=緑、バイパス中=薄く、なし=ふつう。</summary>
    private void UpdateFxButtons()
    {
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            var b = FxButtons[i]; var f = fxSettings[i];
            if (f.IsActive)
            {
                b.Foreground = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)); b.FontWeight = FontWeights.Bold; b.Opacity = 1;
                var parts = new List<string>();
                if (f.EqOn) parts.Add("イコライザ"); if (f.EchoOn) parts.Add("エコー"); if (f.ReverbOn) parts.Add("リバーブ");
                b.ToolTip = $"エフェクト: {string.Join("・", parts)}(録音: {(f.ToRecording ? "反映" : "素の音")} / 出力: {(f.ToOutput ? "反映" : "素の音")})\n右クリックでメニュー";
            }
            else
            {
                b.ClearValue(Control.ForegroundProperty); b.FontWeight = FontWeights.Normal;
                b.Opacity = f.HasAny ? 0.6 : 1;
                b.ToolTip = f.HasAny ? "エフェクトはバイパス中です(クリックで開く)" : "エフェクト(ノイズゲート・イコライザ・エコー・リバーブ)。右クリックでメニュー";
            }
        }
    }
}
