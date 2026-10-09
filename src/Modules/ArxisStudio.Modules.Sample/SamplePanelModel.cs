using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Projects;
using ArxisStudio.Sdk;
using Avalonia.Media;

namespace ArxisStudio.Modules.Sample;

/// <summary>
/// Что панель примера знает о студии.
/// </summary>
/// <remarks>
/// Модель отделена от представления не ради обряда, а ради двух вещей:
/// разметка собирается без студии (иначе её не открыть предпросмотром), а
/// связь с контекстом остаётся в одном месте и проверяется без окна.
/// <para>
/// Подписи панели модель не держит: они приходят из словаря прямо в разметку
/// через <c>{Text}</c> и переводятся вместе со студией. Модель отдаёт только
/// то, чего словарь не знает, — имя открытого проекта и приветствие по
/// настройкам.
/// </para>
/// </remarks>
public sealed class SamplePanelModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStudioContext _context;
    private readonly IStudioProjects? _projects;
    private ProjectsStatus? _status;
    private IDisposable? _inspected;

    /// <summary>
    /// Собирает модель и подписывается на перемены проекта.
    /// </summary>
    /// <param name="context">Что студия даёт модулю.</param>
    /// <remarks>
    /// <see cref="IStudioContext.ProjectPath"/> живой, но о своей перемене не говорит: тому, кому
    /// нужно событие, контракт велит брать службу проектов. Панель показывает проект, пока открыта,
    /// — ей событие и нужно. Прежде она читала путь один раз и до конца сеанса писала «проект не
    /// открыт», что бы ни открыл человек.
    /// <para>
    /// Сначала подписка, потом чтение — перемена между ними иначе прошла бы мимо. Службы может не
    /// быть: модуля проектов нет или он не поднялся. Тогда путь берётся у контекста на момент
    /// чтения, а событий просто не будет — образец обязан это пережить, как любой плагин.
    /// </para>
    /// </remarks>
    public SamplePanelModel(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _projects = context.Projects();
        Settings.PropertyChanged += OnSettings;

        if (_projects is null)
            return;

        _projects.Changed += OnProjects;
        _status = _projects.Status;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Имя файла открытого решения или проекта; null — ничего не открыто.</summary>
    public string? ProjectName => Entry() is { Length: > 0 } path ? Path.GetFileName(path) : null;

    /// <summary>Открыт ли проект.</summary>
    public bool HasProject => ProjectName is not null;

    /// <summary>Настройки панели — их показывает инспектор студии.</summary>
    public SampleSettings Settings { get; } = new();

    /// <summary>Приветствие по настройкам: кого, сколько раз и громко ли.</summary>
    public string Greeting =>
        string.Join(' ', Enumerable.Repeat(
            string.Format(CultureInfo.CurrentCulture, _context.Strings["module.sample.greeting"], Settings.Greeting), Settings.Repeat)) +
        (Settings.Loud ? "!" : string.Empty);

    /// <summary>Тон приветствия — тоном текста темы.</summary>
    public AxTextTone Tone => Settings.Tone switch
    {
        GreetingTone.Secondary => AxTextTone.Secondary,
        GreetingTone.Accent => AxTextTone.Accent,
        _ => AxTextTone.Primary,
    };

    /// <summary>Начертание приветствия: громкое — полужирным.</summary>
    public FontWeight Weight => Settings.Loud ? FontWeight.Bold : FontWeight.Normal;

    /// <summary>Есть ли у студии инспектор: без модуля инспектора показать настройки негде.</summary>
    public bool CanInspect => _context.Inspector() is not null;

    /// <summary>Показывает настройки панели в инспекторе студии — поверх того, что там было.</summary>
    /// <remarks>
    /// Ручку показа модель держит и отпускает, прощаясь: ушла панель — её настройки уходят из инспектора, и он
    /// показывает то, что было до них.
    /// </remarks>
    public void Inspect()
    {
        _inspected?.Dispose();
        _inspected = _context.Inspector()?.Inspect(Settings, _context.Strings["sample.settings"]);
    }

    /// <summary>
    /// Просит студию исполнить команду модуля.
    /// </summary>
    /// <remarks>
    /// Через команду, а не напрямую: та же дорога, что у пункта меню и у
    /// кнопки в полосе, — и одно место, где написано, что делать.
    /// </remarks>
    public void Log() => _context.Commands.Invoke(SampleModule.AboutCommand);

    /// <summary>Отписывается от службы проектов.</summary>
    /// <remarks>
    /// Зовёт панель, прощаясь (<see cref="ToolWindow.Release"/>). Модель без отписки держала бы
    /// себя — а через себя и представление — в списке подписчиков службы до конца сеанса.
    /// </remarks>
    public void Dispose()
    {
        Settings.PropertyChanged -= OnSettings;
        _inspected?.Dispose();
        _inspected = null;

        if (_projects is not null)
            _projects.Changed -= OnProjects;
    }

    private string? Entry() => _status is { } status
        ? status.EntryPoint.IsEmpty ? null : status.EntryPoint.Value
        : _context.ProjectPath;

    /// <summary>Настройки сменились — в панели или в инспекторе: приветствие идёт за ними.</summary>
    private void OnSettings(object? sender, PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Greeting)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tone)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Weight)));
    }

    private void OnProjects(object? sender, ProjectsChangedEventArgs e)
    {
        var was = ProjectName;

        _status = e.Current;

        if (string.Equals(was, ProjectName, StringComparison.Ordinal))
            return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProjectName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProject)));
    }
}
