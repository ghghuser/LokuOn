using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>右上の ⚙ から開く、めったに使わない設定と情報(テーマ、最小化の動作、バージョン、GitHub、使い方)。</summary>
public sealed class AppInfoWindow : Window
{
    public const string GitHubUrl = "https://github.com/ghghuser/LokuOn";

    private readonly AppSettings settings;
    private readonly Action changed;
    private readonly ComboBox themeBox = new();
    private readonly ComboBox backdropBox = new();
    private readonly RadioButton taskbarRadio = new() { Content = "タスクバーに残す(初期値)", GroupName = "min", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton trayRadio = new() { Content = "タスクトレイにしまう", GroupName = "min", Margin = new Thickness(0, 2, 0, 2) };
    private bool loading = true;

    public AppInfoWindow(AppSettings settings, string appDir, Action changed)
    {
        this.settings = settings;
        this.changed = changed;
        Title = "LokuOn - 設定と情報";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12;

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Header("表示"));
        themeBox.ItemsSource = new List<Choice<string>>
        {
            new("システムに合わせる", "system"), new("ダーク", "dark"), new("ライト", "light"),
        };
        panel.Children.Add(Row("テーマ", themeBox));
        backdropBox.ItemsSource = new List<Choice<string>>
        {
            new("なし(通常の背景)", "none"), new("Mica(うっすら壁紙が透ける)", "mica"), new("アクリル(すりガラス風)", "acrylic"),
        };
        panel.Children.Add(Row("背景の効果", backdropBox));
        panel.Children.Add(new TextBlock
        {
            Text = "背景の効果は Windows 11 で使えます（Windows 10 では変わりません）。",
            Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });

        panel.Children.Add(Header("ウィンドウを最小化したとき"));
        panel.Children.Add(taskbarRadio);
        panel.Children.Add(trayRadio);

        panel.Children.Add(Header("このアプリについて"));
        string ver = typeof(AppInfoWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        panel.Children.Add(new TextBlock { Text = $"LokuOn  バージョン {ver}", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = GitHubUrl, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 6) });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        links.Children.Add(MakeButton("GitHub を開く", () => OpenUrl(GitHubUrl)));
        links.Children.Add(MakeButton("設定ファイルのフォルダを開く", () => OpenFolder(appDir)));
        panel.Children.Add(links);

        panel.Children.Add(Header("かんたんな使い方"));
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text =
                "・入力デバイス: 🎤=マイク、🔊=スピーカーの音(PC内部音)。＊は Windows の既定のデバイスです。\n" +
                "・録音: ●録音開始 → ■停止で保存。❚❚一時停止・✂分割も使えます。\n" +
                "・ファイル名: テンプレートの変数 {Date} {Time} {No} {Track} {Start} {End} {Length} などで決まります。\n" +
                "・設定は LokuOn.exe と同じフォルダの settings.ini に保存されます(メモ帳で編集できます)。\n" +
                "・困ったとき: 同じフォルダの lokuon-crash.log と lokuon.log を確認してください。",
        });

        var close = new Button { Content = "閉じる", Width = 96, MinHeight = 28, IsDefault = true, IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (s, e) => Close();
        panel.Children.Add(close);

        Content = panel;

        // 現在の値を反映してから、変更を受け付ける
        Select(themeBox, string.IsNullOrWhiteSpace(settings.Theme) ? "system" : settings.Theme.ToLowerInvariant());
        Select(backdropBox, settings.Backdrop);
        taskbarRadio.IsChecked = !settings.MinimizeToTray;
        trayRadio.IsChecked = settings.MinimizeToTray;
        loading = false;

        themeBox.SelectionChanged += (s, e) => { if (loading) return; settings.Theme = ValueOf(themeBox, "system"); changed(); };
        backdropBox.SelectionChanged += (s, e) => { if (loading) return; settings.Backdrop = ValueOf(backdropBox, "none"); changed(); };
        taskbarRadio.Click += (s, e) => { settings.MinimizeToTray = false; changed(); };
        trayRadio.Click += (s, e) => { settings.MinimizeToTray = true; changed(); };
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 10, 0, 4),
    };

    private static UIElement Row(string label, ComboBox box)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(box, 1);
        g.Children.Add(t);
        g.Children.Add(box);
        return g;
    }

    private static Button MakeButton(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (s, e) => click();
        return b;
    }

    private static void Select(ComboBox cb, string value)
    {
        foreach (var o in cb.Items)
            if (o is Choice<string> c && c.Value == value) { cb.SelectedItem = o; return; }
        cb.SelectedIndex = 0;
    }

    private static string ValueOf(ComboBox cb, string def) => cb.SelectedItem is Choice<string> c ? c.Value : def;

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static void OpenFolder(string dir)
    {
        try { if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch { }
    }
}
