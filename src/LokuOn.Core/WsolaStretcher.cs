namespace LokuOn.Core;

/// <summary>
/// 音程を変えずに再生速度(テンポ)を変える(WSOLA方式)。再生用。
/// 入力は「読み出し関数」(float交互配置の音声を返し、終わりで0を返す)。0.5〜2倍の範囲で使う。
/// </summary>
public sealed class WsolaStretcher
{
    private readonly Func<float[], int, int, int> source;
    private readonly int ch;
    private readonly int hs;        // 出力の進み(=重ね幅)
    private readonly int n;         // 1フレームの長さ(=2*hs)
    private readonly int search;    // 探す範囲(±フレーム)
    private readonly float[] win;
    private readonly int sampleRate;

    private float[] inBuf = Array.Empty<float>();   // 交互配置
    private float[] mono = Array.Empty<float>();
    private long inBase;       // inBuf[0] の絶対フレーム位置
    private int inFrames;      // 持っているフレーム数
    private bool eof;
    private long totalIn = long.MaxValue;

    private double pos;        // 次のフレームの理想の入力位置(フレーム)
    private long prevStart = -1;
    private readonly float[] acc;
    private float[] outQ = Array.Empty<float>();
    private int outCount, outPos;
    private bool finished;
    private bool first = true;
    private readonly float[] tmp;

    /// <summary>テンポ(1.0=等倍、2.0=2倍速)。再生中に変えてよい。</summary>
    public double Tempo { get; set; } = 1.0;

    /// <summary>いま出している音が入力のどのあたりか(秒)。</summary>
    public double PositionSeconds => Math.Max(0, pos - hs * Tempo) / sampleRate;

    public WsolaStretcher(Func<float[], int, int, int> read, int sampleRate, int channels)
    {
        source = read;
        this.sampleRate = sampleRate;
        ch = Math.Max(1, channels);
        hs = Math.Max(64, (int)(sampleRate * 0.020));
        n = hs * 2;
        search = Math.Max(16, (int)(sampleRate * 0.010));
        win = new float[n];
        for (int i = 0; i < n; i++) win[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / n)));
        acc = new float[n * ch];
        tmp = new float[4096 * ch];
    }

    /// <summary>読み出し位置を変えたあとに呼ぶ。startFrame は新しい読み出し開始位置(フレーム)。</summary>
    public void Reset(long startFrame)
    {
        inFrames = 0; inBase = startFrame; eof = false; totalIn = long.MaxValue;
        pos = startFrame; prevStart = -1; first = true; finished = false;
        Array.Clear(acc);
        outCount = outPos = 0;
    }

    /// <summary>出力を count サンプル(交互配置)まで読む。終わりなら0。</summary>
    public int Read(float[] dest, int offset, int count)
    {
        int written = 0;
        while (written < count)
        {
            if (outPos >= outCount)
            {
                outPos = outCount = 0;
                if (finished || !ProduceFrame()) break;
            }
            int take = Math.Min(count - written, outCount - outPos);
            Array.Copy(outQ, outPos, dest, offset + written, take);
            outPos += take;
            written += take;
        }
        return written;
    }

    private void EnsureInput(long upToFrame)
    {
        while (!eof && inBase + inFrames < upToFrame)
        {
            int got = source(tmp, 0, tmp.Length);
            if (got <= 0) { eof = true; totalIn = inBase + inFrames; break; }
            int frames = got / ch;
            if (frames == 0) continue;
            int need = inFrames + frames;
            if (inBuf.Length < need * ch)
            {
                int cap = Math.Max(need * ch, inBuf.Length * 2);
                Array.Resize(ref inBuf, cap);
                Array.Resize(ref mono, cap / ch + 1);
            }
            Array.Copy(tmp, 0, inBuf, inFrames * ch, frames * ch);
            for (int f = 0; f < frames; f++)
            {
                float m = 0;
                for (int c = 0; c < ch; c++) m += tmp[f * ch + c];
                mono[inFrames + f] = m / ch;
            }
            inFrames += frames;
        }
    }

    private float Mono(long frame)
    {
        long i = frame - inBase;
        return (i < 0 || i >= inFrames) ? 0f : mono[i];
    }

    private bool ProduceFrame()
    {
        long target = (long)Math.Round(pos);
        long s = target;
        if (!first)
        {
            long lo = Math.Max(0, target - search), hi = target + search;
            if (lo < inBase) lo = inBase;
            EnsureInput(hi + n + 1);
            long c0 = prevStart + hs;      // 前のフレームの自然な続き
            double best = double.NegativeInfinity;
            long bestS = Math.Min(Math.Max(target, lo), hi);
            for (long cand = lo; cand <= hi; cand++)
            {
                if (cand >= totalIn) break;
                double dot = 0, e = 1e-9;
                for (int i = 0; i < hs; i += 2)
                {
                    float a = Mono(cand + i);
                    dot += a * Mono(c0 + i);
                    e += a * a;
                }
                double score = dot / Math.Sqrt(e);
                if (score > best) { best = score; bestS = cand; }
            }
            s = bestS;
        }
        else
        {
            EnsureInput(s + n + 1);
        }
        EnsureInput(s + n + 1);
        if (eof && s >= totalIn) { finished = true; return false; }

        // 窓をかけて重ねる
        for (int i = 0; i < n; i++)
        {
            float w = (first && i < hs) ? 1f : win[i];
            long fr = s + i - inBase;
            if (fr < 0 || fr >= inFrames) continue;
            for (int c = 0; c < ch; c++) acc[i * ch + c] += w * inBuf[fr * ch + c];
        }
        // 前半(hs フレーム)が確定
        if (outQ.Length < hs * ch) outQ = new float[hs * ch];
        Array.Copy(acc, 0, outQ, 0, hs * ch);
        outCount = hs * ch; outPos = 0;
        Array.Copy(acc, hs * ch, acc, 0, hs * ch);
        Array.Clear(acc, hs * ch, hs * ch);

        prevStart = s;
        first = false;
        pos += hs * Math.Clamp(Tempo, 0.25, 4.0);

        // 使い終わった入力を捨てる
        long keepFrom = Math.Min(prevStart + hs, (long)Math.Round(pos) - search) - 1;
        long drop = keepFrom - inBase;
        if (drop > 65536 && drop < inFrames)
        {
            int d = (int)drop;
            int remain = inFrames - d;
            Array.Copy(inBuf, d * ch, inBuf, 0, remain * ch);
            Array.Copy(mono, d, mono, 0, remain);
            inBase += d; inFrames = remain;
        }
        return true;
    }
}
