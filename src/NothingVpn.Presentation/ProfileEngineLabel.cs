using NothingVpn.Application.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Presentation;

/// <summary>Подпись движка профиля в списках: тот же выбор, что сделает ядро (<see cref="OutboundEnginePolicy"/>).</summary>
public static class ProfileEngineLabel
{
    public static string For(VpnProfile profile)
    {
        try
        {
            return OutboundEnginePolicy.DisplayName(
                OutboundEnginePolicy.Resolve(profile.Engine, profile.Type, profile.Security));
        }
        catch (ArgumentException)
        {
            return "Несовместимый движок";
        }
    }
}
