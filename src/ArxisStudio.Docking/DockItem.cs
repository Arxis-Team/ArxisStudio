using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Docking;

/// <summary>
/// Живая панель: что показывать и как она называется.
/// </summary>
/// <remarks>
/// Дерево раскладки знает про панель только имя. Всё остальное — здесь, и живёт
/// это в <see cref="DockItems"/>, у которого есть хозяин и есть уборка. Так
/// контрол плагина не оказывается в данных, которые переживают сам плагин.
/// <para>
/// Заголовок — свойство Avalonia, а не строка, потому что переводится он на
/// ходу: студия привязывает его к словарю владельца, и вкладка меняет подпись
/// при смене языка, ничего не пересобирая.
/// </para>
/// </remarks>
public sealed class DockItem : AvaloniaObject
{
    /// <summary>Подпись вкладки.</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DockItem, string?>(nameof(Title));

    /// <summary>Заводит панель.</summary>
    /// <param name="id">Имя панели — по нему на неё ссылается дерево.</param>
    /// <param name="content">Что показывать.</param>
    public DockItem(string id, Control content)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(content);

        Id = id;
        Content = content;
    }

    /// <summary>Имя панели.</summary>
    public string Id { get; }

    /// <summary>Что показывать.</summary>
    public Control Content { get; }

    /// <summary>
    /// Можно ли закрыть панель крестиком.
    /// </summary>
    /// <remarks>
    /// Крестик обещает дорогу назад, а не только вперёд. У документа она есть
    /// всегда — тот же файл открывают заново; у панели её обязан завести
    /// хозяин дерева, иначе панель пропадает навсегда, и крестик оказывается
    /// ловушкой. Движок этой дороги не знает и потому ни у кого её не
    /// предполагает: признак ставят снаружи.
    /// </remarks>
    public bool CanClose { get; init; }

    /// <inheritdoc cref="TitleProperty"/>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
