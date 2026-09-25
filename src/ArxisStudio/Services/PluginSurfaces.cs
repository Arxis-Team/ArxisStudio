using System.Reflection;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Панели и свои элементы полосы, построенные из кода расширений: постройка, место на экране,
/// перезапуск упавшей панели и прощание.
/// </summary>
/// <remarks>
/// Вынесено из <see cref="StudioPlugins"/>: у службы расширений шесть дорог жизни плагина, а здесь
/// одна забота — экземпляры, которые студия создала сама и которые, кроме неё, никто не видит.
/// Поэтому и прощаться с ними может только она.
/// <para>
/// Порядок, который держит служба, остаётся её: прощание — первым в уборке перед выгрузкой, пока
/// панель ещё вправе позвать студию; вклады — раньше панелей в приёме поднятого.
/// </para>
/// </remarks>
/// <param name="log">Журнал студии.</param>
/// <param name="guard">Шов, которым зовётся код расширений.</param>
/// <param name="dock">Раскладка, в которую встают панели.</param>
/// <param name="toolbar">Полоса, в которую встают элементы.</param>
internal sealed class PluginSurfaces(IStudioLog log, PluginGuard guard, StudioDock dock, StudioToolBar toolbar)
{
    private readonly IStudioLog _log = log;
    private readonly PluginGuard _guard = guard;
    private readonly StudioDock _dock = dock;
    private readonly StudioToolBar _toolbar = toolbar;

    /// <summary>
    /// Панели и элементы полосы, созданные расширениями, — по хозяину.
    /// </summary>
    /// <remarks>
    /// Держатся ради прощания: у панели есть <c>Release</c>, и позвать его
    /// можно только тому, у кого экземпляр на руках. Прежде студия брала у
    /// панели содержимое и саму панель отпускала — а вместе с ней и всё, что
    /// панель держала: процессы, потоки, подписки. Дотянуться до этого не мог
    /// никто: расширение своих экземпляров не видит, их создаёт студия.
    /// </remarks>
    private readonly Dictionary<string, List<object>> _built = new(StringComparer.Ordinal);

    /// <summary>Ставит панели и свои элементы полосы поднятого расширения.</summary>
    /// <param name="loaded">Поднятый модуль или плагин.</param>
    public void Mount(LoadedPlugin loaded)
    {
        MountPanels(loaded);
        MountToolBar(loaded);
    }

    /// <summary>
    /// Ставит панели модуля или плагина в объявленные зоны.
    /// </summary>
    /// <remarks>
    /// Зону и заголовок берём из манифеста, а сам класс панели — из сборки по
    /// атрибуту: манифест студия читает, не загружая сборку, и список панелей у
    /// неё есть раньше, чем атрибут вообще становится виден.
    /// </remarks>
    private void MountPanels(LoadedPlugin loaded)
    {
        if (loaded.Installed.Manifest is not { } manifest || loaded.Studio is not { } studio)
            return;

        var panels = Declared<ToolWindow, ToolWindowAttribute>(loaded, attribute => attribute.Id);

        foreach (var declared in manifest.Contributions.ToolWindows)
        {
            if (!panels.TryGetValue(declared.Id, out var type))
            {
                _log.Write(StudioLogLevel.Warning, "Plugins",
                    $"Панель {declared.Id} объявлена в манифесте, но в сборке её нет");
                continue;
            }

            if (Build(loaded, declared, type, studio) is not { } built)
                continue;

            // Панель живёт не прямо в дереве окна, а в своей поверхности: сбой
            // на замере или раскладке иначе унёс бы весь проход, а с ним и окно
            // студии со всеми открытыми документами.
            PluginSurface? surface = null;

            // Кто сейчас стоит за поверхностью: перезапуск прощается с ним прежде, чем строить
            // следующего. null — прежний упал на постройке, и прощаться не с кем.
            var standing = built.Panel;

            surface = new PluginSurface(
                built.Content,
                error => _guard.Report(loaded.Installed.Id, $"раскладка панели {declared.Id}", error),
                () => standing = Reload(loaded, declared, type, studio, surface!, standing));

            // Названная панелью цель ложится хранителем каретки на ту самую
            // поверхность, которую держит раскладка: спрашивать панель док не
            // умеет и не должен — он знает контролы, а не плагины.
            Keep(surface, built.Focus);

            Place(loaded.Installed, declared, surface);
        }
    }

