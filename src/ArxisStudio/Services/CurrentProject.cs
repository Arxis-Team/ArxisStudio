using ArxisStudio.Projects;

namespace ArxisStudio.Services;

/// <summary>
/// Открытый проект глазами самой студии.
/// </summary>
/// <remarks>
/// <para>
/// Модель проектов живёт во встроенном модуле, и студия берёт её той же дорогой, что и плагин, —
/// экспортом. Здесь остаётся только то, что нужно ей самой: путь для контекста плагина,
/// переключение проектных настроек и отметка в недавних.
/// </para>
/// <para>
/// Событий два, потому что и поводов два. Путь меняется при всяком открытии и закрытии — за ним
/// идут проектные настройки. «Открыли» случается один раз за сессию: недавние отмечают открытие
/// проекта, а не каждую его перезагрузку, которых на одну сессию приходится сколько угодно.
/// </para>
/// <para>
/// Всё приходит в потоке интерфейса: служба проектов доставляет перемены только туда, а слушают их
/// настройки и окно студии, которые там же и живут.
/// </para>
/// </remarks>
public sealed class CurrentProject
{
    private long _announced;

    /// <summary>Путь к открытому решению или проекту; null — ничего не открыто.</summary>
    public string? Path { get; private set; }

    /// <summary>Путь переменился: открыли другое или закрыли открытое.</summary>
    public event EventHandler<string?>? Changed;

    /// <summary>Проект открылся и прочёлся — первый раз за свою сессию.</summary>
    public event EventHandler<string>? Opened;

    /// <summary>
    /// Следит за службой проектов.
    /// </summary>
    /// <param name="projects">Служба.</param>
    /// <remarks>
    /// Состояние берётся сразу после подписки, а не вместо неё: служба могла открыть проект
    /// раньше, чем за ней стали следить, и тогда первого события студия бы не увидела вовсе.
    /// </remarks>
    public void Follow(IStudioProjects projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        projects.Changed += (_, change) => Apply(change.Current);

        Apply(projects.Status);
    }

    /// <summary>Принимает состояние службы.</summary>
    /// <param name="status">Состояние.</param>
    public void Apply(ProjectsStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var path = status.EntryPoint.IsEmpty ? null : status.EntryPoint.Value;

        if (!string.Equals(path, Path, StringComparison.Ordinal))
        {
            Path = path;
            Changed?.Invoke(this, path);
        }

        // Готовность, а не открытие: путь, который не прочёлся, в недавних не нужен — человек
        // вернётся по нему к тому же провалу. Номер сессии отличает открытие от перезагрузки.
        if (path is not null && status.State == ProjectsState.Ready && _announced != status.Session)
        {
            _announced = status.Session;
            Opened?.Invoke(this, path);
        }
    }
}
