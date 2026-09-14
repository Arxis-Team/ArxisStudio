using System.ComponentModel;
using ArxisStudio.Projects;
using ArxisStudio.Sdk;

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
/// то, чего словарь не знает, — имя открытого проекта.
/// </para>
/// </remarks>
public sealed class SamplePanelModel : INotifyPropertyChanged, IDisposable
{
    private readonly IStudioContext _context;
    private readonly IStudioProjects? _projects;
    private ProjectsStatus? _status;

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
        if (_projects is not null)
            _projects.Changed -= OnProjects;
    }

    private string? Entry() => _status is { } status
        ? status.EntryPoint.IsEmpty ? null : status.EntryPoint.Value
        : _context.ProjectPath;

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
