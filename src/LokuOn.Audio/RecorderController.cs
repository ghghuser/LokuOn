using System.Diagnostics;
using System.Runtime.InteropServices;
using LokuOn.Core;

namespace LokuOn.Audio;

/// <summary>
/// 画面(UI)と録音エンジンの間をつなぐ司令塔。設定の読み書き、デバイスの開閉、録音の開始/停止、
/// 入力確認(モニター)、予約録音、スリープ防止、前回の異常終了からの復旧を受け持つ。
///
/// 重要: すべてのメソッドと Tick() は UI スレッドから呼ぶこと(ASIOドライバがCOMのため)。
/// ただし Message / FileFinished / EngineStopped イベントはエンジン側のスレッドから発生するので、
/// 受け取る側で UI スレッドへ切り替えること。
/// </summary>
public sealed class RecorderController : IDisposable
{
    private sealed class ActiveSource
    {
        public int Slot;
        public SourceBuffer Buffer = null!;
        public CaptureSource Capture = null!;
    }

    private readonly List<ActiveSource> active = new();
    private RecorderEngine? engine;
    private int filesFinished;
    private volatile string stopReason = "";
    private DateTime lastPoll = DateTime.MinValue;
    private DateTime? scheduledStopAt;
    private bool sleepLocked;

    // 入力確認(モニター)
    private bool monitoring;
    private Preset? monitorPreset;
    private WaveformBuffer? monWave;
    private Stopwatch? monClock;
    private long monFed;
    private int monRate, monCh;
    private float[][] monBuf = Array.Empty<float[]>();
    private float[] monMono = Array.Empty<float>();
    private readonly object monLock = new();
    private float[] monSrcPeak = Array.Empty<float>();
    private float monL, monR;
    private bool monClip;
    private volatile float monMasterDb;

    public string AppDir { get; }
    /// <summary>設定ファイルなどを置くフォルダ(通常はexeと同じ。書き込めない場合は LocalAppData)。</summary>
    public string DataDir { get; private set; }
    public string SettingsPath => Path.Combine(DataDir, "settings.ini");
    public string JournalPath => Path.Combine(DataDir, "recording.journal");
    public string LogPath => Path.Combine(DataDir, "lokuon.log");

    public AppSettings Settings { get; private set; } = new();
    public string? FfmpegPath { get; private set; }
    public IReadOnlyList<DeviceInfo> Devices { get; private set; } = Array.Empty<DeviceInfo>();

    /// <summary>予約録音などで、いまの画面の設定を取得するための関数(UIが設定する)。</summary>
    public Func<Preset>? PresetProvider { get; set; }

    public bool IsRecording => engine != null;
    public EngineState EngineState => engine?.State ?? EngineState.Idle;
    public bool IsMonitoring => monitoring;
    public RecorderEngine? Engine => engine;
    public bool IsScheduledRun => scheduledStopAt != null;

    /// <summary>何かメッセージを出したいとき(エンジンのスレッドから発生することがある)。</summary>
    public event Action<string>? Message;
    /// <summary>ファイルが1つ完成したとき(別スレッドから発生)。</summary>
    public event Action<RecordedFile>? FileFinished;
    /// <summary>録音が完全に終わったとき(UIスレッドの Tick / StopRecordingAsync の中で発生)。引数は理由(通常停止は空文字)。</summary>
    public event Action<string>? RecordingEnded;
    /// <summary>録音が開始したとき(UIスレッド)。</summary>
    public event Action? RecordingStarted;

    public RecorderController(string appDir)
    {
        AppDir = appDir;
        DataDir = appDir;
    }

    // ---------------------------------------------------------------- 起動

    public void Initialize()
    {
        DataDir = ChooseDataDir(AppDir);
        Settings = SettingsStore.Load(SettingsPath);
        FfmpegPath = FfmpegTools.Locate(Settings.FfmpegPath, AppDir);
        RefreshDevices();
    }

