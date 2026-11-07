using System.Diagnostics;
using LokuOn.Core;

delegate void CheckFn(bool ok, string name, string detail = "");

static class EngineTests
{
    /// <summary>実時間で音を流し込む疑似デバイス(指定のサンプルレートで届く)。</summary>
    sealed class FakeDevice : IDisposable
    {
        private readonly Thread t; private volatile bool stop;
        public volatile bool Silent;
        public FakeDevice(SourceBuffer target, int devRate, double freq, double amp = 0.4)
        {
            var feeder = new SourceFeeder(target, devRate, 2, 32, true);
            t = new Thread(() =>
            {
                long n = 0; var sw = Stopwatch.StartNew(); long sent = 0;
                while (!stop)
                {
                    long due = (long)(sw.Elapsed.TotalSeconds * devRate) - sent;
                    if (due < devRate / 50) { Thread.Sleep(5); continue; }     // 20msごとにまとめて届く
                    var buf = new float[due * 2];
                    for (int i = 0; i < due; i++)
                    {
                        float v = Silent ? 0f : (float)(amp * Math.Sin(2 * Math.PI * freq * (n + i) / devRate));
                        buf[i * 2] = v; buf[i * 2 + 1] = v;
                    }
                    n += due; sent += due;
                    feeder.FeedFloats(buf, buf.Length);
                }
            }) { IsBackground = true };
            t.Start();
        }
        public void Dispose() { stop = true; t.Join(); }
    }

    static Preset BasePreset(string folder, AudioFormat fmt) => new()
    {
        Name = "テスト", Format = fmt, SampleRate = 48000, Channels = 2, BitrateKbps = 192, BitDepth = 16,
        SaveFolder = folder, UseDateFolder = true, DateFolderFormat = "{yyyy}-{MM}-{dd}", FileNameTemplate = Preset.TrackTemplate,
        Sources = { [0] = new SourceSetting { DeviceId = "x", DeviceName = "テスト入力" } },
    };

