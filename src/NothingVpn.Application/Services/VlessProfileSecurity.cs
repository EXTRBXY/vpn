using NothingVpn.Application.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public static class VlessProfileSecurity
{
    // На диске ECH остаётся в extraQuery: сохраняем существующую схему и экспорт ссылок.
    public static string? Ech(VpnProfile profile) =>
        profile.ExtraQuery.TryGetValue("ech", out var value) ? value : null;

    public static void Validate(VpnProfile profile) =>
        VlessSecurityPolicy.Validate(profile.Encryption, Ech(profile), profile.Security);
}
