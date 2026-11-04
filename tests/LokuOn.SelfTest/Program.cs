using System.Diagnostics;
using LokuOn.Core;

// 簡易セルフテスト(外部のテストライブラリ不要)。 dotnet run --project tests/LokuOn.SelfTest -- [ffmpegパス]
int fails = 0, passes = 0;
void Check(bool ok, string name, string detail = "")
{
    if (ok) { passes++; Console.WriteLine("  ok   " + name); }
    else { fails++; Console.WriteLine("  FAIL " + name + (detail.Length > 0 ? "  -> " + detail : "")); }
}
void Section(string s) => Console.WriteLine("\n# " + s);

string? ffmpeg = args.Length > 0 ? args[0] : FfmpegTools.Locate(null, AppContext.BaseDirectory);

// ---------------------------------------------------------------- 時間表記・ファイル名
Section("時間表記");
Check(TimeText.Format(0, TimeStyle.Auto) == "00:00", "0秒");
Check(TimeText.Format(36 * 60 + 25, TimeStyle.Auto) == "36:25", "36:25");
Check(TimeText.Format(3930, TimeStyle.Auto) == "1:05:30", "1:05:30");
Check(TimeText.Format(125 * 60 + 30, TimeStyle.TotalMinutes) == "125:30", "125:30");
Check(TimeText.Format(59.6, TimeStyle.Auto) == "01:00", "四捨五入");

Section("ファイル名テンプレート");
var ctx = new NameContext { No = 917, Track = 1, StartSec = 0, EndSec = 36 * 60 + 25, TimeStyle = TimeStyle.Auto, FileStartTime = new DateTime(2025, 7, 6, 13, 5, 9) };
string tpl = "No.{No}-Track{Track:000}_{Start}-{End}({Length})";
string s1 = NameTemplate.Sanitize(NameTemplate.Render(tpl, ctx));
Check(s1 == "No.917-Track001_00：00-36：25(36：25)", "例1の再現", s1);
ctx.Track = 2; ctx.StartSec = 36 * 60 + 25; ctx.EndSec = 40 * 60;
string s2 = NameTemplate.Sanitize(NameTemplate.Render(tpl, ctx));
Check(s2 == "No.917-Track002_36：25-40：00(03：35)", "例2の再現(秒は整合)", s2);
ctx.StartSec = 0; ctx.EndSec = 125 * 60 + 30;
Check(NameTemplate.Sanitize(NameTemplate.Render("{End}/{End:m}/{Length:h}/{End:s}", ctx)) == "2：05：30／125：30／2：05：30／7530", "表記の切替");
Check(NameTemplate.Render("{Date}_{Time}_{DateTime:yyyyMMdd}", ctx) == "2025-07-06_13:05:09_20250706", "日付変数");
Check(NameTemplate.Render("{yyyy}{yy}{MM}{dd}-{HH}{mm}{ss}", ctx) == "20252507" + "06-130509", "日付の部品変数");
{
    var dctx = new NameContext { No = 917, FileStartTime = new DateTime(2025, 7, 6, 13, 5, 9), Ext = "flac" };
    string dn = NameTemplate.RenderFileBase(Preset.DefaultTemplate, dctx);
    Check(dn == "2025-07-06 13：05：09 (No.917)", "既定テンプレート(日時形式)", dn);
    dctx.Ext = "mp3";
    Check(NameTemplate.RenderFileBase("{Date} {Time} (No.{No}).{ext}", dctx) == dn, "{ext}は名前に残らない");
    Check(NameTemplate.RenderFolder("{yyyy}-{MM}-{dd}", dctx.FileStartTime) == "2025-07-06", "日付フォルダ(変数形式)");
    Check(NameTemplate.RenderFolder("yyyy-MM-dd", dctx.FileStartTime) == "2025-07-06", "日付フォルダ(書式形式)");
    Check(new Preset().DateFolderFormat == "{yyyy}-{MM}-{dd}", "日付フォルダの既定");
    Check(new Preset().FileNameTemplate == "{Date} {Time} (No.{No}).{ext}", "ファイル名の既定");
    var tctx = new NameContext { No = 917, Track = 1, StartSec = 0, EndSec = 36 * 60 + 25, Ext = "mp3" };
    Check(NameTemplate.RenderFileBase(Preset.TrackTemplate, tctx) == "No.917-Track001_00：00-36：25(36：25)", "トラック形式");
}
Check(NameTemplate.Render("{Unknown}{{x}}", ctx) == "{Unknown}{x}", "未知の変数と{{}}");
ctx.Provisional = true;
Check(NameTemplate.Render("{End}({Length})", ctx) == "録音中(録音中)", "録音中の仮名");
Check(NameTemplate.Sanitize("a:b*c?d\"e<f>g|h/i\\j.  ") == "a：b＊c？d＂e＜f＞g｜h／i＼j", "禁止文字の全角化");
Check(NameTemplate.Sanitize("CON") == "_CON", "予約名");
Check(NameTemplate.Sanitize("   ") == "録音", "空");

