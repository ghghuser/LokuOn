using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LokuOn.Core;

public static class FfmpegTools
{
    /// <summary>ffmpeg.exe を探す。指定パス → アプリ隣の ffmpeg フォルダ → アプリと同じフォルダ → PATH の順。</summary>
    public static string? Locate(string? customPath, string appDir)
    {
        bool win = OperatingSystem.IsWindows();
        string exe = win ? "ffmpeg.exe" : "ffmpeg";
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath)) return customPath;
        foreach (var c in new[] { Path.Combine(appDir, "ffmpeg", exe), Path.Combine(appDir, exe) })
            if (File.Exists(c)) return c;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var c = Path.Combine(dir.Trim(), exe); if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    public static ProcessStartInfo NewStartInfo(string ffmpeg, IEnumerable<string> args, bool redirectStdin)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirectStdin,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>ffmpegを実行して終了を待ち、(終了コード, 標準エラー)を返す。</summary>
    public static (int Exit, string Stderr) Run(string ffmpeg, IEnumerable<string> args, int timeoutMs = 600_000)
    {
        using var proc = new Process { StartInfo = NewStartInfo(ffmpeg, args, false) };
        var err = new StringBuilder();
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (err) { if (err.Length < 200_000) err.AppendLine(e.Data); } } };
        proc.OutputDataReceived += (_, _) => { };
        proc.Start();
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();
        if (!proc.WaitForExit(timeoutMs)) { try { proc.Kill(true); } catch { } return (-1, "timeout"); }
        proc.WaitForExit();
        lock (err) return (proc.ExitCode, err.ToString());
    }

    private static readonly Regex DurationRx = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex TimeRx = new(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>音声ファイルの長さ(秒)。失敗時は null。コンテナ情報が無ければ全体をデコードして測る。</summary>
    public static double? ProbeDuration(string ffmpeg, string file)
    {
        var (_, err1) = Run(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", file }, 120_000);
        var m = DurationRx.Match(err1);
        if (m.Success)
        {
            double d = ToSeconds(m);
            if (d > 0) return d;
        }
        var (_, err2) = Run(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", file, "-vn", "-f", "null", "-" }, 3_600_000);
        double? last = null;
        foreach (Match t in TimeRx.Matches(err2)) last = ToSeconds(t);
        return last;
    }

    private static double ToSeconds(Match m)
        => int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
           + double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 再エンコードなしで作り直す(-c copy)。m4aの断片化解除(faststart)や、トラック番号タグの追記に使う。
    /// 成功したら元ファイルを置き換える。失敗しても元ファイルは変更しない。
    /// </summary>
    public static bool Remux(string ffmpeg, string path, bool faststart, int? trackNo)
    {
        string ext = Path.GetExtension(path);
        string dir = Path.GetDirectoryName(path) ?? ".";
        string tmp = Path.Combine(dir, "~lokuon_tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", path, "-map", "0", "-c", "copy" };
        if (faststart && ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)) { args.Add("-movflags"); args.Add("+faststart"); }
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)) { args.Add("-id3v2_version"); args.Add("3"); args.Add("-write_id3v1"); args.Add("0"); }
        if (trackNo.HasValue) { args.Add("-metadata"); args.Add("track=" + trackNo.Value); }
        args.Add(tmp);
        try
        {
            var (exit, _) = Run(ffmpeg, args, 1_800_000);
            if (exit == 0 && File.Exists(tmp) && new FileInfo(tmp).Length > 0)
            {
                File.Move(tmp, path, true);
                return true;
            }
        }
        catch { }
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        return false;
    }
}
