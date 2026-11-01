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
Check(NameTemplate.Render("{Date}_{Time}_{DateTime:yyyyMMdd}", ctx) == "2025-07-06_13-05-09_20250706", "日付変数");
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
st.Presets.Add(st.Current.Clone());
st.Presets[0].Name = "ライブ録音";
st.Presets[0].MixMode = MixMode.Separate;
string ini = Path.Combine(tmp, "settings.ini");
SettingsStore.Save(ini, st);
var back = SettingsStore.Load(ini);
Check(back.No == 918 && back.TrackStart == 5 && back.Theme == "dark" && back.HotkeysEnabled, "全体設定の往復");
Check(back.Current.Format == AudioFormat.Mp3 && back.Current.SampleRate == 44100, "現在の設定の往復");
Check(back.Current.SaveFolder == @"D:\録音\テスト" && back.Current.Tags.Title == "日本語タイトル ♪ ＡＢＣ", "日本語の往復");
Check(back.Current.Sources[0].GainDb == -3.5 && back.Current.Sources[0].DeviceName == "スピーカー (Realtek)", "デバイス設定の往復");
Check(back.Presets.Count == 1 && back.Presets[0].Name == "ライブ録音" && back.Presets[0].MixMode == MixMode.Separate, "プリセットの往復");
string iniText = File.ReadAllText(ini);
Check(iniText.Contains("[Preset:ライブ録音]") && iniText.Contains("FileNameTemplate="), "iniが読める形");
string json = Path.Combine(tmp, "presets.json");
PresetJson.Export(back.Presets, json);
Check(File.ReadAllText(json).Contains("ライブ録音") && PresetJson.Import(json)[0].MixMode == MixMode.Separate, "JSON書き出し/読み込み");
File.WriteAllText(ini, "[App]\nNo=abc\n[Current]\nFormat=Nonsense\nSampleRate=-5\n");
var bad = SettingsStore.Load(ini);
Check(bad.No == 1 && bad.Current.Format == AudioFormat.Flac && bad.Current.SampleRate == 48000, "壊れた値は既定値へ");

// ---------------------------------------------------------------- リサンプラ
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
