namespace NothingVpn.Domain.Policies;

/// <summary>
/// Каноническое строковое представление enum-настройки (state.json, аргументы командной строки, конфиг sing-box)
/// и толерантный разбор: регистр и пробелы по краям не важны, неизвестное значение даёт <see cref="Fallback"/>.
/// Строки — внешний контракт (docs/REFACTORING.md, раздел 1): менять их можно только с миграцией.
/// </summary>
public sealed class SettingValueMap<T> where T : struct, Enum
{
    private readonly Dictionary<T, string> _values;
    private readonly Dictionary<string, T> _byText = new(StringComparer.OrdinalIgnoreCase);

    public SettingValueMap(T fallback, IReadOnlyDictionary<T, string> values, IReadOnlyDictionary<string, T>? aliases = null)
    {
        if (!values.ContainsKey(fallback))
            throw new ArgumentException("Значение по умолчанию должно иметь строковое представление.", nameof(fallback));

        Fallback = fallback;
        _values = new Dictionary<T, string>(values);
        foreach (var (value, text) in values)
            _byText.Add(text, value);
        foreach (var (text, value) in aliases ?? new Dictionary<string, T>())
            _byText.Add(text, value);
    }

    /// <summary>Значение для неизвестной, пустой (если пустая строка не задана явно) или отсутствующей строки.</summary>
    public T Fallback { get; }

    public IReadOnlyCollection<T> Values => _values.Keys;

    public bool IsDefined(T value) => _values.ContainsKey(value);

    /// <summary>Неопределённое числовое значение enum приводится к <see cref="Fallback"/>.</summary>
    public T Normalize(T value) => IsDefined(value) ? value : Fallback;

    public string ToValue(T value) => _values[Normalize(value)];

    public bool TryParse(string? text, out T value) =>
        _byText.TryGetValue((text ?? string.Empty).Trim(), out value);

    public T Parse(string? text) => TryParse(text, out var value) ? value : Fallback;
}
