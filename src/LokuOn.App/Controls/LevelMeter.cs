using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LokuOn.App.Controls;

/// <summary>横向きの音量メーター(複数本)。-60dB〜0dB。ピーク保持とクリップ表示つき。</summary>
public sealed class LevelMeter : FrameworkElement
{
    private const double MinDb = -60;
    private const double FallDbPerSec = 36;
    private const double PeakHoldSec = 1.5;
    private const double ClipHoldSec = 3.0;

    private string[] labels = { "L", "R" };
    private double[] shown = new double[2];
    private double[] peak = new double[2];
    private double[] peakAt = new double[2];
    private double[] clipUntil = new double[2];
    private bool[] enabled = { true, true };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastUpdate;

    // 文字(ラベルと目盛り)は毎回作り直すと重いので、作ったものを使い回す。
    // 文字の色(TextBrush)・画面の拡大率・ラベルが変わったときだけ作り直す。
    private static readonly Typeface Face = new("Yu Gothic UI, Meiryo UI, Segoe UI");
    private static readonly int[] TickDbs = { -60, -48, -36, -24, -12, -6, 0 };
    private FormattedText[] labelText = Array.Empty<FormattedText>();
    private FormattedText[] tickText = Array.Empty<FormattedText>();
    private double textDpi = -1;
    private Brush? textBrushUsed;

    public Brush BackBrush { get; set; } = Brushes.Black;
    public Brush TextBrush { get; set; } = Brushes.Gray;
    public Brush GoodBrush { get; set; } = new SolidColorBrush(Color.FromRgb(60, 200, 90));
    public Brush WarnBrush { get; set; } = new SolidColorBrush(Color.FromRgb(235, 200, 40));
    public Brush HotBrush { get; set; } = new SolidColorBrush(Color.FromRgb(235, 70, 60));
    public Brush OffBrush { get; set; } = new SolidColorBrush(Color.FromRgb(90, 90, 90));
    public double RowHeight { get; set; } = 13;

    public LevelMeter()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    /// <summary>バーをダブルクリックしたとき(バーの番号)。</summary>
    public event Action<int>? BarDoubleClicked;
    /// <summary>バーを右クリックしたとき(バーの番号)。</summary>
    public event Action<int>? BarRightClicked;

