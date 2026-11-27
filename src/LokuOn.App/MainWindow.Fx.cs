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
        var open = new MenuItem { Header = Loc.Get("mfx.01") };
        open.Click += (s, e) => OpenEffectWindow(slot);
        var bypass = new MenuItem { Header = Loc.Get("mfx.02"), IsCheckable = true };
        bypass.Click += (s, e) => { fxSettings[slot].Bypass = bypass.IsChecked; OnFxChanged(); fxWindow?.Reload(); };
        var rename = new MenuItem { Header = Loc.Get("mfx.03") };
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
                if (f.EqOn) parts.Add(Loc.Get("mfx.04")); if (f.EchoOn) parts.Add(Loc.Get("mfx.05")); if (f.ReverbOn) parts.Add(Loc.Get("mfx.06"));
                b.ToolTip = Loc.Get("mfx.07", string.Join("・", parts), f.ToRecording ? Loc.Get("mfx.08") : Loc.Get("mfx.09"), f.ToOutput ? Loc.Get("mfx.08") : Loc.Get("mfx.09"));
            }
            else
            {
                b.ClearValue(Control.ForegroundProperty); b.FontWeight = FontWeights.Normal;
                b.Opacity = f.HasAny ? 0.6 : 1;
                b.ToolTip = f.HasAny ? Loc.Get("mfx.10") : Loc.Get("mfx.11");
            }
        }
    }
}
