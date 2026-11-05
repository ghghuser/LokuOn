using System.ComponentModel;
using System.Globalization;
using System.IO;

namespace LokuOn.App;

/// <summary>ファイル一覧の1行。</summary>
public sealed class FileItem : INotifyPropertyChanged
{
    private string path = "";
    private double? seconds;
    private long size;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path
    {
        get => path;
        set { path = value; Raise(nameof(Path)); Raise(nameof(Name)); }
    }

    public string Name => System.IO.Path.GetFileName(path);
    public string Folder => System.IO.Path.GetDirectoryName(path) ?? "";

    public double? Seconds
    {
        get => seconds;
        set { seconds = value; Raise(nameof(Seconds)); Raise(nameof(DurationText)); }
    }

    public long Size
    {
        get => size;
        set { size = value; Raise(nameof(SizeText)); }
    }

    public DateTime Time { get; set; } = DateTime.Now;

    public string DurationText => seconds.HasValue ? FormatClock(seconds.Value) : "—";

    public string SizeText
    {
        get
        {
            double mb = size / 1048576.0;
            if (mb >= 1024) return (mb / 1024).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }
    }

    public string TimeText => Time.ToString("MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string FormatClock(double sec)
    {
        long t = (long)Math.Round(Math.Max(0, sec));
        long h = t / 3600, m = (t % 3600) / 60, s = t % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }

    public static FileItem FromPath(string p, double? sec = null)
    {
        var it = new FileItem { path = p, seconds = sec };
        try
        {
            var fi = new FileInfo(p);
            if (fi.Exists) { it.size = fi.Length; it.Time = fi.LastWriteTime; }
        }
        catch { }
        return it;
    }

    /// <summary>一覧の読み込みで得た FileInfo から作る(もう一度ファイルを調べに行かない)。</summary>
    public static FileItem FromInfo(FileInfo fi, double? sec = null)
    {
        var it = new FileItem { path = fi.FullName, seconds = sec };
        try { it.size = fi.Length; it.Time = fi.LastWriteTime; }
        catch { }
        return it;
    }

    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
