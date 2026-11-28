using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>右上の ⚙ から開く、めったに使わない設定と情報(テーマ、背景の効果、最小化の動作、バージョン、GitHub、使い方)。</summary>
public sealed class AppInfoWindow : Window
{
    public const string GitHubUrl = "https://github.com/ghghuser/LokuOn";

    private readonly AppSettings settings;
    private readonly Action changed;
    private readonly ComboBox langBox = new();
    private readonly ComboBox themeBox = new();
    private readonly ComboBox backdropBox = new();
    private readonly Slider strengthSlider = new()
    {
        Minimum = BackdropRules.MinStrength, Maximum = BackdropRules.MaxStrength,
        TickFrequency = 5, IsSnapToTickEnabled = true, SmallChange = 5, LargeChange = 10,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock strengthText = new()
    {
        Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock langNote = new() { Opacity = 0.65, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock backdropNote = new()
    {
        Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
    };
    private readonly RadioButton taskbarRadio = new() { Content = Loc.Get("info.min.taskbar"), GroupName = "min", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton trayRadio = new() { Content = Loc.Get("info.min.tray"), GroupName = "min", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox sleepCheck = new() { Content = Loc.Get("info.sleep"), Margin = new Thickness(0, 2, 0, 2) };
    private bool loading = true;

    public AppInfoWindow(AppSettings settings, string appDir, Action changed)
    {
        this.settings = settings;
        this.changed = changed;
        Title = Loc.Get("info.title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12;

        // メイン画面と同じテーマにする(色・タイトルバー・背景)
        ThemeHelper.ApplyWindowMode(this);
        SourceInitialized += (s, e) => ThemeHelper.ApplyWindow(this);

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(Header(Loc.Get("info.sec.display")));
        langBox.ItemsSource = new List<Choice<string>> { new("日本語", "ja"), new("English", "en") };
        panel.Children.Add(Row(Loc.Get("info.row.language"), langBox));
        langNote.Text = Loc.Get("info.lang.restart");
        panel.Children.Add(langNote);
        themeBox.ItemsSource = new List<Choice<string>>
        {
            new(Loc.Get("info.theme.system"), "system"), new(Loc.Get("info.theme.dark"), "dark"), new(Loc.Get("info.theme.softdark"), "softdark"), new(Loc.Get("info.theme.light"), "light"),
        };
        panel.Children.Add(Row(Loc.Get("info.row.theme"), themeBox));
        backdropBox.ItemsSource = new List<Choice<string>>
        {
            new(Loc.Get("info.backdrop.none"), "none"), new(Loc.Get("info.backdrop.mica"), "mica"), new(Loc.Get("info.backdrop.acrylic"), "acrylic"),
        };
        panel.Children.Add(Row(Loc.Get("info.row.backdrop"), backdropBox));

        var strengthRow = new Grid();
        strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(strengthText, 1);
        strengthRow.Children.Add(strengthSlider);
        strengthRow.Children.Add(strengthText);
        panel.Children.Add(Row(Loc.Get("info.row.strength"), strengthRow));
        panel.Children.Add(backdropNote);

        panel.Children.Add(Header(Loc.Get("info.sec.recording")));
        panel.Children.Add(sleepCheck);

        panel.Children.Add(Header(Loc.Get("info.sec.minimize")));
        panel.Children.Add(taskbarRadio);
        panel.Children.Add(trayRadio);

        panel.Children.Add(Header(Loc.Get("info.sec.about")));
        string ver = typeof(AppInfoWindow).Assembly.GetName().Version?.ToString(4) ?? "1.0.0.0";
        panel.Children.Add(new TextBlock { Text = Loc.Get("info.version", ver), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = GitHubUrl, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 2) });
        panel.Children.Add(new TextBlock { Text = Loc.Get("info.license"), Opacity = 0.8, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        links.Children.Add(MakeButton(Loc.Get("info.btn.github"), () => OpenUrl(GitHubUrl)));
        links.Children.Add(MakeButton(Loc.Get("info.btn.openfolder"), () => OpenFolder(appDir)));
        panel.Children.Add(links);
        var backupRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var backupMsg = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        backupRow.Children.Add(MakeButton(Loc.Get("info.btn.backup"), () => BackupSettings(appDir, backupMsg)));
        panel.Children.Add(backupRow);
        panel.Children.Add(backupMsg);

        panel.Children.Add(Header(Loc.Get("info.sec.help")));
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = Loc.Get("info.help.body"),
        });

        var close = new Button { Content = Loc.Get("info.btn.close"), Width = 96, MinHeight = 28, IsDefault = true, IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        close.Click += (s, e) => Close();
        panel.Children.Add(close);

        Content = panel;

        // 現在の値を反映してから、変更を受け付ける
        Select(themeBox, string.IsNullOrWhiteSpace(settings.Theme) ? "system" : settings.Theme.ToLowerInvariant());
        Select(langBox, LanguageRules.Normalize(settings.Language));
        sleepCheck.IsChecked = settings.PreventSleep;
        taskbarRadio.IsChecked = !settings.MinimizeToTray;
        trayRadio.IsChecked = settings.MinimizeToTray;
        UpdateBackdropUi();
        loading = false;

        langBox.SelectionChanged += (s, e) =>
        {
            if (loading) return;
            settings.Language = LanguageRules.Normalize(ValueOf(langBox, "ja"));
            changed();
            langNote.Visibility = settings.Language == Loc.Language ? Visibility.Collapsed : Visibility.Visible;   // 再起動後に反映
        };
        themeBox.SelectionChanged += (s, e) =>
        {
            if (loading) return;
            settings.Theme = ValueOf(themeBox, "system");
            changed();
            ThemeHelper.ApplyWindow(this);      // この設定ウィンドウ自身もすぐに切り替える
            UpdateBackdropUi();
        };
        backdropBox.SelectionChanged += (s, e) =>
        {
            if (loading) return;
            settings.Backdrop = ValueOf(backdropBox, "none");
            changed();
            UpdateBackdropUi();
        };
        strengthSlider.ValueChanged += (s, e) =>
        {
            if (loading) return;
            settings.BackdropStrength = (int)Math.Round(strengthSlider.Value);
            strengthText.Text = settings.BackdropStrength + "%";
            changed();
        };
        sleepCheck.Click += (s, e) => { settings.PreventSleep = sleepCheck.IsChecked == true; changed(); };
        taskbarRadio.Click += (s, e) => { settings.MinimizeToTray = false; changed(); };
        trayRadio.Click += (s, e) => { settings.MinimizeToTray = true; changed(); };
    }

    /// <summary>
    /// 背景の効果の欄を、いまのテーマに合わせる。
    /// ライトテーマでは「なし」に固定して触れなくする(選んでいた効果は設定に残るので、ダークに戻すと復活する)。
    /// </summary>
    private void UpdateBackdropUi()
    {
        bool prev = loading;
        loading = true;
        try
        {
            bool soft = ThemeHelper.IsSoft(settings.Theme);
            bool dark = ThemeHelper.IsDark(settings.Theme) && !soft;     // ニューモーフィズムは背景の効果なし固定
            Select(backdropBox, dark ? settings.Backdrop : "none");
            backdropBox.IsEnabled = dark;

            bool on = BackdropRules.Effective(settings.Backdrop, dark) != "none";
            strengthSlider.Value = Math.Clamp(settings.BackdropStrength, BackdropRules.MinStrength, BackdropRules.MaxStrength);
            strengthText.Text = (int)Math.Round(strengthSlider.Value) + "%";
            strengthSlider.IsEnabled = on;

            backdropNote.Text = Loc.Get(soft ? "info.note.soft" : !dark ? "info.note.light" : "info.note.dark");
        }
        finally { loading = prev; }
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 10, 0, 4),
    };

    private static Grid Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(control, 1);
        g.Children.Add(t);
        g.Children.Add(control);
        return g;
    }

    /// <summary>settings.ini と Presets を ZIP にして、選んだ場所へ保存する。結果は、ボタンの下に(成功=緑・失敗=赤)で表示する。</summary>
    private static void BackupSettings(string appDir, TextBlock msg)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.Get("info.backup.title"),
            Filter = Loc.Get("info.backup.filter"),
            FileName = $"LokuOn-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            int n = SettingsBackup.Create(Path.Combine(appDir, "settings.ini"), dlg.FileName);
            msg.Foreground = n > 0 ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.DarkGoldenrod;
            msg.Text = n > 0 ? Loc.Get("info.backup.ok", n, dlg.FileName) : Loc.Get("info.backup.empty");
        }
        catch (Exception ex)
        {
            msg.Foreground = System.Windows.Media.Brushes.Crimson;
            msg.Text = Loc.Get("info.backup.fail", ex.Message);
        }
        msg.Visibility = Visibility.Visible;
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
        try { if (Directory.Exists(dir)) Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + dir + "\"") { UseShellExecute = true }); } catch { }
    }
}
