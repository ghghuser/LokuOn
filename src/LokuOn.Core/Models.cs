using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LokuOn.Core;

public enum AudioFormat
{
    Flac = 0,
    Mp3 = 1,
    M4a = 2,
    Aac = 3,
    Wav = 4,
    Opus = 5,
}

public enum MixMode
{
    /// <summary>複数の入力をミックスして1ファイルにする。</summary>
    Mix = 0,
    /// <summary>入力ごとに別ファイルにする。</summary>
    Separate = 1,
}

public enum DeviceLostPolicy
{
    /// <summary>デバイスが切れても録音を続け、同じファイルに録る(切れている間は無音)。</summary>
    Continue = 0,
    /// <summary>戻るまで待機する(録音は一時停止)。</summary>
    Wait = 1,
    /// <summary>戻ったらファイルを分割して続ける。</summary>
    SplitOnReturn = 2,
}

/// <summary>入力デバイス1つぶんの設定。DeviceId が空なら未使用。</summary>
public sealed class SourceSetting
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public double GainDb { get; set; } = 0;

    public SourceSetting Clone() => (SourceSetting)MemberwiseClone();
}

public sealed class TagSettings
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Year { get; set; } = "";
    public string Comment { get; set; } = "";

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Artist) && string.IsNullOrWhiteSpace(Album) &&
        string.IsNullOrWhiteSpace(AlbumArtist) && string.IsNullOrWhiteSpace(Genre) && string.IsNullOrWhiteSpace(Year) &&
        string.IsNullOrWhiteSpace(Comment);

    public TagSettings Clone() => (TagSettings)MemberwiseClone();
}

/// <summary>録音設定一式(プリセット)。</summary>
public sealed class Preset
{
    /// <summary>入力デバイスの枠の数。</summary>
    public const int SourceSlots = 3;

    public string Name { get; set; } = "";

    public SourceSetting[] Sources { get; set; } = NewSources();

    public MixMode MixMode { get; set; } = MixMode.Mix;
    public double MasterGainDb { get; set; } = 0;

    public AudioFormat Format { get; set; } = AudioFormat.Flac;
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 2;
    public int BitrateKbps { get; set; } = 320;
    public int BitDepth { get; set; } = 16;

    public string SaveFolder { get; set; } = "";
    public bool UseDateFolder { get; set; } = true;
    public string DateFolderFormat { get; set; } = "yyyy-MM-dd";
    public string FileNameTemplate { get; set; } = "No.{No}-Track{Track:000}_{Start}-{End}({Length})";
    public TimeStyle TimeStyle { get; set; } = TimeStyle.Auto;

    public int AutoSplitMinutes { get; set; } = 0;
    public int MaxFileHours { get; set; } = 24;

    public bool SplitOnSilence { get; set; } = false;
    public double SilenceDb { get; set; } = -50;
    public double SilenceSeconds { get; set; } = 2;
    public double MinTrackSeconds { get; set; } = 10;

    public bool AutoStartOnSound { get; set; } = false;
    public double AutoStartDb { get; set; } = -45;
    public double AutoStopSilenceSeconds { get; set; } = 0;

    public bool AutoResume { get; set; } = true;
    public DeviceLostPolicy DeviceLostPolicy { get; set; } = DeviceLostPolicy.Continue;

    public TagSettings Tags { get; set; } = new();
    public bool WriteTrackTag { get; set; } = false;

    private static SourceSetting[] NewSources()
    {
        var a = new SourceSetting[SourceSlots];
        for (int i = 0; i < a.Length; i++) a[i] = new SourceSetting();
        return a;
    }

    public Preset Clone()
    {
        var c = (Preset)MemberwiseClone();
        c.Sources = new SourceSetting[SourceSlots];
        for (int i = 0; i < SourceSlots; i++)
            c.Sources[i] = (Sources != null && i < Sources.Length && Sources[i] != null) ? Sources[i].Clone() : new SourceSetting();
        c.Tags = (Tags ?? new TagSettings()).Clone();
        return c;
    }

