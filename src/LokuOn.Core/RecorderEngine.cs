using System.Diagnostics;

namespace LokuOn.Core;

public enum EngineState { Idle = 0, Armed = 1, Recording = 2, Stopping = 3 }

public sealed class EngineOptions
{
    public Preset Preset { get; set; } = new();
    public int No { get; set; } = 1;
    public int TrackStart { get; set; } = 1;
    public string FfmpegPath { get; set; } = "ffmpeg";
    /// <summary>異常終了対策の記録ファイル。null なら記録しない。</summary>
    public string? JournalPath { get; set; }
    public double DiskWarnGB { get; set; } = 5;
    public double DiskStopGB { get; set; } = 0.5;
}

public sealed class EngineLevels
{
    public float[] Sources { get; set; } = Array.Empty<float>();
    public float MasterL { get; set; }
    public float MasterR { get; set; }
    public bool Clipped { get; set; }
}

/// <summary>
/// 録音エンジン。入力バッファ(SourceBuffer)を実時間のクロックで読み出し、
/// ミックス → ゲイン → ffmpeg(直接エンコード) へ流す。分割・自動再開・異常終了対策もここ。
/// 実時間のクロックで進めるので、デバイスが無音/切断でも時間がずれず、無音で埋められる。
/// </summary>
public sealed class RecorderEngine : IDisposable
{
    private const double LatencySec = 0.30;

    private sealed class Output
    {
        public string Label = "";
        public int[] Src = Array.Empty<int>();
        public float[] Mix = Array.Empty<float>();
        public EncoderSession? Enc;
        public string ProvPath = "";
        public int Track;
        public int FilesOpened;
        public long SessionFrames;      // この出力に書いた総フレーム数(録音開始からの位置)
        public long FileStartFrames;
        public long FileFrames;
        public DateTime FileStartTime;
        public bool HasSplit;
        public bool Paused;
        public bool UPaused;            // 利用者の一時停止
        public bool SplitPending;
        public long SilentFrames;
        public bool SoundSeen;
        public long LostFrames;
        public readonly Queue<float[]> PreRoll = new();
        public int PreRollFrames;
    }

    private readonly EngineOptions opt;
    private readonly Preset p;
    private readonly IReadOnlyList<SourceBuffer> sources;
    private readonly int rate, ch;
    private readonly List<Output> outputs = new();
    private readonly RecordingJournal? journal;
    private readonly List<Task> finalizers = new();
    private readonly TaskCompletionSource<bool> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    private volatile bool stopRequested;
    private volatile bool userPause;
    private DateTime sessionStart;
    private bool sessionStartSet;
    private volatile string stopReason = "";
    private int splitRequested;
    private volatile int state;
    private readonly object levelGate = new();
    private float[] srcPeakAcc;
    private float mL, mR;
    private bool clipAcc;
    private readonly bool[] lastConnected;
    private long freeDiskBytes = -1;

    public WaveformBuffer Waveform { get; }
    public volatile float MasterGainDb;

    public event Action<RecordedFile>? FileFinished;
    public event Action<string>? Message;
    /// <summary>エンジンが止まった(理由。ユーザー操作での停止は空文字)。</summary>
    public event Action<string>? Stopped;

    public EngineState State => (EngineState)state;
    public int No => opt.No;
    public double ElapsedSeconds => outputs.Count > 0 ? Volatile.Read(ref outputs[0].SessionFrames) / (double)rate : 0;
    public double CurrentFileSeconds => outputs.Count > 0 ? Volatile.Read(ref outputs[0].FileFrames) / (double)rate : 0;
    public int CurrentTrack => outputs.Count > 0 ? outputs[0].Track : opt.TrackStart;
    public string CurrentFilePath => outputs.Count > 0 ? outputs[0].ProvPath : "";
    public long FreeDiskBytes => Interlocked.Read(ref freeDiskBytes);
    public bool IsPaused => outputs.Count > 0 && outputs.All(o => o.Paused);
    /// <summary>利用者が一時停止中か。</summary>
    public bool IsUserPaused => userPause;

