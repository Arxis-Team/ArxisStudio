using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>Почему модель перечитали.</summary>
public enum ProjectsLoadReason
{
    /// <summary>Открытие: <see cref="IStudioProjects.OpenAsync"/>.</summary>
    Open,

    /// <summary>Просьба перечитать: <see cref="IStudioProjects.ReloadAsync"/> или команда студии.</summary>
    Reload,

    /// <summary>Смена конфигурации: <see cref="IStudioProjects.SetConfigurationAsync"/>.</summary>
    Configuration,

    /// <summary>
    /// Перемены на диске: правили файл проекта, импорт или решение, в папке проекта появился
    /// или пропал файл.
    /// </summary>
    FileSystem,

    /// <summary>
    /// Удачное восстановление: оно переписывает то, из чего собирается модель, и её перечитывают
    /// следом, не дожидаясь ни просьбы, ни перемены на диске.
    /// </summary>
    Restore,

    /// <summary>
    /// Пакеты проекта изменились: их поставили, обновили или убрали, и правка прошла.
    /// </summary>
    Packages,
}

/// <summary>Одна законченная загрузка: зачем она была и чем кончилась.</summary>
public sealed record ProjectsLoad
{
    /// <summary>
    /// Почему перечитывали.
    /// </summary>
    /// <remarks>
    /// Просьбы, склеенные в одну загрузку, называются самой определённой из них: открытие сильнее
    /// смены конфигурации, смена — просьбы перечитать, просьба — перемен на диске.
    /// </remarks>
    public required ProjectsLoadReason Reason { get; init; }

    /// <summary>Итог.</summary>
    public required WorkspaceLoadResult Result { get; init; }

    /// <summary>Файлы, перемены в которых привели к загрузке; пусто, если просил не диск.</summary>
    public ImmutableArray<CanonicalPath> Causes
    {
        get => field.IsDefault ? [] : field;
        init;
    }
}