    /// <summary>
    /// Строит панель плагина: создать, подключить, спросить содержимое.
    /// </summary>
    /// <remarks>
    /// Три чужих вызова подряд, и упасть плагин может на любом. Идут они одним
    /// куском: панель, построенная наполовину, студии не нужна.
    /// </remarks>
    private Built? Build(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolWindow declared,
        Type type,
        IStudioContext studio) =>
        _guard.Get(loaded.Installed.Id, $"панель {declared.Id}", () =>
        {
            if (Activator.CreateInstance(type) is not ToolWindow panel)
                return null;

            panel.Attach(studio);

            var content = panel.Content;

            // Цель фокуса спрашивается здесь же и один раз: панель, отвечающая
            // разное в разное время, получила бы разное поведение на ровном
            // месте. Вызов чужой, и идёт он тем же швом, что и остальные.
            var focus = panel.FocusTarget;

            // Запоминаем после того, как панель построилась: недостроенной
            // прощаться нечем, а звать Release у той, что упала на Build,
            // значит звать её во второй раз подряд по тому же поводу.
            Remember(loaded.Installed.Id, panel);

            return new Built(panel, content, focus);
        });

    /// <summary>Построенная панель: кто она, что показывать и кому отдать каретку.</summary>
    /// <param name="Panel">Сам экземпляр — с ним прощаются при перезапуске.</param>
    /// <param name="Content">Содержимое панели.</param>
    /// <param name="Focus">Кому внутри неё достаётся каретка; null — первому, кто возьмёт.</param>
    private sealed record Built(ToolWindow Panel, Control Content, Control? Focus);

    /// <summary>
    /// Строит упавшую панель заново по кнопке в заглушке.
    /// </summary>
    /// <returns>Новый экземпляр; <c>null</c> — построить не вышло, за поверхностью никого нет.</returns>
    /// <remarks>
    /// Счёт падений при этом обнуляется: человек попросил новую попытку, и
    /// отказать ему на том основании, что прежняя копия падала, значит сделать
    /// кнопку бессмысленной.
    /// <para>
    /// С прежним экземпляром прощаются, и раньше постройки нового. Без прощания упавшая панель
    /// жила до выгрузки плагина, а у встроенного модуля это закрытие студии: терминал держал свои
    /// оболочки, консоль — подписку на журнал, и каждый перезапуск добавлял ещё одну такую. Раньше
    /// постройки — потому что панели модуля делят место встречи с командой: прощание, пришедшее
    /// после, сняло бы с него уже новую панель.
    /// </para>
    /// </remarks>
    private ToolWindow? Reload(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolWindow declared,
        Type type,
        IStudioContext studio,
        PluginSurface surface,
        ToolWindow? previous)
    {
        var id = loaded.Installed.Id;

        _guard.Forget(id);

        if (previous is not null && _built.TryGetValue(id, out var mine) && mine.Remove(previous))
            _guard.Farewell(id, $"прощание панели {declared.Id}", previous.Release);

        if (Build(loaded, declared, type, studio) is not { } built)
            return null;

        // Перезапуск просят из заглушки кнопкой, державшей каретку, а новая панель встаёт на её
        // место и уносила каретку вместе с кнопкой. Она остаётся в панели — там, куда панель
        // велит, как при возвращении в неё.
        var held = surface.IsKeyboardFocusWithin;

        surface.Reset(built.Content);

        Keep(surface, built.Focus);

        if (held)
            Dispatcher.UIThread.Post(() => DockFocus.Restore(surface), DispatcherPriority.Loaded);

        return built.Panel;
    }

