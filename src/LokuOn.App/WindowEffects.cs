using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace LokuOn.App;

/// <summary>Windows 11 の背景効果(Mica / アクリル)と、タイトルバーの暗色化。Windows 10 では何も起きません。</summary>
public static class WindowEffects
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    /// <param name="mode">"none" / "mica" / "acrylic"</param>
    public static void Apply(Window w, string mode, bool dark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;

            int useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));          // タイトルバーを暗く

            bool on = mode == "mica" || mode == "acrylic";
            int type = mode == "mica" ? 2 : mode == "acrylic" ? 3 : 1;           // 1=なし 2=Mica 3=アクリル
            DwmSetWindowAttribute(hwnd, 38, ref type, sizeof(int));

            var src = HwndSource.FromHwnd(hwnd);
            if (on)
            {
                if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                w.Background = Brushes.Transparent;
            }
            else
            {
                // 通常の背景: テーマに合わせた不透明な色をはっきり指定する
                // (指定しないと、システムが明るい設定のとき、ダークテーマでも後ろが白くなって文字が読めなくなる)
                var m = new Margins();
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                Color solid = dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(243, 243, 243);
                if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = solid;
                w.Background = new SolidColorBrush(solid);
            }
        }
        catch { /* 効果が使えない環境では、何もしない */ }
    }
}
