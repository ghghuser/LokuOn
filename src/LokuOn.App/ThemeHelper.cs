using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LokuOn.App;

public static class ThemeHelper
{
    /// <summary>いま選ばれているテーマ("dark" / "softdark" / "light" / "system")。ダイアログを開くときにも使う。</summary>
    public static string CurrentTheme { get; private set; } = "system";

    /// <summary>"dark" / "light" / "system" を実際に暗いかどうかにする。</summary>
    public static bool IsDark(string theme)
    {
        switch ((theme ?? "").Trim().ToLowerInvariant())
        {
            case "dark": case "softdark": return true;
            case "light": return false;
            default: return SystemUsesDark();
        }
    }

    /// <summary>ニューモーフィズム風(やわらかい面+光と影)のダークか。背景の効果(Mica/アクリル)は使わず、不透明の背景にする。</summary>
    public static bool IsSoft(string? theme) => string.Equals((theme ?? "").Trim(), "softdark", StringComparison.OrdinalIgnoreCase);

    /// <summary>効果なしのときのウィンドウ背景色(テーマに合わせた不透明色)。</summary>
    public static Color SolidBack(bool dark, bool soft) =>
        soft ? Color.FromRgb(0x28, 0x2B, 0x31) : dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243);

    private static ResourceDictionary? softDict;

    /// <summary>ニューモーフィズム用の部品(ボタン・入力欄)の定義を、アプリ全体へ足す/外す。読み込みに失敗しても通常のダークで動く。</summary>
    private static void ApplySoftResources(Application app, bool soft)
    {
        try
        {
            if (softDict != null) { app.Resources.MergedDictionaries.Remove(softDict); softDict = null; }
            if (!soft) return;
            softDict = new ResourceDictionary { Source = new Uri("/LokuOn;component/Themes/SoftDark.xaml", UriKind.Relative) };
            app.Resources.MergedDictionaries.Add(softDict);
        }
        catch (Exception ex)
        {
            softDict = null;
            App.WriteCrashLog("ニューモーフィズムの読み込みに失敗: " + ex.Message);
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
            ApplySoftResources(app, IsSoft(theme));
            switch ((theme ?? "").Trim().ToLowerInvariant())
            {
                case "dark": case "softdark": app.ThemeMode = ThemeMode.Dark; break;
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
        WindowEffects.Apply(w, "none", IsDark(CurrentTheme), soft: IsSoft(CurrentTheme));
    }
}