    /// <summary>
    /// Кладёт названную панелью цель целью каретки её поверхности.
    /// </summary>
    /// <param name="surface">Поверхность, которую держит раскладка.</param>
    /// <param name="target">Что назвала панель; null — она не называла ничего.</param>
    /// <remarks>
    /// Цель, а не хранитель: хранителя раскладка переписывает всякий раз, как каретка
    /// уходит из панели, и цель, положенная хранителем, жила до первого ухода — а
    /// хранитель потом умирал вместе со строкой или сеансом, и каретка шла к первому
    /// попавшемуся. Цель остаётся запасом на всю жизнь панели.
    /// <para>
    /// Чужой контрол здесь не отсеивается, и это не упущение: панель могла
    /// назвать что угодно, но проверяет названное <see cref="DockFocus.Restore"/>
    /// — он и отдаёт каретку, и он один знает, лежит ли цель внутри. Вторая
    /// такая же проверка здесь была бы мёртвой: снять её можно, ничего не
    /// сломав, а комментарий над ней утверждал бы обратное.
    /// </para>
    /// </remarks>
    private static void Keep(Control surface, Control? target)
    {
        if (target is not null)
            DockFocus.SetTarget(surface, target);
    }

    /// <summary>Ставит содержимое панели в раскладку студии.</summary>
    /// <param name="plugin">Чья это панель — по нему её потом и снимут.</param>
    /// <param name="declared">Объявление панели из манифеста.</param>
    /// <param name="content">Построенное содержимое панели.</param>
    /// <remarks>
    /// Имя панели в раскладке — с именем плагина впереди: манифест обещает
    /// уникальность только внутри своего плагина, а дерево доков одно на всю
    /// студию и переживает перезапуск.
    /// </remarks>
    private void Place(InstalledPlugin plugin, Sdk.Plugins.PluginToolWindow declared, Control content)
    {
        var id = PanelId(plugin.Id, declared.Id);

        // Замечание о значке уже прозвучало, когда манифест читали, — здесь его
        // только рисуют: панель встаёт при каждом подъёме плагина.
        var icon = ManifestIcons.Resolve(declared.Icon, out _);

        _dock.Add(plugin.Id, id, declared.Wanted, declared.Title, plugin.Strings, content, icon);

        _log.Write(StudioLogLevel.Debug, "Plugins",
            $"Панель «{plugin.Strings.Resolve(declared.Title)}» встала в раскладку");
    }

    /// <summary>Имя панели расширения в раскладке.</summary>
    /// <param name="pluginId">Чья панель.</param>
    /// <param name="toolWindowId">Её имя в манифесте.</param>
    /// <remarks>
    /// Одно на студию: по нему панель ставят, показывают и отдают ей каретку, и имя, собранное в
    /// каждом из этих мест своими руками, разошлось бы с остальными при первой правке одного.
    /// </remarks>
    public static string PanelId(string pluginId, string toolWindowId) => $"{pluginId}:{toolWindowId}";

    /// <summary>Снимает со стен и с полосы всё, что поставило расширение.</summary>
    /// <param name="pluginId">Чьё снять.</param>
    public void Unmount(string pluginId)
    {
        _dock.RemoveOwnedBy(pluginId);
        _toolbar.RemoveOwnedBy(pluginId);
    }

