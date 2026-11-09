using System.Buffers.Binary;
using LokuOn.Core;

/// <summary>PCM(16/24/32bit整数・float)→float の変換が、1サンプルずつ読む素直な計算と一致することを確かめる。</summary>
static class PcmTests
{
    public static void Run(CheckFn check, Action<string> section)
    {
        section("PCM→float 変換(高速化後も結果が同じ)");
        var rnd = new Random(12345);
        var raw = new byte[4 * 4097 + 3];   // 端数のバイトがあっても切り捨てて変換すること
        rnd.NextBytes(raw);

        foreach (int bits in new[] { 16, 24, 32 })
        {
            int bps = bits / 8;
            int bytes = raw.Length;
            int n = bytes / bps;
            var expect = new float[n];
            for (int i = 0; i < n; i++)
            {
                int o = i * bps;
                expect[i] = bits switch
                {
                    16 => BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(o)) / 32768f,
                    24 => (raw[o] | (raw[o + 1] << 8) | ((sbyte)raw[o + 2] << 16)) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(o)) / 2147483648f,
                };
            }
            var dst = new float[n + 8];
            int got = PcmConverter.ToFloat(raw, bytes, bits, false, dst);
            bool same = got == n;
            for (int i = 0; same && i < n; i++) same = BitConverter.SingleToInt32Bits(dst[i]) == BitConverter.SingleToInt32Bits(expect[i]);
            check(same, $"{bits}bit整数が素直な計算とビット一致", $"got={got} n={n}");
        }

        var fl = new float[1000];
        for (int i = 0; i < fl.Length; i++) fl[i] = (float)(rnd.NextDouble() * 2 - 1);
        var fb = new byte[fl.Length * 4];
        Buffer.BlockCopy(fl, 0, fb, 0, fb.Length);
        var fd = new float[fl.Length];
        check(PcmConverter.ToFloat(fb, fb.Length, 32, true, fd) == fl.Length && fd.AsSpan().SequenceEqual(fl), "float32はそのまま");
    }
}