    private static string ChooseDataDir(string appDir)
    {
        try
        {
            string probe = Path.Combine(appDir, ".lokuon_write_test_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return appDir;
        }
        catch
        {
            string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LokuOn");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public void RefreshDevices() => Devices = DeviceCatalog.Enumerate();

    /// <summary>設定の ffmpeg の場所を変えたあとに呼ぶ。見つかったパス(なければ null)を返す。</summary>
    public string? RelocateFfmpeg(string? customPath)
    {
        Settings.FfmpegPath = customPath?.Trim() ?? "";
        FfmpegPath = FfmpegTools.Locate(Settings.FfmpegPath, AppDir);
        return FfmpegPath;
    }

    /// <summary>ファイル名に使う短い入力名(例: "PC内部音", "マイク")。</summary>
    public static string ShortLabel(string deviceId, string display)
    {
        if (deviceId.EndsWith(":default", StringComparison.OrdinalIgnoreCase))
            return deviceId.StartsWith("wasapi-loop", StringComparison.OrdinalIgnoreCase) ? "PC内部音" : "マイク";
        string t = (display ?? "").Trim();
        if (t.EndsWith(DeviceCatalog.DefaultMark)) t = t.Substring(0, t.Length - DeviceCatalog.DefaultMark.Length).TrimEnd();
        if (t.StartsWith(DeviceCatalog.SpeakerIcon)) return "PC内部音 " + t.Substring(DeviceCatalog.SpeakerIcon.Length).Trim();
        if (t.StartsWith(DeviceCatalog.MicIcon)) return "マイク " + t.Substring(DeviceCatalog.MicIcon.Length).Trim();
        int i = t.IndexOf("] ", StringComparison.Ordinal);
        if (t.StartsWith("[") && i > 0) return t.Substring(1, i - 1) + " " + t.Substring(i + 2);
        return string.IsNullOrWhiteSpace(t) ? deviceId : t;
    }

    public void SaveSettings()
    {
        try { SettingsStore.Save(SettingsPath, Settings); }
        catch (Exception ex) { Log("設定の保存に失敗: " + ex.Message); Message?.Invoke("設定ファイルを保存できませんでした: " + ex.Message); }
    }

    public void Log(string text)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}", new System.Text.UTF8Encoding(false)); }
        catch { }
    }

    /// <summary>前回、異常終了などで閉じられていないファイルのパス(確認用)。</summary>
    public List<string> PendingRecoveryFiles()
        => FfmpegPath == null ? new List<string>() : Recovery.PendingPaths(JournalPath);

    /// <summary>確認のうえ、閉じられていないファイルを「…_restored.拡張子」に整える(時間がかかることがあるので別スレッドで)。</summary>
    public Task<List<RecordedFile>> RecoverPendingAsync()
    {
        if (FfmpegPath == null || !Recovery.HasPending(JournalPath)) return Task.FromResult(new List<RecordedFile>());
        string ff = FfmpegPath;
        return Task.Run(() => Recovery.Recover(JournalPath, ff, m => Log(m), "_restored"));
    }

    /// <summary>復旧しない場合。記録だけ消してファイルはそのまま残す。</summary>
    public void DiscardRecovery() => Recovery.Discard(JournalPath);

    // ---------------------------------------------------------------- 入力デバイスの開閉

    private bool OpenSources(Preset p, out string error)
    {
        error = "";
        CloseSources();
        for (int i = 0; i < p.Sources.Length; i++)
        {
            var s = p.Sources[i];
            if (string.IsNullOrWhiteSpace(s.DeviceId)) continue;
            var buf = new SourceBuffer(p.SampleRate, p.Channels) { GainDb = (float)s.GainDb };
            string name = ShortLabel(s.DeviceId, s.DeviceName);
            var cap = new CaptureSource(s.DeviceId, name, buf);
            active.Add(new ActiveSource { Slot = i, Buffer = buf, Capture = cap });
        }
        if (active.Count == 0) { error = "入力デバイスが選ばれていません"; return false; }
        foreach (var a in active) a.Capture.Start();
        return true;
    }

    private void CloseSources()
    {
        foreach (var a in active) { try { a.Capture.Dispose(); } catch { } }
        active.Clear();
    }

    /// <summary>つながらなかった入力があればその説明を返す(なければ空)。</summary>
    public string DescribeSourceProblems()
    {
        var parts = new List<string>();
        foreach (var a in active)
            if (!a.Buffer.Connected)
                parts.Add($"入力{a.Slot + 1}「{a.Capture.DisplayName}」: {(string.IsNullOrEmpty(a.Capture.LastError) ? "接続できません" : a.Capture.LastError)}");
        return string.Join(" / ", parts);
    }

    // ---------------------------------------------------------------- 入力確認(モニター)

    public void StartMonitor(Preset p)
    {
        StopMonitor();
        if (engine != null) return;
        if (!OpenSources(p, out _)) return;
        monitorPreset = p;
        monRate = p.SampleRate; monCh = p.Channels;
        monWave = new WaveformBuffer(monRate);
        monBuf = active.Select(_ => new float[(monRate / 10) * monCh]).ToArray();
        monMono = new float[monRate / 10];
        lock (monLock) { monSrcPeak = new float[active.Count]; monL = monR = 0; monClip = false; }
        monMasterDb = (float)p.MasterGainDb;
        monClock = Stopwatch.StartNew();
        monFed = 0;
        monitoring = true;
    }

    public void StopMonitor()
    {
        if (!monitoring) return;
        monitoring = false;
        CloseSources();
        monWave = null;
        monClock = null;
    }

    private void PumpMonitor()
    {
        if (!monitoring || monClock == null || monWave == null) return;
        long due = (long)(monClock.Elapsed.TotalSeconds * monRate) - monFed;
        int maxChunk = monRate / 10;
        while (due >= monRate / 100)
        {
            int n = (int)Math.Min(due, maxChunk);
            int samples = n * monCh;
            Array.Clear(monMono, 0, n);
            float gm = DbToLin(monMasterDb);
            float pl = 0, pr = 0; bool clip = false;
            var peaks = new float[active.Count];
            var left = new float[n]; var right = new float[n];
            for (int i = 0; i < active.Count; i++)
            {
                var a = active[i];
                int r = a.Buffer.Read(monBuf[i], 0, n);
                if (r < n) Array.Clear(monBuf[i], r * monCh, (n - r) * monCh);
                if (a.Buffer.Connected && a.Buffer.AvailableFrames > monRate) a.Buffer.DiscardTo(monRate / 10);
                float g = DbToLin(a.Buffer.GainDb);
                var b = monBuf[i];
                float pk = 0;
                for (int f = 0; f < n; f++)
                {
                    float l = b[f * monCh] * g;
                    float rr = monCh > 1 ? b[f * monCh + 1] * g : l;
                    left[f] += l; right[f] += rr;
                    float al = l < 0 ? -l : l, ar = rr < 0 ? -rr : rr;
                    if (al > pk) pk = al; if (ar > pk) pk = ar;
                }
                peaks[i] = pk;
            }
            for (int f = 0; f < n; f++)
            {
                float l = left[f] * gm, r2 = right[f] * gm;
                if (l > 1f || l < -1f) { clip = true; l = l > 0 ? 1f : -1f; }
                if (r2 > 1f || r2 < -1f) { clip = true; r2 = r2 > 0 ? 1f : -1f; }
                monMono[f] = (l + r2) * 0.5f;
                float al = l < 0 ? -l : l, ar = r2 < 0 ? -r2 : r2;
                if (al > pl) pl = al; if (ar > pr) pr = ar;
            }
            monWave.Add(monMono, n);
            lock (monLock)
            {
                for (int i = 0; i < peaks.Length && i < monSrcPeak.Length; i++) if (peaks[i] > monSrcPeak[i]) monSrcPeak[i] = peaks[i];
                if (pl > monL) monL = pl; if (pr > monR) monR = pr; if (clip) monClip = true;
            }
            monFed += n;
            due -= n;
        }
    }

    private static float DbToLin(double db) => (float)Math.Pow(10.0, db / 20.0);

    // ---------------------------------------------------------------- メーター・波形

    /// <summary>波形データ(録音中はエンジン、そうでなければモニター)。無ければ null。</summary>
    public WaveformBuffer? Waveform => engine?.Waveform ?? monWave;

    /// <summary>前回の読み出し以降のピーク。Sources は「画面の入力1〜3」の順(使っていない枠は 0)。</summary>
    public EngineLevels ReadLevels()
    {
        EngineLevels raw;
        if (engine != null) raw = engine.ReadLevels();
        else if (monitoring)
        {
            lock (monLock)
            {
                raw = new EngineLevels { Sources = (float[])monSrcPeak.Clone(), MasterL = monL, MasterR = monR, Clipped = monClip };
                Array.Clear(monSrcPeak); monL = 0; monR = 0; monClip = false;
            }
        }
        else return new EngineLevels { Sources = new float[Preset.SourceSlots] };

        var bySlot = new float[Preset.SourceSlots];
        for (int i = 0; i < active.Count && i < raw.Sources.Length; i++) bySlot[active[i].Slot] = raw.Sources[i];
        raw.Sources = bySlot;
        return raw;
    }

    /// <summary>画面の入力枠ごとの接続状態(使っていない枠は null)。</summary>
    public bool?[] SlotConnected()
    {
        var r = new bool?[Preset.SourceSlots];
        foreach (var a in active) r[a.Slot] = a.Buffer.Connected;
        return r;
    }

    public void SetSourceGain(int slot, double db)
    {
        foreach (var a in active) if (a.Slot == slot) a.Buffer.GainDb = (float)db;
    }

    public void SetMasterGain(double db)
    {
        monMasterDb = (float)db;
        var e = engine; if (e != null) e.MasterGainDb = (float)db;
    }

    // ---------------------------------------------------------------- 録音

    /// <summary>録音を始める。失敗したら理由を返す(成功なら null)。</summary>
    public string? StartRecording(Preset preset)
    {
        if (engine != null) return "すでに録音中です";
        if (FfmpegPath == null) return "ffmpeg が見つかりません。ffmpeg.exe を ffmpeg フォルダ(またはexeと同じ場所)に置いてください。";
        if (string.IsNullOrWhiteSpace(preset.SaveFolder)) return "保存先フォルダを指定してください";

        var p = preset.Clone();
        p.Name = preset.Name;
        StopMonitor();
        if (!OpenSources(p, out string err)) return err;

        if (active.All(a => !a.Buffer.Connected) && !p.AutoResume)
        {
            string detail = DescribeSourceProblems();
            CloseSources();
            return "入力デバイスに接続できません。" + (detail.Length > 0 ? " " + detail : "");
        }

        RecorderEngine eng;
        try
        {
            eng = new RecorderEngine(new EngineOptions
            {
                Preset = p, No = Settings.No, TrackStart = Settings.TrackStart, FfmpegPath = FfmpegPath,
                JournalPath = JournalPath, DiskWarnGB = Settings.DiskWarnGB, DiskStopGB = Settings.DiskStopGB,
            }, active.Select(a => a.Buffer).ToList());
            eng.Message += m => { Log(m); Message?.Invoke(m); };
            eng.FileFinished += f => { Interlocked.Increment(ref filesFinished); FileFinished?.Invoke(f); };
            eng.Stopped += r => stopReason = r;
            filesFinished = 0; stopReason = "";
            eng.Start();
        }
        catch (Exception ex)
        {
            CloseSources();
            return ex.Message;
        }
        engine = eng;
        SetSleepLock(Settings.PreventSleep);
        string probs = DescribeSourceProblems();
        if (probs.Length > 0) Message?.Invoke("つながっていない入力があります(復帰を待ちます): " + probs);
        SaveSettings();
        RecordingStarted?.Invoke();
        return null;
    }

    /// <summary>録音を止めてファイルを保存する。保存が終わると完了する。</summary>
    public async Task StopRecordingAsync()
    {
        var e = engine;
        if (e == null) return;
        scheduledStopAt = null;
        try { await e.StopAsync().ConfigureAwait(true); }
        catch (Exception ex) { Log("停止処理で例外: " + ex.Message); }
        FinishRecording();
    }

    public void Split() => engine?.Split();

    /// <summary>録音の一時停止/再開。停止中の時間と音はファイルに入らない。</summary>
    public void TogglePauseRecording()
    {
        var e = engine; if (e == null) return;
        if (e.IsUserPaused) e.Resume(); else e.Pause();
    }

    public bool IsUserPaused => engine?.IsUserPaused ?? false;

    private void FinishRecording()
    {
        var e = engine;
        if (e == null) return;
        engine = null;
        bool produced = e.ElapsedSeconds > 0 || Volatile.Read(ref filesFinished) > 0;
        CloseSources();
        SetSleepLock(false);
        scheduledStopAt = null;
        if (produced) Settings.No++;
        SaveSettings();
        string reason = stopReason;
        try { e.Dispose(); } catch { }
        RecordingEnded?.Invoke(reason);
    }

    // ---------------------------------------------------------------- 定期処理

    /// <summary>UIタイマー(30〜50msごと)から呼ぶ。</summary>
    public void Tick()
    {
        PumpMonitor();

        var e = engine;
        if (e != null)
        {
            if (e.State == EngineState.Idle) { FinishRecording(); return; }     // エンジンが自分で止まった(エラー・ディスク不足など)
            if (scheduledStopAt != null && DateTime.Now >= scheduledStopAt.Value)
            {
                scheduledStopAt = null;
                Message?.Invoke("予約録音の終了時刻になりました");
                _ = StopRecordingAsync();
                return;
            }
        }

        if ((DateTime.Now - lastPoll).TotalSeconds >= 1)
        {
            lastPoll = DateTime.Now;
            foreach (var a in active) { try { a.Capture.Poll(); } catch { } }
            CheckSchedule();
        }
    }

    private void CheckSchedule()
    {
        var sc = Settings.Schedule;
        if (!sc.Enabled || engine != null || PresetProvider == null) return;
        var now = DateTime.Now;
        if (now < sc.StartAt) return;
        var end = sc.StartAt.AddMinutes(Math.Max(1, sc.DurationMinutes));
        if (now >= end)
        {
            // 時間を過ぎてしまった予約
            if (sc.Daily) { AdvanceDaily(sc, now); Message?.Invoke("予約録音の時間を過ぎていたので、次の日に回しました"); }
            else { sc.Enabled = false; Message?.Invoke("予約録音の時間を過ぎていたため、実行せず無効にしました"); }
            SaveSettings();
            return;
        }
        string? err = StartRecording(PresetProvider());
        if (err != null)
        {
            Message?.Invoke("予約録音を開始できませんでした: " + err);
            if (sc.Daily) AdvanceDaily(sc, now); else sc.Enabled = false;
            SaveSettings();
            return;
        }
        scheduledStopAt = end;
        Message?.Invoke($"予約録音を開始しました(終了予定 {end:HH:mm})");
        if (sc.Daily) AdvanceDaily(sc, now); else sc.Enabled = false;
        SaveSettings();
    }

    private static void AdvanceDaily(ScheduleSettings sc, DateTime now)
    {
        var next = sc.StartAt;
        int guard = 0;
        while (next <= now && guard++ < 4000) next = next.AddDays(1);
        sc.StartAt = next;
    }

    // ---------------------------------------------------------------- スリープ防止

    [Flags]
    private enum ExecutionState : uint { Continuous = 0x80000000, SystemRequired = 0x00000001, AwayMode = 0x00000040 }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    private void SetSleepLock(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (on) { SetThreadExecutionState((uint)(ExecutionState.Continuous | ExecutionState.SystemRequired)); sleepLocked = true; }
            else if (sleepLocked) { SetThreadExecutionState((uint)ExecutionState.Continuous); sleepLocked = false; }
        }
        catch { }
    }

    public void Dispose()
    {
        try { StopMonitor(); } catch { }
        var e = engine;
        if (e != null) { try { e.StopAsync().Wait(120_000); } catch { } engine = null; }
        CloseSources();
        SetSleepLock(false);
    }
}
