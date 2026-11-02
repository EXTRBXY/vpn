using System.Globalization;
using System.Windows.Data;
using NothingVpn.Application.Models;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

public sealed class ProfileEngineLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is VpnProfile profile ? ProfileEngineLabel.For(profile) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
