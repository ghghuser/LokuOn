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

    public static List<DeviceInfo> Enumerate()
    {
        var list = new List<DeviceInfo>();
        try
        {
            using var en = new MMDeviceEnumerator();
            list.Add(new DeviceInfo { Id = "wasapi-loop:default", Kind = DeviceKind.WasapiLoopback, Display = "[PC内部音] 既定のスピーカー(出力デバイスに追従)" });
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (d)
                    list.Add(new DeviceInfo { Id = "wasapi-loop:" + d.ID, Kind = DeviceKind.WasapiLoopback, Display = "[PC内部音] " + d.FriendlyName });
            }
            list.Add(new DeviceInfo { Id = "wasapi-cap:default", Kind = DeviceKind.WasapiCapture, Display = "[入力] 既定のマイク(入力デバイスに追従)" });
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (d)
                    list.Add(new DeviceInfo { Id = "wasapi-cap:" + d.ID, Kind = DeviceKind.WasapiCapture, Display = "[入力] " + d.FriendlyName });
            }
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

    public static bool IsAsio(string id) => id.StartsWith("asio:", StringComparison.OrdinalIgnoreCase);
}
