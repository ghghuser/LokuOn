using System.IO.Compression;

namespace LokuOn.Core;

/// <summary>設定のバックアップ。settings.ini と Presets フォルダ(*.json)を1つのZIPにまとめる(復元は、ZIPを展開して同じ場所へ上書きコピー)。</summary>
public static class SettingsBackup
{
    /// <summary>ZIPを作り、入れたファイル数を返す。settings.ini が無く Presets も空なら 0。</summary>
    public static int Create(string settingsPath, string zipPath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? ".";
        string tmp = zipPath + ".tmp";
        int count = 0;
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                if (File.Exists(settingsPath)) { AddShared(zip, settingsPath, Path.GetFileName(settingsPath)); count++; }
                string presets = Path.Combine(dir, "Presets");
                if (Directory.Exists(presets))
                    foreach (var f in Directory.GetFiles(presets, "*.json"))
                    {
                        AddShared(zip, f, "Presets/" + Path.GetFileName(f));
                        count++;
                    }
            }
            File.Move(tmp, zipPath, true);       // 途中で失敗しても、既存のZIPを壊さない
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 後始末の失敗は無視 */ } }
        return count;
    }

    // 録音中などで他から開かれていても読めるよう、共有を許可して読む
    private static void AddShared(ZipArchive zip, string path, string entryName)
    {
        using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var dst = entry.Open();
        src.CopyTo(dst);
    }
}
