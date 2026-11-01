namespace LokuOn.Core;

/// <summary>画面表示用の波形(一定時間ごとの最小値・最大値)を保持する。</summary>
public sealed class WaveformBuffer
{
    private readonly object gate = new();
    private readonly float[] mins, maxs;
    private readonly int bucketFrames;
    private int head, count;
    private float curMin = float.MaxValue, curMax = float.MinValue;
    private int curFrames;

    public int Capacity { get; }
    public double BucketSeconds { get; }

    public WaveformBuffer(int sampleRate, double bucketSeconds = 0.04, int capacity = 1500)
    {
        BucketSeconds = bucketSeconds;
        bucketFrames = Math.Max(1, (int)(sampleRate * bucketSeconds));
        Capacity = capacity;
        mins = new float[capacity];
        maxs = new float[capacity];
    }

    /// <summary>mono(1ch)のサンプルを追加。</summary>
    public void Add(float[] mono, int frames)
    {
        lock (gate)
        {
            for (int i = 0; i < frames; i++)
            {
                float v = mono[i];
                if (v < curMin) curMin = v;
                if (v > curMax) curMax = v;
                if (++curFrames >= bucketFrames)
                {
                    mins[head] = curMin; maxs[head] = curMax;
                    head = (head + 1) % Capacity;
                    if (count < Capacity) count++;
                    curMin = float.MaxValue; curMax = float.MinValue; curFrames = 0;
                }
            }
        }
    }

    public void Clear() { lock (gate) { head = 0; count = 0; curFrames = 0; curMin = float.MaxValue; curMax = float.MinValue; } }

    /// <summary>直近 n 個(古い→新しい)を取り出す。足りない分は 0。</summary>
    public void Snapshot(int n, float[] minOut, float[] maxOut)
    {
        lock (gate)
        {
            for (int i = 0; i < n; i++)
            {
                int age = n - 1 - i;            // 0=最新
                if (age < count)
                {
                    int idx = ((head - 1 - age) % Capacity + Capacity) % Capacity;
                    minOut[i] = mins[idx]; maxOut[i] = maxs[idx];
                }
                else { minOut[i] = 0; maxOut[i] = 0; }
            }
        }
    }
}
