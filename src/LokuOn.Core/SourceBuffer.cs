namespace LokuOn.Core;

/// <summary>
/// 1つの入力デバイスから届いた音声(エンジンの形式に変換済みのfloat)を一時的にためるバッファ。
/// デバイス側スレッドが Push、録音エンジンのスレッドが Read する。
/// </summary>
public sealed class SourceBuffer
{
    private readonly object gate = new();
    private readonly float[] ring;
    private int head;       // 読み出し位置(サンプル単位)
    private int count;      // 入っているサンプル数

    public int SampleRate { get; }
    public int Channels { get; }
    /// <summary>画面表示・{Device}変数に使う名前。</summary>
    public string Label { get; set; } = "";
    /// <summary>デバイスが現在つながっているか(デバイス側が更新)。</summary>
    public volatile bool Connected = true;
    /// <summary>入力ゲイン(dB)。録音中も変更できる。</summary>
    public volatile float GainDb;

    public long DroppedFrames;

    public SourceBuffer(int sampleRate, int channels, double seconds = 4.0)
    {
        SampleRate = sampleRate;
        Channels = channels;
        ring = new float[(int)(sampleRate * seconds) * channels];
    }

    public int AvailableFrames { get { lock (gate) return count / Channels; } }

    public void Clear() { lock (gate) { head = 0; count = 0; } }

    public void Push(float[] data, int offset, int frames)
    {
        int n = frames * Channels;
        if (n <= 0) return;
        lock (gate)
        {
            if (n >= ring.Length)
            {
                // 大きすぎる入力は末尾だけ残す
                DroppedFrames += (count + n - ring.Length) / Channels;
                Array.Copy(data, offset + n - ring.Length, ring, 0, ring.Length);
                head = 0; count = ring.Length;
                return;
            }
            int overflow = count + n - ring.Length;
            if (overflow > 0)
            {
                overflow = (overflow + Channels - 1) / Channels * Channels;
                head = (head + overflow) % ring.Length;
                count -= overflow;
                DroppedFrames += overflow / Channels;
            }
            int tail = (head + count) % ring.Length;
            int first = Math.Min(n, ring.Length - tail);
            Array.Copy(data, offset, ring, tail, first);
            if (first < n) Array.Copy(data, offset + first, ring, 0, n - first);
            count += n;
        }
    }

    /// <summary>最大 frames フレームを読み出し、実際に読めたフレーム数を返す。</summary>
    public int Read(float[] dst, int dstOffset, int frames)
    {
        lock (gate)
        {
            int n = Math.Min(frames * Channels, count);
            n -= n % Channels;
            int first = Math.Min(n, ring.Length - head);
            Array.Copy(ring, head, dst, dstOffset, first);
            if (first < n) Array.Copy(ring, 0, dst, dstOffset + first, n - first);
            head = (head + n) % ring.Length;
            count -= n;
            return n / Channels;
        }
    }

    /// <summary>たまりすぎた分(クロックずれ)を古い方から捨てて、残りを keepFrames フレームにする。</summary>
    public void DiscardTo(int keepFrames)
    {
        lock (gate)
        {
            int keep = keepFrames * Channels;
            if (count <= keep) return;
            int drop = count - keep;
            head = (head + drop) % ring.Length;
            count -= drop;
            DroppedFrames += drop / Channels;
        }
    }
}
