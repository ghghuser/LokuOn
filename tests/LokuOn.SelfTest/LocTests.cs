using System.Text.RegularExpressions;
using LokuOn.Core;

namespace LokuOn.SelfTest;

/// <summary>文言(Lang/ja.ini)の仕組みと、ソースが使うキーの整合を確かめる。</summary>
static partial class LocTests
{
    public static void Run(string tmp, CheckFn check, Action<string> section)
    {
        section("文言(Loc / Lang\\ja.ini)");

        // 解析・エスケープ・書式
        var dup = new List<string>();
        var p = Loc.Parse("; コメント\n[sec]\r\na.b=一行目\\n二行目\\\\\na.b=重複\n#無視\nc=値=等号入り\n", dup);
        check(p.Count == 3 && p[0].Value == "一行目\n二行目\\", "改行・バックスラッシュのエスケープ", p.Count.ToString());
        check(p[2].Key == "c" && p[2].Value == "値=等号入り", "値の中の = はそのまま");
        check(dup.Count == 1 && dup[0] == "a.b", "重複キーを検出");

        // 外部ファイルが優先される(キー単位)。壊れた書式は落ちない
        string ext = Path.Combine(tmp, "loc-ext.ini");
        File.WriteAllText(ext, "info.btn.close=CLOSE\ninfo.version=壊れた {0 書式\n");
        Loc.Init(ext);
        check(Loc.Get("info.btn.close") == "CLOSE", "外部ファイルのキーが優先");
        check(Loc.Get("info.btn.github") == "GitHub を開く", "外部に無いキーは埋め込みの文言");
        check(Loc.Get("info.version", "1.0.0.0") == "壊れた {0 書式", "書式が壊れていても落ちず元の文を返す");
        check(Loc.Get("no.such.key") == "no.such.key", "キーが無いときはキー名を返す");
        Loc.Init(Path.Combine(tmp, "loc-none.ini"));
        check(Loc.Get("info.btn.close") == "閉じる", "外部ファイルが無いときは埋め込みだけ");
        check(Loc.Get("info.version", "1.2.3.4") == "LokuOn  バージョン 1.2.3.4", "引数の埋め込み");

        // ja.ini 自体の検査(リポジトリのソースが見つかるときだけ)
        string? root = FindRoot();
        if (root == null) { Console.WriteLine("  (リポジトリのソースが見つからないため、キー検査はスキップ)"); return; }
        string iniPath = Path.Combine(root, "src", "LokuOn.Core", "Lang", "ja.ini");
        var dups = new List<string>();
        var entries = Loc.Parse(File.ReadAllText(iniPath), dups);
        check(dups.Count == 0, "ja.ini にキーの重複がない", string.Join(",", dups));
        check(entries.All(e => e.Value.Length > 0), "空の文言がない");
        // 文体: 体言止め中心。です・ます調と命令形(してください)を残さない
        var polite = entries.Where(e => Polite().IsMatch(e.Value)).Select(e => e.Key).ToList();
        check(polite.Count == 0, "文言が です・ます調になっていない", string.Join(",", polite));
        var badPh = entries.Where(e => !PlaceholdersOk(e.Value)).Select(e => e.Key).ToList();
        check(badPh.Count == 0, "{0}{1} が 0 から連続している", string.Join(",", badPh));

        // ソースが使うキー(Loc.Get の行の中のキー形式の文字列、XAML の {ui:T キー})
        var used = new HashSet<string>();
        var keyLit = KeyLiteral();
        var xamlKey = XamlKey();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (var line in File.ReadLines(f))
            {
                if (f.EndsWith(".xaml")) foreach (Match m in xamlKey.Matches(line)) used.Add(m.Groups[1].Value);
                else if (line.Contains("Loc.Get(")) foreach (Match m in keyLit.Matches(line)) used.Add(m.Groups[1].Value);
            }
        }
        var have = entries.Select(e => e.Key).ToHashSet();
        var missing = used.Where(k => !have.Contains(k)).OrderBy(k => k).ToList();
        check(missing.Count == 0, $"使われているキー {used.Count} 個がすべて ja.ini にある", string.Join(",", missing));
        var unused = have.Where(k => !used.Contains(k) && !k.StartsWith("eq.preset.", StringComparison.Ordinal)).OrderBy(k => k).ToList();
        check(unused.Count == 0, "ja.ini に未使用のキーがない", string.Join(",", unused));

        // 英語(en.ini): ja にあるキーだけを持ち、ja の全キーを網羅し、{n} の組が同じで、日本語が混ざらない
        var enDup = new List<string>();
        var en = Loc.Parse(Loc.ReadEmbedded("en"), enDup);
        var jaMap = entries.ToDictionary(e => e.Key, e => e.Value);
        var enMap = en.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.Last().Value);
        check(enDup.Count == 0, "en.ini にキーの重複がない", string.Join(",", enDup));
        var enExtra = enMap.Keys.Where(k => !jaMap.ContainsKey(k)).OrderBy(k => k).ToList();
        check(enExtra.Count == 0, "en.ini のキーがすべて ja.ini にもある", string.Join(",", enExtra));
        var enMissing = jaMap.Keys.Where(k => !enMap.ContainsKey(k)).OrderBy(k => k).ToList();
        check(enMissing.Count == 0, $"ja.ini の全キーが英訳されている({jaMap.Count} 個)", string.Join(",", enMissing.Take(12)) + (enMissing.Count > 12 ? $" …ほか{enMissing.Count - 12}" : ""));
        var enPh = enMap.Where(kv => jaMap.TryGetValue(kv.Key, out var jv) && PlaceholderSet(jv) != PlaceholderSet(kv.Value)).Select(kv => kv.Key).ToList();
        check(enPh.Count == 0, "英訳の {n} が日本語と同じ組", string.Join(",", enPh));
        var enJp = enMap.Where(kv => kv.Value.Any(ch => ch >= '\u3040' && ch <= '\u9FFF')).Select(kv => kv.Key).ToList();
        check(enJp.Count == 0, "英訳に日本語が混ざっていない", string.Join(",", enJp.Take(12)));
        check(enMap.TryGetValue("ui.25", out var u25) && enMap.TryGetValue("tray.08", out var t08) && u25.StartsWith(t08, StringComparison.Ordinal)
              && jaMap["ui.25"].StartsWith(jaMap["tray.08"], StringComparison.Ordinal), "トレイの録音判定(tray.08)が録音中の表示文(ui.25)の先頭と一致(日英とも)");
    }

    [GeneratedRegex("\"([a-z][a-z0-9]*(?:\\.[a-z0-9_]+)+)\"", RegexOptions.None, 2000)]
    private static partial Regex KeyLiteral();

    [GeneratedRegex(@"\{ui:T\s+([^\s}]+)\}", RegexOptions.None, 2000)]
    private static partial Regex XamlKey();

    [GeneratedRegex("(です|ます|ません|ました|でした|ください|ましょう|でしょう)", RegexOptions.None, 2000)]
    private static partial Regex Polite();

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}", RegexOptions.None, 2000)]
    private static partial Regex Placeholder();

    private static string PlaceholderSet(string v)
        => string.Join(",", Placeholder().Matches(v).Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(x => x));

    private static bool PlaceholdersOk(string v)
    {
        var idx = Placeholder().Matches(v).Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(x => x).ToList();
        return idx.Select((x, i) => x == i).All(b => b);
    }

    private static string? FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "PROJECT.md")) && Directory.Exists(Path.Combine(d.FullName, "src"))) return d.FullName;
        return null;
    }
}
