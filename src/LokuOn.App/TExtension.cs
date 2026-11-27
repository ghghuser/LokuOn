using System.Windows.Markup;
using LokuOn.Core;

namespace LokuOn.App;

/// <summary>XAML から文言を使う拡張。例: Content="{ui:T main.record.start}"(xmlns:ui="clr-namespace:LokuOn.App")。</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; set; }

    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