    public RecorderEngine(EngineOptions options, IReadOnlyList<SourceBuffer> sourceBuffers)
    {
        opt = options;
        p = options.Preset;
        sources = sourceBuffers;
        if (sources.Count == 0) throw new InvalidOperationException("入力デバイスが選ばれていません");
        rate = sources[0].SampleRate;
        ch = sources[0].Channels;
        srcPeakAcc = new float[sources.Count];
        lastConnected = new bool[sources.Count];
        Waveform = new WaveformBuffer(rate);
        MasterGainDb = (float)p.MasterGainDb;
        if (!string.IsNullOrEmpty(opt.JournalPath)) journal = new RecordingJournal(opt.JournalPath!);
    }

    // ------------------------------------------------------------ 公開操作

    public void Start()
    {
        if (state != (int)EngineState.Idle) throw new InvalidOperationException("すでに録音中です");
        if (string.IsNullOrWhiteSpace(p.SaveFolder)) throw new InvalidOperationException("保存先フォルダが指定されていません");
        if (!File.Exists(opt.FfmpegPath)) throw new InvalidOperationException("ffmpeg が見つかりません: " + opt.FfmpegPath);
        try { Directory.CreateDirectory(p.SaveFolder); }
        catch (Exception ex) { throw new InvalidOperationException("保存先フォルダを作成できません: " + ex.Message, ex); }

        outputs.Clear();
        if (p.MixMode == MixMode.Separate && sources.Count > 1)
        {
            for (int i = 0; i < sources.Count; i++)
                outputs.Add(new Output { Label = sources[i].Label, Src = new[] { i } });
        }
        else
        {
            string label = sources.Count == 1 ? sources[0].Label : string.Join("+", sources.Select(s => s.Label));
            outputs.Add(new Output { Label = label, Src = Enumerable.Range(0, sources.Count).ToArray() });
        }
        foreach (var o in outputs) { o.Track = opt.TrackStart; o.Mix = new float[(rate / 100) * ch]; }
        for (int i = 0; i < sources.Count; i++) { sources[i].Clear(); lastConnected[i] = sources[i].Connected; }
        Waveform.Clear();
        stopRequested = false; stopReason = ""; splitRequested = 0; userPause = false; sessionStartSet = false;

        if (p.AutoStartOnSound) state = (int)EngineState.Armed;
        else
        {
            try { foreach (var o in outputs) OpenFile(o); }
            catch { foreach (var o in outputs) { o.Enc?.Dispose(); o.Enc = null; } throw; }
            state = (int)EngineState.Recording;
        }
        thread = new Thread(Run) { IsBackground = false, Name = "LokuOn.Engine", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    /// <summary>録音を止め、すべてのファイルを保存し終えたら完了するタスクを返す。</summary>
    public Task StopAsync()
    {
        if (state == (int)EngineState.Idle) return Task.CompletedTask;
        stopRequested = true;
        return done.Task;
    }

    /// <summary>今の位置でファイルを分割して、新しいファイルへ続ける。</summary>
    public void Split() { if (state == (int)EngineState.Recording && !userPause) Interlocked.Exchange(ref splitRequested, 1); }

    /// <summary>一時停止。止めている間の時間と音はファイルに入らない(直前は短いフェードアウト)。</summary>
    public void Pause() { if (state == (int)EngineState.Recording) userPause = true; }

    /// <summary>一時停止から再開。同じファイルの続きに書く(最初は短いフェードイン)。</summary>
    public void Resume() { userPause = false; }

    private void Ramp(float[] buf, int frames, bool fadeOut)
    {
        for (int f = 0; f < frames; f++)
        {
            float g = fadeOut ? 1f - (f + 1) / (float)frames : f / (float)frames;
            for (int c = 0; c < ch; c++) buf[f * ch + c] *= g;
        }
    }

    public EngineLevels ReadLevels()
    {
        lock (levelGate)
        {
            var l = new EngineLevels { Sources = (float[])srcPeakAcc.Clone(), MasterL = mL, MasterR = mR, Clipped = clipAcc };
            Array.Clear(srcPeakAcc); mL = 0; mR = 0; clipAcc = false;
            return l;
        }
    }

    public void Dispose()
    {
        try { StopAsync().Wait(60_000); } catch { }
    }

    // ------------------------------------------------------------ メインループ

    private void Run()
    {
        int chunk = rate / 100;     // 10ms
        var srcBuf = new float[sources.Count][];
        for (int i = 0; i < srcBuf.Length; i++) srcBuf[i] = new float[chunk * ch];
        var all = new float[chunk * ch];
        var mono = new float[chunk];
        long clock = 0;
        var sw = Stopwatch.StartNew();
        var diskWatch = Stopwatch.StartNew();
        long allSilent = 0;
        double lastWarn = -999;
        CheckDisk(ref lastWarn, sw.Elapsed.TotalSeconds);

        try
        {
            while (!stopRequested)
            {
                long target = (long)((sw.Elapsed.TotalSeconds - LatencySec) * rate);
                if (target - clock < chunk) { Thread.Sleep(4); continue; }
                if (target - clock > 5L * rate)
                {
                    Message?.Invoke("処理の遅れを検出したため時刻を補正しました(PCのスリープ等)");
                    clock = target - chunk;
                }
                int guard = 0;
                while (target - clock >= chunk && guard++ < 300 && !stopRequested)
                {
                    ProcessChunk(chunk, srcBuf, all, mono, ref allSilent);
                    clock += chunk;
                }
                if (diskWatch.Elapsed.TotalSeconds >= 2)
                {
                    diskWatch.Restart();
                    CheckDisk(ref lastWarn, sw.Elapsed.TotalSeconds);
                }
            }
        }
        catch (Exception ex)
        {
            stopReason = "エンジンエラー: " + ex.Message;
        }
        finally
        {
            state = (int)EngineState.Stopping;
            try { foreach (var o in outputs) CloseOutput(o); } catch (Exception ex) { Message?.Invoke("終了処理エラー: " + ex.Message); }
            Task[] pending; lock (finalizers) pending = finalizers.ToArray();
            try { Task.WaitAll(pending, 300_000); } catch { }
            state = (int)EngineState.Idle;
            done.TrySetResult(true);
            Stopped?.Invoke(stopReason);
        }
    }

    private static float DbToLin(double db) => (float)Math.Pow(10.0, db / 20.0);

    private void ProcessChunk(int n, float[][] srcBuf, float[] all, float[] mono, ref long allSilent)
    {
        int samples = n * ch;
        Array.Clear(all, 0, samples);
        for (int i = 0; i < sources.Count; i++)
        {
            var sb = sources[i];
            int r = sb.Read(srcBuf[i], 0, n);
            if (r < n) Array.Clear(srcBuf[i], r * ch, (n - r) * ch);
            bool conn = sb.Connected;
            if (conn && sb.AvailableFrames > (int)((LatencySec + 0.5) * rate)) sb.DiscardTo((int)(LatencySec * rate));
            if (conn != lastConnected[i])
            {
                lastConnected[i] = conn;
                Message?.Invoke(conn ? $"入力が復帰しました: {sb.Label}" : $"入力が切断されました: {sb.Label}");
            }
            float g = DbToLin(sb.GainDb);
            float pk = 0;
            var b = srcBuf[i];
            for (int j = 0; j < samples; j++)
            {
                float x = b[j] * g;
                b[j] = x;
                all[j] += x;
                float a = x < 0 ? -x : x;
                if (a > pk) pk = a;
            }
            lock (levelGate) { if (pk > srcPeakAcc[i]) srcPeakAcc[i] = pk; }
        }

        float gm = DbToLin(MasterGainDb);
        float pl = 0, pr = 0; bool clip = false;
        for (int f = 0; f < n; f++)
        {
            float l = all[f * ch] * gm;
            float r = ch > 1 ? all[f * ch + 1] * gm : l;
            if (l > 1f || l < -1f) { clip = true; l = l > 0 ? 1f : -1f; }
            if (r > 1f || r < -1f) { clip = true; r = r > 0 ? 1f : -1f; }
            all[f * ch] = l; if (ch > 1) all[f * ch + 1] = r;
            mono[f] = ch > 1 ? (l + r) * 0.5f : l;
            float al = l < 0 ? -l : l, ar = r < 0 ? -r : r;
            if (al > pl) pl = al; if (ar > pr) pr = ar;
        }
        Waveform.Add(mono, n);
        lock (levelGate) { if (pl > mL) mL = pl; if (pr > mR) mR = pr; if (clip) clipAcc = true; }

        double silThr = Math.Pow(10, p.SilenceDb / 20.0);
        float allPeak = Math.Max(pl, pr);

        // 手動分割
        if (Interlocked.Exchange(ref splitRequested, 0) == 1 && state == (int)EngineState.Recording)
            foreach (var o in outputs) if (o.Enc != null && o.FileFrames > 0) SplitOutput(o);

        bool recording = state == (int)EngineState.Recording;
        bool anyTrigger = false;

        foreach (var o in outputs)
        {
            // この出力のミックスを作る
            float opk = 0;
            if (o.Src.Length == sources.Count)
            {
                Array.Copy(all, o.Mix, samples);
                opk = allPeak;
            }
            else
            {
                Array.Clear(o.Mix, 0, samples);
                foreach (int si in o.Src) { var b = srcBuf[si]; for (int j = 0; j < samples; j++) o.Mix[j] += b[j]; }
                for (int j = 0; j < samples; j++)
                {
                    float x = o.Mix[j] * gm;
                    if (x > 1f) x = 1f; else if (x < -1f) x = -1f;
                    o.Mix[j] = x;
                    float a = x < 0 ? -x : x; if (a > opk) opk = a;
                }
            }

            if (state == (int)EngineState.Armed)
            {
                // 音が来るまで待機。直前0.5秒ぶんだけ手元に保持しておく
                o.PreRoll.Enqueue((float[])o.Mix.Clone());
                o.PreRollFrames += n;
                while (o.PreRollFrames > rate / 2 && o.PreRoll.Count > 1) { o.PreRoll.Dequeue(); o.PreRollFrames -= n; }
                if (opk >= Math.Pow(10, p.AutoStartDb / 20.0)) anyTrigger = true;
                continue;
            }
            if (!recording) continue;

            // 利用者の一時停止(止めている間は書かない。つなぎ目はフェードでプツッを防ぐ)
            if (userPause)
            {
                if (!o.UPaused) { Ramp(o.Mix, n, true); WriteToOutput(o, o.Mix, n); o.UPaused = true; }
                continue;
            }
            if (o.UPaused) { Ramp(o.Mix, n, false); o.UPaused = false; o.SilentFrames = 0; }

            // 切断時の挙動
            bool lost = o.Src.All(si => !sources[si].Connected);
            if (lost)
            {
                o.LostFrames += n;
                if (!p.AutoResume)
                {
                    if (o.LostFrames > 2L * rate && !stopRequested) { stopReason = "入力デバイスが切断されたため録音を停止しました"; stopRequested = true; }
                }
                else if (p.DeviceLostPolicy != DeviceLostPolicy.Continue)
                {
                    o.Paused = true;
                    if (p.DeviceLostPolicy == DeviceLostPolicy.SplitOnReturn) o.SplitPending = true;
                    continue;
                }
            }
            else
            {
                o.LostFrames = 0;
                if (o.Paused)
                {
                    o.Paused = false;
                    if (o.SplitPending) { o.SplitPending = false; if (o.FileFrames > 0) SplitOutput(o); }
                }
            }

            WriteToOutput(o, o.Mix, n);

            // 無音検出
            if (opk < silThr) o.SilentFrames += n; else { o.SilentFrames = 0; o.SoundSeen = true; }
            if (p.SplitOnSilence && o.SoundSeen && o.SilentFrames >= (long)(p.SilenceSeconds * rate)
                && o.FileFrames >= (long)(p.MinTrackSeconds * rate))
            {
                o.SoundSeen = false;
                SplitOutput(o);
            }
            // 時間による分割
            long maxFrames = p.MaxFileHours * 3600L * rate;
            if ((p.AutoSplitSeconds > 0 && o.FileFrames >= p.AutoSplitSeconds * (long)rate) || o.FileFrames >= maxFrames)
                SplitOutput(o);
        }

        if (state == (int)EngineState.Armed && anyTrigger)
        {
            try
            {
                foreach (var o in outputs)
                {
                    OpenFile(o);
                    while (o.PreRoll.Count > 0) { var pr2 = o.PreRoll.Dequeue(); WriteToOutput(o, pr2, pr2.Length / ch); }
                    o.PreRollFrames = 0;
                }
                state = (int)EngineState.Recording;
                Message?.Invoke("音を検出したため録音を開始しました");
            }
            catch (Exception ex) { stopReason = "録音を開始できません: " + ex.Message; stopRequested = true; }
        }

        if (recording && p.AutoStopSilenceSeconds > 0)
        {
            if (allPeak < silThr) allSilent += n; else allSilent = 0;
            if (allSilent >= (long)(p.AutoStopSilenceSeconds * rate) && !stopRequested)
            { stopReason = "無音が続いたため録音を停止しました"; stopRequested = true; }
        }
    }

    private void WriteToOutput(Output o, float[] data, int frames)
    {
        var enc = o.Enc;
        if (enc == null) return;
        enc.Write(data, frames * ch);
        Volatile.Write(ref o.FileFrames, o.FileFrames + frames);
        Volatile.Write(ref o.SessionFrames, o.SessionFrames + frames);
    }

    // ------------------------------------------------------------ ファイルの開閉

    private string DestinationFolder()
    {
        string dir = p.SaveFolder;
        if (p.UseDateFolder) dir = Path.Combine(dir, NameTemplate.RenderFolder(p.DateFolderFormat, sessionStart));
        return dir;
    }

    private bool NeedDeviceSuffix => outputs.Count > 1 && !p.FileNameTemplate.Contains("{Device", StringComparison.OrdinalIgnoreCase);

    private NameContext MakeContext(Output o, bool provisional, double endSec) => new()
    {
        No = opt.No, Track = o.Track, StartSec = o.FileStartFrames / (double)rate, EndSec = endSec,
        Provisional = provisional, FileStartTime = o.FileStartTime, Device = o.Label,
        Preset = p.Name, TimeStyle = p.TimeStyle, Ext = EncoderSpec.Extension(p.Format),
    };

    private string BuildBaseName(Output o, NameContext ctx)
    {
        string name = NameTemplate.RenderFileBase(p.FileNameTemplate, ctx);
        if (NeedDeviceSuffix) name += "_" + NameTemplate.Sanitize(o.Label, 60);
        return name;
    }

    private void OpenFile(Output o)
    {
        o.FileStartTime = DateTime.Now;
        if (!sessionStartSet) { sessionStart = o.FileStartTime; sessionStartSet = true; }
        o.FileStartFrames = o.SessionFrames;
        o.FileFrames = 0;
        var ctx = MakeContext(o, true, 0);
        string dir = DestinationFolder();
        Directory.CreateDirectory(dir);
        string ext = EncoderSpec.Extension(p.Format);
        string path = NameTemplate.UniquePath(dir, BuildBaseName(o, ctx), ext);

        var tags = new TagSettings
        {
            Title = NameTemplate.Render(p.Tags.Title, ctx), Artist = NameTemplate.Render(p.Tags.Artist, ctx),
            Album = NameTemplate.Render(p.Tags.Album, ctx), AlbumArtist = NameTemplate.Render(p.Tags.AlbumArtist, ctx),
            Genre = p.Tags.Genre, Year = p.Tags.Year, Comment = NameTemplate.Render(p.Tags.Comment, ctx),
        };
        var spec = new EncoderSpec
        {
            Format = p.Format, SampleRate = rate, Channels = ch, BitrateKbps = p.BitrateKbps, BitDepth = p.BitDepth, CompressionLevel = p.CompressionLevel,
            Tags = tags, FfmpegPath = opt.FfmpegPath,
            TrackTag = p.WriteTrackTag && o.FilesOpened > 0 ? o.Track : null,
        };
        o.Enc = new EncoderSession(spec, path);
        o.ProvPath = path;
        o.FilesOpened++;
        journal?.Add(new JournalEntry
        {
            Path = path, Template = p.FileNameTemplate, TimeStyle = p.TimeStyle, No = opt.No, Track = o.Track,
            StartSec = ctx.StartSec, StartTime = o.FileStartTime, Device = o.Label, DeviceSuffix = NeedDeviceSuffix, Preset = p.Name,
        });
    }

    private void SplitOutput(Output o)
    {
        if (o.Enc == null) return;
        o.HasSplit = true;
        var closing = Snapshot(o);
        o.Enc = null;
        o.Track++;
        try { OpenFile(o); }   // 先に次のファイルを開く(入力はキューに入るので途切れない)
        catch (Exception ex)
        {
            Message?.Invoke("分割に失敗: " + ex.Message);
            stopReason = "分割に失敗したため録音を停止しました"; stopRequested = true;
        }
        StartFinalize(closing);
    }

    private void CloseOutput(Output o)
    {
        if (o.Enc == null) return;
        var closing = Snapshot(o);
        o.Enc = null;
        StartFinalize(closing);
    }

    private sealed record Closing(EncoderSession Enc, string ProvPath, long StartFrames, long Frames, int Track, int FileIndex,
        DateTime StartTime, string Label, bool HadSplit);

    private Closing Snapshot(Output o) => new(o.Enc!, o.ProvPath, o.FileStartFrames, o.FileFrames, o.Track, o.FilesOpened - 1,
        o.FileStartTime, o.Label, o.HasSplit);

    private void StartFinalize(Closing c)
    {
        var t = Task.Run(() => FinalizeFile(c));
        lock (finalizers) { finalizers.RemoveAll(x => x.IsCompleted); finalizers.Add(t); }
    }

    private void FinalizeFile(Closing c)
    {
        try
        {
            var res = c.Enc.Finish();
            c.Enc.Dispose();
            if (c.Frames == 0)
            {
                try { if (File.Exists(c.ProvPath)) File.Delete(c.ProvPath); } catch { }
                journal?.Remove(c.ProvPath);
                return;
            }
            if (!res.Ok)
            {
                Message?.Invoke($"エンコードでエラーが発生しました(ファイル名は次回起動時に整えます): {res.Error}");
                return;     // 記録(journal)を残す
            }
            double startSec = c.StartFrames / (double)rate;
            double endSec = (c.StartFrames + c.Frames) / (double)rate;
            var o = new Output { Label = c.Label, Track = c.Track, FileStartFrames = c.StartFrames, FileStartTime = c.StartTime };
            var ctx = MakeContext(o, false, endSec);
            string dir = Path.GetDirectoryName(c.ProvPath) ?? ".";
            string ext = Path.GetExtension(c.ProvPath);
            string final = NameTemplate.UniquePath(dir, BuildBaseName(o, ctx), ext, c.ProvPath);
            if (!string.Equals(final, c.ProvPath, StringComparison.Ordinal)) File.Move(c.ProvPath, final);

            bool tagTrack = p.WriteTrackTag && c.FileIndex == 0 && c.HadSplit;
            bool faststart = p.Format == AudioFormat.M4a;
            if (tagTrack || faststart)
                FfmpegTools.Remux(opt.FfmpegPath, final, faststart, tagTrack ? c.Track : null);

            journal?.Remove(c.ProvPath);
            FileFinished?.Invoke(new RecordedFile
            {
                Path = final, Seconds = endSec - startSec, No = opt.No, Track = c.Track, Device = c.Label,
            });
        }
        catch (Exception ex)
        {
            Message?.Invoke("ファイルの保存処理でエラー: " + ex.Message);
        }
    }

    // ------------------------------------------------------------ ディスク残量

    private void CheckDisk(ref double lastWarn, double nowSec)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(p.SaveFolder)) ?? "";
            if (root.Length == 0 || root.StartsWith("\\\\")) return;
            long free = new DriveInfo(root).AvailableFreeSpace;
            Interlocked.Exchange(ref freeDiskBytes, free);
            double gb = free / 1073741824.0;
            if (gb < opt.DiskStopGB && !stopRequested)
            {
                stopReason = $"ディスクの空きが少なくなったため録音を停止しました(残り {gb:0.00} GB)";
                stopRequested = true;
            }
            else if (gb < opt.DiskWarnGB && nowSec - lastWarn > 60)
            {
                lastWarn = nowSec;
                Message?.Invoke($"ディスクの空き容量が少なくなっています(残り {gb:0.0} GB)");
            }
        }
        catch { /* 取得できない場合は監視しない */ }
    }
}
