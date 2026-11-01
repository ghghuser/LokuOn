using System.Text;
using System.Text.Json;

namespace LokuOn.Core;

/// <summary>録音中のファイルの記録(アプリが異常終了した場合に、次回起動時にファイル名を整えるため)。</summary>
public sealed class JournalEntry
{
    public string Path { get; set; } = "";
    public string Template { get; set; } = "";
    public TimeStyle TimeStyle { get; set; }
    public int No { get; set; }
    public int Track { get; set; }
    public double StartSec { get; set; }
    public DateTime StartTime { get; set; }
    public string Device { get; set; } = "";
    public bool DeviceSuffix { get; set; }
    public string Preset { get; set; } = "";
}

public sealed class RecordingJournal
{
    private readonly string path;
    private readonly object gate = new();
    private readonly List<JournalEntry> entries = new();
    private static readonly JsonSerializerOptions LineOptions = new(PresetJson.Options) { WriteIndented = false };

    public RecordingJournal(string path) { this.path = path; }

    public void Add(JournalEntry e) { lock (gate) { entries.Add(e); Flush(); } }

    public void Remove(string filePath)
    {
        lock (gate)
        {
            entries.RemoveAll(x => string.Equals(x.Path, filePath, StringComparison.OrdinalIgnoreCase));
            Flush();
        }
    }

    private void Flush()
    {
        try
        {
            if (entries.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
            var sb = new StringBuilder();
            foreach (var e in entries) sb.AppendLine(JsonSerializer.Serialize(e, LineOptions));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch { /* 記録の失敗で録音を止めない */ }
    }

    public static List<JournalEntry> ReadAll(string path)
    {
        var list = new List<JournalEntry>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { var e = JsonSerializer.Deserialize<JournalEntry>(line, PresetJson.Options); if (e != null) list.Add(e); } catch { }
        }
        return list;
    }
}

public sealed class RecordedFile
{
    public string Path { get; set; } = "";
    public double Seconds { get; set; }
    public int No { get; set; }
    public int Track { get; set; }
    public string Device { get; set; } = "";
    public bool Recovered { get; set; }
}

public static class Recovery
{
    /// <summary>前回、異常終了などで「録音中」のまま残ったファイルの長さを調べ、正しいファイル名にする。</summary>
    public static List<RecordedFile> Recover(string journalPath, string ffmpeg, Action<string>? log = null)
    {
        var result = new List<RecordedFile>();
        foreach (var e in JournalEntry_ReadAll(journalPath))
        {
            try
            {
                if (!File.Exists(e.Path) || new FileInfo(e.Path).Length == 0) { log?.Invoke($"空のため破棄: {e.Path}"); continue; }
                double? dur = FfmpegTools.ProbeDuration(ffmpeg, e.Path);
                if (dur == null) { log?.Invoke($"長さを取得できませんでした(そのまま残します): {e.Path}"); continue; }
                string dir = System.IO.Path.GetDirectoryName(e.Path) ?? ".";
                string ext = System.IO.Path.GetExtension(e.Path);
                var ctx = new NameContext
                {
                    No = e.No, Track = e.Track, StartSec = e.StartSec, EndSec = e.StartSec + dur.Value,
                    FileStartTime = e.StartTime, Device = e.Device, Preset = e.Preset, TimeStyle = e.TimeStyle,
                };
                string name = NameTemplate.Sanitize(NameTemplate.Render(e.Template, ctx));
                if (e.DeviceSuffix) name += "_" + NameTemplate.Sanitize(e.Device);
                string final = NameTemplate.UniquePath(dir, name, ext);
                File.Move(e.Path, final);
                if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase)) FfmpegTools.Remux(ffmpeg, final, true, null);
                result.Add(new RecordedFile { Path = final, Seconds = dur.Value, No = e.No, Track = e.Track, Device = e.Device, Recovered = true });
                log?.Invoke($"復旧しました: {final}");
            }
            catch (Exception ex) { log?.Invoke($"復旧に失敗: {e.Path} ({ex.Message})"); }
        }
        try { if (File.Exists(journalPath)) File.Delete(journalPath); } catch { }
        return result;
    }

    private static List<JournalEntry> JournalEntry_ReadAll(string p) => RecordingJournal.ReadAll(p);

    public static bool HasPending(string journalPath) => RecordingJournal.ReadAll(journalPath).Count > 0;
}
