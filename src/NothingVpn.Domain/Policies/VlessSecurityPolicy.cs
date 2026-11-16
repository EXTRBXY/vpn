using System.Text.RegularExpressions;

namespace NothingVpn.Domain.Policies;

/// <summary>Параметры VLESS Encryption и статического ECH для Xray 26.10.31.</summary>
public static class VlessSecurityPolicy
{
    public const int MaxEncryptionLength = 65536;
    public const int MaxEchLength = 90000;
    public static bool RequiresXray(string? encryption, string? ech) =>
        encryption is not null and not "none" || !string.IsNullOrEmpty(ech);

    public static void Validate(string? encryption, string? ech, string? security)
    {
        if (encryption != "none")
        {
            if (encryption is null || encryption.Length > MaxEncryptionLength)
                throw new ArgumentException("VLESS: неподдерживаемый формат encryption.");
            var parts = encryption.Split('.');
            if (parts.Length < 4
                || parts[0] != "mlkem768x25519plus"
                || parts[1] is not ("native" or "xorpub" or "random")
                || parts[2] is not ("0rtt" or "1rtt"))
                throw new ArgumentException("VLESS: неподдерживаемый формат encryption.");
            var hasKey = false;
            foreach (var part in parts.Skip(3))
            {
                if (part.Length < 20 && !hasKey && Regex.IsMatch(part, @"\A[0-9]+(?:-[0-9]+)*\z"))
                    continue;
                if (!Regex.IsMatch(part, @"\A[A-Za-z0-9_-]+\z"))
                    throw new ArgumentException("VLESS: некорректный ключ encryption.");
                try
                {
                    var padded = part.Replace('-', '+').Replace('_', '/');
                    padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
                    if (Convert.FromBase64String(padded).Length is not (32 or 1184))
                        throw new FormatException();
                }
                catch (FormatException)
                {
                    throw new ArgumentException("VLESS: некорректный ключ encryption.");
                }
                hasKey = true;
            }
            if (!hasKey)
                throw new ArgumentException("VLESS: отсутствует ключ encryption.");
        }
        if (string.IsNullOrEmpty(ech))
            return;
        if (security != "tls")
            throw new ArgumentException("ECH требует security=tls.");
        try
        {
            if (ech.Length > MaxEchLength || ech.Any(char.IsWhiteSpace) || Convert.FromBase64String(ech).Length == 0)
                throw new FormatException();
        }
        catch (FormatException)
        {
            throw new ArgumentException("ECH: ожидается статический echConfigList в Base64.");
        }
    }
}
