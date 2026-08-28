namespace ArxisStudio.Sdk;

/// <summary>
/// Что студия даёт плагину: журнал, команды и сведения об открытом проекте.
/// </summary>
/// <remarks>
/// Контекст — единственная дорога от плагина к студии. Плагин не ссылается ни
/// на приложение, ни на его внутренние типы: всё, что ему позволено, перечислено
/// здесь, и потому список услуг можно расширять, не ломая уже написанные
/// плагины.
/// </remarks>
public interface IStudioContext
{
    /// <summary>Журнал студии — то, что видно в панели «Консоль».</summary>
    IStudioLog Log { get; }

    /// <summary>Команды: регистрация своих и вызов чужих.</summary>
    IStudioCommands Commands { get; }

    /// <summary>Настройки плагина, объявленные в его манифесте.</summary>
    IStudioSettings Settings { get; }

    /// <summary>Фоновые задачи: всё долгое делается здесь, а не в потоке интерфейса.</summary>
    IStudioTasks Tasks { get; }

    /// <summary>Строки плагина из его словарей <c>lang/</c>.</summary>
    IStudioStrings Strings { get; }

    /// <summary>Путь к открытому решению или проекту; null, если проект не открыт.</summary>
    string? ProjectPath { get; }

    /// <summary>
    /// Папка самого плагина: там лежат его значки, словари и прочие ресурсы.
    /// </summary>
    /// <remarks>
    /// Искать их рядом со сборкой нельзя. Студия грузит плагин из теневой копии
    /// его <c>bin/</c> — иначе файлы плагина были бы заняты, и пересобрать его,
    /// не закрыв студию, автор бы не смог, — поэтому путь к сборке ведёт во
    /// временную папку, где, кроме сборок, ничего нет. Ресурсы остаются здесь.
    /// </remarks>
    string PluginDirectory { get; }

    /// <summary>
    /// Достаёт службу студии по типу.
    /// </summary>
    /// <typeparam name="T">Тип службы — интерфейс или класс.</typeparam>
    /// <returns>Служба или null, если студия такой не предоставляет.</returns>
    /// <remarks>
    /// Так расширяется то, что студия даёт плагину: новая служба — новая
    /// регистрация, а не новое свойство контекста, ломающее каждую реализацию.
    /// </remarks>
    T? GetService<T>() where T : class;
}

/// <summary>Строка состояния студии.</summary>
public interface IStudioStatus
{
    /// <summary>Показывает сообщение в строке состояния.</summary>
    /// <param name="message">Что показать.</param>
    void Show(string message);
}

/// <summary>Уровень записи в журнале.</summary>
public enum StudioLogLevel
{
    /// <summary>Подробности для отладки.</summary>
    Debug,

    /// <summary>Обычное сообщение.</summary>
    Info,

    /// <summary>Предупреждение.</summary>
    Warning,

    /// <summary>Ошибка.</summary>
    Error,
}

/// <summary>Журнал студии.</summary>
public interface IStudioLog
{
    /// <summary>Пишет строку в журнал.</summary>
    /// <param name="level">Уровень записи.</param>
    /// <param name="source">Кто пишет: имя плагина или подсистемы.</param>
    /// <param name="message">Сообщение.</param>
    void Write(StudioLogLevel level, string source, string message);
}

/// <summary>Команды студии.</summary>
public interface IStudioCommands
{
    /// <summary>
    /// Заявляет обработчик команды, объявленной в манифесте.
    /// </summary>
    /// <param name="id">Идентификатор команды.</param>
    /// <param name="handler">Что сделать по команде.</param>
    void Register(string id, Action handler);

    /// <summary>Вызывает команду.</summary>
    /// <param name="id">Идентификатор команды.</param>
    /// <returns>Нашёлся ли обработчик.</returns>
    bool Invoke(string id);
}
