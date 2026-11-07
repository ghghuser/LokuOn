using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LokuOn.Audio;

namespace LokuOn.App;

/// <summary>出力(試験機能・段階2)の画面まわり。段階3で専用ウィンドウに置き換える予定の仮の画面。</summary>
public partial class MainWindow
{
    private DispatcherTimer? outTimer;
    private bool outUiLoading;

    private void InitOutputUi()
    {
        outUiLoading = true;
        try
        {
            var list = DeviceCatalog.EnumerateOutputs();
            OutDeviceCombo.ItemsSource = list;
            var cur = list.FirstOrDefault(d => d.Id == ctl.Settings.OutputDeviceId);
            OutDeviceCombo.SelectedItem = cur ?? list.FirstOrDefault();
            OutVolSlider.Value = ctl.Settings.OutputVolumeDb;
            ctl.SetOutputVolume(ctl.Settings.OutputVolumeDb);
            UpdateOutVolText();
        }
        catch (Exception ex) { App.WriteCrashLog("出力UI: " + ex); }
        finally { outUiLoading = false; }

        OutDeviceCombo.SelectionChanged += (s, e) =>
        {
            if (outUiLoading) return;
            if (OutDeviceCombo.SelectedItem is DeviceInfo d) ctl.Settings.OutputDeviceId = d.Id;
            if (OutEnableCheck.IsChecked == true) ApplyOutput();     // 動作中に変えたら付け替える
        };
        OutEnableCheck.Click += (s, e) => ApplyOutput();
        OutVolSlider.ValueChanged += (s, e) =>
        {
            if (outUiLoading) return;
            ctl.Settings.OutputVolumeDb = OutVolSlider.Value;
            ctl.SetOutputVolume(OutVolSlider.Value);
            UpdateOutVolText();
        };
        OutVolSlider.MouseDoubleClick += (s, e) => OutVolSlider.Value = 0;

        outTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        outTimer.Tick += (s, e) => UpdateOutputStatus();
        outTimer.Start();
    }

    private void UpdateOutVolText()
        => OutVolText.Text = (OutVolSlider.Value >= 0 ? "+" : "") + OutVolSlider.Value.ToString("0") + " dB";

    private void ApplyOutput()
    {
        try
        {
            bool on = OutEnableCheck.IsChecked == true;
            string id = (OutDeviceCombo.SelectedItem as DeviceInfo)?.Id ?? "";
            SetInlineError(OutErrorText, null);
            string? err = ctl.SetOutput(id, on, ReadPreset());
            if (err != null)
            {
                OutEnableCheck.IsChecked = false;
                SetInlineError(OutErrorText, err);
                AddLog("出力: " + err);
            }
            else AddLog(on ? "出力を開始しました: " + ctl.OutputDeviceName : "出力を止めました");
            UpdateMeterSlots();
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("出力: " + ex);
            OutEnableCheck.IsChecked = false;
            SetInlineError(OutErrorText, "出力でエラー: " + ex.Message);
        }
        UpdateOutputStatus();
    }

    private void UpdateOutputStatus()
    {
        if (!ctl.OutputWanted) { OutStatusText.Text = "停止中"; return; }
        string st = ctl.OutputStatus;
        var (l, r, clip) = ctl.ReadOutputPeaks();
        float pk = Math.Max(l, r);
        string level = pk > 0.0001f ? $"  音量ピーク {20 * Math.Log10(pk):0} dB{(clip ? "  ※クリップ" : "")}" : "  (無音)";
        OutStatusText.Text = (st.Length > 0 ? st : "出力中") + (ctl.IsOutputRunning ? level : "");
    }
}
