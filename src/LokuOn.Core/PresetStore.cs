using System.Text.Json;

namespace LokuOn.Core;

/// <summary>
/// プリセットを「Presets\名前.json」で保存・読み込みする。exe と同じ場所の Presets フォルダに置く。
/// 利用者がファイルをコピーして共有したり、削除したりできる。
/// </summary>
public static class PresetStore
{
    public static string FolderFor(string settingsPath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? ".", "Presets");

    public static string FileFor(string folder, string name)
        => Path.Combine(folder, NameTemplate.Sanitize(name) + ".json");

    /// <summary>フォルダ内の *.json を全部読む。名前はファイル名から決める。読めないファイルは飛ばす。</summary>
    public static List<Preset> LoadAll(string folder, out List<string> problems)
    {
        problems = new List<string>();
        var list = new List<Preset>();
        if (!Directory.Exists(folder)) return list;
        foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var p = JsonSerializer.Deserialize<Preset>(File.ReadAllText(f, System.Text.Encoding.UTF8), PresetJson.Options);
                if (p == null) throw new InvalidDataException("空のファイルです");
                p.Name = Path.GetFileNameWithoutExtension(f);
                p.Normalize();
                list.Add(p);
            }
            catch (Exception ex)
            {
                problems.Add(Path.GetFileName(f) + ": " + ex.Message);
            }
        }
        return list;
    }

    public static void Save(string folder, Preset p)
    {
        Directory.CreateDirectory(folder);
        string path = FileFor(folder, p.Name);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(p, PresetJson.Options), new System.Text.UTF8Encoding(false));
        File.Move(tmp, path, true);
    }

    public static void Delete(string folder, string name)
    {
        string path = FileFor(folder, name);
        if (File.Exists(path)) File.Delete(path);
    }
}
