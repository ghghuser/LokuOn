using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>Windows 11 の背景効果(Mica / アクリル)と、タイトルバーの暗色化、ウィンドウの背景色。Windows 10 では効果は出ません。</summary>
public static class WindowEffects
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    /// <param name="mode">"none" / "mica" / "acrylic"。ライトテーマのときは、何を渡しても「none」になる。</param>
    /// <param name="strength">効果の上に重ねる暗い色の濃さ(30〜100)。大きいほど無地に近く、背景の模様が目立たない。</param>
    public static void Apply(Window w, string? mode, bool dark, int strength = BackdropRules.DefaultStrength, bool soft = false)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;

            string effect = soft ? "none" : BackdropRules.Effective(mode, dark);   // ニューモーフィズムは影の見え方のため、効果なしの不透明

            int useDark = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));          // タイトルバーの明暗

            int type = effect == "mica" ? 2 : effect == "acrylic" ? 3 : 1;       // 1=なし 2=Mica 3=アクリル
            DwmSetWindowAttribute(hwnd, 38, ref type, sizeof(int));

            var src = HwndSource.FromHwnd(hwnd);
            if (effect != "none")
            {
                if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);

                // 効果の上に、暗い色を重ねる(濃いほど背景の模様が見えにくい)。アクリルは Mica より薄く重ねて、よく透けるようにする
                double alpha = BackdropRules.TintAlpha(effect, strength);
                w.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), 24, 24, 26));
            }
            else
            {
                // 通常の背景: テーマに合わせた不透明な色をはっきり指定する
                // (指定しないと、システムが明るい設定のとき、ダークテーマでも後ろが白くなって文字が読めなくなる)
                var m = new Margins();
                DwmExtendFrameIntoClientArea(hwnd, ref m);
                Color solid = ThemeHelper.SolidBack(dark, soft);
                if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = solid;
                w.Background = new SolidColorBrush(solid);
            }
        }
        catch { /* 効果が使えない環境では、何もしない */ }
    }
}
