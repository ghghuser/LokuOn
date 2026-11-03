using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LokuOn.Audio;
using LokuOn.Core;

namespace LokuOn.App;

public partial class MainWindow : Window
{
    private readonly RecorderController ctl;
    private readonly DispatcherTimer timer;
    private readonly ObservableCollection<string> logItems = new();
    private bool loading;
    private bool forceClose;
    private int tickCount;
    private DateTime dirtyAt = DateTime.MinValue;
    private bool dirty;
    private DateTime lastStatusUpdate = DateTime.MinValue;

    public MainWindow()
    {
        ctl = new RecorderController(AppContext.BaseDirectory);
        try { ctl.Initialize(); }
        catch (Exception ex) { App.WriteCrashLog("初期化: " + ex); }

        // 最初の描画の前に配色を決めておく(ちらつき防止)
        ThemeHelper.Apply(ctl.Settings.Theme);
        InitializeComponent();

        timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += OnTick;

        Loaded += OnLoaded;
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
    }

    // ---------------------------------------------------------------- 起動

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        loading = true;
        SetupStaticCombos();
        WireEvents();
        MasterMeter.Configure(new[] { "L", "R" });
        SourceMeter.Configure(new[] { "入力1", "入力2", "入力3" });
        LogList.ItemsSource = logItems;
        FileList.ItemsSource = files;
        BuildFileContextMenu();
        InitPlayer();
        ApplyBackdrop();

        PopulateDeviceCombos();
        RefreshPresetCombo(ctl.Settings.LastPresetName);
        LoadPresetToUi(ctl.Settings.Current);
        LoadAppSettingsToUi();
        loading = false;

        ctl.PresetProvider = ReadPreset;
        ctl.Message += m => Dispatcher.InvokeAsync(() => AddLog(m));
        ctl.FileFinished += f => Dispatcher.InvokeAsync(() => OnFileFinished(f));
        ctl.RecordingEnded += OnRecordingEnded;
        ctl.RecordingStarted += OnRecordingStarted;

        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ApplyCustomColors();
        UpdateFormatDependentUi();
        UpdatePreview();
        UpdateMeterSlots();
        UpdateButtons();
        UpdateScheduleStatus();
        ApplyCompactSizes(this);
        RegisterHotkeys();

        if (ctl.FfmpegPath == null)
        {
            AddLog("ffmpeg.exe が見つかりません。exeと同じ場所の ffmpeg フォルダに置くか、「アプリ設定」で指定してください。");
            Tabs.SelectedIndex = 2;
        }
        AddLog("起動しました");

