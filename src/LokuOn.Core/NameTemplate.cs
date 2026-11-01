using System.Globalization;
using System.Text;

namespace LokuOn.Core;

/// <summary>ファイル名テンプレートに渡す、1ファイル分の情報。</summary>
public sealed class NameContext
{
    public int No { get; set; }
    public int Track { get; set; }
    /// <summary>このファイルの先頭の、録音開始からの経過秒。</summary>
    public double StartSec { get; set; }
    /// <summary>このファイルの末尾の、録音開始からの経過秒。</summary>
    public double EndSec { get; set; }
    /// <summary>録音中の仮の名前を作るとき true(End / Length が「録音中」になる)。</summary>
    public bool Provisional { get; set; }
    public DateTime FileStartTime { get; set; } = DateTime.Now;
    public string Device { get; set; } = "";
    public string Preset { get; set; } = "";
    public TimeStyle TimeStyle { get; set; } = TimeStyle.Auto;
}

/// <summary>
/// 「{名前}」「{名前:書式}」形式のテンプレート展開と、Windowsで使えるファイル名への整形。
/// 変数: {No} {Track:000} {Start} {End} {Length} {Date} {Time} {DateTime:書式} {Device} {Preset}
/// 時間変数の書式: 指定なし=設定の表記 / m=分のみ(125：30) / h=時：分：秒 / s=秒数
/// </summary>
public static class NameTemplate
{
    public const string ProvisionalText = "録音中";

    public static readonly string[] KnownVariables =
    {
        "No", "Track", "Start", "End", "Length", "Date", "Time", "DateTime", "Device", "Preset",
    };

    private static readonly Dictionary<char, char> FullWidth = new()
    {
        ['\\'] = '＼',
        ['/'] = '／',
        [':'] = '：',
        ['*'] = '＊',
        ['?'] = '？',
        ['"'] = '＂',
        ['<'] = '＜',
        ['>'] = '＞',
        ['|'] = '｜',
    };

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // ------------------------------------------------------------ 展開

    public static string Render(string template, NameContext c)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        var sb = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            char ch = template[i];
            if (ch == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                int close = template.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(template, i, template.Length - i); break; }
                string token = template.Substring(i + 1, close - i - 1);
                string name = token;
                string? fmt = null;
                int colon = token.IndexOf(':');
                if (colon >= 0) { name = token.Substring(0, colon); fmt = token.Substring(colon + 1); }
                string? value = Evaluate(name.Trim(), fmt, c);
                sb.Append(value ?? "{" + token + "}");
                i = close + 1;
            }
            else if (ch == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
            }
            else
            {
                sb.Append(ch);
                i++;
            }
        }
        return sb.ToString();
    }

    private static string? Evaluate(string name, string? fmt, NameContext c)
    {
        switch (name.ToLowerInvariant())
        {
            case "no": return FormatInt(c.No, fmt);
            case "track": return FormatInt(c.Track, fmt);
            case "start": return FormatTime(TimeText.ToWholeSeconds(c.StartSec), fmt, c.TimeStyle);
            case "end":
                return c.Provisional ? ProvisionalText : FormatTime(TimeText.ToWholeSeconds(c.EndSec), fmt, c.TimeStyle);
            case "length":
                if (c.Provisional) return ProvisionalText;
                long start = TimeText.ToWholeSeconds(c.StartSec);
                long end = TimeText.ToWholeSeconds(c.EndSec);
                return FormatTime(Math.Max(0, end - start), fmt, c.TimeStyle);
            case "date": return FormatDate(c.FileStartTime, fmt, "yyyy-MM-dd");
            case "time": return FormatDate(c.FileStartTime, fmt, "HH-mm-ss");
            case "datetime": return FormatDate(c.FileStartTime, fmt, "yyyy-MM-dd_HH-mm-ss");
            case "device": return c.Device ?? "";
            case "preset": return c.Preset ?? "";
            default: return null;
        }
    }

    private static string FormatInt(int value, string? fmt)
    {
        if (string.IsNullOrEmpty(fmt)) return value.ToString(CultureInfo.InvariantCulture);
        try { return value.ToString(fmt, CultureInfo.InvariantCulture); }
        catch (FormatException) { return value.ToString(CultureInfo.InvariantCulture); }
    }

    private static string FormatDate(DateTime t, string? fmt, string defaultFormat)
    {
        string f = string.IsNullOrEmpty(fmt) ? defaultFormat : fmt;
        try { return t.ToString(f, CultureInfo.InvariantCulture); }
        catch (FormatException) { return t.ToString(defaultFormat, CultureInfo.InvariantCulture); }
    }

    private static string FormatTime(long totalSeconds, string? fmt, TimeStyle style)
    {
        switch ((fmt ?? "").Trim().ToLowerInvariant())
        {
            case "m": return TimeText.FormatWhole(totalSeconds, TimeStyle.TotalMinutes);
            case "s": return totalSeconds.ToString(CultureInfo.InvariantCulture);
            case "h":
            {
                long h = totalSeconds / 3600;
                long m = (totalSeconds % 3600) / 60;
                long s = totalSeconds % 60;
                return string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}");
            }
            case "a": return TimeText.FormatWhole(totalSeconds, TimeStyle.Auto);
            default: return TimeText.FormatWhole(totalSeconds, style);
        }
    }

    // ------------------------------------------------------------ ファイル名の整形

    /// <summary>
    /// Windowsで使えるファイル名(拡張子なし)にする。使えない記号は全角に置き換え、制御文字は除く。
    /// 末尾の「.」と空白は取り除き、予約名(CON等)は先頭に「_」を付け、空なら「録音」にする。
    /// </summary>
    public static string Sanitize(string name, int maxLength = 180)
    {
        var sb = new StringBuilder((name ?? "").Length);
        foreach (char ch in name ?? "")
        {
            if (FullWidth.TryGetValue(ch, out var full)) sb.Append(full);
            else if (ch < 0x20 || ch == 0x7F) continue;
            else sb.Append(ch);
        }
        string s = sb.ToString().Trim().TrimEnd('.', ' ');
        if (s.Length == 0) return "録音";

        int dot = s.IndexOf('.');
        string head = (dot >= 0 ? s.Substring(0, dot) : s).Trim();
        if (Reserved.Contains(head)) s = "_" + s;

        if (maxLength > 0 && s.Length > maxLength)
        {
            s = s.Substring(0, maxLength).TrimEnd('.', ' ');
            if (s.Length == 0) s = "録音";
        }
        return s;
    }

    public static string SanitizeFolderName(string name) => Sanitize(name, 100);

    /// <summary>同名ファイルがあれば「名前 (2).拡張子」のように連番を付けたパスを返す。ext は「.flac」でも「flac」でもよい。</summary>
    public static string UniquePath(string dir, string baseName, string ext)
    {
        if (ext.Length > 0 && ext[0] != '.') ext = "." + ext;
        string path = Path.Combine(dir, baseName + ext);
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        for (int n = 2; n < 10000; n++)
        {
            string candidate = Path.Combine(dir, baseName + " (" + n + ")" + ext);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
    }
}
