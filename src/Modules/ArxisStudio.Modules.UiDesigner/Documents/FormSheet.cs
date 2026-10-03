using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Media;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Форма во вкладке дизайнера: рамка её размера на холсте и подпись над рамкой.
/// </summary>
/// <remarks>
/// Рамка — клиентская область формы, ровно <c>Width</c> × <c>Height</c>, а не окно с заголовком: так их
/// и понимает Avalonia, и хром, нарисованный здесь, был бы выдумкой о системе, на которой форму запустят.
/// Имя файла, <c>Title</c> окна и размер стоят подписью над рамкой, как имя фрейма в Figma. Форма без
/// объявленного размера рисуется размером из темы — тем, что пишут в превью шаблоны Avalonia, — и
/// подпись говорит, что размер не объявлен.
/// <para>
/// Содержимого формы рамка пока не показывает: его построит загрузчик разметки, когда дизайнер его
/// подключит. До тех пор внутри — то, что известно из корня: элемент и класс за ним.
/// </para>
/// </remarks>
internal sealed class FormSheet : INotifyPropertyChanged
{
    private readonly IStudioStrings _strings;
    private readonly Size _fallback;

    /// <summary>Заводит рамку.</summary>
    /// <param name="path">Файл формы.</param>
    /// <param name="root">Корень разметки.</param>
    /// <param name="fallback">Размер для формы, которая своего не объявила.</param>
    /// <param name="strings">Словари модуля.</param>
    public FormSheet(string path, FormRoot root, Size fallback, IStudioStrings strings)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(strings);

        FilePath = path;
        Root = root;
        _fallback = fallback;
        _strings = strings;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Файл формы.</summary>
    public string FilePath { get; }

    /// <summary>Имя файла: им же подписана вкладка.</summary>
    public string Name => Path.GetFileName(FilePath);

    /// <summary>Корень разметки, прочитанный последним.</summary>
    public FormRoot Root { get; private set; }

    /// <summary>Ширина рамки: объявленная формой, а нет её — из темы.</summary>
    public double Width => Root.Width ?? _fallback.Width;

    /// <summary>Высота рамки той же дорогой.</summary>
    public double Height => Root.Height ?? _fallback.Height;

    /// <summary>Подпись над рамкой: файл, а у окна с заголовком — и заголовок.</summary>
    public string Caption => Root.Title is { } title ? $"{Name} · {title}" : Name;

    /// <summary>Размер словами: объявленный или признание, что его нет.</summary>
    public string SizeText => Root.HasSize
        ? string.Create(CultureInfo.CurrentCulture, $"{Width:0.##} × {Height:0.##}")
        : _strings["form.size.none"];

    /// <summary>Значок вида — тот же, что на карточке доски.</summary>
    public Geometry Glyph => FormCard.GlyphOf(Root.Kind);

    /// <summary>Вид словами — подсказка значка.</summary>
    public string KindText => FormCard.KindTextOf(_strings, Root.Kind);

    /// <summary>Элемент в корне: <c>Window</c>, <c>UserControl</c>.</summary>
    public string Element => Root.Element;

    /// <summary>Класс за разметкой.</summary>
    public string? ClassName => Root.ClassName;

    /// <summary>Есть ли класс.</summary>
    public bool HasClass => !string.IsNullOrEmpty(Root.ClassName);

    /// <summary>Разметка больше не читается.</summary>
    public bool IsUnreadable => Root.Kind == FormKind.Unreadable;

    /// <summary>Почему не читается.</summary>
    public string? Problem => IsUnreadable ? Root.Problem : null;

    /// <summary>
    /// Принимает корень, перечитанный с диска.
    /// </summary>
    /// <param name="root">Корень.</param>
    /// <returns>Изменилось ли что-то.</returns>
    public bool Update(FormRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root == Root)
            return false;

        Root = root;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        return true;
    }
}