        RescanFiles();
        _ = RecoverPendingAsync();
        if (ctl.Settings.MonitorInput) StartMonitorSafe();
        timer.Start();
    }

    private async Task RecoverPendingAsync()
    {
        try
        {
            var pending = ctl.PendingRecoveryFiles();
            if (pending.Count == 0) { ctl.DiscardRecovery(); return; }
            var ask = MessageBox.Show(this,
                $"前回、正常に終了しなかった録音ファイルが {pending.Count} 件あります。\n\n" +
                string.Join("\n", pending.Take(8).Select(x => System.IO.Path.GetFileName(x))) + (pending.Count > 8 ? "\n…" : "") +
                "\n\n長さを調べて、名前の終わりに「_restored」を付けたファイルに整えますか？\n(「いいえ」の場合、ファイルはそのまま残します)",
                "LokuOn - 録音の復旧", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) { ctl.DiscardRecovery(); AddLog("前回の録音ファイルの復旧は行いませんでした"); return; }
            var list = await ctl.RecoverPendingAsync();
            if (list.Count == 0) return;
            foreach (var f in list) files.Insert(0, FileItem.FromPath(f.Path, f.Seconds));
            UpdateFileCount();
            AddLog($"前回、正常に終了しなかった録音を {list.Count} 件復旧しました");
            MessageBox.Show(this, $"{list.Count} 件を復旧しました。\n\n" +
                                  string.Join("\n", list.Take(8).Select(x => System.IO.Path.GetFileName(x.Path))) +
                                  (list.Count > 8 ? "\n…" : ""),
                "LokuOn - 録音の復旧", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { App.WriteCrashLog("復旧: " + ex); }
    }

    /// <summary>テキストボックスやプルダウンの高さを少し詰めて、1画面に収まりやすくする。</summary>
    private static void ApplyCompactSizes(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is TextBox tb && tb.MinHeight < 26) { tb.MinHeight = 26; }
            else if (child is ComboBox cb) { cb.MinHeight = 26; }
            else if (child is Button b && b.Height.Equals(double.NaN)) { b.MinHeight = 26; }
            if (child is DependencyObject d) ApplyCompactSizes(d);
        }
    }

    private void AddLog(string text)
    {
        logItems.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        while (logItems.Count > 300) logItems.RemoveAt(0);
        if (logItems.Count > 0) LogList.ScrollIntoView(logItems[logItems.Count - 1]);
        StatusText.Text = text;
    }

    // ---------------------------------------------------------------- 配色

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() => { ApplyCustomColors(); ApplyBackdrop(); });
    }

    private void ApplyCustomColors()
    {
        bool dark = ThemeHelper.IsDark(ctl.Settings.Theme);
        Color back = dark ? Color.FromRgb(24, 26, 30) : Color.FromRgb(248, 249, 251);
        Color wave = dark ? Color.FromRgb(96, 196, 255) : Color.FromRgb(20, 110, 200);
        Color axis = dark ? Color.FromRgb(80, 86, 96) : Color.FromRgb(190, 196, 206);
        Color text = dark ? Color.FromRgb(200, 205, 214) : Color.FromRgb(70, 76, 88);
        Color off = dark ? Color.FromRgb(60, 64, 72) : Color.FromRgb(205, 208, 214);

        WaveView.BackBrush = new SolidColorBrush(back);
        WaveView.WaveBrush = new SolidColorBrush(wave);
        WaveView.AxisBrush = new SolidColorBrush(axis);
        foreach (var m in new[] { MasterMeter, SourceMeter })
        {
            m.BackBrush = new SolidColorBrush(back);
            m.TextBrush = new SolidColorBrush(text);
            m.OffBrush = new SolidColorBrush(off);
            m.InvalidateVisual();
        }
        WaveView.InvalidateVisual();
    }

    // ---------------------------------------------------------------- 定期処理

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            ctl.Tick();
            tickCount++;

            var lv = ctl.ReadLevels();
            MasterMeter.Update(new[] { lv.MasterL, lv.MasterR }, lv.Clipped);
            SourceMeter.Update(lv.Sources, false);
            if ((tickCount & 1) == 0) WaveView.Refresh(ctl.Waveform);

            if ((DateTime.Now - lastStatusUpdate).TotalMilliseconds >= 200)
            {
                lastStatusUpdate = DateTime.Now;
                UpdateStatus();
            }
            UpdatePlayerPosition();

            if (dirty && (DateTime.Now - dirtyAt).TotalSeconds >= 2)
            {
                dirty = false;
                SyncSettingsFromUi();
                ctl.SaveSettings();
            }
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("Tick: " + ex);
        }
    }

    private void MarkDirty()
    {
        if (loading) return;
        dirty = true;
        dirtyAt = DateTime.Now;
    }

    private static string Hms(double sec)
    {
        long t = (long)Math.Max(0, sec);
        return string.Create(CultureInfo.InvariantCulture, $"{t / 3600}:{(t % 3600) / 60:00}:{t % 60:00}");
    }

    private void UpdateStatus()
    {
        var eng = ctl.Engine;
        if (eng == null)
        {
            ElapsedText.Text = "0:00:00";
            StateText.Text = ctl.IsMonitoring ? "待機中(入力確認)" : "待機中";
            TrackInfoText.Text = $"次の No.{ParseInt(NoBox.Text, 1, 0, 99999999)}";
            FileNameText.Text = "";
            DiskText.Text = "";
            string probs = ctl.IsMonitoring ? ctl.DescribeSourceProblems() : "";
            if (probs.Length > 0) DiskText.Text = "未接続: " + probs;
            SetTray(null);
            return;
        }

        var st = eng.State;
        switch (st)
        {
            case EngineState.Armed:
                StateText.Text = "音が出るのを待っています";
                ElapsedText.Text = "0:00:00";
                break;
            case EngineState.Stopping:
                StateText.Text = "保存しています…";
                break;
            default:
                StateText.Text = eng.IsUserPaused ? "❚❚ 一時停止中(この間の音は保存されません)"
                    : eng.IsPaused ? "デバイス待機中(一時停止)" : "● 録音中";
                ElapsedText.Text = Hms(eng.ElapsedSeconds);
                break;
        }
        TrackInfoText.Text = $"No.{eng.No}  Track {eng.CurrentTrack}  このファイル {Hms(eng.CurrentFileSeconds)}";
        FileNameText.Text = string.IsNullOrEmpty(eng.CurrentFilePath) ? "" : Path.GetFileName(eng.CurrentFilePath);
        long free = eng.FreeDiskBytes;
        DiskText.Text = free >= 0 ? $"ディスク空き {free / 1073741824.0:0.0} GB" : "";
        SetTray(st == EngineState.Recording ? (eng.IsUserPaused ? "一時停止中 " : "録音中 ") + Hms(eng.ElapsedSeconds) : "待機");
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool rec = ctl.IsRecording;
        var st = ctl.EngineState;
        BtnRecord.Content = rec ? "■  録音停止" : "●  録音開始";
        BtnRecord.IsEnabled = st != EngineState.Stopping;
        BtnSplit.IsEnabled = rec && st == EngineState.Recording && !ctl.IsUserPaused;
        BtnPauseRec.IsEnabled = rec && st == EngineState.Recording;
        BtnPauseRec.Content = ctl.IsUserPaused ? "▶  再開" : "❚❚  一時停止";
        LockPanel.IsEnabled = !rec;
        PresetCombo.IsEnabled = !rec;
        BtnPresetSave.IsEnabled = BtnPresetSaveAs.IsEnabled = BtnPresetDelete.IsEnabled = !rec;
        BtnPresetImport.IsEnabled = !rec;
        MonitorCheck.IsEnabled = !rec;
        Gain1.IsEnabled = Gain2.IsEnabled = Gain3.IsEnabled = MasterGain.IsEnabled = true;
    }

    // ---------------------------------------------------------------- 録音の開始・停止

    private async void OnRecordClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ctl.IsRecording) { await StopRecordingUi(); return; }
            StartRecordingUi();
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("録音ボタン: " + ex);
            MessageBox.Show(this, ex.Message, "LokuOn", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartRecordingUi()
    {
        var p = ReadPreset();
        SyncSettingsFromUi();
        string? err = ctl.StartRecording(p);
        if (err != null)
        {
            AddLog("録音を開始できません: " + err);
            MessageBox.Show(this, err, "録音を開始できません", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (ctl.Settings.MonitorInput) StartMonitorSafe();
            return;
        }
        UpdateButtons();
    }

    private async Task StopRecordingUi()
    {
        BtnRecord.IsEnabled = false;
        StateText.Text = "保存しています…";
        await ctl.StopRecordingAsync();
        UpdateButtons();
    }

    private void OnRecordingStarted()
    {
        AddLog("録音を開始しました");
        WaveView.Refresh(null);
        MasterMeter.Reset();
        SourceMeter.Reset();
        UpdateButtons();
    }

    private void OnRecordingEnded(string reason)
    {
        NoBox.Text = ctl.Settings.No.ToString(CultureInfo.InvariantCulture);
        UpdateButtons();
        UpdateScheduleStatus();
        if (reason.Length > 0)
        {
            AddLog(reason);
            Dispatcher.InvokeAsync(() =>
            {
                ShowFromTray();
                MessageBox.Show(this, reason, "録音が停止しました", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        else AddLog("録音を終了しました");
        if (ctl.Settings.MonitorInput) StartMonitorSafe();
        else WaveView.Refresh(null);
    }

    private void OnSplitClick(object sender, RoutedEventArgs e)
    {
        ctl.Split();
        AddLog("分割します");
    }

    private void StartMonitorSafe()
    {
        try
        {
            if (ctl.IsRecording) return;
            ctl.StartMonitor(ReadPreset());
        }
        catch (Exception ex) { App.WriteCrashLog("モニター: " + ex); }
    }

    /// <summary>デバイスや形式が変わったとき、待機中の入力確認を作り直す。</summary>
    private void RestartMonitor()
    {
        if (loading || ctl.IsRecording) return;
        if (ctl.Settings.MonitorInput) StartMonitorSafe();
        else ctl.StopMonitor();
        UpdateMeterSlots();
    }

    private void UpdateMeterSlots()
    {
        var p = ReadPreset();
        for (int i = 0; i < Preset.SourceSlots; i++) SourceMeter.SetEnabled(i, !string.IsNullOrWhiteSpace(p.Sources[i].DeviceId));
    }

    // ---------------------------------------------------------------- 終了

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (forceClose) { Cleanup(); return; }

        if (ctl.IsRecording)
        {
            if (ctl.Settings.ConfirmCloseWhileRecording)
            {
                var r = MessageBox.Show(this,
                    "録音中です。\n\n「はい」: 録音を停止してファイルを保存し、終了します。\n「いいえ」: 録音を続けます。",
                    "LokuOn - 録音中です", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            }
            e.Cancel = true;
            try
            {
                BtnRecord.IsEnabled = false;
                StateText.Text = "保存して終了します…";
                await ctl.StopRecordingAsync();
            }
            catch (Exception ex) { App.WriteCrashLog("終了時の停止: " + ex); }
            forceClose = true;
            Close();
            return;
        }
        Cleanup();
    }

    private void Cleanup()
    {
        try { timer.Stop(); } catch { }
        try { SyncSettingsFromUi(); ctl.SaveSettings(); } catch { }
        try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; } catch { }
        try { UnregisterHotkeys(); } catch { }
        try { DisposeTray(); } catch { }
        try { ShutdownPlayer(); } catch { }
        try { ctl.Dispose(); } catch { }
    }
}
