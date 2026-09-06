using System.Reflection;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Что плагины добавили к студии экземплярами: редакторы документов.
/// </summary>
/// <remarks>
/// Реестр один на студию и заполняется при поднятии плагинов. Здесь живут
/// вклады, которые нельзя объявить манифестом: студия спрашивает редактор о
/// каждом открываемом файле, а ответить может только его код.
/// <para>
/// Рисовальщики свойств и свои инспекторы жили здесь же и сняты вместе со
/// своим контрактом: показывать их было некому с тех пор, как дизайнер уехал
/// из репозитория (этап 17). Вернутся они вместе с ним и вместе с тем, кто их
/// показывает, — заодно избавившись от изъяна, с которым уходят: рисовальщик
/// не видел студию и потому не мог взять даже свои строки.
/// </para>
/// </remarks>
public sealed class PluginContributionRegistry
{
    private readonly List<EditorRegistration> _editors = [];

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
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var type in assemblies.SelectMany(assembly => assembly.GetTypes()))
        {
            if (type is not { IsAbstract: false, IsPublic: true })
                continue;

            // Редактор документов живёт экземпляром: студия спрашивает его о
            // каждом открываемом файле.
            if (typeof(DocumentEditor).IsAssignableFrom(type) &&
                Activator.CreateInstance(type) is DocumentEditor editor)
            {
                if (studio is not null)
                    editor.Attach(studio);

                _editors.Add(new EditorRegistration(pluginId, editor));
            }
        }
    }

    /// <summary>Находит редактор, который берётся за файл.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Редактор вместе с плагином, который его дал, или null.</returns>
    /// <remarks>
    /// Хозяин возвращается вместе с редактором: открытый документ живёт дольше
    /// одного вызова, и когда плагин станут перезагружать, его документы надо
    /// будет закрыть — иначе в студии останутся вкладки, за которыми стоят
    /// объекты из выгруженного контекста.
    /// </remarks>
    public EditorMatch? EditorFor(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Перебор, а не FirstOrDefault: запись о редакторе — структура, и
        // «ничего не нашлось» вернулось бы пустой записью, которую от находки
        // не отличить.
        foreach (var registration in _editors)
        {
            if (registration.Editor.CanOpen(filePath))
                return new EditorMatch(registration.Editor, registration.PluginId);
        }

        return null;
    }

    /// <summary>Убирает вклады плагина, который выключают.</summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    public void Remove(string pluginId) =>
        _editors.RemoveAll(registration => registration.PluginId == pluginId);

    private readonly record struct EditorRegistration(string PluginId, DocumentEditor Editor);
}

/// <summary>Найденный редактор документов и плагин, который его дал.</summary>
/// <param name="Editor">Редактор, взявшийся за файл.</param>
/// <param name="PluginId">Идентификатор плагина-хозяина.</param>
public sealed record EditorMatch(DocumentEditor Editor, string PluginId);
