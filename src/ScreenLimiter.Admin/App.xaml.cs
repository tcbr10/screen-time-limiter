using System.Windows;
using System.Windows.Markup;
using ScreenLimiter.Core;

namespace ScreenLimiter.Admin;

public partial class App : Application;

/// <summary>Lets XAML pull text from the shared Hebrew/English table: <c>Text="{local:T edit.save}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
