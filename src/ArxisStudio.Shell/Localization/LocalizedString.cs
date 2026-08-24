using System.ComponentModel;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Одна строка интерфейса, следящая за языком. Привязки смотрят на
/// <see cref="Value"/> — обычное свойство: привязка к индексатору
/// <see cref="Localizer"/> обновления не получает, каким бы именем он о себе ни
/// сообщал, поэтому расширение разметки выдаёт вот такой объект.
/// </summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    internal LocalizedString(string key) => Key = key;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Ключ строки в словаре.</summary>
    public string Key { get; }

    /// <summary>Строка на текущем языке.</summary>
    public string Value => Localizer.Instance[Key];

    /// <inheritdoc/>
    public override string ToString() => Value;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}
