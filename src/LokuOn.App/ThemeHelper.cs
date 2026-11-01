using System.Windows;
using Microsoft.Win32;

namespace LokuOn.App;

public static class ThemeHelper
{
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
}
