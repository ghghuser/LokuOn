namespace LokuOn.Core;

/// <summary>いま開いている入力が、これから録音/入力確認を始めるプリセットでそのまま使えるか(開き直さずに引き継げるか)を決める。</summary>
public static class SourceReuse
{
    /// <summary>開いている入力1本の情報。</summary>
    public readonly record struct OpenSource(int Slot, string DeviceId, int SampleRate, int Channels, bool Connected);

    /// <summary>
    /// 引き継げる条件: 使う入力の枠・デバイス・サンプルレート・チャンネル数がすべて同じで、どの入力もつながっている。
    /// つながっていない入力があるときは、従来どおり開き直す(最初から接続をやり直すため)。
    /// </summary>
    public static bool CanReuse(IReadOnlyList<OpenSource> open, Preset p)
    {
        if (open.Count == 0) return false;
        int wanted = 0;
        for (int i = 0; i < p.Sources.Length; i++)
        {
            var s = p.Sources[i];
            if (string.IsNullOrWhiteSpace(s.DeviceId)) continue;
            wanted++;
            bool found = false;
            foreach (var o in open)
                if (o.Slot == i) { found = o.DeviceId == s.DeviceId; break; }
            if (!found) return false;
        }
        if (wanted != open.Count) return false;
        foreach (var o in open)
            if (!o.Connected || o.SampleRate != p.SampleRate || o.Channels != p.Channels) return false;
        return true;
    }
}
