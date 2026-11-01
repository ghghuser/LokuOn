namespace LokuOn.Core;

public sealed class ScheduleSettings
{
    public bool Enabled { get; set; } = false;
    public DateTime StartAt { get; set; } = DateTime.Today.AddDays(1);
    public int DurationMinutes { get; set; } = 60;
    public bool Daily { get; set; } = false;
}

/// <summary>アプリ全体の設定(プリセットに含まれないもの)と、現在の作業用プリセット、保存済みプリセット一覧。</summary>
/// <summary>名前を付けて保存したファイル名テンプレート。</summary>
public sealed class SavedTemplate
{
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";
}

/// <summary>最初から入っているテンプレート(削除・上書きはできない)。</summary>
public static class TemplateLibrary
{
    public static readonly (string Name, string Template)[] BuiltIn =
    {
        ("日時形式", Preset.DefaultTemplate),
        ("トラック形式", Preset.TrackTemplate),
    };

    public static bool IsBuiltIn(string name) => BuiltIn.Any(b => string.Equals(b.Name, name, StringComparison.Ordinal));

    /// <summary>テンプレート名として使えるか(使えないときは理由を返す)。</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "名前を入力してください。";
        if (name.IndexOfAny(new[] { '[', ']', '\r', '\n', '\t' }) >= 0) return "名前に [ ] や改行は使えません。";
        if (IsBuiltIn(name.Trim())) return "最初から入っているテンプレートの名前は使えません。";
        return null;
    }
}

public sealed class AppSettings
{
    /// <summary>次に使う録音ナンバー(録音が終わるたびに+1。アプリを閉じても保持)。</summary>
    public int No { get; set; } = 1;
    /// <summary>録音ごとのトラック番号の開始値(入力値をそのまま保持)。</summary>
    public int TrackStart { get; set; } = 1;
    public string LastPresetName { get; set; } = "";

    /// <summary>"dark" / "light" / "system"</summary>
    public string Theme { get; set; } = "system";
    public bool PreventSleep { get; set; } = true;
    /// <summary>録音していないときも入力のメーターと波形を表示する(入力確認)。</summary>
    public bool MonitorInput { get; set; } = true;
    public bool ConfirmCloseWhileRecording { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool HideToTrayWhileRecording { get; set; } = false;

    public bool HotkeysEnabled { get; set; } = false;
    public string HotkeyStartStop { get; set; } = "Ctrl+Alt+F9";
    public string HotkeySplit { get; set; } = "Ctrl+Alt+F10";

    public double DiskWarnGB { get; set; } = 5;
    public double DiskStopGB { get; set; } = 0.5;
    public string FfmpegPath { get; set; } = "";

    public ScheduleSettings Schedule { get; set; } = new();

    /// <summary>画面に出ている現在の設定(終了時に保存され、次回そのまま復元される)。</summary>
    public Preset Current { get; set; } = new();
    public List<Preset> Presets { get; set; } = new();

    /// <summary>保存したファイル名テンプレート(最初から入っているものは含まない)。</summary>
    public List<SavedTemplate> Templates { get; set; } = new();
}

public static class SettingsStore
{
    private const string Header =
        "LokuOn 設定ファイル (UTF-8)。テキストエディタで編集できます。アプリを終了した状態で編集してください。\n" +
        "[App]=全体設定 / [Current]=現在の画面の設定 / [Preset:名前]=保存済みプリセット。バックアップはこのファイルをコピーするだけです。";

