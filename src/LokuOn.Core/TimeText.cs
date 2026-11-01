using System.Globalization;

namespace LokuOn.Core;

/// <summary>時間の表記スタイル。</summary>
public enum TimeStyle
{
    /// <summary>1時間未満は「36:25」、1時間以上は「1:05:30」。</summary>
    Auto = 0,
    /// <summary>常に分:秒。例「125:30」。</summary>
    TotalMinutes = 1,
}

public static class TimeText
{
    /// <summary>秒数(小数可)を四捨五入して整数秒にし、指定スタイルで文字列化する。区切りは半角コロン(ファイル名化の際に全角へ変換される)。</summary>
    public static string Format(double seconds, TimeStyle style)
        => FormatWhole(ToWholeSeconds(seconds), style);

    public static long ToWholeSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) return 0;
        return (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
    }

    public static string FormatWhole(long totalSeconds, TimeStyle style)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        long sec = totalSeconds % 60;
        if (style == TimeStyle.TotalMinutes)
        {
            long mins = totalSeconds / 60;
            return string.Create(CultureInfo.InvariantCulture, $"{mins:00}:{sec:00}");
        }
        long h = totalSeconds / 3600;
        long m = (totalSeconds % 3600) / 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{sec:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m:00}:{sec:00}");
    }
}
