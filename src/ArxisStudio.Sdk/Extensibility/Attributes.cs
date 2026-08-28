namespace ArxisStudio.Sdk;

/// <summary>
/// Помечает панель плагина: студия покажет её в объявленной зоне.
/// </summary>
/// <remarks>
/// Идентификатор должен совпадать с объявленным в манифесте: манифест студия
/// читает, не загружая сборку, и по нему строит список панелей до того, как
/// атрибут вообще станет виден.
/// </remarks>
/// <param name="id">Идентификатор панели, как в манифесте.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ToolWindowAttribute(string id) : Attribute
{
    /// <summary>Идентификатор панели.</summary>
    public string Id { get; } = id;
}

/// <summary>
/// Помечает метод обработчиком команды, объявленной в манифесте.
/// </summary>
/// <remarks>
/// Студия сама заявит его при подъёме плагина — звать
/// <see cref="IStudioCommands.Register"/> руками не нужно. Метод должен быть
/// без параметров; лежать он может и в точке входа, и в службе, и в любом
/// другом открытом классе сборки — статический или обычный, лишь бы у класса
/// был конструктор без аргументов.
/// <para>
/// Идентификатор тот же, что в манифесте: по манифесту студия строит меню, не
/// загружая сборку, а атрибут связывает объявленное с кодом. Команда, которой
/// нет в манифесте, работать будет, но в меню не появится — её некому туда
/// поставить.
/// </para>
/// </remarks>
/// <param name="id">Идентификатор команды, как в манифесте.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CommandAttribute(string id) : Attribute
{
    /// <summary>Идентификатор команды.</summary>
    public string Id { get; } = id;
}
