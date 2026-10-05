using System.ComponentModel;
using ArxisStudio.Icons;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ArxisStudio.Modules.UiDesigner.Board;

/// <summary>
/// Форма на доске: что за файл, чем он объявлен и где стоит.
/// </summary>
/// <remarks>
/// <para>
/// Место — свойство формы, а не контейнера: холст читает его у данных через <c>ItemLocationBinding</c>, в
/// обе стороны, а контейнер формы может смениться. Тяга и отмена пишут сюда, и отсюда же место уходит в
/// файл доски.
/// </para>
/// <para>
/// Форма переживает перечитывание решения: доска узнаёт её по пути и обновляет поля, а не заменяет, —
/// иначе каждая перезагрузка снимала бы выделение и роняла контейнер под курсором.
/// </para>
/// </remarks>
internal sealed class FormCard : INotifyPropertyChanged
{
    private Point _location;
    private Bitmap? _preview;
    private bool _isPreviewStale;

    /// <summary>Заводит форму доски.</summary>
    /// <param name="file">Файл и проект.</param>
    /// <param name="root">Корень разметки.</param>
    public FormCard(FormFile file, FormRoot root)
    {
        ArgumentNullException.ThrowIfNull(file);

        Path = file.Path;
        Project = file.Project;
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Путь файла: им форма узнаётся между снимками.</summary>
    public CanonicalPath Path { get; }

    /// <summary>Имя файла.</summary>
    public string Name => Path.FileName;

    /// <summary>Проект, которому файл принадлежит.</summary>
    public string Project { get; private set; }

    /// <summary>Корень разметки, прочитанный без службы XAML: вид формы и объявленный размер.</summary>
    public FormRoot Root { get; private set; }

    /// <summary>Вид формы.</summary>
    public FormKind Kind => Root.Kind;

    /// <summary>Левый верхний угол формы в мировых координатах холста.</summary>
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

    /// <summary>Снимок формы, пока она на виду; null — снимка нет или формы не видно.</summary>
    /// <remarks>
    /// Ставит и снимает его <see cref="BoardPreviews"/>: растр живёт, пока форма на виду, и освобождает его
    /// тот, кто снял. Холст показывает его на месте формы, пока живая форма не встала.
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
        }
    }

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
        }
    }

    /// <summary>Значок вида формы — в меню доски и на рамке во вкладке.</summary>
    /// <param name="kind">Вид формы.</param>
    public static Geometry GlyphOf(FormKind kind) => kind switch
    {
        FormKind.Window => AxIcons.Window,
        FormKind.UserControl => AxIcons.Component,
        FormKind.Control => AxIcons.Template,
        _ => AxIcons.WarningTriangle,
    };

    /// <summary>Вид формы словами — подсказка значка на рамке во вкладке.</summary>
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
    public void Update(FormFile file, FormRoot root)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(root);

        if (file.Project == Project && root == Root)
            return;

        Project = file.Project;
        Root = root;
        Raise(null);
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
