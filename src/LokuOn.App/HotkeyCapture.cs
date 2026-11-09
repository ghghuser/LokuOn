using System.Windows.Controls;
using System.Windows.Input;

namespace LokuOn.App;

/// <summary>
/// ホットキーの入力欄を「キーを押すだけで入力」にする(アイデアは LokuOnX から)。
/// 欄をクリックして Ctrl・Alt・Shift・Win のどれかと一緒にキーを押すと、"Ctrl+Alt+F9" の形で入る。Delete か BackSpace で空にする。
/// 文字を直接打つ必要がなく、綴りの間違いや日本語入力のまま打つミスが起きない。
/// </summary>
public static class HotkeyCapture
{
    public const string Tip = "欄をクリックして、割り当てたいキーを押します(Ctrl・Alt・Shift・Win のどれかと一緒に)。Delete か BackSpace で未設定にします";

    public static void Attach(TextBox tb)
    {
        InputMethod.SetIsInputMethodEnabled(tb, false);
        tb.ToolTip = Tip;
        tb.PreviewKeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
        if (key == Key.Tab || key == Key.Escape) return;            // 画面の移動・閉じる操作はそのまま
        e.Handled = true;

        var mods = Keyboard.Modifiers;
        if (key is Key.Delete or Key.Back && mods == ModifierKeys.None) { tb.Text = ""; return; }
        string? name = KeyName(key);
        if (name == null) return;                                    // 修飾キーだけ・対応していないキーは無視して待つ
        if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) == 0) return;   // 修飾キーなしは誤爆するので不可

        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        tb.Text = string.Join("+", parts);
        tb.CaretIndex = tb.Text.Length;
    }

    /// <summary>HotkeyParser(文字→登録値)が読める名前にする。対応していないキーは null。</summary>
    public static string? KeyName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((int)k - (int)Key.D0).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Num" + ((int)k - (int)Key.NumPad0);
        if (k >= Key.F1 && k <= Key.F24) return k.ToString();
        return k switch
        {
            Key.Space => "Space", Key.Enter => "Enter", Key.Insert => "Insert", Key.Delete => "Delete",
            Key.Home => "Home", Key.End => "End", Key.Prior => "PageUp", Key.Next => "PageDown",
            Key.Left => "Left", Key.Up => "Up", Key.Right => "Right", Key.Down => "Down", Key.Pause => "Pause",
            _ => null,
        };
    }
}
