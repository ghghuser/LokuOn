namespace LokuOn.Core;

/// <summary>MixBus.Process の結果(このまとまりの中の最大値と、切り詰めがあったか)。</summary>
public struct MixStats
{
    public float PeakL, PeakR;
    public bool Clipped;
}

/// <summary>
/// ミキサー1本ぶん(バス)。複数の入力を、入力ごとの「送り」(オンオフと音量)に従って足し合わせ、
/// 全体音量をかけて、±1 を超えた分は切り詰めて(切り詰めたことは記録して)書き出す。
/// 録音のミックスと、これから作る出力デバイスごとのミックスで、同じ計算を使う。
/// </summary>
public sealed class MixBus
{
    private readonly bool[] on;
    private readonly float[] gain;      // 送りの音量(リニア)。1 のときは掛け算を省く
    private volatile float master = 1f; // 全体音量(リニア)

    public int InputCount { get; }

    /// <param name="inputCount">入力の数</param>
    /// <param name="allOn">最初にすべての入力を送るか</param>
    public MixBus(int inputCount, bool allOn = true)
    {
        InputCount = inputCount;
        on = new bool[inputCount];
        gain = new float[inputCount];
        for (int i = 0; i < inputCount; i++) { on[i] = allOn; gain[i] = 1f; }
    }

    /// <summary>全体音量(リニア。1=そのまま)。</summary>
    public float MasterGain { get => master; set => master = value; }

    /// <summary>入力 input をこのバスに送るか、送る音量(リニア)を決める。範囲外の入力は無視する。</summary>
    public void SetSend(int input, bool isOn, float linearGain = 1f)
    {
        if ((uint)input >= (uint)InputCount) return;
        gain[input] = linearGain;
        on[input] = isOn;
    }

    public bool IsSendOn(int input) => (uint)input < (uint)InputCount && on[input];
    public float SendGain(int input) => (uint)input < (uint)InputCount ? gain[input] : 0f;

    /// <summary>
    /// 入力を足し合わせて dest に書く。inputs[i] は frames*ch サンプル(インターリーブ、入力ゲイン適用済み)。
    /// 全体音量をかけたあと、先頭の2チャンネル(モノラルは1チャンネル)だけ±1に切り詰める。
    /// mono を渡すと、(L+R)/2 を frames 個書く(波形表示用)。post を渡すと、全体音量のあと・切り詰めの前に呼ぶ(出力ごとのEQ・リミッター用)。
    /// </summary>
    public MixStats Process(float[][] inputs, int frames, int ch, float[] dest, float[]? mono = null, Action<float[], int>? post = null)
    {
        int samples = frames * ch;
        Array.Clear(dest, 0, samples);
        int count = Math.Min(inputs.Length, InputCount);
        for (int i = 0; i < count; i++)
        {
            if (!on[i]) continue;
            var b = inputs[i];
            float g = gain[i];
            if (g == 1f) { for (int j = 0; j < samples; j++) dest[j] += b[j]; }
            else if (g != 0f) { for (int j = 0; j < samples; j++) dest[j] += b[j] * g; }
        }

        float gm = master;
        if (post != null)
        {
            // 全体音量をかけたあと、切り詰める前にエフェクト(EQ・リミッター)を通す
            if (gm != 1f) for (int j = 0; j < samples; j++) dest[j] *= gm;
            post(dest, frames);
            gm = 1f;
        }
        var st = new MixStats();
        float pl = 0, pr = 0;
        for (int f = 0; f < frames; f++)
        {
            float l = dest[f * ch] * gm;
            float r = ch > 1 ? dest[f * ch + 1] * gm : l;
            if (l > 1f || l < -1f) { st.Clipped = true; l = l > 0 ? 1f : -1f; }
            if (r > 1f || r < -1f) { st.Clipped = true; r = r > 0 ? 1f : -1f; }
            dest[f * ch] = l; if (ch > 1) dest[f * ch + 1] = r;
            if (mono != null) mono[f] = ch > 1 ? (l + r) * 0.5f : l;
            float al = l < 0 ? -l : l, ar = r < 0 ? -r : r;
            if (al > pl) pl = al;
            if (ar > pr) pr = ar;
        }
        st.PeakL = pl; st.PeakR = pr;
        return st;
    }
}
