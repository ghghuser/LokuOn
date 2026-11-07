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
    Ogg = 6,
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

/// <summary>出力デバイス1つ(画面では「カード」1枚)の設定。</summary>
public sealed class OutputSetting
{
    public const int NameMax = 12;
    public string Name { get; set; } = "";
    /// <summary>出力先の再生デバイスID(wasapi-out:...)。空=未選択。</summary>
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    /// <summary>この出力の全体音量(dB)。</summary>
    public double MasterGainDb { get; set; } = 0;
    /// <summary>入力ごと(画面の入力1〜4)に、この出力へ送るか。</summary>
    public bool[] SendOn { get; set; } = { true, true, true, true };
    /// <summary>入力ごとの、この出力への送りの音量(dB)。</summary>
    public double[] SendGainDb { get; set; } = new double[Preset.SourceSlots];
    /// <summary>入力の一覧を畳んでいるか(手動のみ)。</summary>
    public bool Collapsed { get; set; } = false;

    public static string DefaultName(int index) => "ミックス" + (index + 1);

    public OutputSetting Clone()
    {
        var c = (OutputSetting)MemberwiseClone();
        c.SendOn = (bool[])(SendOn ?? new[] { true, true, true, true }).Clone();
        c.SendGainDb = (double[])(SendGainDb ?? new double[Preset.SourceSlots]).Clone();
        return c;
    }

    public void Normalize(int index)
    {
        DeviceId ??= ""; DeviceName ??= "";
        Name = (Name ?? "").Trim();
        if (Name.Length == 0) Name = DefaultName(index);
        if (Name.Length > NameMax) Name = Name.Substring(0, NameMax);
        MasterGainDb = double.IsFinite(MasterGainDb) ? Math.Clamp(MasterGainDb, Preset.GainMinDb, Preset.GainMaxDb) : 0;
        var on = new bool[Preset.SourceSlots]; var g = new double[Preset.SourceSlots];
        for (int i = 0; i < Preset.SourceSlots; i++)
        {
            on[i] = SendOn != null && i < SendOn.Length ? SendOn[i] : true;
            double v = SendGainDb != null && i < SendGainDb.Length ? SendGainDb[i] : 0;
            g[i] = double.IsFinite(v) ? Math.Clamp(v, Preset.GainMinDb, Preset.GainMaxDb) : 0;
        }
        SendOn = on; SendGainDb = g;
    }
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
    public const int SourceSlots = 4;
    /// <summary>入力ゲインの範囲(dB)。</summary>
    public const double GainMinDb = -30, GainMaxDb = 20;

    public string Name { get; set; } = "";

    public SourceSetting[] Sources { get; set; } = NewSources();

    /// <summary>出力デバイスの最大数。</summary>
    public const int MaxOutputs = 4;
    /// <summary>出力(カード)。最初は1枚。旧プリセットにはない項目なので、無ければ1枚になる。</summary>
    public List<OutputSetting> Outputs { get; set; } = new() { new OutputSetting { Name = OutputSetting.DefaultName(0) } };
    /// <summary>入力1〜4の表示名(空=既定の「入力1〜4」)。画面・出力ウィンドウで共通に使う。</summary>
    public string[] InputNames { get; set; } = new string[SourceSlots] { "", "", "", "" };

    public string InputName(int slot)
        => InputNames != null && (uint)slot < (uint)InputNames.Length && !string.IsNullOrWhiteSpace(InputNames[slot]) ? InputNames[slot] : "入力" + (slot + 1);

    public MixMode MixMode { get; set; } = MixMode.Mix;
    public double MasterGainDb { get; set; } = 0;

    public AudioFormat Format { get; set; } = AudioFormat.Flac;
    public int SampleRate { get; set; } = 48000;
    public int Channels { get; set; } = 2;
    public int BitrateKbps { get; set; } = 320;
    public int BitDepth { get; set; } = 16;
    /// <summary>圧縮率(FLAC 0-12 / MP3 0-9 / Opus 0-10)。-1=形式ごとの標準。Vorbisには設定がない。</summary>
    public int CompressionLevel { get; set; } = -1;

    /// <summary>保存先。プリセットには含めない(画面の現在の設定と settings.ini にだけ保存)。</summary>
    [JsonIgnore]
    public string SaveFolder { get; set; } = "";
    public bool UseDateFolder { get; set; } = true;
    public string DateFolderFormat { get; set; } = "{yyyy}-{MM}-{dd}";
    public string FileNameTemplate { get; set; } = DefaultTemplate;
    public const string DefaultTemplate = "{Date} {Time} (No.{No}).{ext}";
    public const string TrackTemplate = "No.{No}-Track{Track:000}_{Start}-{End}({Length}).{ext}";
    public static string DefaultSaveFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LokuOn", "Rec");
    public TimeStyle TimeStyle { get; set; } = TimeStyle.Auto;

