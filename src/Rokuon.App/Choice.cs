namespace Rokuon.App;

/// <summary>プルダウンに出す「表示名と値」の組。</summary>
public sealed class Choice<T>
{
    public string Display { get; }
    public T Value { get; }
    public Choice(string display, T value) { Display = display; Value = value; }
    public override string ToString() => Display;
}
