using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Карточка формы на доске: что за файл, чем он объявлен и где стоит.
/// </summary>
/// <remarks>
/// Место — свойство карточки, а не контейнера: холст держит контейнеры только у видимых карточек и
/// читает место у данных через <c>ItemLocationBinding</c>, в обе стороны. Тяга и отмена пишут сюда,
/// и отсюда же место уходит в файл доски.
/// <para>
/// Карточка переживает перечитывание решения: доска узнаёт её по пути и обновляет поля, а не
/// заменяет, — иначе каждая перезагрузка снимала бы выделение и роняла контейнер под курсором.
/// </para>
/// </remarks>
internal sealed class FormCard : INotifyPropertyChanged
{
    private Point _location;
    private Bitmap? _preview;
    private bool _isPreviewStale;

    /// <summary>Заводит карточку.</summary>
    /// <param name="file">Файл и проект.</param>
    /// <param name="root">Корень разметки.</param>
    /// <param name="kindText">Вид формы словами.</param>
    public FormCard(FormFile file, FormRoot root, string kindText)
    {
        ArgumentNullException.ThrowIfNull(file);

        Path = file.Path;
        Project = file.Project;
        Root = root ?? throw new ArgumentNullException(nameof(root));
        KindText = kindText;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Путь файла: им карточка узнаётся между снимками.</summary>
    public CanonicalPath Path { get; }

    /// <summary>Имя файла.</summary>
    public string Name => Path.FileName;

    /// <summary>Проект, которому файл принадлежит.</summary>
    public string Project { get; private set; }

    /// <summary>Корень разметки.</summary>
    public FormRoot Root { get; private set; }

    /// <summary>Вид формы.</summary>
    public FormKind Kind => Root.Kind;

    /// <summary>Вид формы словами — подсказка значка.</summary>
    public string KindText { get; private set; }

    /// <summary>Разметка не прочиталась.</summary>
    public bool IsUnreadable => Kind == FormKind.Unreadable;

    /// <summary>Значок вида: окно, элемент, контрол, беда.</summary>
    public Geometry Glyph => GlyphOf(Kind);

    /// <summary>
    /// Вторая строка: класс за разметкой, у нечитаемой — почему.
    /// </summary>
    /// <remarks>
    /// Класс пишется полным именем: в решении бывают одноимённые окна в разных пространствах, и имя
    /// файла их не различает.
    /// </remarks>
    public string? Detail => IsUnreadable ? Root.Problem : Root.ClassName;

    /// <summary>Есть ли вторая строка.</summary>
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>Тон второй строки: причина беды — тоном ошибки.</summary>
    public AxTextTone DetailTone => IsUnreadable ? AxTextTone.Error : AxTextTone.Secondary;

    /// <summary>Третья строка: проект и объявленный размер.</summary>
    public string Footer => Root.HasSize
        ? string.Create(CultureInfo.CurrentCulture, $"{Project} · {Root.Width:0.##} × {Root.Height:0.##}")
        : Project;

    /// <summary>Левый верхний угол в мировых координатах холста.</summary>
    public Point Location
    {
        get => _location;
        set
        {
            if (_location == value)
                return;

            _location = value;
            Raise(nameof(Location));
        }
    }

    /// <summary>Место в координатах модели.</summary>
    public Spot Spot => new(_location.X, _location.Y);

    /// <summary>Снимок формы, пока карточка на виду; null — снимка нет или карточку не видно.</summary>
    /// <remarks>
    /// Ставит и снимает его <see cref="BoardPreviews"/>: растр живёт, пока у карточки есть контейнер, и
    /// освобождает его тот, кто снял.
    /// </remarks>
    public Bitmap? Preview
    {
        get => _preview;
        set
        {
            if (ReferenceEquals(_preview, value))
                return;

            _preview = value;
            Raise(nameof(Preview));
            Raise(nameof(HasPreview));
            Raise(nameof(ShowsStale));
        }
    }

    /// <summary>Снимок есть.</summary>
    public bool HasPreview => _preview is not null;

    /// <summary>Снимок старше файла: форма менялась после него.</summary>
    public bool IsPreviewStale
    {
        get => _isPreviewStale;
        set
        {
            if (_isPreviewStale == value)
                return;

            _isPreviewStale = value;
            Raise(nameof(IsPreviewStale));
            Raise(nameof(ShowsStale));
        }
    }

    /// <summary>Карточка отмечает снимок устаревшим: старое показано, но человек знает, что оно старое.</summary>
    public bool ShowsStale => _isPreviewStale && _preview is not null;

    /// <summary>Значок вида формы — один на карточке доски и на рамке во вкладке.</summary>
    /// <param name="kind">Вид формы.</param>
    public static Geometry GlyphOf(FormKind kind) => kind switch
    {
        FormKind.Window => AxIcons.Window,
        FormKind.UserControl => AxIcons.Component,
        FormKind.Control => AxIcons.Template,
        _ => AxIcons.WarningTriangle,
    };

    /// <summary>Вид формы словами — подсказка значка на карточке и на рамке во вкладке.</summary>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="kind">Вид формы.</param>
    public static string KindTextOf(IStudioStrings strings, FormKind kind)
    {
        ArgumentNullException.ThrowIfNull(strings);

        return strings[kind switch
        {
            FormKind.Window => "board.kind.window",
            FormKind.UserControl => "board.kind.userControl",
            FormKind.Control => "board.kind.control",
            _ => "board.kind.unreadable",
        }];
    }

    /// <summary>Перечитанный снимок принёс новое о том же файле.</summary>
    /// <param name="file">Файл и проект.</param>
    /// <param name="root">Корень разметки.</param>
    /// <param name="kindText">Вид формы словами.</param>
    public void Update(FormFile file, FormRoot root, string kindText)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(root);

        if (file.Project == Project && root == Root && kindText == KindText)
            return;

        Project = file.Project;
        Root = root;
        KindText = kindText;
        Raise(null);
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
