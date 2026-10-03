using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Xaml;

/// <summary>
/// Типы, которые можно поставить в форму: контролы проекта — собранные и ещё не собранные — и вопрос,
/// разрешится ли имя в проекте формы.
/// </summary>
/// <remarks>
/// <para>
/// Ответы — имена (<see cref="XamlPlaceable"/>), а не типы: палитра стоит на экране через любую замену
/// поколения, и тип, удержанный ею, держал бы поколение.
/// </para>
/// <para>
/// <b>Потоки.</b> Звать из потока интерфейса; задачи кончаются в нём же. Появилось в контракте 1.1.
/// </para>
/// </remarks>
public interface IStudioXamlTypes
{
    /// <summary>
    /// Контролы проекта формы и проектов, на которые он ссылается: что собрано в живом поколении, и
    /// формы, класса которых ещё нет, — помеченные несобранными.
    /// </summary>
    /// <param name="document">Форма, в которую ставят.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Контролы; пусто — документ не в решении, поколения нет или ставить нечего.</returns>
    /// <remarks>Класс приложения и окна в ответ не входят: их в форму не ставят.</remarks>
    Task<IReadOnlyList<XamlPlaceable>> GetPlaceableAsync(CanonicalPath document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Собирает проект контрола, если класса его ещё нет ни в одном поколении, и ждёт поколения, в
    /// котором он есть.
    /// </summary>
    /// <param name="control">Контрол из <see cref="GetPlaceableAsync"/>.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns><c>false</c> — сборка не дала класса: что сказала она, видно в журнале и в полосе задач.</returns>
    /// <remarks>
    /// Идёт через отсрочки замены: пока жест держит замену, ответ ждёт его конца. Поэтому место, куда
    /// ставят, помнят путём, а не объектом: после замены объекты другие.
    /// </remarks>
    Task<bool> EnsureBuiltAsync(XamlPlaceable control, CancellationToken cancellationToken = default);

    /// <summary>Разрешится ли тип в проекте формы — или ему не хватает пакета.</summary>
    /// <param name="document">Форма, в которую ставят.</param>
    /// <param name="type">Тип именем.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>
    /// <c>true</c> — разрешится, или спросить не у кого: решения нет, поколения нет. Отказ лучше сказать до
    /// правки, а не узнать после — пустым местом на холсте.
    /// </returns>
    Task<bool> ResolvesAsync(CanonicalPath document, XamlSnippet type, CancellationToken cancellationToken = default);
}

/// <summary>Контрол проекта, который можно поставить в форму, — именами.</summary>
/// <param name="Name">Имя элемента: <c>Badge</c>.</param>
/// <param name="ClassName">Полное имя класса: <c>Stand.Controls.Badge</c>.</param>
/// <param name="XmlNamespace">Пространство, в котором его пишут: <c>using:Stand.Controls</c>.</param>
/// <param name="SuggestedPrefix">Приставка, которую предлагает библиотека; null — своей нет, пишут <c>local</c>.</param>
/// <param name="Project">Проект, который его строит.</param>
/// <param name="Document">Разметка контрола; пусто — у контрола её нет.</param>
/// <param name="IsBuilt">Класс есть в живом поколении; иначе его соберёт <see cref="IStudioXamlTypes.EnsureBuiltAsync"/>.</param>
/// <remarks>Появилось в контракте 1.1.</remarks>
public sealed record XamlPlaceable(
    string Name,
    string ClassName,
    string XmlNamespace,
    string? SuggestedPrefix,
    ProjectIdentity Project,
    CanonicalPath Document,
    bool IsBuilt);

/// <summary>
/// Тип, который ставят в форму, — именем и разметкой, которую вставка напишет: кнопка палитры, контрол
/// проекта, контрол плагина.
/// </summary>
/// <param name="XmlNamespace">Пространство разметки типа: у контролов Avalonia — <c>https://github.com/avaloniaui</c>.</param>
/// <param name="Name">Имя элемента: <c>Button</c>.</param>
/// <remarks>
/// <para>
/// Это и данные тяги в формате <see cref="XamlDataFormats.Snippet"/>: палитра — первый источник, но
/// принести тип в форму может и плагин.
/// </para>
/// <para>Появилось в контракте 1.1.</para>
/// </remarks>
public sealed record XamlSnippet(string XmlNamespace, string Name)
{
    /// <summary>Пространство разметки контролов Avalonia.</summary>
    public const string AvaloniaNamespace = "https://github.com/avaloniaui";

    /// <summary>
    /// Разметка, которую пишет вставка, — элемент с разумными для нового контрола атрибутами и детьми:
    /// <c>&lt;Button Content="Button" /&gt;</c>. Пространства в ней не объявляют: её читают в
    /// <see cref="XmlNamespace"/>. Null — пустой элемент.
    /// </summary>
    public string? Markup { get; init; }

    /// <summary>
    /// Приставка, под которой пишут элемент вне пространства Avalonia; null — без приставки. Объявление её
    /// вставка добавит корню, если его там нет.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>Контрол проекта, которого ещё может не быть в поколении; null — тип не из проекта.</summary>
    public XamlPlaceable? Placeable { get; init; }

    /// <summary>Пакет, в котором лежит тип, если не в Avalonia: отказ назовёт, что добавить.</summary>
    public string? Package { get; init; }
}

/// <summary>Форматы данных тяги, которые понимает дизайнер форм.</summary>
/// <remarks>Появилось в контракте 1.1.</remarks>
public static class XamlDataFormats
{
    /// <summary>Тип, который ставят в форму, — с разметкой, которую напишет вставка.</summary>
    /// <remarks>
    /// Тип значения — из контракта, поэтому его видят обе стороны тяги: палитра дизайнера и плагин,
    /// который несёт свой контрол.
    /// </remarks>
    public static StudioDataFormat<XamlSnippet> Snippet { get; } = new("arxis.xaml.snippet");
}
