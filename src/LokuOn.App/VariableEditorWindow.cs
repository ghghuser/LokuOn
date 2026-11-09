using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LokuOn.App;

/// <summary>
/// 変数を使う入力欄(ファイル名テンプレート・日付フォルダ名)の編集ウィンドウ。
/// 変数の説明と、押すとカーソル位置に入るボタン、結果のプレビューを出す(アイデアは LokuOnX から)。
/// </summary>
public sealed class VariableEditorWindow : Window
{
    private readonly TextBox box = new() { MinHeight = 28, Padding = new Thickness(4, 3, 4, 3), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock previewText = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontWeight = FontWeights.SemiBold };
    private readonly Func<string, string> preview;

    private VariableEditorWindow(string title, string initial, IReadOnlyList<VarInfo> vars, Func<string, string> preview, string note)
    {
        this.preview = preview;
        Title = title;
        Width = 660; Height = 560; MinWidth = 520; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12.5;
        ThemeHelper.ApplyWindowMode(this);
        SourceInitialized += (s, e) => ThemeHelper.ApplyWindow(this);

        var grid = new Grid { Margin = new Thickness(14) };
        for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var label = new TextBlock { Text = "入力欄(下の変数のボタンを押すと、カーソルの位置に入ります)", Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(label, 0);
        Grid.SetRow(box, 1);
        previewText.Margin = new Thickness(0, 6, 0, 8);
        Grid.SetRow(previewText, 2);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var v in vars)
        {
            var b = new Button
            {
                Content = v.Token, ToolTip = v.Description, Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 4, 4),
                Focusable = false,      // 押しても、入力欄のカーソル位置が動かないように
            };
            string token = v.Token;
            b.Click += (s, e) => Insert(token);
            buttons.Children.Add(b);
        }
        Grid.SetRow(buttons, 3);

        var list = new StackPanel { Margin = new Thickness(8, 6, 8, 6) };
        foreach (var v in vars)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tok = new TextBlock { Text = v.Token, FontFamily = new FontFamily("Consolas, Yu Gothic UI") };
            var desc = new TextBlock { Text = v.Description, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(desc, 1);
            row.Children.Add(tok);
            row.Children.Add(desc);
            list.Children.Add(row);
        }
        list.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 0) });
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list,
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
        };
        Grid.SetRow(scroll, 4);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 84, MinHeight = 26, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 84, MinHeight = 26, IsCancel = true };
        ok.Click += (s, e) => DialogResult = true;
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);
        Grid.SetRow(bottom, 5);

        foreach (UIElement e in new UIElement[] { label, box, previewText, buttons, scroll, bottom }) grid.Children.Add(e);
        Content = grid;

        box.Text = initial;
        box.CaretIndex = box.Text.Length;
        box.TextChanged += (s, e) => UpdatePreview();
        Loaded += (s, e) => box.Focus();
        UpdatePreview();
    }

    private void Insert(string token)
    {
        int start = box.SelectionStart;
        box.SelectedText = token;
        box.Select(start + token.Length, 0);
        box.Focus();
    }

    private void UpdatePreview()
    {
        string p;
        try { p = preview(box.Text); } catch (Exception ex) { p = "プレビューできません: " + ex.Message; }
        previewText.Text = "例: " + p;
    }

    /// <summary>OKなら編集後の文字、キャンセルなら null。</summary>
    public static string? Show(Window owner, string title, string initial, IReadOnlyList<VarInfo> vars, Func<string, string> preview, string note)
    {
        var w = new VariableEditorWindow(title, initial, vars, preview, note) { Owner = owner };
        return w.ShowDialog() == true ? w.box.Text : null;
    }
}
