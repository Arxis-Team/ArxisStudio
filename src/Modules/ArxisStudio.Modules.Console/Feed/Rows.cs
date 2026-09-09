using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ArxisStudio.Modules.Console.Feed;

/// <summary>
/// Список строк панели: умеет замениться целиком за одно событие.
/// </summary>
/// <typeparam name="T">Строка панели.</typeparam>
/// <remarks>
/// Обычная наблюдаемая коллекция сообщает о каждом добавлении по отдельности,
/// и замена двух тысяч строк стоила бы двух тысяч уведомлений — каждое со
/// своим проходом по привязкам. Полная замена — это одно событие
/// <c>Reset</c>, и список перестраивается разом.
/// <para>
/// Дописывание при этом остаётся обычным <c>Add</c>: у быстрого пути строк
/// единицы, и точечные уведомления там дешевле сброса — сброс уронил бы
/// выделение и прокрутку, а дописывание их бережёт.
/// </para>
/// </remarks>
public sealed class Rows<T> : ObservableCollection<T>
{
    /// <summary>
    /// Заменяет содержимое целиком.
    /// </summary>
    /// <param name="replacement">Новые строки по порядку.</param>
    public void Reset(IReadOnlyList<T> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        Items.Clear();

        foreach (var row in replacement)
            Items.Add(row);

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
