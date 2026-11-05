using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LokuOn.Audio;

/// <summary>"Ctrl+Alt+F9" のような文字列を、Windowsのホットキー登録用の値に変換する。</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    public static bool TryParse(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        bool haveKey = false;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string t = raw.ToLowerInvariant();
            switch (t)
            {
                case "ctrl": case "control": case "ctl": modifiers |= ModControl; continue;
                case "alt": modifiers |= ModAlt; continue;
                case "shift": modifiers |= ModShift; continue;
                case "win": case "windows": modifiers |= ModWin; continue;
            }
            if (haveKey) return false;       // キーは1つだけ
            if (!TryKey(t, out vk)) return false;
            haveKey = true;
        }
        if (!haveKey) return false;
        if ((modifiers & (ModControl | ModAlt | ModShift | ModWin)) == 0) return false;   // 修飾キーなしは誤爆するので不可
        modifiers |= ModNoRepeat;
        return true;
    }

    private static bool TryKey(string t, out uint vk)
    {
        vk = 0;
        if (t.Length == 1)
        {
            char c = t[0];
            if (c >= 'a' && c <= 'z') { vk = (uint)(c - 'a' + 0x41); return true; }
            if (c >= '0' && c <= '9') { vk = (uint)(c - '0' + 0x30); return true; }
        }
        if (t.Length >= 2 && t[0] == 'f' && int.TryParse(t.AsSpan(1), out int fn) && fn >= 1 && fn <= 24) { vk = (uint)(0x70 + fn - 1); return true; }
        if (t.StartsWith("num") && t.Length == 4 && char.IsDigit(t[3])) { vk = (uint)(0x60 + (t[3] - '0')); return true; }
        switch (t)
        {
            case "space": vk = 0x20; return true;
            case "enter": case "return": vk = 0x0D; return true;
            case "esc": case "escape": vk = 0x1B; return true;
            case "tab": vk = 0x09; return true;
            case "insert": case "ins": vk = 0x2D; return true;
            case "delete": case "del": vk = 0x2E; return true;
            case "home": vk = 0x24; return true;
            case "end": vk = 0x23; return true;
            case "pageup": case "pgup": vk = 0x21; return true;
            case "pagedown": case "pgdn": vk = 0x22; return true;
            case "left": vk = 0x25; return true;
            case "up": vk = 0x26; return true;
            case "right": vk = 0x27; return true;
            case "down": vk = 0x28; return true;
            case "pause": vk = 0x13; return true;
        }
        return false;
    }
}

/// <summary>グローバルホットキー(他のアプリを使っていても反応する)の登録。</summary>
public static class GlobalHotkeys
{
    public const int WmHotkey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static bool Register(IntPtr hwnd, int id, string text)
    {
        if (!HotkeyParser.TryParse(text, out uint mod, out uint vk)) return false;
        try { return RegisterHotKey(hwnd, id, mod, vk); } catch { return false; }
    }

    public static void Unregister(IntPtr hwnd, int id)
    {
        try { UnregisterHotKey(hwnd, id); } catch { }
    }
}

public static class FileOps
{
    /// <summary>ごみ箱へ移す(完全に削除はしない)。</summary>
    public static void MoveToRecycleBin(string path)
    {
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
            path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    /// <summary>エクスプローラーでファイルのあるフォルダを開き、そのファイルを選択状態にする。</summary>
    public static void RevealInExplorer(string filePath)
    {
        if (File.Exists(filePath))
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + filePath + "\"") { UseShellExecute = true });
        else
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) OpenFolder(dir);
        }
    }

    public static void OpenFolder(string dir)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
    }

    public static readonly string[] AudioExtensions = { ".flac", ".mp3", ".m4a", ".aac", ".wav", ".opus", ".ogg" };

    public static bool IsAudio(string path)
        => AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>保存先フォルダ(日付フォルダを含む)から、新しい順に音声ファイルを集める。</summary>
    public static List<FileInfo> ScanRecent(string root, int max)
    {
        var list = new List<FileInfo>();
        try
        {
            if (!Directory.Exists(root)) return list;
            var opt = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", opt))
            {
                if (f.Name.StartsWith("~") || f.Name.Contains(LokuOn.Core.NameTemplate.ProvisionalText)) continue;
                if (IsAudio(f.FullName)) list.Add(f);
            }
        }
        catch { }
        // UTC で並べる(ローカル時刻への変換を毎回しないぶん速く、夏時間の切り替わりでも順序がずれない)
        return list.OrderByDescending(f => f.LastWriteTimeUtc).Take(max).ToList();
    }
}
