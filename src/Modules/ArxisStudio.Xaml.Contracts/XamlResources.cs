using ArxisStudio.Markup;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Xaml;

/// <summary>Где объявлен ресурс, который элемент может назвать по ключу.</summary>
/// <remarks>Появилось в контракте 1.5.</remarks>
public enum XamlResourceScope
{
    /// <summary>Ресурсы самого элемента — и стилей, которые он пишет.</summary>
    Element,

    /// <summary>Ресурсы предка в документе — и стилей, которые он пишет.</summary>
    Ancestor,

    /// <summary>Ресурсы приложения формы: <c>App.axaml</c> и словари, которые оно включает.</summary>
    Application,

    /// <summary>Тема приложения — стили, которые оно подключает: <c>FluentTheme</c>, свои.</summary>
    Theme,
}

/// <summary>Ресурс, который элемент может назвать по ключу, — именами.</summary>
/// <param name="Key">Ключ, как его пишет выражение: <c>AccentBrush</c>.</param>
/// <param name="Scope">Где объявлен.</param>
/// <remarks>Имена и значения, которые можно держать, а не объекты: значение — объект поколения. Появилось в контракте 1.5.</remarks>
public sealed record XamlResourceInfo(string Key, XamlResourceScope Scope)
{
    /// <summary>Тип значения: <c>SolidColorBrush</c>, <c>Color</c>, <c>Double</c>; пусто — значения нет.</summary>
    public string? TypeName { get; init; }

    /// <summary>Значение текстом, как его написал бы документ: <c>#FF2563EB</c>, <c>8</c>; пусто — текстом его не написать.</summary>
    public string? ValueText { get; init; }

    /// <summary>
    /// Само значение, если его можно держать: цвет сплошной кисти, снимок градиента, число, структура; пусто — нельзя.
    /// </summary>
    public object? Sample { get; init; }

    /// <summary>
    /// Ключ есть только в словарях тем: у каждой темы значение своё, и показано значение темы, в которой форма показана.
    /// </summary>
    public bool IsThemed { get; init; }

    /// <summary>Значение подходит члену, для которого спрашивали: тот же или приводимый тип, цвет — кисти.</summary>
    public bool Fits { get; init; }

    /// <summary>Значение — конвертер привязки: его называют её аргументом <c>Converter</c>.</summary>
    public bool IsConverter { get; init; }

    /// <summary>
    /// <c>StaticResource</c> его найдёт. Ключ, который есть только в словарях тем, статическая ссылка находит лишь там,
    /// где тема известна, пока элемент строится, — в окне или <c>ThemeVariantScope</c>, — и за темой потом не идёт.
    /// </summary>
    public bool StaticReachable { get; init; }

    /// <summary>Элемент, который объявляет ресурс, в показанном документе; пусто — объявлен не в нём.</summary>
    public XamlElementPath? Element { get; init; }

    /// <summary>Файл, где объявлен ресурс, если не в показанном документе: приложение формы; пусто — не известен.</summary>
    public CanonicalPath File { get; init; }

    /// <summary>Где в своём файле записан ключ; пусто — не известно.</summary>
    public TextSpan? KeySpan { get; init; }
}