    /// <summary>範囲外・不正な値を使える値に直す(設定ファイルを手で書き換えた場合など)。</summary>
    public void Normalize()
    {
        var src = Sources ?? Array.Empty<SourceSetting>();
        var fixedSources = new SourceSetting[SourceSlots];
        for (int i = 0; i < SourceSlots; i++)
        {
            var s = i < src.Length && src[i] != null ? src[i] : new SourceSetting();
            s.DeviceId ??= "";
            s.DeviceName ??= "";
            s.GainDb = ClampD(s.GainDb, -30, 30, 0);
            fixedSources[i] = s;
        }
        Sources = fixedSources;
        Tags ??= new TagSettings();
        Name = (Name ?? "").Trim();

        if (!Enum.IsDefined(Format)) Format = AudioFormat.Flac;
        if (!Enum.IsDefined(MixMode)) MixMode = MixMode.Mix;
        if (!Enum.IsDefined(DeviceLostPolicy)) DeviceLostPolicy = DeviceLostPolicy.Continue;
        if (!Enum.IsDefined(TimeStyle)) TimeStyle = TimeStyle.Auto;

        if (Channels != 1) Channels = 2;
        if (!AllowedRates(Format).Contains(SampleRate)) SampleRate = 48000;
        BitrateKbps = Math.Clamp(BitrateKbps, 32, 320);
        bool depthOk = BitDepth == 16 || BitDepth == 24 || (Format == AudioFormat.Wav && BitDepth == 32);
        if (!depthOk) BitDepth = 16;

        MasterGainDb = ClampD(MasterGainDb, -30, 30, 0);
        AutoSplitMinutes = Math.Clamp(AutoSplitMinutes, 0, 1440);
        MaxFileHours = Math.Clamp(MaxFileHours, 1, 168);
        SilenceDb = ClampD(SilenceDb, -100, 0, -50);
        SilenceSeconds = ClampD(SilenceSeconds, 0.1, 3600, 2);
        MinTrackSeconds = ClampD(MinTrackSeconds, 0, 86400, 10);
        AutoStartDb = ClampD(AutoStartDb, -100, 0, -45);
        AutoStopSilenceSeconds = ClampD(AutoStopSilenceSeconds, 0, 864000, 0);

        SaveFolder = (SaveFolder ?? "").Trim();
        if (string.IsNullOrWhiteSpace(DateFolderFormat)) DateFolderFormat = "yyyy-MM-dd";
        if (string.IsNullOrWhiteSpace(FileNameTemplate)) FileNameTemplate = new Preset().FileNameTemplate;
    }

    private static double ClampD(double v, double min, double max, double fallback)
        => double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Clamp(v, min, max);

    private static readonly int[] RatesAll = { 44100, 48000, 88200, 96000 };
    private static readonly int[] RatesMp3 = { 32000, 44100, 48000 };
    private static readonly int[] RatesOpus = { 48000 };

    private static int[] AllowedRates(AudioFormat f) => f switch
    {
        AudioFormat.Mp3 => RatesMp3,
        AudioFormat.Opus => RatesOpus,
        _ => RatesAll,
    };

    // ------------------------------------------------------------ ini