    public static AppSettings Load(string path)
    {
        var s = new AppSettings();
        if (!File.Exists(path)) { s.Current.Normalize(); return s; }
        IniDocument doc;
        try { doc = IniDocument.Load(path); }
        catch
        {
            // 壊れていたらバックアップを試す
            if (File.Exists(path + ".bak")) { try { doc = IniDocument.Load(path + ".bak"); } catch { return s; } }
            else return s;
        }
        var a = doc.Find("App");
        if (a != null)
        {
            var d = new AppSettings();
            s.No = Math.Max(0, a.GetInt("No", d.No));
            s.TrackStart = Math.Max(0, a.GetInt("TrackStart", d.TrackStart));
            s.LastPresetName = a.GetString("LastPresetName");
            s.Theme = a.GetString("Theme", d.Theme);
            s.PreventSleep = a.GetBool("PreventSleep", d.PreventSleep);
            s.MonitorInput = a.GetBool("MonitorInput", d.MonitorInput);
            s.ConfirmCloseWhileRecording = a.GetBool("ConfirmCloseWhileRecording", d.ConfirmCloseWhileRecording);
            s.MinimizeToTray = a.GetBool("MinimizeToTray", d.MinimizeToTray);
            s.HideToTrayWhileRecording = a.GetBool("HideToTrayWhileRecording", d.HideToTrayWhileRecording);
            s.HotkeysEnabled = a.GetBool("HotkeysEnabled", d.HotkeysEnabled);
            s.HotkeyStartStop = a.GetString("HotkeyStartStop", d.HotkeyStartStop);
            s.HotkeySplit = a.GetString("HotkeySplit", d.HotkeySplit);
            s.DiskWarnGB = a.GetDouble("DiskWarnGB", d.DiskWarnGB);
            s.DiskStopGB = a.GetDouble("DiskStopGB", d.DiskStopGB);
            s.FfmpegPath = a.GetString("FfmpegPath");
        }
        var sc = doc.Find("Schedule");
        if (sc != null)
        {
            s.Schedule.Enabled = sc.GetBool("Enabled", false);
            if (DateTime.TryParse(sc.GetString("StartAt"), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt)) s.Schedule.StartAt = dt;
            s.Schedule.DurationMinutes = Math.Max(1, sc.GetInt("DurationMinutes", 60));
            s.Schedule.Daily = sc.GetBool("Daily", false);
        }
        var cur = doc.Find("Current");
        if (cur != null) s.Current = Preset.ReadFrom(cur, "");
        foreach (var sec in doc.Sections)
        {
            if (!sec.Name.StartsWith("Preset:", StringComparison.OrdinalIgnoreCase)) continue;
            string name = sec.Name.Substring(7).Trim();
            if (name.Length == 0) continue;
            s.Presets.Add(Preset.ReadFrom(sec, name));
        }
        foreach (var sec in doc.Sections)
        {
            if (!sec.Name.StartsWith("Template:", StringComparison.OrdinalIgnoreCase)) continue;
            string name = sec.Name.Substring(9).Trim();
            string t = sec.GetString("Template");
            if (name.Length == 0 || t.Length == 0 || TemplateLibrary.IsBuiltIn(name)) continue;
            s.Templates.Add(new SavedTemplate { Name = name, Template = t });
        }
        return s;
    }

    public static void Save(string path, AppSettings s)
    {
        var doc = new IniDocument { HeaderComment = Header };
        var a = doc.Section("App");
        a.Set("No", s.No, "次の録音ナンバー({No})。録音が終わると+1されます");
        a.Set("TrackStart", s.TrackStart, "トラック番号({Track})の開始値");
        a.Set("LastPresetName", s.LastPresetName);
        a.Set("Theme", s.Theme, "dark / light / system");
        a.Set("PreventSleep", s.PreventSleep, "録音中にPCがスリープしないようにする");
        a.Set("MonitorInput", s.MonitorInput, "録音していないときも入力のメーターと波形を表示する(入力確認)");
        a.Set("ConfirmCloseWhileRecording", s.ConfirmCloseWhileRecording);
        a.Set("MinimizeToTray", s.MinimizeToTray, "最小化したときタスクトレイに格納する");
        a.Set("HideToTrayWhileRecording", s.HideToTrayWhileRecording);
        a.Set("HotkeysEnabled", s.HotkeysEnabled, "グローバルホットキー(他のアプリ使用中でも操作可)");
        a.Set("HotkeyStartStop", s.HotkeyStartStop);
        a.Set("HotkeySplit", s.HotkeySplit);
        a.Set("DiskWarnGB", s.DiskWarnGB, "空き容量がこれ未満になったら警告(GB)");
        a.Set("DiskStopGB", s.DiskStopGB, "空き容量がこれ未満になったら安全に録音を停止(GB)");
        a.Set("FfmpegPath", s.FfmpegPath, "空欄=ffmpegフォルダ内のffmpeg.exeを自動検出");

        var sc = doc.Section("Schedule");
        sc.Set("Enabled", s.Schedule.Enabled);
        sc.Set("StartAt", s.Schedule.StartAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        sc.Set("DurationMinutes", s.Schedule.DurationMinutes);
        sc.Set("Daily", s.Schedule.Daily);

        s.Current.WriteTo(doc.Section("Current"), true);
        foreach (var p in s.Presets)
            p.WriteTo(doc.Section("Preset:" + p.Name), false);
        foreach (var t in s.Templates)
            doc.Section("Template:" + t.Name).Set("Template", t.Template);
        doc.Save(path);
    }
}
