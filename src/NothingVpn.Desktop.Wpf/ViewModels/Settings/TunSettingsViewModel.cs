using System.Globalization;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Desktop.Wpf;

public sealed class TunSettingsViewModel : SettingsSection
{
    public string InterfaceName { get; set; } = "";
    public string AddressCidr { get; set; } = "auto";

    public int Mtu
    {
        get => int.TryParse(MtuText, out var value) ? value : 0;
        set => MtuText = value.ToString(CultureInfo.InvariantCulture);
    }

    public string MtuText { get; set; } = "1500";
    public TunStack Stack { get; set; } = TunSettingsPolicy.DefaultStack;
    public bool AutoRoute { get; set; }
    public bool StrictRoute { get; set; }

    internal void Load(AppStateModel state)
    {
        InterfaceName = state.TunInterfaceName;
        AddressCidr = state.TunAddressCidr;
        Mtu = TunSettingsPolicy.NormalizeMtu(state.TunMtu);
        Stack = ComboStack(state.TunStack);
        AutoRoute = state.TunAutoRoute;
        StrictRoute = state.TunStrictRoute;
        OnPropertiesChanged(
            nameof(InterfaceName), nameof(AddressCidr), nameof(Mtu), nameof(MtuText),
            nameof(Stack), nameof(AutoRoute), nameof(StrictRoute));
    }

    /// <summary>MTU проверяется по введённому тексту: нечисловое значение не должно молча стать числом.</summary>
    internal TunSettings ToSettings()
    {
        if (!int.TryParse(MtuText, out var mtu) || mtu < TunSettingsPolicy.MinMtu || mtu > TunSettingsPolicy.MaxMtu)
            throw new InvalidOperationException(
                $"MTU должен быть целым числом от {TunSettingsPolicy.MinMtu} до {TunSettingsPolicy.MaxMtu}.");

        return new TunSettings
        {
            InterfaceName = InterfaceName,
            AddressCidr = AddressCidr,
            Mtu = mtu,
            Stack = Stack,
            AutoRoute = AutoRoute,
            StrictRoute = StrictRoute
        };
    }

    // В списке стеков нет отдельного «system»: явный system показывается и сохраняется как стек по умолчанию.
    private static TunStack ComboStack(TunStack stack) =>
        stack is TunStack.Mixed or TunStack.Gvisor ? stack : TunStack.Default;
}
