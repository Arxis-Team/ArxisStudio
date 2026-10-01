using System.Globalization;
using System.Xml;

namespace ArxisStudio.Modules.UiDesigner.Model;

/// <summary>Чем форма объявлена в корне разметки.</summary>
internal enum FormKind
{
    /// <summary>Окно: корень называется <c>…Window</c>.</summary>
    Window,

    /// <summary>Пользовательский элемент: корень <c>…UserControl</c>.</summary>
    UserControl,

    /// <summary>Другой контрол в корне — шаблонный, панель.</summary>
    Control,

    /// <summary>Разметка не читается: файл пропал, занят или это не XML.</summary>
    Unreadable,
}

/// <summary>
/// Корень файла разметки: какой элемент, какой класс за ним и какого он размера.
/// </summary>
/// <param name="Kind">Вид формы.</param>
/// <param name="Element">Имя корневого элемента без приставки; у нечитаемого файла — пусто.</param>
/// <param name="ClassName"><c>x:Class</c>, если объявлен.</param>
/// <param name="Width">Ширина: <c>Width</c>, а нет её — <c>d:DesignWidth</c>.</param>
/// <param name="Height">Высота той же дорогой.</param>
/// <param name="Problem">Почему файл не прочитан; у прочитанного — null.</param>
/// <remarks>
/// Читается один корень, а не документ: доске нужно знать, форма ли это и какая, а разбор целиком —
/// работа Markup, когда он придёт. Читатель потоковый, DTD запрещён: файл приходит из решения, то есть
/// от человека, и сущность в нём не должна ни раздувать память, ни ходить на диск.
/// </remarks>
internal sealed record FormRoot(
    FormKind Kind,
    string Element,
    string? ClassName,
    double? Width,
    double? Height,
    string? Problem)
{
    /// <summary>Пространство имён разметки XAML: в нём <c>x:Class</c>.</summary>
    public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Пространство времени разработки: в нём <c>d:DesignWidth</c>.</summary>
    public const string DesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";

    /// <summary>
    /// Корни, которые формами не бывают: приложение и словари стилей и ресурсов.
    /// </summary>
    /// <remarks>
    /// Окна и элементы в решении узнаются по имени корня, а не по типу: тип живёт в сборке, которую
    /// доска не грузит. Поэтому список — исключений, а не форм: свой наследник <c>Window</c> под другим
    /// именем остаётся на доске контролом, а не пропадает.
    /// </remarks>
    private static readonly HashSet<string> NotForms = new(StringComparer.Ordinal)
    {
        "Application",
        "Styles",
        "Style",
        "ResourceDictionary",
    };

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>Размер, объявленный формой, — у обоих измерений сразу.</summary>
    public bool HasSize => Width is not null && Height is not null;

    /// <summary>
    /// Читает корень файла.
    /// </summary>
    /// <param name="path">Абсолютный путь к <c>.axaml</c>.</param>
    /// <returns>Корень; null — файл не форма (приложение, стили, словарь ресурсов).</returns>
    public static FormRoot? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return Read(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unreadable(e.Message);
        }
    }

    /// <summary>Читает корень из потока — тестам и тому, у кого файл уже открыт.</summary>
    /// <param name="stream">Разметка.</param>
    /// <returns>Корень; null — не форма.</returns>
    public static FormRoot? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using var reader = XmlReader.Create(stream, Settings);

            if (reader.MoveToContent() != XmlNodeType.Element)
                return Unreadable(null);

            var element = reader.LocalName;

            if (NotForms.Contains(element))
                return null;

            return new FormRoot(
                KindOf(element),
                element,
                reader.GetAttribute("Class", XamlNamespace),
                Length(reader.GetAttribute("Width")) ?? Length(reader.GetAttribute("DesignWidth", DesignNamespace)),
                Length(reader.GetAttribute("Height")) ?? Length(reader.GetAttribute("DesignHeight", DesignNamespace)),
                null);
        }
        catch (XmlException e)
        {
            return Unreadable(e.Message);
        }
    }

    /// <summary>Вид по имени корня.</summary>
    /// <param name="element">Имя без приставки.</param>
    public static FormKind KindOf(string element) =>
        element.EndsWith("Window", StringComparison.Ordinal) ? FormKind.Window
        : element.EndsWith("UserControl", StringComparison.Ordinal) ? FormKind.UserControl
        : FormKind.Control;

    private static FormRoot Unreadable(string? problem) =>
        new(FormKind.Unreadable, string.Empty, null, null, null, problem ?? string.Empty);

    /// <summary>
    /// Длина атрибута, если это число: <c>Auto</c>, привязка и ресурс размером не считаются.
    /// </summary>
    private static double? Length(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var length)
        && double.IsFinite(length) && length > 0
            ? length
            : null;
}
