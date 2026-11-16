using System.Text.RegularExpressions;

namespace NothingVpn.Domain.Policies;

/// <summary>Параметры VLESS Encryption и статического ECH для Xray 26.10.31.</summary>
public static class VlessSecurityPolicy
{
    public const int MaxEncryptionLength = 65536;
    public const int MaxEchLength = 90000;
    private const int EchDraftVersion = 0xfe0d;

    /// <summary>
    /// Xray нужен для VLESS Encryption и для ECH, который его TLS-стек действительно примет.
    /// Пустой или битый <c>ech</c> движок не переключает: Xray 26.10.31 на таком списке
    /// рвёт каждое соединение.
    /// </summary>
    public static bool RequiresXray(string? encryption, string? ech) =>
        encryption is not null and not "none" || IsAcceptableEchConfigList(ech);

    /// <summary>
    /// ECHConfigList, который <c>crypto/tls</c> Go 1.27 не отбросит целиком:
    /// внешняя длина сходится и есть хотя бы один config <c>0xfe0d</c> с непустым <c>public_name</c>.
    /// </summary>
    public static bool IsAcceptableEchConfigList(string? ech)
    {
        if (string.IsNullOrEmpty(ech) || ech.Length > MaxEchLength || ech.Any(char.IsWhiteSpace))
            return false;

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(ech);
        }
        catch (FormatException)
        {
            return false;
        }

        if (raw.Length < 4 || ReadU16(raw, 0) != raw.Length - 2)
            return false;

        var offset = 2;
        var acceptable = false;
        while (offset + 4 <= raw.Length)
        {
            var version = ReadU16(raw, offset);
            var length = ReadU16(raw, offset + 2);
            offset += 4;
            if (length > raw.Length - offset)
                return false;
            if (version == EchDraftVersion && HasPublicName(raw, offset, length))
                acceptable = true;
            offset += length;
        }

        return acceptable && offset == raw.Length;
    }

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

    private static bool HasPublicName(byte[] raw, int start, int length)
    {
        var end = start + length;
        var offset = start;
        if (length < 1 + 2 + 2)
            return false;
        offset += 1 + 2;
        var keyLength = ReadU16(raw, offset);
        offset += 2;
        if (keyLength < 1 || keyLength > end - offset)
            return false;
        offset += keyLength;
        if (end - offset < 2)
            return false;
        var suitesLength = ReadU16(raw, offset);
        offset += 2;
        if (suitesLength < 4 || (suitesLength & 3) != 0 || suitesLength > end - offset)
            return false;
        offset += suitesLength;
        if (end - offset < 2)
            return false;
        offset += 1;
        var nameLength = raw[offset];
        offset += 1;
        if (nameLength < 1 || nameLength > end - offset)
            return false;
        offset += nameLength;
        if (end - offset < 2)
            return false;
        var extensionsLength = ReadU16(raw, offset);
        offset += 2;
        return extensionsLength == end - offset;
    }

    private static int ReadU16(byte[] raw, int offset) => (raw[offset] << 8) | raw[offset + 1];
}
