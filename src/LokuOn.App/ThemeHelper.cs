using System.Windows;
using Microsoft.Win32;

namespace LokuOn.App;

public static class ThemeHelper
{
    /// <summary>いま選ばれているテーマ("dark" / "light" / "system")。ダイアログを開くときにも使う。</summary>
    public static string CurrentTheme { get; private set; } = "system";

    /// <summary>"dark" / "light" / "system" を実際に暗いかどうかにする。</summary>
    public static bool IsDark(string theme)
    {
        switch ((theme ?? "").Trim().ToLowerInvariant())
        {
            case "dark": return true;
            case "light": return false;
            default: return SystemUsesDark();
        }
    }

    private static bool SystemUsesDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>ウィンドウ全体の配色(WPFのFluentテーマ)を切り替える。</summary>
    public static void Apply(string theme)
    {
        CurrentTheme = string.IsNullOrWhiteSpace(theme) ? "system" : theme.Trim().ToLowerInvariant();
        try
        {
            var app = Application.Current;
            if (app == null) return;
            switch ((theme ?? "").Trim().ToLowerInvariant())
            {
                case "dark": app.ThemeMode = ThemeMode.Dark; break;
                case "light": app.ThemeMode = ThemeMode.Light; break;
                default: app.ThemeMode = ThemeMode.System; break;
            }
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("テーマ切り替えに失敗: " + ex.Message);
        }
    }

    /// <summary>ウィンドウ自身の色テーマだけを、いまのテーマに合わせる(表示する前に呼べる)。</summary>
    public static void ApplyWindowMode(Window w)
    {
        try { w.ThemeMode = IsDark(CurrentTheme) ? ThemeMode.Dark : ThemeMode.Light; }
        catch { }
    }

    /// <summary>設定ウィンドウなどのダイアログにも、いまのテーマ(色・タイトルバー・背景)を反映する。表示後(SourceInitialized以降)に呼ぶ。</summary>
    public static void ApplyWindow(Window w)
    {
        ApplyWindowMode(w);
        WindowEffects.Apply(w, "none", IsDark(CurrentTheme));
    }
}
