using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Rokuon.App;

/// <summary>1行の文字を入力してもらう小さなダイアログ。</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox box = new();

    private PromptWindow(string title, string message, string initial)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI");
        FontSize = 12;

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        box.Text = initial;
        box.Margin = new Thickness(0, 0, 0, 12);
        box.MinHeight = 26;
        panel.Children.Add(box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 84, MinHeight = 26, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 84, MinHeight = 26, IsCancel = true };
        ok.Click += (s, e) => { DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
    }

    /// <summary>OKなら入力した文字、キャンセルなら null。</summary>
    public static string? Show(Window owner, string title, string message, string initial)
    {
        var w = new PromptWindow(title, message, initial) { Owner = owner };
        return w.ShowDialog() == true ? w.box.Text : null;
    }
}
