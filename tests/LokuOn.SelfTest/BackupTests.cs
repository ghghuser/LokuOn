using System.IO.Compression;
using LokuOn.Core;

namespace LokuOn.SelfTest;

/// <summary>設定バックアップ(settings.ini と Presets のZIP化)の確認。</summary>
static class BackupTests
{
    public static void Run(string tmp, CheckFn check, Action<string> section)
    {
        section("設定バックアップ");
        string dir = Path.Combine(tmp, "backup-test");
        Directory.CreateDirectory(Path.Combine(dir, "Presets"));
        string ini = Path.Combine(dir, "settings.ini");
        File.WriteAllText(ini, "[App]\nTheme=dark\n");
        File.WriteAllText(Path.Combine(dir, "Presets", "日本語.json"), "{\"a\":1}");
        File.WriteAllText(Path.Combine(dir, "Presets", "memo.txt"), "対象外");
        string zip = Path.Combine(dir, "out.zip");

        int n = SettingsBackup.Create(ini, zip);
        check(n == 2, "settings.ini と Presets の *.json だけを入れる", n.ToString());
        using (var z = ZipFile.OpenRead(zip))
        {
            var names = z.Entries.Select(e => e.FullName).OrderBy(x => x).ToArray();
            check(names.SequenceEqual(new[] { "Presets/日本語.json", "settings.ini" }), "ZIPの中身(日本語名を含む)", string.Join(",", names));
            using var r = new StreamReader(z.GetEntry("settings.ini")!.Open());
            check(r.ReadToEnd() == "[App]\nTheme=dark\n", "内容が一致");
        }
        check(!File.Exists(zip + ".tmp"), "一時ファイルが残らない");

        int empty = SettingsBackup.Create(Path.Combine(dir, "none", "settings.ini"), Path.Combine(dir, "empty.zip"));
        check(empty == 0, "何も無いときは0件", empty.ToString());
    }
}