// ---------------------------------------------------------------- ini / プリセット
Section("設定ファイル");
string tmp = Path.Combine(Path.GetTempPath(), "lokuon_selftest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);
var st = new AppSettings { No = 918, TrackStart = 5, Theme = "dark", HotkeysEnabled = true };
st.Current.Format = AudioFormat.Mp3;
st.Current.SampleRate = 44100;
st.Current.SaveFolder = @"D:\録音\テスト";
st.Current.Tags.Title = "日本語タイトル ♪ ＡＢＣ";
st.Current.Sources[0] = new SourceSetting { DeviceId = "wasapi-loop:default", DeviceName = "スピーカー (Realtek)", GainDb = -3.5 };
string ini = Path.Combine(tmp, "settings.ini");
{
    var lp = st.Current.Clone();
    lp.Name = "ライブ録音"; lp.MixMode = MixMode.Separate; lp.SaveFolder = @"E:\別の場所";
    lp.CompressionLevel = 8; lp.AutoSplitSeconds = 5430;
    PresetStore.Save(PresetStore.FolderFor(ini), lp);
}
SettingsStore.Save(ini, st);
var back = SettingsStore.Load(ini);
Check(back.No == 918 && back.TrackStart == 5 && back.Theme == "dark" && back.HotkeysEnabled, "全体設定の往復");
Check(back.Current.Format == AudioFormat.Mp3 && back.Current.SampleRate == 44100, "現在の設定の往復");
Check(back.Current.SaveFolder == @"D:\録音\テスト" && back.Current.Tags.Title == "日本語タイトル ♪ ＡＢＣ", "日本語の往復");
Check(back.Current.Sources[0].GainDb == -3.5 && back.Current.Sources[0].DeviceName == "スピーカー (Realtek)", "デバイス設定の往復");
Check(back.Presets.Count == 1 && back.Presets[0].Name == "ライブ録音" && back.Presets[0].MixMode == MixMode.Separate, "プリセットの往復");
string iniText = File.ReadAllText(ini);
Check(!iniText.Contains("[Preset:") && iniText.Contains("FileNameTemplate="), "プリセットはiniに入れない");
Check(File.Exists(Path.Combine(tmp, "Presets", "ライブ録音.json")), "Presets\\名前.json に保存");
string pj = File.ReadAllText(Path.Combine(tmp, "Presets", "ライブ録音.json"));
Check(!pj.Contains("SaveFolder") && !pj.Contains("E:\\"), "プリセットに保存先を含めない");
Check(back.Presets[0].CompressionLevel == 8 && back.Presets[0].AutoSplitSeconds == 5430, "プリセットの圧縮率・自動分割(秒)");
// 旧形式([Preset:名前] in ini)からの移行
{
    string old = Path.Combine(tmp, "old", "settings.ini");
    Directory.CreateDirectory(Path.GetDirectoryName(old)!);
    File.WriteAllText(old, "[App]\nDiskWarnGB=5\nDiskStopGB=0.5\n[Preset:旧プリセット]\nFormat=Mp3\nAutoSplitMinutes=30\nSource1Device=wasapi-loop:default\n");
    var mig = SettingsStore.Load(old);
    Check(mig.Presets.Count == 1 && mig.Presets[0].Name == "旧プリセット" && mig.Presets[0].Format == AudioFormat.Mp3
        && mig.Presets[0].AutoSplitSeconds == 1800, "旧iniのプリセットをPresetsへ移行(分→秒)");
    Check(File.Exists(Path.Combine(tmp, "old", "Presets", "旧プリセット.json")), "移行したjsonが作られる");
    Check(mig.DiskWarnGB == 1 && mig.DiskStopGB == 0.1, "旧い既定のディスク閾値は新既定(1GB/100MB)へ");
    File.WriteAllText(Path.Combine(tmp, "old", "Presets", "壊れ.json"), "{ not json");
    var mig2 = SettingsStore.Load(old);
    Check(mig2.Presets.Count == 1 && mig2.PresetProblems.Count == 1, "壊れたプリセットは飛ばして報告");
}
Check(Preset.SourceSlots == 4 && Preset.GainMaxDb == 20 && Preset.GainMinDb == -30, "入力4つ・ゲイン-30〜+20");
{
    var gp = new Preset(); gp.Sources[3].GainDb = 99; gp.Sources[0].GainDb = -99; gp.Normalize();
    Check(gp.Sources[3].GainDb == 20 && gp.Sources[0].GainDb == -30, "ゲインの範囲を丸める");
    var e = new EncoderSpec { Format = AudioFormat.Ogg, BitrateKbps = 192 }.BuildArgs("x.ogg");
    Check(e.Contains("libvorbis") && EncoderSpec.Extension(AudioFormat.Ogg) == ".ogg", "Oggの引数");
    var f0 = new EncoderSpec { Format = AudioFormat.Flac, CompressionLevel = 12 }.BuildArgs("x.flac");
    Check(f0[f0.IndexOf("-compression_level") + 1] == "12", "FLAC圧縮率の反映");
    var f1 = new EncoderSpec { Format = AudioFormat.Flac }.BuildArgs("x.flac");
    Check(f1[f1.IndexOf("-compression_level") + 1] == "5", "FLAC圧縮率の既定");
}
string json = Path.Combine(tmp, "presets.json");
PresetJson.Export(back.Presets, json);
Check(File.ReadAllText(json).Contains("ライブ録音") && PresetJson.Import(json)[0].MixMode == MixMode.Separate, "JSON書き出し/読み込み");
File.WriteAllText(ini, "[App]\nNo=abc\n[Current]\nFormat=Nonsense\nSampleRate=-5\n");
var bad = SettingsStore.Load(ini);
Check(bad.No == 1 && bad.Current.Format == AudioFormat.Flac && bad.Current.SampleRate == 48000, "壊れた値は既定値へ");

// ---------------------------------------------------------------- リサンプラ
Section("テンプレート保存・初期値");
{
    string tp = Path.Combine(tmp, "tpl.ini");
    var st0 = new AppSettings();
    st0.Templates.Add(new SavedTemplate { Name = "自分用：日付のみ", Template = "{yyyy}{MM}{dd}_{HH}{mm}{ss}" });
    SettingsStore.Save(tp, st0);
    var st1 = SettingsStore.Load(tp);
    Check(st1.Templates.Count == 1 && st1.Templates[0].Name == "自分用：日付のみ" && st1.Templates[0].Template == "{yyyy}{MM}{dd}_{HH}{mm}{ss}", "独自テンプレートの保存と読み込み");
    Check(TemplateLibrary.IsBuiltIn("日時形式") && TemplateLibrary.ValidateName("日時形式") != null, "最初のテンプレート名は使えない");
    Check(TemplateLibrary.ValidateName("OK名") == null && TemplateLibrary.ValidateName("a[b") != null, "テンプレート名の検査");
    Check(new Preset().Sources[0].DeviceId == "wasapi-cap:default", "初期の入力は既定のマイク");
    Check(new Preset().Sources[1].DeviceId == "", "2つ目以降は未使用");
    Check(new Preset().Format == AudioFormat.Flac, "形式の初期値はflac");
    Check(Preset.DefaultSaveFolder.EndsWith(Path.Combine("LokuOn", "Rec")), "保存先の初期値");
}

Section("名前の重複判定・設定の追加項目");
{
    string ud = Path.Combine(tmp, "uniq"); Directory.CreateDirectory(ud);
    string f1 = Path.Combine(ud, "a (No.1).mp3"); File.WriteAllText(f1, "x");
    Check(NameTemplate.UniquePath(ud, "a (No.1)", "mp3").EndsWith("a (No.1) (2).mp3"), "他のファイルと重複したら(2)");
    Check(NameTemplate.UniquePath(ud, "a (No.1)", "mp3", f1) == f1, "自分自身とは重複とみなさない");
    Check(NameTemplate.UniquePath(ud, "A (NO.1)", ".MP3", f1) == Path.Combine(ud, "A (NO.1).MP3"), "大文字小文字だけ違う自分の名前も重複としない");
    var a0 = new AppSettings { MinimizeToTray = true, Backdrop = "acrylic" };
    a0.HiddenTemplates.Add("日時形式");
    string ap = Path.Combine(tmp, "app2.ini");
    SettingsStore.Save(ap, a0);
    var a1 = SettingsStore.Load(ap);
    Check(a1.MinimizeToTray && a1.Backdrop == "acrylic" && a1.HiddenTemplates.SequenceEqual(new[] { "日時形式" }), "最小化の動作・背景効果・非表示テンプレートの保存");
    Check(!new AppSettings().MinimizeToTray && new AppSettings().Backdrop == "none", "最小化はタスクバーに残す・背景効果なしが初期値");
    string old = Path.Combine(tmp, "old.ini");
    File.WriteAllText(old, "[App]\nMinimizeToTray=true\n");
    Check(!SettingsStore.Load(old).MinimizeToTray, "古い設定の MinimizeToTray は引き継がず、新しい初期値(タスクバー)になる");
}

Section("背景の効果のルール");
{
    Check(BackdropRules.Effective("mica", false) == "none" && BackdropRules.Effective("acrylic", false) == "none", "ライトテーマでは背景の効果はなしに固定");
    Check(BackdropRules.Effective("mica", true) == "mica" && BackdropRules.Effective("acrylic", true) == "acrylic", "ダークテーマでは選んだ効果");
    Check(BackdropRules.Effective("xxx", true) == "none" && BackdropRules.Effective(null, true) == "none", "不正な値はなし");
    Check(BackdropRules.TintAlpha("mica", 80) > BackdropRules.TintAlpha("acrylic", 80), "アクリルは同じ濃さでもMicaより透ける");
    Check(Math.Abs(BackdropRules.TintAlpha("mica", 80) - 0.8) < 1e-9 && Math.Abs(BackdropRules.TintAlpha("acrylic", 80) - 0.64) < 1e-9, "濃さ80のとき Mica 0.80 / アクリル 0.64");
    Check(BackdropRules.TintAlpha("mica", 0) == 0.3 && BackdropRules.TintAlpha("mica", 500) == 1.0, "濃さは30〜100に収まる");
    var b0 = new AppSettings { Backdrop = "acrylic", BackdropStrength = 55 };
    string bp = Path.Combine(tmp, "bd.ini");
    SettingsStore.Save(bp, b0);
    var b1 = SettingsStore.Load(bp);
    Check(b1.Backdrop == "acrylic" && b1.BackdropStrength == 55, "背景の効果と濃さの保存");
    Check(new AppSettings().BackdropStrength == 80, "濃さの初期値は80");
    File.WriteAllText(Path.Combine(tmp, "bd2.ini"), "[App]\nBackdropStrength=5\n");
    Check(SettingsStore.Load(Path.Combine(tmp, "bd2.ini")).BackdropStrength == 30, "範囲外の濃さは30に直る");
}

Section("リサンプラ");
foreach (var (inR, outR) in new[] { (44100, 48000), (48000, 44100), (96000, 48000), (48000, 48000), (22050, 48000) })
{
    var rs = new StreamResampler(inR, outR, 2);
    double f = 1000;
    int totalIn = inR * 4;
    var output = new List<float>();
    var rnd = new Random(1);
    int pos = 0;
    while (pos < totalIn)
    {
        int n = Math.Min(rnd.Next(100, 3000), totalIn - pos);
        var buf = new float[n * 2];
        for (int i = 0; i < n; i++) { float v = (float)(0.5 * Math.Sin(2 * Math.PI * f * (pos + i) / inR)); buf[i * 2] = v; buf[i * 2 + 1] = -v; }
        rs.Process(buf, n, output);
        pos += n;
    }
    int outFrames = output.Count / 2;
    double expect = totalIn * (double)outR / inR;
    // 先頭0.1秒とリング末尾を除いて、理想の正弦波との誤差(SNR)を測る
    double sig = 0, err = 0; int skip = outR / 10;
    for (int i = skip; i < outFrames - skip; i++)
    {
        double ideal = 0.5 * Math.Sin(2 * Math.PI * f * i / outR);
        double d = output[i * 2] - ideal;
        sig += ideal * ideal; err += d * d;
        if (Math.Abs(output[i * 2 + 1] + output[i * 2]) > 1e-4) { err += 1; }
    }
    double snr = 10 * Math.Log10(sig / Math.Max(err, 1e-30));
    Check(Math.Abs(outFrames - expect) < 100 + inR / 100, $"{inR}→{outR} 長さ", $"{outFrames} vs {expect}");
    Check(snr > 60, $"{inR}→{outR} SNR {snr:0.0}dB", $"{snr:0.0}dB");
}

Section("チャンネル変換・PCM変換");
{
    var src = new float[] { 0.5f, -0.5f, 1f, 0f };
    var mono = new float[2];
    ChannelMapper.Convert(src, 2, 2, 1, mono);
    Check(Math.Abs(mono[0]) < 1e-6 && Math.Abs(mono[1] - 0.5f) < 1e-6, "2→1");
    var st2 = new float[4];
    ChannelMapper.Convert(new float[] { 0.25f, 0.75f }, 2, 1, 2, st2);
    Check(st2[0] == 0.25f && st2[1] == 0.25f && st2[2] == 0.75f && st2[3] == 0.75f, "1→2");
    var f = new float[4];
    PcmConverter.ToFloat(new byte[] { 0x00, 0x40, 0x00, 0xC0 }, 4, 16, false, f);
    Check(Math.Abs(f[0] - 0.5f) < 1e-4 && Math.Abs(f[1] + 0.5f) < 1e-4, "16bit");
    PcmConverter.ToFloat(new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 }, 6, 24, false, f);
    Check(Math.Abs(f[0] - 0.5f) < 1e-4 && Math.Abs(f[1] + 0.5f) < 1e-4, "24bit");
}

