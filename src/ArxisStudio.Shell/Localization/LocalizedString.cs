using System.ComponentModel;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Одна строка интерфейса, следящая за языком. Привязки смотрят на
/// <see cref="Value"/> — обычное свойство: привязка к индексатору
/// <see cref="Localizer"/> обновления не получает, каким бы именем он о себе ни
/// сообщал, поэтому расширение разметки выдаёт вот такой объект.
/// </summary>
/// <remarks>
/// Откуда взять текст, строка не решает: источник ей выдают при создании. Так
/// заголовок панели плагина берётся из словарей самого плагина, а обновляется
/// при смене языка тем же способом, что и весь остальной интерфейс.
/// </remarks>
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly IStringSource _source;

    internal LocalizedString(IStringSource source, string key)
    {
        _source = source;
        Key = key;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Ключ строки в словаре.</summary>
    public string Key { get; }

    /// <summary>Строка на текущем языке.</summary>
    public string Value => _source[Key];

    /// <inheritdoc/>
    public override string ToString() => Value;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
