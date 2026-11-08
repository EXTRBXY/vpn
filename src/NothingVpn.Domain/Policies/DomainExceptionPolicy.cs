using System.Globalization;
using System.Net;

namespace NothingVpn.Domain.Policies;

public static class DomainExceptionPolicy
{
    public const int MaxEntries = 500;
    public const int MaxDomainLength = 253;
    public const bool DefaultIncludeSubdomains = true;

    public static string NormalizeDomain(string? value)
    {
        var domain = (value ?? string.Empty).Trim();
        if (domain.EndsWith('.')) domain = domain[..^1];
        if (domain.Length == 0 || domain.Length > MaxDomainLength)
            throw new ArgumentException("Укажите домен без протокола, пути и порта, например hitmos.fm.");
        try { domain = new IdnMapping().GetAscii(domain).ToLowerInvariant(); }
        catch (ArgumentException) { throw new ArgumentException("Некорректное доменное имя."); }
        var labels = domain.Split('.');
        if (domain.Length > MaxDomainLength || labels.Length < 2 || IPAddress.TryParse(domain, out _)
            || labels.Any(label => label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-'
                || label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("Укажите домен без протокола, пути и порта, например hitmos.fm.");
        return domain;
    }

    public static void Validate(IEnumerable<string?> domains)
    {
        var count = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var domain in domains)
        {
            if (++count > MaxEntries)
                throw new ArgumentException($"Допускается не более {MaxEntries} исключений.");
            if (!seen.Add(NormalizeDomain(domain)))
                throw new ArgumentException("Этот домен уже добавлен в исключения.");
        }
    }
}
