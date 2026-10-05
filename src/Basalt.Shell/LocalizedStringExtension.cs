using Avalonia.Markup.Xaml;
using Basalt.Core.Localization;

namespace Basalt.Shell;

public sealed class LocalizedStringExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Localizer.Get(key);
}
