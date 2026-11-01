using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LokuOn.Audio;

public enum DeviceKind { WasapiLoopback, WasapiCapture, Asio }

/// <summary>画面のプルダウンに出す入力デバイス1件。</summary>
public sealed class DeviceInfo
{
    /// <summary>保存用ID。 wasapi-loop:default / wasapi-loop:{エンドポイントID} / wasapi-cap:... / asio:{ドライバ名}|{先頭チャンネル}</summary>
    public string Id { get; init; } = "";
    public string Display { get; init; } = "";
    public DeviceKind Kind { get; init; }
    public override string ToString() => Display;
}

public static class DeviceCatalog
{
    public const string None = "";

    public const string MicIcon = "🎤";
    public const string SpeakerIcon = "🔊";
    /// <summary>Windows の既定デバイスの名前の末尾に付ける印。</summary>
    public const string DefaultMark = "＊";

    /// <summary>
    /// 一覧の並び: 🎤マイク(名前順) → 🔊スピーカー=PC内部音(名前順) → ASIO。
    /// 各グループの先頭は「既定のデバイスに追従」。Windows の既定のデバイス自体には名前の末尾に「＊」を付ける。
    /// </summary>
    public static List<DeviceInfo> Enumerate()
    {
        var list = new List<DeviceInfo>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string defCap = DefaultId(en, DataFlow.Capture);
            string defRen = DefaultId(en, DataFlow.Render);

            list.Add(new DeviceInfo { Id = "wasapi-cap:default", Kind = DeviceKind.WasapiCapture, Display = $"{MicIcon} 既定のマイク(Windowsの既定に追従)" });
            foreach (var (id, name) in Endpoints(en, DataFlow.Capture))
                list.Add(new DeviceInfo { Id = "wasapi-cap:" + id, Kind = DeviceKind.WasapiCapture, Display = $"{MicIcon} {name}{(id == defCap ? DefaultMark : "")}" });

            list.Add(new DeviceInfo { Id = "wasapi-loop:default", Kind = DeviceKind.WasapiLoopback, Display = $"{SpeakerIcon} 既定のスピーカー=PC内部音(Windowsの既定に追従)" });
            foreach (var (id, name) in Endpoints(en, DataFlow.Render))
                list.Add(new DeviceInfo { Id = "wasapi-loop:" + id, Kind = DeviceKind.WasapiLoopback, Display = $"{SpeakerIcon} {name}{(id == defRen ? DefaultMark : "")}" });
        }
        catch { /* オーディオサービスが使えない環境では空のまま */ }

        try
        {
            if (AsioOut.isSupported())
            {
                foreach (var name in AsioOut.GetDriverNames())
                    for (int ch = 0; ch < 8; ch += 2)
                        list.Add(new DeviceInfo
                        {
                            Id = $"asio:{name}|{ch}",
                            Kind = DeviceKind.Asio,
                            Display = $"[ASIO] {name}  入力 {ch + 1}-{ch + 2}ch",
                        });
            }
        }
        catch { /* ASIOドライバが無い環境 */ }
        return list;
    }

    private static string DefaultId(MMDeviceEnumerator en, DataFlow flow)
    {
        try { using var d = en.GetDefaultAudioEndpoint(flow, Role.Multimedia); return d.ID; }
        catch { return ""; }
    }

    private static List<(string Id, string Name)> Endpoints(MMDeviceEnumerator en, DataFlow flow)
    {
        var r = new List<(string, string)>();
        foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (d) r.Add((d.ID, d.FriendlyName));
        }
        r.Sort((x, y) => string.Compare(x.Item2, y.Item2, StringComparison.CurrentCultureIgnoreCase));
        return r;
    }

    public static bool IsAsio(string id) => id.StartsWith("asio:", StringComparison.OrdinalIgnoreCase);
}