    /// <summary>
    /// Ставит в полосу свои контролы модуля или плагина.
    /// </summary>
    /// <remarks>
    /// Кнопки и меню стоят с объявления; здесь достраивается то, чего без
    /// сборки не нарисовать. Класс — по атрибуту, как у панели. Объявленное
    /// объявляется заново: реестр ничего не пересобирает, а на дороге
    /// перезагрузки возвращает снятое.
    /// </remarks>
    private void MountToolBar(LoadedPlugin loaded)
    {
        if (loaded.Installed.Manifest is not { } manifest || loaded.Studio is not { } studio)
            return;

        var items = Declared<ToolBarItem, ToolBarItemAttribute>(loaded, attribute => attribute.Id);

        foreach (var declared in manifest.Contributions.ToolBar)
        {
            if (!declared.IsCustom)
            {
                _toolbar.Add(loaded.Installed, declared);
                continue;
            }

            if (!items.TryGetValue(declared.Id, out var type))
            {
                _log.Write(StudioLogLevel.Warning, "Plugins",
                    $"Элемент полосы {declared.Id} объявлен в манифесте, но в сборке его нет");
                continue;
            }

            if (BuildItem(loaded, declared, type, studio) is not { } content)
                continue;

            var id = loaded.Installed.Id;

            // Заглушки в полосе нет: в сорок пикселей она не поместится, а
            // держала бы замыкание с типами плагина. Упавший элемент снимается
            // — следующим проходом, потому что сюда приходят из прохода
            // раскладки, и вынимать контрол посреди него нельзя.
            var surface = new PluginSurface(
                content,
                error =>
                {
                    _guard.Report(id, $"раскладка элемента полосы {declared.Id}", error);
                    Dispatcher.UIThread.Post(() => _toolbar.Remove(id, declared.Id));
                });

            _toolbar.Add(loaded.Installed, declared, surface);
        }
    }

    /// <summary>Строит свой контрол плагина: создать, подключить, спросить содержимое — одним куском.</summary>
    private Control? BuildItem(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolBarItem declared,
        Type type,
        IStudioContext studio) =>
        _guard.Get(loaded.Installed.Id, $"элемент полосы {declared.Id}", () =>
        {
            if (Activator.CreateInstance(type) is not ToolBarItem item)
                return null;

            item.Attach(studio);

            var content = item.Content;

            Remember(loaded.Installed.Id, item);

            return content;
        });

    /// <summary>Запоминает созданное расширением — чтобы было с кем прощаться.</summary>
    /// <param name="pluginId">Чьё это.</param>
    /// <param name="built">Панель или элемент полосы.</param>
    private void Remember(string pluginId, object built)
    {
        if (!_built.TryGetValue(pluginId, out var mine))
            _built[pluginId] = mine = [];

        mine.Add(built);
    }

    /// <summary>
    /// Прощается с панелями и элементами полосы расширения.
    /// </summary>
    /// <remarks>
    /// Зовётся с уборкой хоста, до выгрузки сборки: панели она и нужна — там
    /// закрываются процессы и снимаются подписки, которые иначе не дали бы
    /// контексту загрузки уйти.
    /// <para>
    /// Через шов, как всякий чужой вызов: расширение вольно упасть и на
    /// прощании, а выгрузка от этого останавливаться не должна. Но прощальной его дорогой, а не
    /// рабочей: рабочая отказывает отключённому за сбои, и как раз у него <c>Release</c> не звался.
    /// </para>
    /// </remarks>
    /// <param name="pluginId">Кто уходит.</param>
    public void Release(string pluginId)
    {
        if (!_built.Remove(pluginId, out var mine))
            return;

        foreach (var built in mine)
        {
            switch (built)
            {
                case ToolWindow panel:
                    _guard.Farewell(pluginId, "прощание панели", panel.Release);
                    break;

                case ToolBarItem item:
                    _guard.Farewell(pluginId, "прощание элемента полосы", item.Release);
                    break;
            }
        }
    }

    /// <summary>
    /// Классы расширения, помеченные атрибутом вклада, — по объявленному имени.
    /// </summary>
    /// <remarks>
    /// Панели и элементы полосы ищутся одинаково, и разница между ними ровно в
    /// двух типах. Два одинаковых перебора сборок рядом расходились бы при
    /// первой же правке одного из них.
    /// </remarks>
    private static Dictionary<string, Type> Declared<TBase, TAttribute>(
        LoadedPlugin loaded,
        Func<TAttribute, string> name)
        where TAttribute : Attribute =>
        loaded.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(TBase).IsAssignableFrom(type))
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<TAttribute>()))
            .Where(found => found.Attribute is not null)
            .ToDictionary(found => name(found.Attribute!), found => found.Type, StringComparer.Ordinal);
}
