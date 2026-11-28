using System.Reflection;

namespace LokuOn.Core;

/// <summary>
/// 画面の文言(キー=文)。exe に埋め込んだ Lang/ja.ini を既定とし、exe と同じ場所の Lang\ja.ini があればキーごとに上書きする。
/// キーが無いときは「キー名そのもの」を返す(落とさない。画面にキー名が出たら漏れと分かる)。
/// </summary>
public static class Loc
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? table;

    /// <summary>いま使っている言語("ja" / "en")。</summary>
    public static string Language { get; private set; } = "ja";

    /// <summary>キーが定義されているか(確認用)。</summary>
    public static bool Has(string key) { Ensure(); return table!.ContainsKey(key); }

    /// <summary>読み込み済みの文言の数(確認用)。</summary>
    public static int Count { get { Ensure(); return table!.Count; } }

    /// <summary>文言を返す。引数なしなら {…} もそのまま(書式として扱わない)。</summary>
    public static string Get(string key)
    {
        Ensure();
        return table!.TryGetValue(key, out var v) ? v : key;
    }

    /// <summary>{0} {1} に値を入れた文言を返す。書式が壊れているときは、入れずに元の文を返す。</summary>
    public static string Get(string key, params object?[] args)
    {
        string t = Get(key);
        if (args.Length == 0) return t;
        try { return string.Format(t, args); }
        catch (FormatException) { return t; }
    }

    /// <summary>
    /// 起動時に呼ぶ(呼ばなくても最初の Get で日本語を読み込む)。読み込み順: 埋め込みの ja.ini(全キー) → 言語が ja 以外なら埋め込みのその言語
    /// (定義の無いキーは日本語のまま) → exe と同じ場所の Lang\&lt;言語&gt;.ini(キーごとに上書き)。外部ファイルの場所は引数で指定できる。
    /// </summary>
    public static void Init(string? externalPath = null, string language = "ja")
    {
        lock (Gate)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            try { Merge(d, ReadEmbedded("ja")); } catch { /* 埋め込みが読めなくてもキー名表示で動く */ }
            if (language != "ja") { try { Merge(d, ReadEmbedded(language)); } catch { /* 無い言語は日本語のまま */ } }
            Language = language;
            string path = externalPath ?? Path.Combine(AppContext.BaseDirectory, "Lang", language + ".ini");
            try { if (File.Exists(path)) Merge(d, File.ReadAllText(path)); } catch { /* 外部ファイルが壊れていても埋め込みで動く */ }
            table = d;
        }
    }

    private static void Ensure()
    {
        if (table != null) return;
        lock (Gate) { if (table == null) Init(); }
    }

    /// <summary>埋め込みの Lang/&lt;言語&gt;.ini の本文(テストからも使う)。無ければ例外。</summary>
    public static string ReadEmbedded(string language)
    {
        var asm = typeof(Loc).Assembly;
        string name = asm.GetManifestResourceNames().First(n => n.EndsWith("Lang." + language + ".ini", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var r = new StreamReader(s, System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static void Merge(Dictionary<string, string> into, string text)
    {
        foreach (var (k, v) in Parse(text, null)) into[k] = v;
    }

    /// <summary>ini の文を「キー, 値」の並びにする。重複したキーは duplicates へ(自己テスト用)。</summary>
    public static List<KeyValuePair<string, string>> Parse(string text, List<string>? duplicates)
    {
        var list = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimStart('﻿');
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            if (!seen.Add(key)) duplicates?.Add(key);
            list.Add(new(key, Unescape(line[(eq + 1)..])));
        }
        return list;
    }

    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                if (n == 'n') { sb.Append('\n'); i++; continue; }
                if (n == '\\') { sb.Append('\\'); i++; continue; }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
