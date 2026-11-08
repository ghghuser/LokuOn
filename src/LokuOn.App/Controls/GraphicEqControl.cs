using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LokuOn.Core;

namespace LokuOn.App.Controls;

/// <summary>
/// 10バンドのグラフィックイコライザ(縦のつまみを横に並べ、実際の周波数特性のカーブを重ねて表示)。
/// ドラッグで上げ下げ、ダブルクリックでその帯域を0dBに、マウスホイールで0.5dBずつ。
/// </summary>
public sealed class GraphicEqControl : Control
{
    private const double LeftW = 38, RightW = 10, TopH = 14, BottomH = 24, ThumbW = 22, ThumbH = 11;
    private static readonly Typeface Face = new("Yu Gothic UI, Meiryo UI, Segoe UI");

    private double[] bands = new double[EqBands.Count];
    private double hpfHz;
    private ParamBand[]? param;
    private int dragBand = -1;
    private int hoverBand = -1;

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(GraphicEqControl),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3B, 0x8E, 0xF0)), FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    /// <summary>グラフィックEQがオフのとき true。曲線にはグラフィックの帯域を含めず(ローカット・パラメトリックだけ)、つまみは薄く描く。</summary>
    public bool Dimmed { get => dimmed; set { dimmed = value; InvalidateVisual(); } }
    private bool dimmed;
    private static readonly double[] flat = new double[EqBands.Count];

    /// <summary>つまみが動いた(ドラッグ中も続けて起きる)。</summary>
    public event Action? BandsChanged;

    public GraphicEqControl()
    {
        Height = 170; MinWidth = 380; Focusable = false; Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
    }

    /// <summary>画面に出す値(10帯域のdB)。コピーを持つ。</summary>
    public void SetBands(double[] v)
    {
        for (int i = 0; i < bands.Length; i++) bands[i] = i < v.Length ? Math.Clamp(v[i], -EqBands.MaxDb, EqBands.MaxDb) : 0;
        InvalidateVisual();
    }

    /// <summary>ローカットの周波数(0=なし)。カーブに反映する。</summary>
    public void SetHpf(double hz) { hpfHz = hz; InvalidateVisual(); }

    /// <summary>パラメトリックEQの設定(オフのときは null)。カーブに反映する。</summary>
    public void SetParams(ParamBand[]? p) { param = p?.Select(b => b.Clone()).ToArray(); InvalidateVisual(); }

    public double[] GetBands() => (double[])bands.Clone();

    // ------------------------------------------------------------ 座標

    private double PlotW => Math.Max(10, ActualWidth - LeftW - RightW);
    private double PlotH => Math.Max(10, ActualHeight - TopH - BottomH);
    private double ColW => PlotW / EqBands.Count;
    private double XOf(int i) => LeftW + (i + 0.5) * ColW;
    private double YOf(double db) => TopH + (EqBands.MaxDb - db) / (2 * EqBands.MaxDb) * PlotH;
    private double DbOf(double y) => Math.Round(Math.Clamp(EqBands.MaxDb - (y - TopH) / PlotH * 2 * EqBands.MaxDb, -EqBands.MaxDb, EqBands.MaxDb) * 2) / 2;
    private int BandAt(double x) => Math.Clamp((int)((x - LeftW) / ColW), 0, EqBands.Count - 1);

    // ------------------------------------------------------------ 描画

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 60 || h < 60) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var fg = Foreground ?? Brushes.Gray;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // どこでもマウスを受け取る
        dc.PushOpacity(1.0);

        // 目盛り(+12 / 0 / -12)
        var faint = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
        foreach (int db in new[] { 12, 6, 0, -6, -12 })
        {
            double y = YOf(db);
            bool major = db % 12 == 0;
            dc.DrawLine(new Pen(faint, db == 0 ? 1.2 : 0.6) { DashStyle = db == 0 ? DashStyles.Solid : DashStyles.Dash }, new Point(LeftW, y), new Point(w - RightW, y));
            if (major || db == 0)
            {
                var t = new FormattedText((db > 0 ? "+" : "") + db, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, fg, dpi);
                dc.DrawText(t, new Point(LeftW - 6 - t.Width, y - t.Height / 2));
            }
        }

        // 周波数のカーブ(実際のフィルターの特性)
        int n = 160;
        var pts = new Point[n + 1];
        for (int k = 0; k <= n; k++)
        {
            double pos = -0.5 + (EqBands.Count) * (double)k / n;       // 帯域の番号(連続値)
            double hz = EqBands.Freqs[0] * Math.Pow(2, pos);
            double db = Math.Clamp(EqMath.ResponseDb(dimmed ? flat : bands, hz, 48000, hpfHz, param), -EqBands.MaxDb * 1.2, EqBands.MaxDb * 1.2);
            pts[k] = new Point(LeftW + pos * ColW + 0.5 * ColW, Math.Clamp(YOf(db), TopH - 4, TopH + PlotH + 4));
        }
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            g.BeginFigure(new Point(pts[0].X, TopH + PlotH), true, true);
            foreach (var p in pts) g.LineTo(p, true, true);
            g.LineTo(new Point(pts[n].X, TopH + PlotH), true, true);
        }
        curve.Freeze();
        var accent = Accent is SolidColorBrush sb ? sb.Color : Color.FromRgb(0x3B, 0x8E, 0xF0);
        var fill = new LinearGradientBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B), Color.FromArgb(0x08, accent.R, accent.G, accent.B), 90);
        dc.PushClip(new RectangleGeometry(new Rect(LeftW, TopH - 4, PlotW, PlotH + 8)));
        dc.DrawGeometry(fill, null, curve);
        var line = new StreamGeometry();
        using (var g = line.Open()) { g.BeginFigure(pts[0], false, false); for (int k = 1; k <= n; k++) g.LineTo(pts[k], true, true); }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 1.6), line);
        dc.Pop();

        // レール・つまみ・周波数の名前
        var rail = new SolidColorBrush(Color.FromArgb(0x70, 0x90, 0x90, 0x90));
        for (int i = 0; i < EqBands.Count; i++)
        {
            double x = XOf(i);
            if (dimmed) dc.PushOpacity(0.45);
            dc.DrawRoundedRectangle(rail, null, new Rect(x - 1.5, TopH, 3, PlotH), 1.5, 1.5);
            // 0dBから現在値まで色を付ける
            double y0 = YOf(0), y1 = YOf(bands[i]);
            if (Math.Abs(y1 - y0) > 0.5)
                dc.DrawRectangle(new SolidColorBrush(accent), null, new Rect(x - 1.5, Math.Min(y0, y1), 3, Math.Abs(y1 - y0)));
            bool hot = i == dragBand || i == hoverBand;
            var thumb = new Rect(x - ThumbW / 2, y1 - ThumbH / 2, ThumbW, ThumbH);
            dc.DrawRoundedRectangle(hot ? Brushes.White : new SolidColorBrush(accent), new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), 0.8), thumb, 3, 3);
            if (dimmed) dc.Pop();
            var lab = new FormattedText(EqBands.Labels[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, fg, dpi);
            dc.DrawText(lab, new Point(x - lab.Width / 2, h - BottomH + 6));
        }

        // ドラッグ中・ホバー中は値を出す
        int shown = dragBand >= 0 ? dragBand : hoverBand;
        if (shown >= 0)
        {
            string txt = (bands[shown] > 0 ? "+" : "") + bands[shown].ToString("0.0", CultureInfo.InvariantCulture) + " dB";
            var t = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, fg, dpi);
            double tx = Math.Clamp(XOf(shown) - t.Width / 2, LeftW, w - RightW - t.Width);
            double ty = YOf(bands[shown]) - ThumbH / 2 - t.Height - 2;
            if (ty < 0) ty = YOf(bands[shown]) + ThumbH / 2 + 2;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x20)), null, new Rect(tx - 3, ty - 1, t.Width + 6, t.Height + 2), 3, 3);
            dc.DrawText(new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11, Brushes.White, dpi), new Point(tx, ty));
        }
        dc.Pop();
    }

    // ------------------------------------------------------------ 操作

    private void SetFromMouse(int band, double y)
    {
        double v = DbOf(y);
        if (bands[band] == v) return;
        bands[band] = v;
        InvalidateVisual();
        BandsChanged?.Invoke();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        if (p.X < LeftW || p.X > ActualWidth - RightW) return;
        int b = BandAt(p.X);
        if (e.ClickCount == 2) { bands[b] = 0; InvalidateVisual(); BandsChanged?.Invoke(); e.Handled = true; return; }
        dragBand = b;
        CaptureMouse();
        SetFromMouse(b, p.Y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (dragBand >= 0) { SetFromMouse(dragBand, p.Y); return; }
        int hb = p.X >= LeftW && p.X <= ActualWidth - RightW ? BandAt(p.X) : -1;
        if (hb != hoverBand) { hoverBand = hb; InvalidateVisual(); }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragBand >= 0) { dragBand = -1; ReleaseMouseCapture(); InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverBand >= 0 && dragBand < 0) { hoverBand = -1; InvalidateVisual(); }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = e.GetPosition(this);
        if (p.X < LeftW || p.X > ActualWidth - RightW) return;
        int b = BandAt(p.X);
        bands[b] = Math.Clamp(bands[b] + (e.Delta > 0 ? 0.5 : -0.5), -EqBands.MaxDb, EqBands.MaxDb);
        InvalidateVisual(); BandsChanged?.Invoke();
        e.Handled = true;
    }
}
