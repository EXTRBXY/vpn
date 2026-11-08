using System.IO.Compression;

namespace NothingVpn.Infrastructure.RuleSets;

/// <summary>
/// Читает только структуру SRS v1–4 (sing-box 1.13.21), не сопоставляет правила и адреса.
/// Формат: SagerNet/sing-box common/srs/binary.go; доменные блоки: sing common/domain/set.go.
/// </summary>
internal sealed class SrsRuleSetInspector(Stream stream)
{
    private const int MaxExpandedBytes = 128 * 1024 * 1024;
    private int _remainingBytes = MaxExpandedBytes;

    public static bool HasIpConditions(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            file.ReadExactly(header);
            if (!header[..3].SequenceEqual("SRS"u8) || header[3] is < 1 or > 4)
                throw new InvalidDataException("Неподдерживаемый формат SRS.");

            using var decompressed = new ZLibStream(file, CompressionMode.Decompress);
            return new SrsRuleSetInspector(decompressed).ReadRules(0);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OverflowException)
        {
            throw new InvalidOperationException($"Не удалось прочитать список «{Path.GetFileName(path)}»: {ex.Message}", ex);
        }
    }

    private bool ReadRules(int depth)
    {
        if (depth > 100)
            throw new InvalidDataException("Слишком глубокая вложенность правил SRS.");

        var count = ReadLength();
        for (var i = 0; i < count; i++)
        {
            switch (ReadByte())
            {
                case 0:
                    if (ReadDefaultRule()) return true;
                    break;
                case 1:
                    if (ReadByte() > 1) throw new InvalidDataException("Неизвестная операция SRS.");
                    if (ReadRules(depth + 1)) return true;
                    ReadByte(); // invert
                    break;
                default:
                    throw new InvalidDataException("Неизвестный тип правила SRS.");
            }
        }
        return false;
    }

    private bool ReadDefaultRule()
    {
        while (true)
        {
            switch (ReadByte())
            {
                case 5: // source_ip_cidr
                case 6: // ip_cidr
                case 21: // network_interface_address (v4)
                case 22: // default_interface_address (v4)
                    return true;
                case 2: // domain / domain_suffix: compiled succinct set
                case 16: // AdGuard domain matcher: same serialized set
                    ReadByte();
                    Skip(checked(ReadLength() * 8)); // leaves
                    Skip(checked(ReadLength() * 8)); // label bitmap
                    Skip(ReadLength()); // labels
                    break;
                case 0: // query_type
                case 7: // source_port
                case 9: // port
                    Skip(checked(ReadLength() * 2));
                    break;
                case 18: // network_type
                    Skip(ReadLength());
                    break;
                case 19: // network_is_expensive
                case 20: // network_is_constrained
                    break;
                case 1: // network
                case 3: // domain_keyword
                case 4: // domain_regex
                case 8: // source_port_range
                case 10: // port_range
                case 11: // process_name
                case 12: // process_path
                case 13: // package_name
                case 14: // wifi_ssid
                case 15: // wifi_bssid
                case 17: // process_path_regex
                    var count = ReadLength();
                    for (var i = 0; i < count; i++) Skip(ReadLength());
                    break;
                case 255:
                    ReadByte(); // invert
                    return false;
                default:
                    throw new InvalidDataException("Неизвестное условие SRS.");
            }
        }
    }

    private int ReadLength()
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var next = ReadByte();
            if (shift == 63 && next > 1) break;
            value |= (ulong)(next & 127) << shift;
            if ((next & 128) == 0)
            {
                if (value <= MaxExpandedBytes) return (int)value;
                break;
            }
        }
        throw new InvalidDataException("Превышен допустимый размер SRS.");
    }

    private byte ReadByte()
    {
        if (--_remainingBytes < 0) throw new InvalidDataException("Превышен допустимый размер SRS.");
        var value = stream.ReadByte();
        return value < 0 ? throw new EndOfStreamException("Список SRS оборван.") : (byte)value;
    }

    private void Skip(int count)
    {
        if (count > _remainingBytes) throw new InvalidDataException("Превышен допустимый размер SRS.");
        _remainingBytes -= count;
        Span<byte> buffer = stackalloc byte[4096];
        while (count > 0)
        {
            var chunk = Math.Min(count, buffer.Length);
            stream.ReadExactly(buffer[..chunk]);
            count -= chunk;
        }
    }
}