    public void WriteTo(IniSection s, bool comments)
    {
        string? C(string text) => comments ? text : null;

        for (int i = 0; i < SourceSlots; i++)
        {
            var src = Sources != null && i < Sources.Length && Sources[i] != null ? Sources[i] : new SourceSetting();
            int n = i + 1;
            s.Set($"Source{n}Device", src.DeviceId, i == 0 ? C("入力デバイス1～3。Device=デバイスID(空=未使用) / Name=表示名 / GainDb=入力ごとのゲイン") : null);
            s.Set($"Source{n}Name", src.DeviceName);
            s.Set($"Source{n}GainDb", src.GainDb);
        }
        s.Set("MixMode", MixMode, C("Mix=ミックスして1ファイル / Separate=入力ごとに別ファイル"));
        s.Set("MasterGainDb", MasterGainDb, C("録音ゲイン(dB)"));

        s.Set("Format", Format, C("Flac / Mp3 / M4a(AAC) / Aac(ADTS) / Wav / Opus"));
        s.Set("SampleRate", SampleRate, C("サンプリング周波数(Hz)"));
        s.Set("Channels", Channels, C("1=モノラル 2=ステレオ"));
        s.Set("BitrateKbps", BitrateKbps, C("ビットレート(kbps)。Mp3/M4a/Aac/Opusで有効"));
        s.Set("BitDepth", BitDepth, C("ビット深度。Flac/Wavで有効(Wavのみ32=float)"));

        s.Set("SaveFolder", SaveFolder, C("保存先フォルダ"));
        s.Set("UseDateFolder", UseDateFolder, C("保存先の中に日付フォルダを作る"));
        s.Set("DateFolderFormat", DateFolderFormat, C("日付フォルダの書式(例: yyyy-MM-dd)"));
        s.Set("FileNameTemplate", FileNameTemplate,
            C("ファイル名(拡張子は自動)。変数: {No} {Track:000} {Start} {End} {Length} {Date} {Time} {DateTime:書式} {Device} {Preset}。時間は {End:m}=125：30 形式 / {End:h}=時：分：秒 / {End:s}=秒数"));
        s.Set("TimeStyle", TimeStyle, C("{Start}{End}{Length}の既定の表記。Auto=36：25 / 1：05：30、TotalMinutes=125：30"));

        s.Set("AutoSplitMinutes", AutoSplitMinutes, C("この分数ごとに自動分割(0=しない)"));
        s.Set("MaxFileHours", MaxFileHours, C("1ファイルの最大時間(時間)。超えると強制的に分割"));
        s.Set("SplitOnSilence", SplitOnSilence, C("無音を検出して自動分割する"));
        s.Set("SilenceDb", SilenceDb);
        s.Set("SilenceSeconds", SilenceSeconds);
        s.Set("MinTrackSeconds", MinTrackSeconds, C("無音分割で、これより短いファイルは作らない(秒)"));
        s.Set("AutoStartOnSound", AutoStartOnSound, C("録音ボタンを押したあと、音が出たら録音を始める"));
        s.Set("AutoStartDb", AutoStartDb);
        s.Set("AutoStopSilenceSeconds", AutoStopSilenceSeconds, C("無音がこの秒数続いたら録音を止める(0=しない)"));

        s.Set("AutoResume", AutoResume, C("デバイスが切れたら自動で再接続を試みる"));
        s.Set("DeviceLostPolicy", DeviceLostPolicy, C("Continue=同じファイルで継続 / Wait=戻るまで待機 / SplitOnReturn=戻ったら分割"));

        s.Set("TagTitle", Tags.Title, C("タグ(日本語OK)。{No} {Track} などの変数も使えます"));
        s.Set("TagArtist", Tags.Artist);
        s.Set("TagAlbum", Tags.Album);
        s.Set("TagAlbumArtist", Tags.AlbumArtist);
        s.Set("TagGenre", Tags.Genre);
        s.Set("TagYear", Tags.Year);
        s.Set("TagComment", Tags.Comment);
        s.Set("WriteTrackTag", WriteTrackTag, C("分割した録音にトラック番号タグを付ける"));
    }

