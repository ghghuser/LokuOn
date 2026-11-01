namespace LokuOn.Core;

/// <summary>
/// ストリーム用のサンプルレート変換(窓関数付きsinc補間)。
/// 入力を少しずつ渡して Process を呼ぶと、変換済みの出力が得られる。
/// </summary>
public sealed class StreamResampler
{
    private const int Oversample = 512;
    private const int ZeroCrossings = 16;

    private readonly int channels;
    private readonly double step;           // 出力1サンプルあたりの入力位置の進み
    private readonly int halfWidth;         // 片側のタップ数(入力サンプル単位)
    private readonly float[] table;         // カーネルの表(0..halfWidth)
    private readonly double tableScale;

    private float[] buf = new float[0];     // 入力フレームのバッファ(インターリーブ)
    private int bufFrames;
    private long baseIndex;                 // buf[0] の絶対入力フレーム番号
    private double pos;                     // 次に出力するサンプルの絶対入力位置

    public bool IsPassThrough { get; }

    public StreamResampler(int inRate, int outRate, int channels)
    {
        this.channels = channels;
        IsPassThrough = inRate == outRate;
        step = (double)inRate / outRate;
        double cutoff = 0.5 * Math.Min(1.0, (double)outRate / inRate) * 0.97;   // 入力サンプル間隔あたりのカットオフ
        halfWidth = (int)Math.Ceiling(ZeroCrossings / (2 * cutoff));
        tableScale = Oversample;
        table = new float[halfWidth * Oversample + 2];
        for (int i = 0; i < table.Length; i++)
        {
            double t = (double)i / Oversample;
            double x = 2 * cutoff * t;
            double sinc = t == 0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
            double w = t >= halfWidth ? 0 : 0.42 + 0.5 * Math.Cos(Math.PI * t / halfWidth) + 0.08 * Math.Cos(2 * Math.PI * t / halfWidth);
            table[i] = (float)(2 * cutoff * sinc * w);
        }
        // 最初の入力の前は無音として扱う
        baseIndex = -halfWidth;
        buf = new float[(halfWidth + 4096) * channels];
        bufFrames = halfWidth;
    }

    /// <summary>input(インターリーブ)を渡し、変換結果を output に追加して返す。</summary>
    public void Process(float[] input, int inputFrames, List<float> output)
    {
        if (IsPassThrough)
        {
            for (int i = 0; i < inputFrames * channels; i++) output.Add(input[i]);
            return;
        }
        int need = (bufFrames + inputFrames) * channels;
        if (buf.Length < need) Array.Resize(ref buf, Math.Max(need, buf.Length * 2));
        Array.Copy(input, 0, buf, bufFrames * channels, inputFrames * channels);
        bufFrames += inputFrames;

        long endIndex = baseIndex + bufFrames;   // 絶対位置で endIndex 未満のフレームが使える
        while (true)
        {
            long i0 = (long)Math.Floor(pos);
            if (i0 + halfWidth >= endIndex) break;
            double frac = pos - i0;
            for (int ch = 0; ch < channels; ch++)
            {
                double sum = 0;
                for (int k = -halfWidth + 1; k <= halfWidth; k++)
                {
                    double t = Math.Abs(k - frac);
                    double ti = t * tableScale;
                    int ii = (int)ti;
                    if (ii >= table.Length - 1) continue;
                    float h = table[ii] + (table[ii + 1] - table[ii]) * (float)(ti - ii);
                    sum += buf[(int)(i0 + k - baseIndex) * channels + ch] * h;
                }
                output.Add((float)sum);
            }
            pos += step;
        }

        // 不要になった過去分を捨てる
        long keepFrom = (long)Math.Floor(pos) - halfWidth;
        int drop = (int)(keepFrom - baseIndex);
        if (drop > 0)
        {
            int remain = bufFrames - drop;
            Array.Copy(buf, drop * channels, buf, 0, remain * channels);
            bufFrames = remain;
            baseIndex += drop;
        }
    }
}
