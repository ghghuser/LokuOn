using System.Windows;
using System.Windows.Media;
using LokuOn.Core;

namespace LokuOn.App.Controls;

/// <summary>録音中(または入力確認中)の直近の波形を流れるように表示する。</summary>
public sealed class WaveformView : FrameworkElement
{
    private float[] mins = new float[0];
    private float[] maxs = new float[0];
    private int bucketCount;
    private double secondsShown;

    public Brush BackBrush { get; set; } = Brushes.Black;
    public Brush WaveBrush { get; set; } = Brushes.LimeGreen;
    public Brush AxisBrush { get; set; } = Brushes.Gray;

    /// <summary>表示を更新する(UIタイマーから呼ぶ)。null なら空表示。</summary>
    public void Refresh(WaveformBuffer? source)
    {
        if (source == null)
        {
            if (bucketCount != 0) { bucketCount = 0; InvalidateVisual(); }
            return;
        }
        int n = source.Capacity;
        if (mins.Length != n) { mins = new float[n]; maxs = new float[n]; }
        source.Snapshot(n, mins, maxs);
        bucketCount = n;
        secondsShown = n * source.BucketSeconds;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) return;
        dc.DrawRectangle(BackBrush, null, new Rect(0, 0, w, h));
        double mid = h / 2;
        var axis = new Pen(AxisBrush, 1);
        dc.DrawLine(axis, new Point(0, mid), new Point(w, mid));
        if (bucketCount == 0) return;

        var pen = new Pen(WaveBrush, 1);
        int cols = (int)w;
        for (int x = 0; x < cols; x++)
        {
            int a = (int)((long)x * bucketCount / cols);
            int b = (int)((long)(x + 1) * bucketCount / cols);
            if (b <= a) b = a + 1;
            if (b > bucketCount) b = bucketCount;
            float lo = 0, hi = 0;
            for (int i = a; i < b; i++)
            {
                if (mins[i] < lo) lo = mins[i];
                if (maxs[i] > hi) hi = maxs[i];
            }
            if (hi <= 0 && lo >= 0) continue;
            // 小さい音も見えるように、振幅を少し持ち上げて表示する
            double yHi = mid - Lift(hi) * (mid - 2);
            double yLo = mid - Lift(lo) * (mid - 2);
            if (yLo - yHi < 1) yLo = yHi + 1;
            dc.DrawLine(pen, new Point(x + 0.5, yHi), new Point(x + 0.5, yLo));
        }
    }

    private static double Lift(float v)
    {
        double a = Math.Min(1.0, Math.Abs((double)v));
        double l = Math.Pow(a, 0.6);
        return v < 0 ? -l : l;
    }
}
