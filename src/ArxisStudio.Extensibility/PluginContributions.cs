using System.Reflection;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Что плагины добавили к инспектору: рисовальщики свойств и свои инспекторы.
/// </summary>
/// <remarks>
/// Реестр один на студию и заполняется при поднятии плагинов. Заявку на тип,
/// который уже занят другим плагином, реестр отклоняет: два рисовальщика на
/// одно свойство — это не выбор, а гонка, и выиграл бы тот, кого раньше
/// загрузили.
/// </remarks>
public sealed class PluginContributionRegistry
{
    private readonly Dictionary<Type, Registration<PropertyDrawer>> _drawers = [];
    private readonly Dictionary<Type, Registration<InspectorEditor>> _inspectors = [];
    private readonly List<DocumentEditor> _editors = [];

    /// <summary>Кто-то попытался занять уже занятый тип.</summary>
    public event EventHandler<string>? Conflict;

    /// <summary>Типы значений, для которых есть рисовальщик.</summary>
    public IReadOnlyCollection<Type> DrawnTypes => _drawers.Keys;

    /// <summary>
    /// Собирает вклады плагина из его сборок.
    /// </summary>
    /// <param name="plugin">Поднятый плагин.</param>
    public void Add(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        Add(plugin.Installed.Id, plugin.Installed.DisplayName, plugin.Assemblies, plugin.Installed.Directory, plugin.Studio);
    }

    /// <summary>
    /// Собирает вклады из перечисленных сборок.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <param name="displayName">Как плагин называется в сообщениях.</param>
    /// <param name="assemblies">Сборки, в которых искать вклады.</param>
    /// <param name="directory">Папка плагина; null, если её нет.</param>
    /// <param name="studio">Контекст, который получат редакторы документов.</param>
    public void Add(
        string pluginId,
        string displayName,
        IEnumerable<Assembly> assemblies,
        string? directory = null,
        IStudioContext? studio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()))
        {
            if (type is not { IsAbstract: false, IsPublic: true })
                continue;

            if (type.GetCustomAttribute<PropertyDrawerAttribute>() is { } drawer &&
                typeof(PropertyDrawer).IsAssignableFrom(type))
            {
                Register(_drawers, drawer.ValueType, type, pluginId, displayName, directory, "рисовальщик");
            }

            if (type.GetCustomAttribute<CustomInspectorAttribute>() is { } inspector &&
                typeof(InspectorEditor).IsAssignableFrom(type))
            {
                Register(_inspectors, inspector.TargetType, type, pluginId, displayName, directory, "инспектор");
            }

            // Редактор документов — единственный вклад, живущий экземпляром:
            // студия спрашивает его о каждом открываемом файле.
            if (typeof(DocumentEditor).IsAssignableFrom(type) &&
                Activator.CreateInstance(type) is DocumentEditor editor)
            {
                if (studio is not null)
                    editor.Attach(studio);

                _editors.Add(editor);
            }
        }
    }

    /// <summary>Находит редактор, который берётся за файл.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Редактор или null, если за файл никто не взялся.</returns>
    public DocumentEditor? EditorFor(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return _editors.FirstOrDefault(editor => editor.CanOpen(filePath));
    }

    /// <summary>Убирает вклады плагина, который выключают.</summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    public void Remove(string pluginId)
    {
        Drop(_drawers, pluginId);
        Drop(_inspectors, pluginId);

        static void Drop<T>(Dictionary<Type, Registration<T>> registry, string id) where T : class
        {
            foreach (var key in registry.Where(pair => pair.Value.PluginId == id).Select(pair => pair.Key).ToList())
                registry.Remove(key);
        }
    }

    /// <summary>
    /// Находит рисовальщика для типа значения.
    /// </summary>
    /// <param name="valueType">Тип значения свойства.</param>
    /// <returns>Свежий рисовальщик или null, если на этот тип никто не заявлялся.</returns>
    public PropertyDrawer? DrawerFor(Type valueType) => Create(_drawers, valueType);

    /// <summary>
    /// Находит свой инспектор для типа контрола.
    /// </summary>
    /// <param name="targetType">Тип выделенного контрола.</param>
    /// <returns>Инспектор вместе с плагином, который его дал, или null.</returns>
    /// <remarks>
    /// Ищется и по самому типу, и по его предкам: инспектор, заявленный на
    /// <c>Button</c>, должен доставаться и наследнику кнопки — иначе своя кнопка
    /// в библиотеке отменяла бы чужую работу.
    /// <para>
    /// Вместе с инспектором возвращается и хозяин: контекст, который ему потом
    /// выдадут, обязан указывать на папку его плагина, а не чью-то ещё.
    /// </para>
    /// </remarks>
    public InspectorMatch? InspectorFor(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        for (var type = targetType; type is not null; type = type.BaseType)
        {
            if (_inspectors.TryGetValue(type, out var registration) &&
                Activator.CreateInstance(registration.Type) is InspectorEditor editor)
            {
                return new InspectorMatch(editor, registration.PluginId, registration.Directory);
            }
        }

        return null;
    }

    private static T? Create<T>(Dictionary<Type, Registration<T>> registry, Type key) where T : class =>
        registry.TryGetValue(key, out var registration) && Activator.CreateInstance(registration.Type) is T created
            ? created
            : null;

    private void Register<T>(
        Dictionary<Type, Registration<T>> registry,
        Type key,
        Type type,
        string pluginId,
        string displayName,
        string? directory,
        string what)
        where T : class
    {
        if (registry.TryGetValue(key, out var taken))
        {
            Conflict?.Invoke(this, $"{displayName}: {what} для {key.Name} уже заявлен плагином {taken.PluginId}");
            return;
        }

        registry[key] = new Registration<T>(pluginId, type, directory);
    }

    private readonly record struct Registration<T>(string PluginId, Type Type, string? Directory) where T : class;
}

/// <summary>Найденный инспектор и плагин, который его дал.</summary>
/// <param name="Editor">Свежий инспектор.</param>
/// <param name="PluginId">Идентификатор плагина-хозяина.</param>
/// <param name="PluginDirectory">Папка плагина-хозяина, если она известна.</param>
public sealed record InspectorMatch(InspectorEditor Editor, string PluginId, string? PluginDirectory);
