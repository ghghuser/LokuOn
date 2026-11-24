namespace LokuOn.Core;

/// <summary>小数の「同じ」判定。設定値などの比較で、== を直接使わないためのもの(誤差が 1e-9 以内なら同じ)。</summary>
public static class Fp
{
    public static bool Eq(double a, double b) => Math.Abs(a - b) <= 1e-9;
}
