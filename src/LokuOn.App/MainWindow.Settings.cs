using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using LokuOn.Audio;
using LokuOn.Core;

namespace LokuOn.App;

// 画面の設定 ⇔ Preset / AppSettings の受け渡し、プリセットの管理
public partial class MainWindow
{
    private readonly Dictionary<string, string> missingDeviceNames = new();
    private ComboBox[] DevCombos => new[] { DevCombo1, DevCombo2, DevCombo3, DevCombo4 };
    private Slider[] Gains => new[] { Gain1, Gain2, Gain3, Gain4 };
    private TextBlock[] GainTexts => new[] { Gain1Text, Gain2Text, Gain3Text, Gain4Text };

    private static readonly int[] AllRates = { 44100, 48000, 88200, 96000 };
    private static readonly int[] Mp3Rates = { 32000, 44100, 48000 };
    private static readonly int[] Bitrates = { 64, 96, 128, 160, 192, 224, 256, 320 };

    // ---------------------------------------------------------------- プルダウンの中身

    private void SetupStaticCombos()
    {
        FormatCombo.ItemsSource = new List<Choice<AudioFormat>>
        {
            new("FLAC (.flac)  可逆圧縮", AudioFormat.Flac),
            new("MP3 (.mp3)", AudioFormat.Mp3),
            new("AAC (.m4a)", AudioFormat.M4a),
            new("AAC (.aac)  ADTS", AudioFormat.Aac),
            new("WAV (.wav)  無圧縮", AudioFormat.Wav),
            new("Ogg Vorbis (.ogg)", AudioFormat.Ogg),
            new("Opus (.opus)", AudioFormat.Opus),
        };
        ChannelsCombo.ItemsSource = new List<Choice<int>> { new("ステレオ", 2), new("モノラル", 1) };
        BitrateCombo.ItemsSource = Bitrates.Select(b => new Choice<int>(b + " kbps", b)).ToList();
        TimeStyleCombo.ItemsSource = new List<Choice<TimeStyle>>
        {
            new("自動  36：25 / 1：05：30", TimeStyle.Auto),
            new("分のみ  125：30", TimeStyle.TotalMinutes),
        };
        LostPolicyCombo.ItemsSource = new List<Choice<DeviceLostPolicy>>
        {
            new("同じファイルで続ける(無音を挿入)", DeviceLostPolicy.Continue),
            new("戻るまで待機(一時停止)", DeviceLostPolicy.Wait),
            new("戻ったら別ファイルに分割", DeviceLostPolicy.SplitOnReturn),
        };
        SpeedCombo.ItemsSource = new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0 }
            .Select(v => new Choice<double>(v.ToString("0.##", CultureInfo.InvariantCulture) + "x", v)).ToList();
        SpeedCombo.SelectedIndex = 2;
        RateCombo.ItemsSource = AllRates.Select(RateChoice).ToList();
    }

    private static Choice<int> RateChoice(int r) => new((r / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " kHz", r);

    private static void SelectValue<T>(ComboBox cb, T value)
    {
        foreach (var o in cb.Items)
            if (o is Choice<T> c && EqualityComparer<T>.Default.Equals(c.Value, value)) { cb.SelectedItem = o; return; }
    }

    private static T ValueOf<T>(ComboBox cb, T def) => cb.SelectedItem is Choice<T> c ? c.Value : def;

    private void PopulateDeviceCombos()
    {
        // いま選ばれているものを覚えておいて、一覧を作り直したあとに戻す
        var want = new SourceSetting[Preset.SourceSlots];
        for (int i = 0; i < want.Length; i++)
        {
            var d = DevCombos[i].SelectedItem as DeviceInfo;
            want[i] = d != null
                ? new SourceSetting { DeviceId = d.Id, DeviceName = OriginalName(d) }
                : ctl.Settings.Current.Sources[i];
        }
        bool prev = loading; loading = true;
        try
        {
            for (int i = 0; i < want.Length; i++) FillDeviceCombo(DevCombos[i], want[i]);
        }
        finally { loading = prev; }
    }

    private void FillDeviceCombo(ComboBox cb, SourceSetting s)
    {
        var list = new List<DeviceInfo> { new() { Id = "", Display = "(使わない)", Kind = DeviceKind.WasapiCapture } };
        list.AddRange(ctl.Devices);
        if (!string.IsNullOrWhiteSpace(s.DeviceId) && !list.Any(d => d.Id == s.DeviceId))
        {
            string name = string.IsNullOrWhiteSpace(s.DeviceName) ? s.DeviceId : s.DeviceName;
            missingDeviceNames[s.DeviceId] = name;
            list.Add(new DeviceInfo { Id = s.DeviceId, Display = "(見つかりません) " + name, Kind = DeviceKind.WasapiCapture });
        }
        cb.ItemsSource = list;
        cb.SelectedItem = list.FirstOrDefault(d => d.Id == (s.DeviceId ?? "")) ?? list[0];
    }

    private string OriginalName(DeviceInfo d)
    {
        if (d.Id.Length == 0) return "";
        return missingDeviceNames.TryGetValue(d.Id, out var n) ? n : d.Display;
    }

    // ---------------------------------------------------------------- イベントの接続

    private void WireEvents()
    {
        BtnRecord.Click += OnRecordClick;
        BtnSplit.Click += OnSplitClick;
        TemplateCombo.SelectionChanged += (s, e) => OnTemplateComboChanged();
        TemplateBox.TextChanged += (s, e) => SyncTemplateCombo();
        BtnTemplateSave.Click += (s, e) => OnTemplateSave();
        BtnTemplateDelete.Click += (s, e) => OnTemplateDelete();
        BtnTemplateReset.Click += (s, e) => OnTemplateReset();
        AutoSplitBox.TextChanged += (s, e) => UpdateSplitHint();
        MaxHoursBox.TextChanged += (s, e) => UpdateSplitHint();
        BtnSettings.Click += (s, e) => OnOpenAppInfo();
        BtnPauseRec.Click += (s, e) => { ctl.TogglePauseRecording(); UpdateButtons(); };

        foreach (var cb in DevCombos) cb.SelectionChanged += (s, e) => { if (loading) return; OnSettingChanged(); UpdateMeterSlots(); RestartMonitor(); };
        MixModeMix.Click += (s, e) => OnSettingChanged();
        MixModeSep.Click += (s, e) => OnSettingChanged();
        BtnRefreshDevices.Click += (s, e) =>
        {
            ctl.RefreshDevices();
            PopulateDeviceCombos();
            AddLog($"デバイス一覧を更新しました({ctl.Devices.Count} 件)");
            RestartMonitor();
        };

        FormatCombo.SelectionChanged += (s, e) =>
        {
            if (loading) return;
            UpdateFormatDependentUi(null, null);
            OnSettingChanged();
            RestartMonitor();
        };
        RateCombo.SelectionChanged += (s, e) => { if (loading) return; OnSettingChanged(); RestartMonitor(); };
        ChannelsCombo.SelectionChanged += (s, e) => { if (loading) return; OnSettingChanged(); RestartMonitor(); };
        BitrateCombo.SelectionChanged += (s, e) => OnSettingChanged();
        BitDepthCombo.SelectionChanged += (s, e) => OnSettingChanged();
        TimeStyleCombo.SelectionChanged += (s, e) => OnSettingChanged();
        LostPolicyCombo.SelectionChanged += (s, e) => OnSettingChanged();

        foreach (var tb in new[] { FolderBox, DateFormatBox, TemplateBox, NoBox, TrackBox, AutoSplitBox, MaxHoursBox, SilenceDbBox,
                     SilenceSecBox, MinTrackBox, AutoStartDbBox, AutoStopBox, TagTitle, TagArtist, TagAlbum, TagAlbumArtist,
                     TagGenre, TagYear, TagComment, DiskWarnBox, DiskStopBox })
            tb.TextChanged += (s, e) => OnSettingChanged();
        foreach (var tb in new[] { AutoSplitBox, MaxHoursBox, SilenceDbBox, SilenceSecBox, MinTrackBox, AutoStartDbBox, AutoStopBox,
                     NoBox, TrackBox, DiskWarnBox, DiskStopBox })
            tb.LostFocus += (s, e) => NormalizeNumbers();
        // 保存先を書き換えたときだけ一覧を読み直す(そのままなら無駄な読み込みと選択の解除を避ける。手動は「保存先から読み込み」ボタン)
        FolderBox.LostFocus += (s, e) => { if (FolderBox.Text.Trim() != lastScanRoot) RescanFiles(); };

        foreach (var ck in new[] { UseDateFolderCheck, SilenceSplitCheck, AutoStartCheck, AutoResumeCheck, WriteTrackTagCheck })
            ck.Click += (s, e) => OnSettingChanged();

        BtnBrowse.Click += (s, e) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "録音の保存先フォルダを選んでください" };
            if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text;
            if (dlg.ShowDialog(this) == true) { FolderBox.Text = dlg.FolderName; RescanFiles(); }
        };
        BtnOpenFolder.Click += (s, e) =>
        {
            try
            {
                string dir = FolderBox.Text.Trim();
                if (dir.Length == 0) return;
                Directory.CreateDirectory(dir);
                FileOps.OpenFolder(dir);
            }
            catch (Exception ex) { SetInlineError(ErrorText, "フォルダを開けません: " + ex.Message); }
        };

        // ゲイン
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            int slot = i;
            Gains[i].ValueChanged += (s, e) =>
            {
                UpdateGainTexts();
                ctl.SetSourceGain(slot, Gains[slot].Value);
                MarkDirty();
            };
            Gains[i].MouseDoubleClick += (s, e) => Gains[slot].Value = 0;
        }
        MasterGain.ValueChanged += (s, e) => { UpdateGainTexts(); ctl.SetMasterGain(MasterGain.Value); MarkDirty(); };
        MasterGain.MouseDoubleClick += (s, e) => MasterGain.Value = 0;

        MonitorCheck.Click += (s, e) =>
        {
            ctl.Settings.MonitorInput = MonitorCheck.IsChecked == true;
            MarkDirty();
            RestartMonitor();
            if (!ctl.Settings.MonitorInput) WaveView.Refresh(null);
        };

        // プリセット
        PresetCombo.SelectionChanged += OnPresetSelected;
        BtnPresetSave.Click += (s, e) => OnPresetSave();
        BtnPresetSaveAs.Click += (s, e) => OnPresetSaveAs();
        BtnPresetDelete.Click += (s, e) => OnPresetDelete();
        BtnPresetFolder.Click += (s, e) => OnPresetFolder();
        PresetCombo.DropDownOpened += (s, e) => ReloadPresetsFromFolder();

        // アプリ設定
        foreach (var ck in new[] { ConfirmCloseCheck })
            ck.Click += (s, e) => { SyncAppFlagsFromUi(); MarkDirty(); };
        HotkeysCheck.Click += (s, e) => { SyncAppFlagsFromUi(); RegisterHotkeys(); MarkDirty(); };
        HotkeyStartBox.LostFocus += (s, e) => { SyncAppFlagsFromUi(); RegisterHotkeys(); MarkDirty(); };
        HotkeySplitBox.LostFocus += (s, e) => { SyncAppFlagsFromUi(); RegisterHotkeys(); MarkDirty(); };
        FfmpegBox.LostFocus += (s, e) => ApplyFfmpegPath();
        BtnFfmpegBrowse.Click += (s, e) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "ffmpeg.exe を選んでください", Filter = "ffmpeg.exe|ffmpeg.exe|実行ファイル (*.exe)|*.exe" };
            if (dlg.ShowDialog(this) == true) { FfmpegBox.Text = dlg.FileName; ApplyFfmpegPath(); }
        };

        // 予約録音
        ScheduleCheck.Click += (s, e) => { ApplyScheduleFromUi(); };
        ScheduleDailyCheck.Click += (s, e) => { ApplyScheduleFromUi(); };
        ScheduleDate.SelectedDateChanged += (s, e) => ApplyScheduleFromUi();
        ScheduleTimeBox.LostFocus += (s, e) => ApplyScheduleFromUi();
        ScheduleMinutesBox.LostFocus += (s, e) => ApplyScheduleFromUi();

        // ファイル一覧とプレイヤー
        WireFileEvents();
    }

    private void OnSettingChanged()
    {
        if (loading) return;
        UpdatePreview();
        UpdateQualitySummary();
        MarkDirty();
    }

    // ---------------------------------------------------------------- 形式に応じた表示の切り替え

    /// <summary>形式に合わせて、サンプリング・ビットレート・ビット深度の選択肢を整える。</summary>
    private void UpdateFormatDependentUi(int? wantRate = null, int? wantDepth = null, int? wantLevel = null)
    {
        bool prev = loading; loading = true;
        try
        {
            var fmt = ValueOf(FormatCombo, AudioFormat.Flac);
            int curRate = wantRate ?? ValueOf(RateCombo, 48000);
            int curDepth = wantDepth ?? ValueOf(BitDepthCombo, 16);
            int curLevel = wantLevel ?? ValueOf(CompressionCombo, -1);

            int[] rates = fmt switch
            {
                AudioFormat.Mp3 => Mp3Rates,
                AudioFormat.Opus => new[] { 48000 },
                _ => AllRates,
            };
            var rateList = rates.ToList();
            if (!rateList.Contains(curRate))
            {
                // 範囲外の値(設定ファイルを手で書き換えた場合など)は、近い値に寄せる
                curRate = rateList.OrderBy(r => Math.Abs(r - curRate)).First();
            }
            RateCombo.ItemsSource = rateList.Select(RateChoice).ToList();
            SelectValue(RateCombo, curRate);

            bool lossy = fmt == AudioFormat.Mp3 || fmt == AudioFormat.M4a || fmt == AudioFormat.Aac || fmt == AudioFormat.Opus || fmt == AudioFormat.Ogg;
            BitrateCombo.IsEnabled = BitrateLabel.IsEnabled = lossy;

            int maxLevel = Preset.MaxCompression(fmt);
            var levels = new List<Choice<int>> { new(maxLevel == 0 ? "なし(Vorbisは設定不可)" : "標準", -1) };
            for (int l = 0; l <= maxLevel; l++) levels.Add(new Choice<int>(l.ToString(CultureInfo.InvariantCulture), l));
            CompressionCombo.ItemsSource = levels;
            if (!levels.Any(c => c.Value == curLevel)) curLevel = -1;
            SelectValue(CompressionCombo, curLevel);
            CompressionCombo.IsEnabled = CompressionLabel.IsEnabled = maxLevel > 0;

            bool hasDepth = fmt == AudioFormat.Flac || fmt == AudioFormat.Wav;
            BitDepthCombo.IsEnabled = BitDepthLabel.IsEnabled = hasDepth;
            var depths = new List<Choice<int>> { new("16 bit", 16), new("24 bit", 24) };
            if (fmt == AudioFormat.Wav) depths.Add(new Choice<int>("32 bit float", 32));
            BitDepthCombo.ItemsSource = depths;
            if (!depths.Any(d => d.Value == curDepth)) curDepth = 16;
            SelectValue(BitDepthCombo, curDepth);
        }
        finally { loading = prev; }
        UpdateQualitySummary();
    }

    private void UpdateQualitySummary()
    {
        var p = ReadPreset();
        string rate = (p.SampleRate / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "kHz";
        string ch = p.Channels == 1 ? "mono" : "stereo";
        bool lossy = p.Format is AudioFormat.Mp3 or AudioFormat.M4a or AudioFormat.Aac or AudioFormat.Opus or AudioFormat.Ogg;
        string text;
        if (lossy)
        {
            double mbPerHour = p.BitrateKbps * 1000.0 / 8 * 3600 / 1048576.0;
            text = $"{p.BitrateKbps}kbps / {rate} / {ch}    目安: 約 {mbPerHour:0} MB / 時間";
        }
        else
        {
            double pcm = p.SampleRate * p.Channels * (p.BitDepth / 8.0) * 3600 / 1048576.0;
            string bits = p.BitDepth >= 32 && p.Format == AudioFormat.Wav ? "32bit float" : p.BitDepth + "bit";
            text = p.Format == AudioFormat.Wav
                ? $"WAV {bits} / {rate} / {ch}    目安: 約 {pcm:0} MB / 時間"
                : $"FLAC {bits} / {rate} / {ch}    目安: 約 {pcm * 0.4:0}〜{pcm * 0.7:0} MB / 時間(音による)";
        }
        QualitySummary.Text = text;
    }

    private void UpdatePreview()
    {
        try
        {
            var p = ReadPreset();
            int no = ParseInt(NoBox.Text, 1, 0, 99999999);
            int track = ParseInt(TrackBox.Text, 1, 0, 99999999);
            string dev = p.Sources.Where(s => s.DeviceId.Length > 0)
                .Select(s => RecorderController.ShortLabel(s.DeviceId, s.DeviceName)).FirstOrDefault() ?? "";
            var ctx = new NameContext
            {
                No = no, Track = track, StartSec = 0, EndSec = 2185, FileStartTime = DateTime.Now,
                Device = dev, Preset = p.Name, TimeStyle = p.TimeStyle,
            };
            string name = NameTemplate.Sanitize(NameTemplate.Render(p.FileNameTemplate, ctx));
            string ext = EncoderSpec.Extension(p.Format);
            ctx.Track = track + 1; ctx.StartSec = 2184; ctx.EndSec = 2400;
            string name2 = NameTemplate.Sanitize(NameTemplate.Render(p.FileNameTemplate, ctx));
            string date = "";
            if (p.UseDateFolder)
            {
                date = NameTemplate.RenderFolder(p.DateFolderFormat, DateTime.Now) + "\\";
            }
            TemplatePreview.Text = $"例: {date}{name}{ext}\n分割後: {date}{name2}{ext}";
        }
        catch (Exception ex) { TemplatePreview.Text = "プレビューできません: " + ex.Message; }
    }

    private void UpdateGainTexts()
    {
        for (int i = 0; i < Preset.SourceSlots; i++) GainTexts[i].Text = FormatDb(Gains[i].Value);
        MasterGainText.Text = FormatDb(MasterGain.Value);
    }

    private static string FormatDb(double v)
        => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture) + " dB";

    // ---------------------------------------------------------------- 数値の読み取り

    private static string Norm(string? s) => (s ?? "").Normalize(NormalizationForm.FormKC).Trim();

    private static int ParseInt(string? s, int def, int min, int max)
        => int.TryParse(Norm(s), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? Math.Clamp(v, min, max) : def;

    private static double ParseDouble(string? s, double def, double min, double max)
        => double.TryParse(Norm(s), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) ? Math.Clamp(v, min, max) : def;

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>範囲外・全角などの数字を整えて、テキストボックスに書き戻す。</summary>
    private void NormalizeNumbers()
    {
        var p = ReadPreset();
        bool prev = loading; loading = true;
        try
        {
            AutoSplitBox.Text = FormatHms(p.AutoSplitSeconds);
            MaxHoursBox.Text = p.MaxFileHours.ToString(CultureInfo.InvariantCulture);
            SilenceDbBox.Text = Num(p.SilenceDb);
            SilenceSecBox.Text = Num(p.SilenceSeconds);
            MinTrackBox.Text = Num(p.MinTrackSeconds);
            AutoStartDbBox.Text = Num(p.AutoStartDb);
            AutoStopBox.Text = Num(p.AutoStopSilenceSeconds);
            NoBox.Text = ParseInt(NoBox.Text, ctl.Settings.No, 0, 99999999).ToString(CultureInfo.InvariantCulture);
            TrackBox.Text = ParseInt(TrackBox.Text, ctl.Settings.TrackStart, 0, 99999999).ToString(CultureInfo.InvariantCulture);
            DiskWarnBox.Text = Num(ParseDouble(DiskWarnBox.Text, ctl.Settings.DiskWarnGB, 0, 100000));
            DiskStopBox.Text = Num(ParseDouble(DiskStopBox.Text, ctl.Settings.DiskStopGB, 0, 100000));
        }
        finally { loading = prev; }
        UpdatePreview();
        MarkDirty();
    }

    // ---------------------------------------------------------------- 画面 → Preset

    private Preset ReadPreset()
    {
        var d = new Preset();
        var p = new Preset { Name = PresetCombo.SelectedItem as string ?? "" };
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            var dev = DevCombos[i].SelectedItem as DeviceInfo;
            p.Sources[i].DeviceId = dev?.Id ?? "";
            p.Sources[i].DeviceName = dev == null ? "" : OriginalName(dev);
            p.Sources[i].GainDb = Math.Round(Gains[i].Value, 1);
        }
        p.MixMode = MixModeSep.IsChecked == true ? MixMode.Separate : MixMode.Mix;
        p.MasterGainDb = Math.Round(MasterGain.Value, 1);
        p.Format = ValueOf(FormatCombo, d.Format);
        p.SampleRate = ValueOf(RateCombo, d.SampleRate);
        p.Channels = ValueOf(ChannelsCombo, d.Channels);
        p.BitrateKbps = ValueOf(BitrateCombo, d.BitrateKbps);
        p.BitDepth = ValueOf(BitDepthCombo, d.BitDepth);
        p.CompressionLevel = ValueOf(CompressionCombo, d.CompressionLevel);
        p.SaveFolder = FolderBox.Text.Trim();
        p.UseDateFolder = UseDateFolderCheck.IsChecked == true;
        p.DateFolderFormat = string.IsNullOrWhiteSpace(DateFormatBox.Text) ? d.DateFolderFormat : DateFormatBox.Text.Trim();
        p.FileNameTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text) ? d.FileNameTemplate : TemplateBox.Text.Trim();
        p.TimeStyle = ValueOf(TimeStyleCombo, d.TimeStyle);
        p.AutoSplitSeconds = ReadAutoSplitSeconds();
        p.MaxFileHours = ParseInt(MaxHoursBox.Text, d.MaxFileHours, 1, 168);
        p.SplitOnSilence = SilenceSplitCheck.IsChecked == true;
        p.SilenceDb = ParseDouble(SilenceDbBox.Text, d.SilenceDb, -100, 0);
        p.SilenceSeconds = ParseDouble(SilenceSecBox.Text, d.SilenceSeconds, 0.1, 3600);
        p.MinTrackSeconds = ParseDouble(MinTrackBox.Text, d.MinTrackSeconds, 0, 86400);
        p.AutoStartOnSound = AutoStartCheck.IsChecked == true;
        p.AutoStartDb = ParseDouble(AutoStartDbBox.Text, d.AutoStartDb, -100, 0);
        p.AutoStopSilenceSeconds = ParseDouble(AutoStopBox.Text, d.AutoStopSilenceSeconds, 0, 864000);
        p.AutoResume = AutoResumeCheck.IsChecked == true;
        p.DeviceLostPolicy = ValueOf(LostPolicyCombo, d.DeviceLostPolicy);
        p.Tags.Title = TagTitle.Text.Trim();
        p.Tags.Artist = TagArtist.Text.Trim();
        p.Tags.Album = TagAlbum.Text.Trim();
        p.Tags.AlbumArtist = TagAlbumArtist.Text.Trim();
        p.Tags.Genre = TagGenre.Text.Trim();
        p.Tags.Year = TagYear.Text.Trim();
        p.Tags.Comment = TagComment.Text.Trim();
        p.WriteTrackTag = WriteTrackTagCheck.IsChecked == true;
        p.Outputs = outCards.Select(o => o.Clone()).ToList();
        p.InputNames = (string[])inputNames.Clone();
        p.Normalize();
        return p;
    }

    // ---------------------------------------------------------------- Preset → 画面

    private void LoadPresetToUi(Preset p, bool applyFolder = true)
    {
        bool prev = loading; loading = true;
        try
        {
            p.Normalize();
            for (int i = 0; i < Preset.SourceSlots; i++)
            {
                FillDeviceCombo(DevCombos[i], p.Sources[i]);
                Gains[i].Value = Math.Clamp(p.Sources[i].GainDb, Gains[i].Minimum, Gains[i].Maximum);
            }
            MixModeMix.IsChecked = p.MixMode == MixMode.Mix;
            MixModeSep.IsChecked = p.MixMode == MixMode.Separate;
            MasterGain.Value = Math.Clamp(p.MasterGainDb, MasterGain.Minimum, MasterGain.Maximum);
            SelectValue(FormatCombo, p.Format);
            SelectValue(ChannelsCombo, p.Channels);
            SelectValue(BitrateCombo, p.BitrateKbps);
            if (BitrateCombo.SelectedItem == null) BitrateCombo.SelectedIndex = Bitrates.Length - 1;
            UpdateFormatDependentUi(p.SampleRate, p.BitDepth, p.CompressionLevel);
            if (applyFolder) FolderBox.Text = p.SaveFolder;
            UseDateFolderCheck.IsChecked = p.UseDateFolder;
            DateFormatBox.Text = p.DateFolderFormat;
            TemplateBox.Text = p.FileNameTemplate;
            RefreshTemplateCombo();
            SelectValue(TimeStyleCombo, p.TimeStyle);
            AutoSplitBox.Text = FormatHms(p.AutoSplitSeconds);
            MaxHoursBox.Text = p.MaxFileHours.ToString(CultureInfo.InvariantCulture);
            SilenceSplitCheck.IsChecked = p.SplitOnSilence;
            SilenceDbBox.Text = Num(p.SilenceDb);
            SilenceSecBox.Text = Num(p.SilenceSeconds);
            MinTrackBox.Text = Num(p.MinTrackSeconds);
            AutoStartCheck.IsChecked = p.AutoStartOnSound;
            AutoStartDbBox.Text = Num(p.AutoStartDb);
            AutoStopBox.Text = Num(p.AutoStopSilenceSeconds);
            AutoResumeCheck.IsChecked = p.AutoResume;
            SelectValue(LostPolicyCombo, p.DeviceLostPolicy);
            TagTitle.Text = p.Tags.Title;
            TagArtist.Text = p.Tags.Artist;
            TagAlbum.Text = p.Tags.Album;
            TagAlbumArtist.Text = p.Tags.AlbumArtist;
            TagGenre.Text = p.Tags.Genre;
            TagYear.Text = p.Tags.Year;
            TagComment.Text = p.Tags.Comment;
            WriteTrackTagCheck.IsChecked = p.WriteTrackTag;
            UpdateGainTexts();
            ApplyOutputsFromPreset(p);
        }
        finally { loading = prev; }
        UpdatePreview();
        UpdateQualitySummary();
    }

    // ---------------------------------------------------------------- アプリ設定 ⇔ 画面

    private void LoadAppSettingsToUi()
    {
        var s = ctl.Settings;
        bool prev = loading; loading = true;
        try
        {
            NoBox.Text = s.No.ToString(CultureInfo.InvariantCulture);
            TrackBox.Text = s.TrackStart.ToString(CultureInfo.InvariantCulture);
            MonitorCheck.IsChecked = s.MonitorInput;
            ConfirmCloseCheck.IsChecked = s.ConfirmCloseWhileRecording;
            HotkeysCheck.IsChecked = s.HotkeysEnabled;
            HotkeyStartBox.Text = s.HotkeyStartStop;
            HotkeySplitBox.Text = s.HotkeySplit;
            DiskWarnBox.Text = Num(s.DiskWarnGB);
            DiskStopBox.Text = Num(s.DiskStopGB);
            FfmpegBox.Text = s.FfmpegPath;
            DataDirText.Text = "設定ファイル: " + ctl.SettingsPath + "\n(このファイルをコピーすればバックアップになります。テキストエディタでも編集できます)";
            UpdateFfmpegStatus();
            LoadScheduleToUi();
        }
        finally { loading = prev; }
    }

    private void SyncAppFlagsFromUi()
    {
        var s = ctl.Settings;
        s.MonitorInput = MonitorCheck.IsChecked == true;
        s.ConfirmCloseWhileRecording = ConfirmCloseCheck.IsChecked == true;
        s.HotkeysEnabled = HotkeysCheck.IsChecked == true;
        s.HotkeyStartStop = HotkeyStartBox.Text.Trim();
        s.HotkeySplit = HotkeySplitBox.Text.Trim();
        s.DiskWarnGB = ParseDouble(DiskWarnBox.Text, s.DiskWarnGB, 0, 100000);
        s.DiskStopGB = ParseDouble(DiskStopBox.Text, s.DiskStopGB, 0, 100000);
    }

    /// <summary>画面の内容を、保存用の設定(ctl.Settings)へ写す。</summary>
    private void SyncSettingsFromUi()
    {
        var s = ctl.Settings;
        SyncAppFlagsFromUi();
        s.Current = ReadPreset();
        s.Current.Name = "";
        s.No = ParseInt(NoBox.Text, s.No, 0, 99999999);
        s.TrackStart = ParseInt(TrackBox.Text, s.TrackStart, 0, 99999999);
        s.LastPresetName = PresetCombo.SelectedItem as string ?? "";
    }

    private void UpdateFfmpegStatus()
    {
        FfmpegStatus.Text = ctl.FfmpegPath != null
            ? "使用中: " + ctl.FfmpegPath
            : "ffmpeg.exe が見つかりません。exeと同じ場所の ffmpeg フォルダに置くか、上で場所を指定してください。";
    }

    private void ApplyFfmpegPath()
    {
        if (loading) return;
        ctl.RelocateFfmpeg(FfmpegBox.Text);
        UpdateFfmpegStatus();
        MarkDirty();
    }

    // ---------------------------------------------------------------- 予約録音

    private void LoadScheduleToUi()
    {
        var sc = ctl.Settings.Schedule;
        bool prev = loading; loading = true;
        try
        {
            ScheduleCheck.IsChecked = sc.Enabled;
            ScheduleDate.SelectedDate = sc.StartAt.Date;
            ScheduleTimeBox.Text = sc.StartAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            ScheduleMinutesBox.Text = sc.DurationMinutes.ToString(CultureInfo.InvariantCulture);
            ScheduleDailyCheck.IsChecked = sc.Daily;
        }
        finally { loading = prev; }
        UpdateScheduleStatus();
    }

    private void ApplyScheduleFromUi()
    {
        if (loading) return;
        var sc = ctl.Settings.Schedule;
        var date = ScheduleDate.SelectedDate ?? sc.StartAt.Date;
        TimeSpan time = sc.StartAt.TimeOfDay;
        string t = Norm(ScheduleTimeBox.Text).Replace('：', ':');
        if (TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1)) time = ts;
        sc.StartAt = date.Date + time;
        sc.DurationMinutes = ParseInt(ScheduleMinutesBox.Text, sc.DurationMinutes, 1, 24 * 60 * 7);
        sc.Daily = ScheduleDailyCheck.IsChecked == true;
        sc.Enabled = ScheduleCheck.IsChecked == true;
        bool prev = loading; loading = true;
        try
        {
            ScheduleTimeBox.Text = sc.StartAt.ToString("HH:mm", CultureInfo.InvariantCulture);
            ScheduleMinutesBox.Text = sc.DurationMinutes.ToString(CultureInfo.InvariantCulture);
        }
        finally { loading = prev; }
        if (sc.Enabled && !sc.Daily && sc.StartAt.AddMinutes(sc.DurationMinutes) <= DateTime.Now)
        {
            sc.Enabled = false;
            ScheduleCheck.IsChecked = false;
            AddLog("予約の時刻がすでに過ぎているため、予約を有効にできませんでした");
        }
        UpdateScheduleStatus();
        MarkDirty();
    }

    private void UpdateScheduleStatus()
    {
        var sc = ctl.Settings.Schedule;
        ScheduleStatus.Text = sc.Enabled
            ? $"次の予約: {sc.StartAt:yyyy/MM/dd HH:mm} から {sc.DurationMinutes} 分間{(sc.Daily ? "(毎日くり返し)" : "")}"
            : "予約は無効です";
    }

    // ---------------------------------------------------------------- プリセット

    private void RefreshPresetCombo(string? select)
    {
        bool prev = loading; loading = true;
        try
        {
            var names = ctl.Settings.Presets.Select(x => x.Name).ToList();
            PresetCombo.ItemsSource = names;
            PresetCombo.SelectedItem = !string.IsNullOrEmpty(select) && names.Contains(select) ? select : null;
            PresetHint.Text = names.Count == 0
                ? "保存したプリセットはありません。いまの画面の設定を「名前を付けて保存」できます。"
                : "";
        }
        finally { loading = prev; }
    }

    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (loading) return;
        if (PresetCombo.SelectedItem is not string name) return;
        var p = ctl.Settings.Presets.FirstOrDefault(x => x.Name == name);
        if (p == null) return;
        LoadPresetToUi(p.Clone(), applyFolder: false);
        ctl.Settings.LastPresetName = name;
        UpdateMeterSlots();
        RestartMonitor();
        MarkDirty();
        AddLog($"プリセット「{name}」を読み込みました");
    }

    private void OnPresetSave()
    {
        if (PresetCombo.SelectedItem is not string name || name.Length == 0) { OnPresetSaveAs(); return; }
        StorePreset(name);
    }

    /// <summary>「○分ごと」と「安全上限」が重なるとき、実際にどうなるかを文章で見せる。</summary>
    private int lastAutoSplitSec;

    /// <summary>「時:分:秒」を秒にする。1:30:00 / 90:00(=分:秒) / 全角も可。読めなければ null。</summary>
    private static int? ParseHms(string? text)
    {
        string t = Norm(text).Replace('：', ':');
        if (t.Length == 0) return 0;
        var parts = t.Split(':');
        if (parts.Length > 3) return null;
        long total = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
            total = total * 60 + n;
        }
        if (parts.Length == 1) return null;   // 数字だけでは単位が分からないので受け付けない
        if (total > 86400) return null;
        return (int)total;
    }

    private static string FormatHms(int sec)
        => $"{sec / 3600:00}:{sec / 60 % 60:00}:{sec % 60:00}";

    private int ReadAutoSplitSeconds()
    {
        var v = ParseHms(AutoSplitBox.Text);
        if (v.HasValue) { lastAutoSplitSec = v.Value; return v.Value; }
        return lastAutoSplitSec;
    }

    /// <summary>自動分割の間隔と安全上限が重なるとき、実際にどうなるかを文章で見せる。入力が読めないときは赤文字で知らせる。</summary>
    private void UpdateSplitHint()
    {
        var parsed = ParseHms(AutoSplitBox.Text);
        SetInlineError(SplitError, parsed.HasValue ? null : "自動分割は 時:分:秒 で入力してください(例 00:30:00、24:00:00まで)。いまは直前の値のままです。");
        int sec = ReadAutoSplitSeconds();
        int hours = ParseInt(MaxHoursBox.Text, 24, 1, 168);
        long cap = hours * 3600L;
        string t;
        if (sec <= 0) t = $"→ 自動分割はしません。ただし1つのファイルが {hours} 時間になる前に必ず分割します。";
        else if (sec >= cap) t = $"→ {hours} 時間ごとに分割されます(安全上限のほうが先に働くため、{FormatHms(sec)} の指定は使われません)。";
        else t = $"→ {FormatHms(sec)} ごとに分割されます(安全上限 {hours} 時間は、その前に分割されるため働きません)。";
        SplitHint.Text = t + " 手動の分割ボタンはいつでも使えます。";
    }

    /// <summary>エラーを音の鳴るポップアップではなく、原因の近くに赤文字で出す。null で消す。</summary>
    private static void SetInlineError(TextBlock tb, string? message)
    {
        tb.Text = message ?? "";
        tb.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------------------------------------------------------------- ⚙ 設定と情報

    private void OnOpenAppInfo()
    {
        var w = new AppInfoWindow(ctl.Settings, AppContext.BaseDirectory, () =>
        {
            ThemeHelper.Apply(ctl.Settings.Theme);
            ApplyCustomColors();
            ApplyBackdrop();
            SetTray(ctl.IsRecording ? "録音中" : null);
            MarkDirty();
        }) { Owner = this };
        w.ShowDialog();
        ApplyBackdrop();
        ctl.SaveSettings();
    }

    /// <summary>背景の効果(Mica / アクリル)とタイトルバーの明暗を反映する。</summary>
    private void ApplyBackdrop()
    {
        WindowEffects.Apply(this, ctl.Settings.Backdrop, ThemeHelper.IsDark(ctl.Settings.Theme), ctl.Settings.BackdropStrength);
    }

    // ---------------------------------------------------------------- ファイル名テンプレートの保存

    private bool templateComboBusy;

    private List<(string Name, string Template, bool BuiltIn)> AllTemplates()
    {
        var hidden = ctl.Settings.HiddenTemplates;
        var l = TemplateLibrary.BuiltIn.Where(b => !hidden.Contains(b.Name)).Select(b => (b.Name, b.Template, true)).ToList();
        l.AddRange(ctl.Settings.Templates.Select(t => (t.Name, t.Template, false)));
        return l;
    }

    private void RefreshTemplateCombo()
    {
        templateComboBusy = true;
        try
        {
            TemplateCombo.ItemsSource = AllTemplates().Select(t => t.BuiltIn ? "*" + t.Name : t.Name).ToList();
        }
        finally { templateComboBusy = false; }
        SyncTemplateCombo();
    }

    /// <summary>入力欄の内容と同じテンプレートがあれば、プルダウンをそれに合わせる。</summary>
    private void SyncTemplateCombo()
    {
        if (templateComboBusy || TemplateCombo.ItemsSource == null) return;
        templateComboBusy = true;
        try
        {
            var all = AllTemplates();
            int idx = all.FindIndex(t => t.Template == TemplateBox.Text.Trim());
            TemplateCombo.SelectedIndex = idx;
            BtnTemplateDelete.IsEnabled = idx >= 0;
        }
        finally { templateComboBusy = false; }
    }

    private void OnTemplateComboChanged()
    {
        if (templateComboBusy) return;
        int idx = TemplateCombo.SelectedIndex;
        var all = AllTemplates();
        if (idx < 0 || idx >= all.Count) return;
        TemplateBox.Text = all[idx].Template;
    }

    private void OnTemplateSave()
    {
        string t = TemplateBox.Text.Trim();
        if (t.Length == 0) return;
        string initial = TemplateCombo.SelectedIndex >= 0 && !AllTemplates()[TemplateCombo.SelectedIndex].BuiltIn
            ? AllTemplates()[TemplateCombo.SelectedIndex].Name : "";
        string? input = PromptWindow.Show(this, "テンプレートを保存", "このファイル名テンプレートの名前を入力してください。", initial);
        if (input == null) return;
        string name = input.Trim();
        string? err = TemplateLibrary.ValidateName(name);
        if (err != null) { SetInlineError(PresetError, err); return; }
        var list = ctl.Settings.Templates;
        int i = list.FindIndex(x => x.Name == name);
        if (i >= 0)
        {
            var r = MessageBox.Show(this, $"「{name}」はすでにあります。上書きしますか？", "LokuOn", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            list[i].Template = t;
        }
        else list.Add(new SavedTemplate { Name = name, Template = t });
        RefreshTemplateCombo();
        ctl.SaveSettings();
        AddLog($"テンプレート「{name}」を保存しました");
    }

    /// <summary>最初のテンプレートを全部復活させて、日時形式に戻す。</summary>
    private void OnTemplateReset()
    {
        ctl.Settings.HiddenTemplates.Clear();
        TemplateBox.Text = Preset.DefaultTemplate;
        RefreshTemplateCombo();
        ctl.SaveSettings();
        AddLog("最初のテンプレートを復元しました(日時形式に戻しました)");
    }

    private void OnTemplateDelete()
    {
        int idx = TemplateCombo.SelectedIndex;
        var all = AllTemplates();
        if (idx < 0 || idx >= all.Count) return;
        string name = all[idx].Name;
        bool builtIn = all[idx].BuiltIn;
        var r = MessageBox.Show(this, builtIn
                ? $"テンプレート「{name}」を一覧から削除しますか？\n(「初期に戻す」でいつでも復活できます)"
                : $"テンプレート「{name}」を削除しますか？",
            "LokuOn", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        if (builtIn) { if (!ctl.Settings.HiddenTemplates.Contains(name)) ctl.Settings.HiddenTemplates.Add(name); }
        else ctl.Settings.Templates.RemoveAll(x => x.Name == name);
        RefreshTemplateCombo();
        ctl.SaveSettings();
        AddLog($"テンプレート「{name}」を削除しました");
    }

    private string PresetFolderPath => PresetStore.FolderFor(ctl.SettingsPath);

    private void OnPresetSaveAs()
    {
        SetInlineError(PresetError, null);
        string initial = PresetCombo.SelectedItem as string ?? "";
        string? input = PromptWindow.Show(this, "プリセットを保存", "プリセット名を入力してください(Presets フォルダに「名前.json」で保存されます)。", initial);
        if (input == null) return;
        // ファイル名になるので、使えない文字は全角に置き換えた名前で保存する
        string name = NameTemplate.Sanitize(input.Trim());
        if (input.Trim().Length == 0) { SetInlineError(PresetError, "プリセット名を入力してください。"); return; }
        if (name != initial && ctl.Settings.Presets.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            var r = MessageBox.Show(this, $"「{name}」はすでにあります。上書きしますか？", "LokuOn", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        StorePreset(name);
    }

    private void StorePreset(string name)
    {
        var p = ReadPreset();
        p.Name = name;
        try
        {
            PresetStore.Save(PresetFolderPath, p);
            SetInlineError(PresetError, null);
        }
        catch (Exception ex)
        {
            SetInlineError(PresetError, "プリセットを保存できませんでした: " + ex.Message);
            return;
        }
        var list = ctl.Settings.Presets;
        int idx = list.FindIndex(x => x.Name == name);
        if (idx >= 0) list[idx] = p; else list.Add(p);
        ctl.Settings.LastPresetName = name;
        RefreshPresetCombo(name);
        SyncSettingsFromUi();
        ctl.SaveSettings();
        AddLog($"プリセット「{name}」を保存しました");
    }

    private void OnPresetDelete()
    {
        if (PresetCombo.SelectedItem is not string name) return;
        var r = MessageBox.Show(this, $"プリセット「{name}」を削除しますか？", "LokuOn", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        try { PresetStore.Delete(PresetFolderPath, name); SetInlineError(PresetError, null); }
        catch (Exception ex) { SetInlineError(PresetError, "プリセットを削除できませんでした: " + ex.Message); return; }
        ctl.Settings.Presets.RemoveAll(x => x.Name == name);
        ctl.Settings.LastPresetName = "";
        RefreshPresetCombo(null);
        ctl.SaveSettings();
        AddLog($"プリセット「{name}」を削除しました");
    }

    private void OnPresetFolder()
    {
        try
        {
            Directory.CreateDirectory(PresetFolderPath);
            FileOps.OpenFolder(PresetFolderPath);
            SetInlineError(PresetError, null);
        }
        catch (Exception ex) { SetInlineError(PresetError, "Presets フォルダを開けません: " + ex.Message); }
    }

    /// <summary>Presets フォルダに手でコピー/削除されたファイルを一覧へ反映する。</summary>
    private void ReloadPresetsFromFolder()
    {
        string? sel = PresetCombo.SelectedItem as string;
        var before = ctl.Settings.Presets.Select(x => x.Name).ToList();
        SettingsStore.LoadPresets(ctl.Settings, ctl.SettingsPath);
        if (!before.SequenceEqual(ctl.Settings.Presets.Select(x => x.Name))) RefreshPresetCombo(sel);
        SetInlineError(PresetError, ctl.Settings.PresetProblems.Count > 0
            ? "読み込めないプリセットがあります: " + string.Join(" / ", ctl.Settings.PresetProblems.Take(2)) : null);
    }
}