Section("再生速度(音程を変えない)");
{
    const int R = 48000;
    // 440Hz のステレオの正弦波 10秒を読み出し関数にする
    Func<Func<float[], int, int, int>> mk = () =>
    {
        long pos = 0; long total = R * 10L;
        return (buf, off, cnt) =>
        {
            int frames = (int)Math.Min(cnt / 2, total - pos);
            for (int i = 0; i < frames; i++)
            {
                float v = (float)(0.5 * Math.Sin(2 * Math.PI * 440.0 * (pos + i) / R));
                buf[off + i * 2] = v; buf[off + i * 2 + 1] = v;
            }
            pos += frames;
            return frames * 2;
        };
    };
    foreach (double tempo in new[] { 0.5, 0.75, 1.0, 1.5, 2.0 })
    {
        var wst = new WsolaStretcher(mk(), R, 2) { Tempo = tempo };
        var all = new List<float>();
        var b = new float[8192];
        int got;
        while ((got = wst.Read(b, 0, b.Length)) > 0) { for (int i = 0; i < got; i += 2) all.Add(b[i]); }
        double sec = all.Count / (double)R;
        Check(Math.Abs(sec - 10.0 / tempo) < 0.15 * (1 + 1 / tempo), $"テンポ{tempo}: 長さ≒{10.0 / tempo:0.0}秒", $"実際 {sec:0.00}秒");
        // 中ほど2秒の周波数(ゼロ交差)が 440Hz のまま
        int a0 = (int)(all.Count * 0.4), a1 = a0 + R * 2 > all.Count ? all.Count : a0 + R * 2;
        int cross = 0;
        for (int i = a0 + 1; i < a1; i++) if (all[i - 1] < 0 && all[i] >= 0) cross++;
        double hz = cross / ((a1 - a0) / (double)R);
        Check(Math.Abs(hz - 440) < 440 * 0.03, $"テンポ{tempo}: 音程が440Hzのまま", $"実際 {hz:0.0}Hz");
        double peak = all.Skip(a0).Take(a1 - a0).Max(x => Math.Abs(x));
        Check(peak > 0.4 && peak < 0.6, $"テンポ{tempo}: 音量が変わらない", $"ピーク {peak:0.00}");
    }
    // 等倍はほぼそのまま
    {
        var wst = new WsolaStretcher(mk(), R, 2) { Tempo = 1.0 };
        var b = new float[R * 2];
        int got = wst.Read(b, 0, b.Length);
        double err = 0;
        for (int i = 0; i < R / 2; i++) { float want = (float)(0.5 * Math.Sin(2 * Math.PI * 440.0 * (i + 2000) / R)); err = Math.Max(err, Math.Abs(b[(i + 2000) * 2] - want)); }
        Check(got == b.Length && err < 0.02, "等倍はほぼそのまま", $"最大誤差 {err:0.0000}");
    }
}

Section("入力バッファ");
{
    var sb = new SourceBuffer(48000, 2, 1.0);
    var data = new float[2 * 1000];
    for (int i = 0; i < data.Length; i++) data[i] = i;
    sb.Push(data, 0, 1000);
    var dst = new float[2 * 600];
    int r = sb.Read(dst, 0, 600);
    Check(r == 600 && dst[1199] == 1199, "読み出し");
    sb.Push(data, 0, 1000);
    for (int k = 0; k < 100; k++) sb.Push(data, 0, 1000);      // あふれさせる
    Check(sb.AvailableFrames <= 48000 && sb.DroppedFrames > 0, "あふれは古い方を捨てる");
}

// ---------------------------------------------------------------- 録音エンジン(実際にffmpegで書き出す)
if (ffmpeg == null)
{
    Console.WriteLine("\nffmpeg が見つからないためエンジンテストをスキップ");
}
else
{
    Console.WriteLine("\nffmpeg: " + ffmpeg);
    EngineTests.Run(ffmpeg, tmp, new CheckFn(Check), Section);
}

try { Directory.Delete(tmp, true); } catch { }
Console.WriteLine($"\n結果: {passes} 件成功 / {fails} 件失敗");
return fails == 0 ? 0 : 1;
