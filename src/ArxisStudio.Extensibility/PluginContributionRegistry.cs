using System.Reflection;
using ArxisStudio.Sdk;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Что плагины добавили к студии экземплярами: редакторы документов и код пунктов создания.
/// </summary>
/// <remarks>
/// Реестр один на студию и заполняется при поднятии плагинов. Здесь живут
/// вклады, которые нельзя объявить манифестом: студия спрашивает редактор о
/// каждом открываемом файле, а ответить может только его код. Пункт создания
/// вида <c>code</c> объявлен манифестом, но файлы собирает его класс — и он
/// живёт здесь же, экземпляром, до выгрузки хозяина.
/// <para>
/// Рисовальщики свойств и свои инспекторы жили здесь же и сняты вместе со
/// своим контрактом: показывать их было некому с тех пор, как дизайнер уехал
/// из репозитория (этап 17). Вернутся они вместе с ним и вместе с тем, кто их
/// показывает, — заодно избавившись от изъяна, с которым уходят: рисовальщик
/// не видел студию и потому не мог взять даже свои строки.
/// </para>
/// <para>
/// Редактор — чужой код, и зовётся он через шов на каждой дороге: постройка, подключение, вопрос
/// «возьмёшься ли». Пока он звался напрямую, конструктор одного редактора обрывал приём всех
/// плагинов, поднятых после него, — хост их уже активировал, а панелей, полосы и редакторов они не
/// получали, — а бросающий <c>CanOpen</c> валил открытие любого файла у всех.
/// </para>
/// </remarks>
/// <param name="guard">Шов вызовов плагина; null — завести свой, без счёта на всю студию.</param>
public sealed class PluginContributionRegistry(PluginGuard? guard = null)
{
    private readonly PluginGuard _guard = guard ?? new PluginGuard();
    private readonly List<EditorRegistration> _editors = [];
    private readonly List<MakerRegistration> _makers = [];

    /// <summary>
    /// Собирает вклады плагина из его сборок.
    /// </summary>
    /// <param name="plugin">Поднятый плагин.</param>
    public void Add(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        Add(plugin.Installed.Id, plugin.Installed.DisplayName, plugin.Assemblies, plugin.Studio);
    }

    /// <summary>
    /// Собирает вклады из перечисленных сборок.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <param name="displayName">Как плагин называется в сообщениях.</param>
    /// <param name="assemblies">Сборки, в которых искать вклады.</param>
    /// <param name="studio">Контекст, который получат редакторы документов.</param>
    public void Add(
        string pluginId,
        string displayName,
        IEnumerable<Assembly> assemblies,
        IStudioContext? studio = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var type in PluginTypes.Concrete(assemblies))
        {
            if (typeof(NewItemMaker).IsAssignableFrom(type))
            {
                AddMaker(pluginId, type, studio);
                continue;
            }

            // Редактор документов живёт экземпляром: студия спрашивает его о
            // каждом открываемом файле.
            if (!typeof(DocumentEditor).IsAssignableFrom(type))
                continue;

            // Постройка и подключение — одним куском и через шов: редактор, построенный
            // наполовину, студии не нужен, а упавший не должен стоить соседям ничего.
            var editor = _guard.Get(pluginId, $"редактор {type.Name}", () =>
            {
                if (Activator.CreateInstance(type) is not DocumentEditor built)
                    return null;

                if (studio is not null)
                    built.Attach(studio);

                return built;
            });

            if (editor is not null)
                _editors.Add(new EditorRegistration(pluginId, editor));
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
            var takes = false;

            // Вопрос — тоже чужой код. Упавший на нём редактор за файл не берётся, и спрашивают
            // следующего: один сломанный плагин не должен закрывать файлы остальным.
            if (_guard.Run(
                    registration.PluginId,
                    $"вопрос редактору о {Path.GetFileName(filePath)}",
                    () => takes = registration.Editor.CanOpen(filePath))
                && takes)
            {
                return new EditorMatch(registration.Editor, registration.PluginId);
            }
        }

        return null;
    }

    /// <summary>Код пункта создания, который дал плагин.</summary>
    /// <param name="pluginId">Идентификатор плагина — хозяина пункта.</param>
    /// <param name="itemId">Идентификатор пункта, как в манифесте.</param>
    /// <returns>Экземпляр кода; null — плагин не поднят или такого класса у него нет.</returns>
    /// <remarks>
    /// По хозяину и пункту, а не по одному пункту: приставка в идентификаторе — уговор, а не запрет,
    /// и одноимённый пункт соседа не должен собирать чужие файлы.
    /// </remarks>
    public NewItemMaker? MakerFor(string pluginId, string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        foreach (var registration in _makers)
        {
            if (registration.PluginId == pluginId && registration.ItemId == itemId)
                return registration.Maker;
        }

        return null;
    }

    /// <summary>Убирает вклады плагина, который выключают.</summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    public void Remove(string pluginId)
    {
        _editors.RemoveAll(registration => registration.PluginId == pluginId);
        _makers.RemoveAll(registration => registration.PluginId == pluginId);
    }

    /// <summary>
    /// Заводит код пункта создания: класс с <see cref="NewItemAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Класс без атрибута не заводится: связать его с пунктом манифеста нечем. Второй класс на тот же
    /// пункт тоже: собирать файлы пункта обязан кто-то один, и им остаётся первый.
    /// </remarks>
    private void AddMaker(string pluginId, Type type, IStudioContext? studio)
    {
        if (type.GetCustomAttribute<NewItemAttribute>() is not { Id: { Length: > 0 } itemId } ||
            MakerFor(pluginId, itemId) is not null)
        {
            return;
        }

        // Постройка и подключение — одним куском и через шов, как у редактора.
        var maker = _guard.Get(pluginId, $"код пункта создания {itemId}", () =>
        {
            if (Activator.CreateInstance(type) is not NewItemMaker built)
                return null;

            if (studio is not null)
                built.Attach(studio);

            return built;
        });

        if (maker is not null)
            _makers.Add(new MakerRegistration(pluginId, itemId, maker));
    }

    private readonly record struct EditorRegistration(string PluginId, DocumentEditor Editor);

    private readonly record struct MakerRegistration(string PluginId, string ItemId, NewItemMaker Maker);
}

/// <summary>Найденный редактор документов и плагин, который его дал.</summary>
/// <param name="Editor">Редактор, взявшийся за файл.</param>
/// <param name="PluginId">Идентификатор плагина-хозяина.</param>
public sealed record EditorMatch(DocumentEditor Editor, string PluginId);
