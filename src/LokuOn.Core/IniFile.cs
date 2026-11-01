using System.Globalization;
using System.Text;

namespace LokuOn.Core;

/// <summary>1つのセクション([名前]の下のキー=値の集まり)。</summary>
public sealed class IniSection
{
    private readonly List<string> order = new();
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> comments = new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; }
    public IniSection(string name) { Name = name; }

    public IEnumerable<string> Keys => order;

    public void Set(string key, string value, string? comment = null)
    {
        if (!values.ContainsKey(key)) order.Add(key);
        values[key] = value.Replace("\r", " ").Replace("\n", " ");
        if (comment != null) comments[key] = comment;
    }

    public void Set(string key, int value, string? comment = null) => Set(key, value.ToString(CultureInfo.InvariantCulture), comment);
    public void Set(string key, double value, string? comment = null) => Set(key, value.ToString("0.###", CultureInfo.InvariantCulture), comment);
    public void Set(string key, bool value, string? comment = null) => Set(key, value ? "true" : "false", comment);
    public void Set<T>(string key, T value, string? comment = null) where T : struct, Enum => Set(key, value.ToString(), comment);

    public string? Raw(string key) => values.TryGetValue(key, out var v) ? v : null;
    public string GetString(string key, string def = "") => values.TryGetValue(key, out var v) ? v : def;

    public int GetInt(string key, int def)
        => values.TryGetValue(key, out var v) && int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : def;

    public double GetDouble(string key, double def)
        => values.TryGetValue(key, out var v) && double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : def;

    public bool GetBool(string key, bool def)
    {
        if (!values.TryGetValue(key, out var v)) return def;
        switch (v.Trim().ToLowerInvariant())
        {
            case "true": case "1": case "yes": case "on": return true;
            case "false": case "0": case "no": case "off": return false;
            default: return def;
        }
    }

    public T GetEnum<T>(string key, T def) where T : struct, Enum
        => values.TryGetValue(key, out var v) && Enum.TryParse<T>(v.Trim(), true, out var r) && Enum.IsDefined(r) ? r : def;

    internal void Write(StringBuilder sb)
    {
        sb.Append('[').Append(Name).Append("]\n");
        foreach (var k in order)
        {
            if (comments.TryGetValue(k, out var c)) sb.Append("; ").Append(c).Append('\n');
            sb.Append(k).Append('=').Append(values[k]).Append('\n');
        }
        sb.Append('\n');
    }
}

/// <summary>テキストエディタで編集できるiniファイルの読み書き(UTF-8)。</summary>
public sealed class IniDocument
{
    private readonly List<IniSection> sections = new();
    public IReadOnlyList<IniSection> Sections => sections;
    public string HeaderComment { get; set; } = "";

    public IniSection Section(string name)
    {
        var s = sections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (s == null) { s = new IniSection(name); sections.Add(s); }
        return s;
    }

    public IniSection? Find(string name)
        => sections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IniDocument Parse(string text)
    {
        var doc = new IniDocument();
        IniSection? cur = null;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                cur = doc.Section(line.Substring(1, line.Length - 2).Trim());
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || cur == null) continue;
            cur.Set(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
        }
        return doc;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (HeaderComment.Length > 0)
        {
            foreach (var l in HeaderComment.Split('\n')) sb.Append("; ").Append(l.TrimEnd('\r')).Append('\n');
            sb.Append('\n');
        }
        foreach (var s in sections) s.Write(sb);
        return sb.ToString();
    }

    public static IniDocument Load(string path)
        => Parse(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>一時ファイルに書いてから置き換える(書き込み中に落ちても設定が壊れない)。直前の内容は .bak に残す。</summary>
    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ToString(), new UTF8Encoding(false));
        if (File.Exists(path))
        {
            try { File.Copy(path, path + ".bak", true); } catch { /* バックアップ失敗は無視 */ }
            File.Move(tmp, path, true);
        }
        else File.Move(tmp, path);
    }
}