    static List<string> Files(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".journal")).OrderBy(f => f).ToList();

    static double Dur(string ffmpeg, string f) => FfmpegTools.ProbeDuration(ffmpeg, f) ?? -1;

    /// <summary>ファイルの最大音量(dB)。ffmpeg の volumedetect で測る。測れなければ 0。</summary>
    static double MaxDb(string ffmpeg, string f)
    {
        var psi = new ProcessStartInfo(ffmpeg) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-i", f, "-af", "volumedetect", "-f", "null", "-" }) psi.ArgumentList.Add(a);
        using var pr = Process.Start(psi)!;
        string err = pr.StandardError.ReadToEnd();
        pr.WaitForExit();
        var m = System.Text.RegularExpressions.Regex.Match(err, @"max_volume:\s*(-?[0-9.]+)\s*dB");
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
    }

    public static void Run(string ffmpeg, string tmp, CheckFn check, Action<string> section)
    {
        // ---- 1) 手動分割 + 日本語ファイル名 + No/Track + タグ (flac)
        section("エンジン: flac / 手動分割 / 日本語タグ");
        {
            string dir = Path.Combine(tmp, "e1");
            var p = BasePreset(dir, AudioFormat.Flac);
            p.Tags.Title = "日本語タイトル"; p.Tags.Artist = "アーティスト♪"; p.Tags.Comment = "No{No}-{Track}";
            p.WriteTrackTag = true;
            var sb = new SourceBuffer(48000, 2) { Label = "テスト入力" };
            var finished = new List<RecordedFile>();
            var eng = new RecorderEngine(new EngineOptions { Preset = p, No = 917, TrackStart = 1, FfmpegPath = ffmpeg, JournalPath = Path.Combine(dir, "j.journal") }, new[] { sb });
            eng.FileFinished += f => { lock (finished) finished.Add(f); };
            using var dev = new FakeDevice(sb, 44100, 440);     // 44.1kHzのデバイス → 48kHzへ変換される
            eng.Start();
            Thread.Sleep(3500);
            eng.Split();
            Thread.Sleep(3000);
            var lv = eng.ReadLevels();
            check(lv.MasterL > 0.2f && lv.MasterL < 0.6f, "音量メーター", lv.MasterL.ToString());
            double elapsedBefore = eng.ElapsedSeconds;
            eng.StopAsync().Wait();
            var files = Files(dir);
            Console.WriteLine("    " + string.Join(" | ", files.Select(f => Path.GetRelativePath(dir, f))));
            check(files.Count == 2, "2ファイルできた", files.Count.ToString());
            check(files.All(f => Path.GetFileName(f).StartsWith("No.917-Track")), "名前の先頭");
            if (files.Count == 2)
            {
                check(Path.GetFileName(files[0]).Contains("Track001_00：00-00：0"), "Track001名", Path.GetFileName(files[0]));
                check(Path.GetFileName(files[1]).Contains("Track002_00：0"), "Track002名", Path.GetFileName(files[1]));
                double d0 = Dur(ffmpeg, files[0]), d1 = Dur(ffmpeg, files[1]);
                check(Math.Abs(d0 - 3.5) < 0.35, "Track001の長さ≒3.5秒", d0.ToString("0.00"));
                check(Math.Abs(d1 - 3.0) < 0.5, "Track002の長さ≒3秒", d1.ToString("0.00"));
                check(Math.Abs(d0 + d1 - elapsedBefore) < 0.6, "合計=録音時間(途切れなし)", $"{d0 + d1:0.00} vs {elapsedBefore:0.00}");
                var (_, meta) = FfmpegTools.Run(ffmpeg, new[] { "-hide_banner", "-i", files[0] });
                check(meta.Contains("日本語タイトル") && meta.Contains("アーティスト♪"), "日本語タグ", "");
                check(meta.Contains("No917-1") || meta.Contains("No917-"), "タグ内の変数", "");
                check(System.Text.RegularExpressions.Regex.IsMatch(meta, @"(?i)track\s*:\s*1"), "1個目にもトラック番号が追記された", "");
                check(System.Text.RegularExpressions.Regex.IsMatch(FfmpegTools.Run(ffmpeg, new[] { "-hide_banner", "-i", files[1] }).Stderr, @"(?i)track\s*:\s*2"), "2個目のトラック番号", "");
            }
            check(finished.Count == 2, "FileFinished が2回", finished.Count.ToString());
            check(!File.Exists(Path.Combine(dir, "j.journal")), "記録ファイルは正常終了で消える");
        }

        // ---- 2) mp3 / m4a / aac / wav / opus が作れて再生可能
        foreach (var fmt in new[] { AudioFormat.Mp3, AudioFormat.M4a, AudioFormat.Aac, AudioFormat.Wav, AudioFormat.Opus, AudioFormat.Ogg })
        {
            section("エンジン: " + fmt);
            string dir = Path.Combine(tmp, "f_" + fmt);
            var p = BasePreset(dir, fmt);
            var sb = new SourceBuffer(48000, 2) { Label = "テスト入力" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, FfmpegPath = ffmpeg }, new[] { sb });
            using var dev = new FakeDevice(sb, 48000, 1000);
            eng.Start(); Thread.Sleep(2500); eng.StopAsync().Wait();
            var files = Files(dir);
            check(files.Count == 1 && files[0].EndsWith(EncoderSpec.Extension(fmt)), "ファイル作成", string.Join(",", files));
            if (files.Count == 1)
            {
                double d = Dur(ffmpeg, files[0]);
                check(Math.Abs(d - 2.5) < 0.5, "長さ≒2.5秒", d.ToString("0.00"));
                var err = FfmpegTools.Run(ffmpeg, new[] { "-v", "error", "-i", files[0], "-f", "null", "-" }).Stderr.Trim();
                check(err.Length == 0, "デコードエラーなし", err);
            }
        }

        // ---- 2b) 既定の日時形式では、重複していないのに「(2)」が付かない
        {
            section("エンジン: 日時形式の名前に(2)が付かない");
            string dir = Path.Combine(tmp, "dflt");
            var p = BasePreset(dir, AudioFormat.Mp3);
            p.FileNameTemplate = Preset.DefaultTemplate;
            var sb = new SourceBuffer(48000, 2) { Label = "テスト入力" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, No = 9, FfmpegPath = ffmpeg }, new[] { sb });
            using var dev = new FakeDevice(sb, 48000, 1000);
            eng.Start(); Thread.Sleep(2200); eng.StopAsync().Wait();
            var files = Files(dir);
            check(files.Count == 1, "ファイルは1つだけ", string.Join(",", files));
            if (files.Count == 1)
            {
                string n = Path.GetFileName(files[0]);
                check(n.EndsWith("(No.9).mp3") && !n.Contains("(2)"), "名前に(2)が付かない", n);
            }
        }

        // ---- 3) 2デバイスのミックス/別ファイル、時間による自動分割、デバイス切断(無音挿入)
        section("エンジン: 2デバイス・ミックス + 自動分割");
        {
            string dir = Path.Combine(tmp, "e3");
            var p = BasePreset(dir, AudioFormat.Flac);
            p.Sources[1] = new SourceSetting { DeviceId = "y", DeviceName = "マイク" };
            p.MixMode = MixMode.Mix; p.AutoSplitSeconds = 0;
            var a = new SourceBuffer(48000, 2) { Label = "PC音" };
            var b = new SourceBuffer(48000, 2) { Label = "マイク" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, FfmpegPath = ffmpeg }, new[] { a, b });
            using var d1 = new FakeDevice(a, 48000, 440, 0.3);
            using var d2 = new FakeDevice(b, 48000, 880, 0.3);
            eng.Start(); Thread.Sleep(2000);
            b.Connected = false;                // マイクが外れた
            Thread.Sleep(1500);
            b.Connected = true;
            Thread.Sleep(1000);
            var lv = eng.ReadLevels();
            eng.StopAsync().Wait();
            var files = Files(dir);
            check(files.Count == 1, "ミックスは1ファイル", files.Count.ToString());
            if (files.Count == 1) check(Math.Abs(Dur(ffmpeg, files[0]) - 4.5) < 0.5, "切断中も時間がずれない(≒4.5秒)", Dur(ffmpeg, files[0]).ToString("0.00"));
            check(lv.Sources.Length == 2 && lv.Sources[0] > 0.1f, "入力別メーター");
        }
        section("エンジン: 2デバイス・別ファイル");
        {
            string dir = Path.Combine(tmp, "e4");
            var p = BasePreset(dir, AudioFormat.Flac);
            p.Sources[1] = new SourceSetting { DeviceId = "y", DeviceName = "マイク" };
            p.MixMode = MixMode.Separate;
            var a = new SourceBuffer(48000, 2) { Label = "PC音" };
            var b = new SourceBuffer(48000, 2) { Label = "マイク" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, FfmpegPath = ffmpeg }, new[] { a, b });
            using var d1 = new FakeDevice(a, 48000, 440, 0.3);
            using var d2 = new FakeDevice(b, 48000, 880, 0.3);
            eng.Start(); Thread.Sleep(2000); eng.StopAsync().Wait();
            var files = Files(dir);
            Console.WriteLine("    " + string.Join(" | ", files.Select(f => Path.GetFileName(f))));
            check(files.Count == 2 && files.Any(f => f.Contains("_PC音")) && files.Any(f => f.Contains("_マイク")), "デバイスごとに別ファイル(名前に_デバイス名)");
        }
        section("エンジン: 入力ごとに録音へ送る/送らない");
        {
            // 同じ2入力(440Hzと880Hz、振幅0.3)を、送りの設定だけ変えて2回録音して、最大音量を比べる
            double Record(string name, bool sendSecond, out float meter2)
            {
                string dir = Path.Combine(tmp, name);
                var p = BasePreset(dir, AudioFormat.Flac);
                p.Sources[1] = new SourceSetting { DeviceId = "y", DeviceName = "マイク" };
                p.MixMode = MixMode.Mix;
                var a = new SourceBuffer(48000, 2) { Label = "PC音" };
                var b = new SourceBuffer(48000, 2) { Label = "マイク" };
                var eng = new RecorderEngine(new EngineOptions { Preset = p, FfmpegPath = ffmpeg }, new[] { a, b });
                eng.RecordingBus.SetSend(1, sendSecond);
                using var d1 = new FakeDevice(a, 48000, 440, 0.3);
                using var d2 = new FakeDevice(b, 48000, 880, 0.3);
                eng.Start(); Thread.Sleep(2000);
                var lv = eng.ReadLevels();
                eng.StopAsync().Wait();
                meter2 = lv.Sources.Length > 1 ? lv.Sources[1] : 0;
                var fs = Files(dir);
                return fs.Count == 1 ? MaxDb(ffmpeg, fs[0]) : 0;
            }
            double both = Record("e3b", true, out _);
            double firstOnly = Record("e3c", false, out float m2);
            Console.WriteLine($"    最大音量: 2入力={both:0.0}dB / 1入力のみ={firstOnly:0.0}dB");
            check(both > -7.0 && both < -1.0, "2入力を送ると足し合わさる", both.ToString("0.0"));
            check(firstOnly > -12.0 && firstOnly < -9.0, "送りをオフにした入力は録音に入らない", firstOnly.ToString("0.0"));
            check(m2 > 0.1f, "録音に送らなくても、その入力のメーターは動く", m2.ToString("0.000"));
        }

        // ---- 4) 切断時: 待機して戻ったら別ファイルに分割
        section("エンジン: 切断→復帰で分割");
        {
            string dir = Path.Combine(tmp, "e5");
            var p = BasePreset(dir, AudioFormat.Flac);
            p.DeviceLostPolicy = DeviceLostPolicy.SplitOnReturn;
            var a = new SourceBuffer(48000, 2) { Label = "PC音" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, FfmpegPath = ffmpeg }, new[] { a });
            using var d1 = new FakeDevice(a, 48000, 440, 0.3);
            eng.Start(); Thread.Sleep(2000);
            a.Connected = false; Thread.Sleep(1500);
            a.Connected = true; Thread.Sleep(1500);
            eng.StopAsync().Wait();
            var files = Files(dir);
            check(files.Count == 2, "戻ったら別ファイル", files.Count.ToString());
            if (files.Count == 2)
            {
                double total = Dur(ffmpeg, files[0]) + Dur(ffmpeg, files[1]);
                check(Math.Abs(total - 3.5) < 0.6, "切断中は録音時間に含まない(≒3.5秒)", total.ToString("0.00"));
            }
        }

        // ---- 5) 時間による自動分割(強制最長を使って短時間で確認) と 無音で自動分割
        section("エンジン: 無音で自動分割");
        {
            string dir = Path.Combine(tmp, "e6");
            var p = BasePreset(dir, AudioFormat.Flac);
            p.SplitOnSilence = true; p.SilenceDb = -50; p.SilenceSeconds = 1; p.MinTrackSeconds = 1;
            var a = new SourceBuffer(48000, 2) { Label = "PC音" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, FfmpegPath = ffmpeg }, new[] { a });
            using var d1 = new FakeDevice(a, 48000, 440, 0.3);
            eng.Start(); Thread.Sleep(2000);
            d1.Silent = true; Thread.Sleep(2000);
            d1.Silent = false; Thread.Sleep(1500);
            eng.StopAsync().Wait();
            var files = Files(dir);
            check(files.Count == 2, "無音後に分割", files.Count.ToString());
        }

        section("エンジン: 音で自動開始");
        {
            string dir = Path.Combine(tmp, "e7");
            var p = BasePreset(dir, AudioFormat.Flac);
            p.AutoStartOnSound = true; p.AutoStartDb = -40;
            var a = new SourceBuffer(48000, 2) { Label = "PC音" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, FfmpegPath = ffmpeg }, new[] { a });
            using var d1 = new FakeDevice(a, 48000, 440, 0.3) { Silent = true };
            eng.Start();
            check(eng.State == EngineState.Armed, "待機状態");
            Thread.Sleep(1500);
            check(!Directory.Exists(dir) || Files(dir).Count == 0, "音が出るまでファイルを作らない");
            d1.Silent = false; Thread.Sleep(2000);
            check(eng.State == EngineState.Recording, "音で録音開始");
            eng.StopAsync().Wait();
            var files = Files(dir);
            check(files.Count == 1 && Dur(ffmpeg, files[0]) > 1.5, "ファイルができた");
        }

        // ---- 6) 異常終了(ffmpegが強制終了された) からの復旧
        section("異常終了からの復旧");
        {
            string dir = Path.Combine(tmp, "e8");
            string journal = Path.Combine(tmp, "e8.journal");
            var p = BasePreset(dir, AudioFormat.Mp3);
            var a = new SourceBuffer(48000, 2) { Label = "PC音" };
            var eng = new RecorderEngine(new EngineOptions { Preset = p, No = 5, FfmpegPath = ffmpeg, JournalPath = journal }, new[] { a });
            string? err = null; eng.Message += m => err = m;
            using var d1 = new FakeDevice(a, 48000, 440, 0.3);
            eng.Start(); Thread.Sleep(3000);
            check(File.Exists(journal), "録音中は記録ファイルがある");
            check(Files(dir).Any(f => f.Contains("録音中")), "録音中の仮名");
            // ffmpeg を強制終了して「落ちた」状態を再現(OS側が閉じるのと同じ状態)
            foreach (var pr in Process.GetProcessesByName("ffmpeg")) { try { pr.Kill(); } catch { } }
            Thread.Sleep(1000);
            eng.StopAsync().Wait();
            check(File.Exists(journal), "異常時は記録が残る", err ?? "");
            var rec = Recovery.Recover(journal, ffmpeg, Console.WriteLine);
            check(rec.Count == 1 && !rec[0].Path.Contains("録音中") && File.Exists(rec[0].Path), "復旧して名前が整う", rec.Count.ToString());
            if (rec.Count == 1) check(rec[0].Seconds > 1.5, "復旧後の長さ", rec[0].Seconds.ToString("0.00"));
            check(!File.Exists(journal), "復旧後は記録が消える");
        }
    }
}