    /// <summary>自動分割の間隔(秒)。0=しない。</summary>
    public int AutoSplitSeconds { get; set; } = 0;
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
        // 初期値は「既定の入力(マイク)」。前回の選択がある場合は設定ファイルのほうが優先される
        a[0].DeviceId = "wasapi-cap:default";
        a[0].DeviceName = "🎤 既定のマイク(Windowsの既定に追従)";
        return a;
    }

    public Preset Clone()
    {
        var c = (Preset)MemberwiseClone();
        c.Sources = new SourceSetting[SourceSlots];
        for (int i = 0; i < SourceSlots; i++)
            c.Sources[i] = (Sources != null && i < Sources.Length && Sources[i] != null) ? Sources[i].Clone() : new SourceSetting();
        c.Tags = (Tags ?? new TagSettings()).Clone();
        c.Outputs = (Outputs ?? new List<OutputSetting>()).Select(o => (o ?? new OutputSetting()).Clone()).ToList();
        c.InputNames = (string[])(InputNames ?? new string[SourceSlots]).Clone();
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
            s.GainDb = ClampD(s.GainDb, GainMinDb, GainMaxDb, 0);
            fixedSources[i] = s;
        }
        Sources = fixedSources;
        Tags ??= new TagSettings();
        Name = (Name ?? "").Trim();
        Outputs = (Outputs ?? new List<OutputSetting>()).Where(o => o != null).Take(MaxOutputs).ToList();
        for (int i = 0; i < Outputs.Count; i++) Outputs[i].Normalize(i);
        var names = new string[SourceSlots];
        for (int i = 0; i < SourceSlots; i++)
        {
            string n = InputNames != null && i < InputNames.Length ? (InputNames[i] ?? "").Trim() : "";
            names[i] = n.Length > OutputSetting.NameMax ? n.Substring(0, OutputSetting.NameMax) : n;
        }
        InputNames = names;

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
        AutoSplitSeconds = Math.Clamp(AutoSplitSeconds, 0, 86400);
        CompressionLevel = Math.Clamp(CompressionLevel, -1, MaxCompression(Format));
        MaxFileHours = Math.Clamp(MaxFileHours, 1, 168);
        SilenceDb = ClampD(SilenceDb, -100, 0, -50);
        SilenceSeconds = ClampD(SilenceSeconds, 0.1, 3600, 2);
        MinTrackSeconds = ClampD(MinTrackSeconds, 0, 86400, 10);
        AutoStartDb = ClampD(AutoStartDb, -100, 0, -45);
        AutoStopSilenceSeconds = ClampD(AutoStopSilenceSeconds, 0, 864000, 0);

        SaveFolder = (SaveFolder ?? "").Trim();
        if (SaveFolder.Length == 0) SaveFolder = DefaultSaveFolder;
        if (string.IsNullOrWhiteSpace(DateFolderFormat)) DateFolderFormat = "{yyyy}-{MM}-{dd}";
        if (string.IsNullOrWhiteSpace(FileNameTemplate)) FileNameTemplate = new Preset().FileNameTemplate;
    }

    /// <summary>圧縮率の最大値(0=設定なし)。</summary>
    public static int MaxCompression(AudioFormat f) => f switch
    {
        AudioFormat.Flac => 12,
        AudioFormat.Mp3 => 9,
        AudioFormat.Opus => 10,
        _ => 0,
    };

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

    public void WriteTo(IniSection s, bool comments, bool includeSaveFolder = false)
    {
        string? C(string text) => comments ? text : null;

        for (int i = 0; i < SourceSlots; i++)
        {
            var src = Sources != null && i < Sources.Length && Sources[i] != null ? Sources[i] : new SourceSetting();
            int n = i + 1;
            s.Set($"Source{n}Device", src.DeviceId, i == 0 ? C("入力デバイス1～4。Device=デバイスID(空=未使用) / Name=表示名 / GainDb=入力ごとのゲイン") : null);
            s.Set($"Source{n}Name", src.DeviceName);
            s.Set($"Source{n}GainDb", src.GainDb);
        }
        s.Set("OutputCount", Outputs.Count, C("出力デバイス(カード)の数 0〜4。出力は画面の「出力」ウィンドウで設定"));
        for (int i = 0; i < Outputs.Count; i++)
        {
            var o = Outputs[i]; int n = i + 1;
            s.Set($"Output{n}Name", o.Name);
            s.Set($"Output{n}Device", o.DeviceId);
            s.Set($"Output{n}DeviceName", o.DeviceName);
            s.Set($"Output{n}MasterDb", o.MasterGainDb);
            s.Set($"Output{n}SendOn", string.Join(",", o.SendOn.Select(b => b ? "1" : "0")), i == 0 ? C("入力1〜4を送るか(1/0)") : null);
            s.Set($"Output{n}SendDb", string.Join(",", o.SendGainDb.Select(v => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))), i == 0 ? C("入力1〜4の送りの音量(dB)") : null);
            s.Set($"Output{n}Collapsed", o.Collapsed);
        }
        for (int i = 0; i < SourceSlots; i++)
            s.Set($"Input{i + 1}Label", InputNames != null && i < InputNames.Length ? InputNames[i] : "", i == 0 ? C("入力1〜4の表示名(空=既定の「入力1〜4」)") : null);
        s.Set("MixMode", MixMode, C("Mix=ミックスして1ファイル / Separate=入力ごとに別ファイル"));
        s.Set("MasterGainDb", MasterGainDb, C("録音ゲイン(dB)"));

        s.Set("Format", Format, C("Flac / Mp3 / M4a(AAC) / Aac(ADTS) / Wav / Ogg(Vorbis) / Opus"));
        s.Set("SampleRate", SampleRate, C("サンプリング周波数(Hz)"));
        s.Set("Channels", Channels, C("1=モノラル 2=ステレオ"));
        s.Set("BitrateKbps", BitrateKbps, C("ビットレート(kbps)。Mp3/M4a/Aac/Opusで有効"));
        s.Set("CompressionLevel", CompressionLevel, C("圧縮率。-1=標準。Flac 0-12 / Mp3 0-9 / Opus 0-10(大きいほどよく圧縮、音質は変わらないものが多い)"));
        s.Set("BitDepth", BitDepth, C("ビット深度。Flac/Wavで有効(Wavのみ32=float)"));

        if (includeSaveFolder) s.Set("SaveFolder", SaveFolder, C("保存先フォルダ(プリセットには含まれません)"));
        s.Set("UseDateFolder", UseDateFolder, C("保存先の中に日付フォルダを作る"));
        s.Set("DateFolderFormat", DateFolderFormat, C("日付フォルダ名({yyyy}-{MM}-{dd} など。録音を始めた日で固定)"));
        s.Set("FileNameTemplate", FileNameTemplate,
            C("ファイル名。{ext}を書かなくても拡張子は自動で付く。変数: {No} {Track:000} {Start} {End} {Length} {Date} {Time} {yyyy} {yy} {MM} {dd} {HH} {mm} {ss} {DateTime:書式} {Device} {Preset} {ext}。時間は {End:m}=125：30 形式 / {End:h}=時：分：秒 / {End:s}=秒数"));
        s.Set("TimeStyle", TimeStyle, C("{Start}{End}{Length}の既定の表記。Auto=36：25 / 1：05：30、TotalMinutes=125：30"));

        s.Set("AutoSplitSeconds", AutoSplitSeconds, C("この秒数ごとに自動分割(0=しない。画面では 時:分:秒 で指定)"));
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
        int outCount = Math.Clamp(s.GetInt("OutputCount", 1), 0, MaxOutputs);
        p.Outputs = new List<OutputSetting>();
        for (int i = 0; i < outCount; i++)
        {
            int n = i + 1;
            var o = new OutputSetting
            {
                Name = s.GetString($"Output{n}Name"),
                DeviceId = s.GetString($"Output{n}Device"),
                DeviceName = s.GetString($"Output{n}DeviceName"),
                MasterGainDb = s.GetDouble($"Output{n}MasterDb", 0),
                Collapsed = s.GetBool($"Output{n}Collapsed", false),
            };
            var on = s.GetString($"Output{n}SendOn").Split(',', StringSplitOptions.TrimEntries);
            for (int k = 0; k < SourceSlots; k++) o.SendOn[k] = k < on.Length && on[0].Length > 0 ? on[k] != "0" : true;
            var gs = s.GetString($"Output{n}SendDb").Split(',', StringSplitOptions.TrimEntries);
            for (int k = 0; k < SourceSlots; k++)
                o.SendGainDb[k] = k < gs.Length && double.TryParse(gs[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gv) ? gv : 0;
            p.Outputs.Add(o);
        }
        for (int i = 0; i < SourceSlots; i++) p.InputNames[i] = s.GetString($"Input{i + 1}Label");
        p.MixMode = s.GetEnum("MixMode", d.MixMode);
        p.MasterGainDb = s.GetDouble("MasterGainDb", d.MasterGainDb);

        p.Format = s.GetEnum("Format", d.Format);
        p.SampleRate = s.GetInt("SampleRate", d.SampleRate);
        p.Channels = s.GetInt("Channels", d.Channels);
        p.BitrateKbps = s.GetInt("BitrateKbps", d.BitrateKbps);
        p.BitDepth = s.GetInt("BitDepth", d.BitDepth);
        p.CompressionLevel = s.GetInt("CompressionLevel", d.CompressionLevel);

        p.SaveFolder = s.GetString("SaveFolder", d.SaveFolder);
        p.UseDateFolder = s.GetBool("UseDateFolder", d.UseDateFolder);
        p.DateFolderFormat = s.GetString("DateFolderFormat", d.DateFolderFormat);
        p.FileNameTemplate = s.GetString("FileNameTemplate", d.FileNameTemplate);
        p.TimeStyle = s.GetEnum("TimeStyle", d.TimeStyle);

        p.AutoSplitSeconds = s.GetInt("AutoSplitSeconds", s.GetInt("AutoSplitMinutes", 0) * 60);
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