    private int BarAt(Point p)
    {
        int i = (int)(p.Y / (RowHeight + 2));
        return Math.Clamp(i, 0, Math.Max(0, labels.Length - 1));
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2 && labels.Length > 0) { BarDoubleClicked?.Invoke(BarAt(e.GetPosition(this))); e.Handled = true; }
    }

    protected override void OnMouseRightButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (labels.Length > 0) { BarRightClicked?.Invoke(BarAt(e.GetPosition(this))); e.Handled = true; }
    }

    /// <summary>本数とラベルを設定する。</summary>
    public void Configure(string[] barLabels)
    {
        labels = barLabels;
        textDpi = -1;       // ラベルが変わるので文字を作り直す
        int n = labels.Length;
        shown = new double[n]; peak = new double[n]; peakAt = new double[n]; clipUntil = new double[n];
        enabled = new bool[n];
        for (int i = 0; i < n; i++) { enabled[i] = true; shown[i] = MinDb; peak[i] = MinDb; }
        Height = n * (RowHeight + 2) + 14;
        InvalidateVisual();
    }

    public void SetEnabled(int index, bool on)
    {
        if (index >= 0 && index < enabled.Length && enabled[index] != on) { enabled[index] = on; InvalidateVisual(); }
    }

    /// <summary>直近のピーク(0〜1の直線値)を渡す。UIタイマーごとに呼ぶ。</summary>
    public void Update(float[] linearPeaks, bool clipped)
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Max(0, now - lastUpdate);
        lastUpdate = now;
        for (int i = 0; i < shown.Length; i++)
        {
            double lin = i < linearPeaks.Length ? linearPeaks[i] : 0;
            double db = lin <= 0.000001 ? MinDb : Math.Max(MinDb, 20 * Math.Log10(lin));
            if (db >= shown[i]) shown[i] = db;
            else shown[i] = Math.Max(MinDb, shown[i] - FallDbPerSec * dt);
            if (db >= peak[i] || now - peakAt[i] > PeakHoldSec)
            {
                if (db >= peak[i]) { peak[i] = db; peakAt[i] = now; }
                else peak[i] = Math.Max(shown[i], peak[i] - FallDbPerSec * dt);
            }
            if (lin >= 0.999) clipUntil[i] = now + ClipHoldSec;
        }
        if (clipped && shown.Length > 0) for (int i = 0; i < clipUntil.Length; i++) if (enabled[i]) clipUntil[i] = Math.Max(clipUntil[i], now + ClipHoldSec);
        InvalidateVisual();
    }

    public void Reset()
    {
        for (int i = 0; i < shown.Length; i++) { shown[i] = MinDb; peak[i] = MinDb; clipUntil[i] = 0; }
        InvalidateVisual();
    }

    private static double X(double db, double w) => (Math.Max(MinDb, Math.Min(0, db)) - MinDb) / -MinDb * w;

    private void EnsureText(double dpi)
    {
        if (textDpi == dpi && ReferenceEquals(textBrushUsed, TextBrush) && labelText.Length == labels.Length) return;
        var brush = TextBrush;
        labelText = new FormattedText[labels.Length];
        for (int i = 0; i < labels.Length; i++)
            labelText[i] = new FormattedText(labels[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 11, brush, dpi);
        tickText = new FormattedText[TickDbs.Length];
        for (int i = 0; i < TickDbs.Length; i++)
            tickText[i] = new FormattedText(TickDbs[i].ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 9, brush, dpi);
        textDpi = dpi;
        textBrushUsed = brush;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 4) return;
        const double labelW = 44, clipW = 12;
        double barX = labelW, barW = w - labelW - clipW - 4;
        if (barW < 10) return;
        double now = clock.Elapsed.TotalSeconds;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        EnsureText(dpi);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // どこを押してもクリックを受け取る

        for (int i = 0; i < shown.Length; i++)
        {
            double y = i * (RowHeight + 2);
            var label = labelText[i];
            dc.DrawText(label, new Point(0, y + (RowHeight - label.Height) / 2));
            dc.DrawRectangle(BackBrush, null, new Rect(barX, y, barW, RowHeight));
            if (!enabled[i])
            {
                dc.DrawRectangle(OffBrush, null, new Rect(barX, y + RowHeight / 2 - 0.5, barW, 1));
                continue;
            }
            double xv = X(shown[i], barW);
            double x1 = X(-18, barW), x2 = X(-6, barW);
            if (xv > 0) dc.DrawRectangle(GoodBrush, null, new Rect(barX, y, Math.Min(xv, x1), RowHeight));
            if (xv > x1) dc.DrawRectangle(WarnBrush, null, new Rect(barX + x1, y, Math.Min(xv, x2) - x1, RowHeight));
            if (xv > x2) dc.DrawRectangle(HotBrush, null, new Rect(barX + x2, y, xv - x2, RowHeight));
            double xp = X(peak[i], barW);
            if (xp > 1) dc.DrawRectangle(TextBrush, null, new Rect(barX + xp - 1, y, 2, RowHeight));
            bool clip = now < clipUntil[i];
            dc.DrawRectangle(clip ? HotBrush : OffBrush, null, new Rect(barX + barW + 3, y, clipW, RowHeight));
        }

        // 目盛り
        double ty = shown.Length * (RowHeight + 2);
        for (int ti = 0; ti < TickDbs.Length; ti++)
        {
            int db = TickDbs[ti];
            var t = tickText[ti];
            double x = barX + X(db, barW);
            double tx = Math.Min(Math.Max(barX, x - t.Width / 2), barX + barW - t.Width);
            dc.DrawText(t, new Point(tx, ty));
        }
    }
}