    public static Preset ReadFrom(IniSection s, string name)
    {
        var d = new Preset();
        var p = new Preset { Name = name };
        for (int i = 0; i < SourceSlots; i++)
        {
            int n = i + 1;
            p.Sources[i] = new SourceSetting
            {
                DeviceId = s.GetString($"Source{n}Device"),
                DeviceName = s.GetString($"Source{n}Name"),
                GainDb = s.GetDouble($"Source{n}GainDb", 0),
            };
        }
        p.MixMode = s.GetEnum("MixMode", d.MixMode);
        p.MasterGainDb = s.GetDouble("MasterGainDb", d.MasterGainDb);

        p.Format = s.GetEnum("Format", d.Format);
        p.SampleRate = s.GetInt("SampleRate", d.SampleRate);
        p.Channels = s.GetInt("Channels", d.Channels);
        p.BitrateKbps = s.GetInt("BitrateKbps", d.BitrateKbps);
        p.BitDepth = s.GetInt("BitDepth", d.BitDepth);

        p.SaveFolder = s.GetString("SaveFolder", d.SaveFolder);
        p.UseDateFolder = s.GetBool("UseDateFolder", d.UseDateFolder);
        p.DateFolderFormat = s.GetString("DateFolderFormat", d.DateFolderFormat);
        p.FileNameTemplate = s.GetString("FileNameTemplate", d.FileNameTemplate);
        p.TimeStyle = s.GetEnum("TimeStyle", d.TimeStyle);

        p.AutoSplitMinutes = s.GetInt("AutoSplitMinutes", d.AutoSplitMinutes);
        p.MaxFileHours = s.GetInt("MaxFileHours", d.MaxFileHours);
        p.SplitOnSilence = s.GetBool("SplitOnSilence", d.SplitOnSilence);
        p.SilenceDb = s.GetDouble("SilenceDb", d.SilenceDb);
        p.SilenceSeconds = s.GetDouble("SilenceSeconds", d.SilenceSeconds);
        p.MinTrackSeconds = s.GetDouble("MinTrackSeconds", d.MinTrackSeconds);
        p.AutoStartOnSound = s.GetBool("AutoStartOnSound", d.AutoStartOnSound);
        p.AutoStartDb = s.GetDouble("AutoStartDb", d.AutoStartDb);
        p.AutoStopSilenceSeconds = s.GetDouble("AutoStopSilenceSeconds", d.AutoStopSilenceSeconds);

        p.AutoResume = s.GetBool("AutoResume", d.AutoResume);
        p.DeviceLostPolicy = s.GetEnum("DeviceLostPolicy", d.DeviceLostPolicy);

        p.Tags = new TagSettings
        {
            Title = s.GetString("TagTitle"),
            Artist = s.GetString("TagArtist"),
            Album = s.GetString("TagAlbum"),
            AlbumArtist = s.GetString("TagAlbumArtist"),
            Genre = s.GetString("TagGenre"),
            Year = s.GetString("TagYear"),
            Comment = s.GetString("TagComment"),
        };
        p.WriteTrackTag = s.GetBool("WriteTrackTag", d.WriteTrackTag);
        p.Normalize();
        return p;
    }
}

/// <summary>プリセットのJSON書き出し/読み込み(バックアップや他のPCへの移行用)。</summary>
public static class PresetJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class Root
    {
        public string Format { get; set; } = "LokuOn.Presets";
        public int Version { get; set; } = 1;
        public List<Preset> Presets { get; set; } = new();
    }

    public static void Export(IEnumerable<Preset> presets, string path)
    {
        var root = new Root { Presets = presets.Select(p => p.Clone()).ToList() };
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        if (dir.Length > 0) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(root, Options), new System.Text.UTF8Encoding(false));
    }

    public static List<Preset> Import(string path)
    {
        string text = File.ReadAllText(path, System.Text.Encoding.UTF8);
        var list = new List<Preset>();
        using var doc = JsonDocument.Parse(text);
        JsonElement arr;
        if (doc.RootElement.ValueKind == JsonValueKind.Array) arr = doc.RootElement;
        else if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                 doc.RootElement.TryGetProperty("presets", out arr) || doc.RootElement.TryGetProperty("Presets", out arr)) { }
        else throw new InvalidDataException("プリセットのJSONとして読めません");

        foreach (var el in arr.EnumerateArray())
        {
            var p = el.Deserialize<Preset>(Options);
            if (p == null) continue;
            p.Normalize();
            list.Add(p);
        }
        return list;
    }
}
