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
/// <param name="id">Идентификатор команды, как в манифесте.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CommandAttribute(string id) : Attribute
{
    /// <summary>Идентификатор команды.</summary>
    public string Id { get; } = id;
}

/// <summary>
/// Пункт меню, вызывающий метод напрямую — для плагина, которому не нужен
/// отдельный идентификатор команды.
/// </summary>
/// <param name="path">Путь вида <c>Tools/Figma/Import…</c>.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class MenuItemAttribute(string path) : Attribute
{
    /// <summary>Путь пункта меню.</summary>
    public string Path { get; } = path;
}
