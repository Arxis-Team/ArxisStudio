using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Жизнь расширений студии: поднять, показать, снять, поднять заново.
/// </summary>
/// <remarks>
/// Путь у модуля и плагина один: оба активируются общим контрактом, заявляют
/// панели и редакторы, попадают в реестр вкладов. Разница только в доставке —
/// модуль приезжает со студией и живёт в основном контексте.
/// <para>
/// Служба, а не часть окна. Здесь шесть дорог, на которых расширение меняет
/// состояние студии — первый подъём, пробуждение спящего, перезагрузка,
/// отключение за сбои, снятие панелей, закрытие студии, — и все шесть трогают
/// одни и те же реестры. Пока они лежали в окне, проверить их было нечем:
/// главное окно поднимает раскладку, читает папку плагинов и цепляется к
/// обработчикам платформы. Списки на этих дорогах уже разъезжались — снятие
/// упавшего забывало команды, а закрытие студии не убирало ничего.
/// </para>
/// </remarks>
public sealed class StudioPlugins
{
    private readonly StudioLog _log;
    private readonly PluginGuard _guard;
    private readonly StudioTaskRegistry _tasks;
    private readonly PluginContributionRegistry _contributions;
    private readonly StudioExportRegistry _exports = new();
    private readonly PluginRelease _release;

    private PluginHost? _host;
    private IReadOnlyList<InstalledPlugin> _installed = [];
    private IReadOnlyList<InstalledPlugin> _modules = [];

    /// <summary>
    /// Заводит службу над реестрами студии.
    /// </summary>
    /// <param name="log">Журнал студии.</param>
    /// <param name="guard">Шов, которым считаются сбои расширений.</param>
    /// <param name="tasks">Реестр задач: их останавливают перед выгрузкой.</param>
    /// <param name="contributions">Реестр вкладов: рисовальщики, инспекторы, редакторы.</param>
    public StudioPlugins(
        StudioLog log,
        PluginGuard guard,
        StudioTaskRegistry tasks,
        PluginContributionRegistry contributions)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(contributions);

        _log = log;
        _guard = guard;
        _tasks = tasks;
        _contributions = contributions;

        _release = new PluginRelease(tasks);
    }

    /// <summary>Реестр команд: через него идут щелчки по кнопкам расширений.</summary>
    public required StudioCommands Commands { get; init; }

    /// <summary>Раскладка, в которую встают панели.</summary>
    public required StudioDock Dock { get; init; }

    /// <summary>Полоса, в которую встают кнопки и меню.</summary>
    public required StudioToolBar ToolBar { get; init; }

    /// <summary>Документы: их закрывают перед выгрузкой хозяина.</summary>
    public required StudioDocuments Documents { get; init; }

    /// <summary>Что студия даёт расширениям сверх обязательного.</summary>
    public required IReadOnlyDictionary<Type, object> Services { get; init; }

    /// <summary>
    /// Сборки встроенных модулей в порядке подъёма.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="Catalog"/>, потому что и берутся они по-разному:
    /// модуль приезжает со студией, плагин — из папки.
    /// </remarks>
    public IReadOnlyList<Assembly> Assemblies { get; init; } = StudioModules.Assemblies;

    /// <summary>
    /// Где студия берёт список установленных плагинов.
    /// </summary>
    /// <remarks>
    /// Не поле, а способ спросить: список перечитывается с диска и при старте, и
    /// при каждой перезагрузке — манифест мог измениться вместе со сборкой.
    /// </remarks>
    public Func<IReadOnlyList<InstalledPlugin>> Catalog { get; init; } = () => new PluginCatalog().Scan();

    /// <summary>Установленные плагины — как их видит каталог.</summary>
    public IReadOnlyList<InstalledPlugin> Installed => _installed;

    /// <summary>Встроенные модули — записи о них, а не поднятый код.</summary>
    public IReadOnlyList<InstalledPlugin> Modules => _modules;

    /// <summary>
    /// Кто сейчас вправе вкладываться в меню: модули и установленные, кроме
    /// отключённых за сбои.
    /// </summary>
    public IEnumerable<InstalledPlugin> Contributing =>
        _modules.Concat(_installed).Where(plugin => !_guard.IsFaulty(plugin.Id));

    /// <summary>
    /// Плагины, которые можно поднять заново.
    /// </summary>
    /// <remarks>
    /// Только внешние: у встроенного модуля нет своего контекста загрузки, и
    /// предлагать перезагрузить то, что перезагрузить нельзя, — обещание,
    /// которое студия не сдержит.
    /// </remarks>
    public IReadOnlyList<InstalledPlugin> Reloadable =>
        _host?.Loaded
            .Where(plugin => plugin is { IsLoaded: true, Context: not null })
            .Select(plugin => plugin.Installed)
            .ToList() ?? [];

    /// <summary>
    /// Поднимает всё разом: подготовка, модули, плагины.
    /// </summary>
    /// <remarks>
    /// Три шага порознь нужны заставке: она называет человеку, что именно
    /// сейчас грузится, и «загрузка модулей» с «загрузкой плагинов» — это
    /// разные строки, а не одна. Тому, кому подробности не нужны, довольно
    /// этого вызова.
    /// </remarks>
    public void Start()
    {
        Prepare();
        LoadModules();
        LoadPlugins();
    }

    /// <summary>
    /// Готовит хост, реестры и полосу — не поднимая ничьего кода.
    /// </summary>
    /// <remarks>
    /// Подписки ставятся здесь, а не в конструкторе: свойства инициализации к
    /// этому моменту заполнены, а до первого подъёма событий, на которые тут
    /// подписываются, всё равно нет.
    /// <para>
    /// Повторный вызов ничего не делает: два следующих шага зовут подготовку
    /// сами, и порядок из-за этого перестаёт быть ловушкой — забыть его нельзя.
    /// </para>
    /// </remarks>
    public void Prepare()
    {
        if (_host is not null)
            return;

        var roster = new StudioPluginRoster();

        // Уборка перед выгрузкой: задачи, документы, экран — в одном порядке на
        // все дороги. Реестры владельца хост убирает сам, по своему Unloading.
        _release.Documents = Documents.CloseOwnedByAsync;
        _release.Views = Unmount;

        _exports.Conflict += (_, message) => _log.Write(StudioLogLevel.Warning, "Plugins", message);
        _contributions.Conflict += (_, message) => _log.Write(StudioLogLevel.Warning, "Plugins", message);

        _guard.Failed += (_, failure) => _log.Write(
            StudioLogLevel.Error, "Plugins",
            $"{Named(failure.PluginId)}: {failure.What} — {failure.Message}");

        _guard.Disabled += (_, failure) => Disable(failure);

        _release.Lingered += (_, id) => _log.Write(StudioLogLevel.Warning, "Plugins",
            $"{Named(id)}: фоновая задача не остановилась за пять секунд");

        // Открытие файла — тоже событие: плагин, объявивший его тип, ждал
        // именно этого.
        Documents.Opening += (_, path) => Activate(
            waiting => PluginActivation.WaitsForFileType(waiting.Manifest, Path.GetExtension(path)));

        var host = new PluginHost(new StudioContextFactory(
            _log,
            Commands,
            // Проекта у студии пока нет: работа с ними приедет модулем.
            // Место в контракте плагинов остаётся — сам контракт не менялся.
            projectPath: null,
            Services,
            settings: null,
            tasks: _tasks,
            guard: _guard,
            plugins: roster,
            exports: _exports,
            toolbar: ToolBar,
            dock: Dock));

        // Уборка реестров, заведённых на владельца, — по одному сигналу от
        // хоста: он один знает про все дороги выгрузки. Раньше её переписывал
        // каждый, кто выгружает, и списки успели разъехаться — снятие
        // упавшего забывало команды, а закрытие студии не убирало ничего.
        host.Unloading += (_, id) =>
        {
            Commands.RemoveOwnedBy(id);
            _exports.RemoveOwnedBy(id);
            _contributions.Remove(id);
        };

        _host = host;
        _installed = Catalog();

        // Ядро подключается до первого подъёма: контексты раздаются при
        // загрузке, и служба соседей обязана отвечать правду с первого.
        roster.Attach(host, () => _installed);

        // Пробуждение по команде живёт в реестре, а не только в меню: команду
        // соседа зовут и из кода плагина, и дорога обязана быть одна. Подъём
        // ставит панели, поэтому вне потока интерфейса он откладывается — тот
        // Invoke честно вернёт false, а хозяин поднимется следом.
        Commands.Awaken = command =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Activate(waiting => PluginActivation.WaitsForCommand(waiting.Manifest, command));
                return;
            }

            _log.Write(StudioLogLevel.Warning, "Plugins",
                $"Команда {command} позвана вне потока интерфейса — хозяин поднимется следом");

            Dispatcher.UIThread.Post(() =>
                Activate(waiting => PluginActivation.WaitsForCommand(waiting.Manifest, command)));
        };

        // Полоса собирается по манифестам до подъёма кого бы то ни было: кнопки
        // и меню сборки не требуют, и спящий плагин получает их здесь и только
        // здесь. Модуль, поднятый следом, может тут же выключить свой элемент
        // из Activate — слово должно найти запись.
        MountDeclared(StudioModules.Describe(Assemblies).Concat(_installed));
    }

    /// <summary>
    /// Поднимает встроенные модули.
    /// </summary>
    /// <remarks>
    /// Первыми, до внешних плагинов: панели студии должны стоять на своих
    /// местах раньше, чем к ним встанут чужие.
    /// </remarks>
    public void LoadModules()
    {
        Prepare();

        if (_host is not { } host)
            return;

        var modules = Assemblies.Select(host.LoadBuiltIn).ToList();

        _modules = modules.Select(loaded => loaded.Installed).ToList();

        foreach (var loaded in modules)
            Accept(loaded);
    }

    /// <summary>Поднимает включённые плагины — тех из них, кто не ждёт события.</summary>
    public void LoadPlugins()
    {
        Prepare();

        if (_host is not { } host)
            return;

        foreach (var loaded in host.LoadStartup(_installed))
            Accept(loaded);

        // Заметки графа — не отказы, но молчать о них нельзя: устаревший
        // необязательный сосед считается отсутствующим, и человек должен
        // узнать об этом отсюда, а не гадать, почему связка не работает.
        foreach (var note in host.Resolution?.Notes ?? [])
            _log.Write(StudioLogLevel.Warning, "Plugins", note);

        foreach (var waiting in host.Deferred)
            _log.Write(StudioLogLevel.Debug, "Plugins", $"{waiting.DisplayName} ждёт своего события");
    }

    /// <summary>
    /// Поднимает ждущие расширения, которым подошло событие.
    /// </summary>
    /// <param name="matches">Какое событие произошло.</param>
    public void Activate(Func<InstalledPlugin, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (_host is not { } host)
            return;

        foreach (var waiting in host.Deferred.Where(matches).ToList())
        {
            _log.Write(StudioLogLevel.Info, "Plugins",
                $"{Localizer.Instance["menu.activating"]}: {waiting.DisplayName}");

            foreach (var loaded in host.Activate(waiting.Id))
                Accept(loaded);
        }
    }

    /// <summary>
    /// Приписывает исключение расширению, если его код есть в стеке.
    /// </summary>
    /// <param name="error">Исключение, пришедшее мимо шва.</param>
    /// <param name="what">Чем занималась студия, когда оно пришло.</param>
    /// <returns><c>true</c>, если виновник найден и записан.</returns>
    public bool Blame(Exception? error, string what)
    {
        if (_host?.Blame(error) is not { } plugin || error is null)
            return false;

        _guard.Report(plugin.Installed.Id, what, error);

        return true;
    }

    /// <summary>Отпускает хост со всеми контекстами загрузки — студию закрывают.</summary>
    public void Stop() => _host?.Dispose();

    /// <summary>
    /// Опускает плагин вместе с зависимыми и поднимает всех заново.
    /// </summary>
    /// <param name="pluginId">Кого перезагружают.</param>
    /// <returns>Жалоба человеку, если прежняя копия осталась в памяти; иначе null.</returns>
    public async Task<string?> ReloadAsync(string pluginId)
    {
        if (_host is not { } host)
            return null;

        // Зависимые считаются по манифестам прежних копий: перезагружают
        // потому, что плагин изменился, и свежий манифест мог зависимость
        // убрать — а прежний зависимый всё ещё держит прежние типы. Вместе с
        // необязательными: их гарантия «сосед стоит подо мной» не делится.
        var dependents = PluginGraph.Dependents(
                pluginId,
                host.Loaded
                    .Where(loaded => loaded is { IsLoaded: true, Context: not null })
                    .Select(loaded => loaded.Installed)
                    .ToList(),
                includeOptional: true)
            .Select(dependent => dependent.Id)
            .ToList();

        // Манифесты могли измениться вместе со сборками — записи берутся с
        // диска. Опускаются зависимые первыми, зависимость последней;
        // поднимается всё в обратном порядке.
        _installed = Catalog();

        if (_installed.FirstOrDefault(plugin => plugin.Id == pluginId) is not { } installed)
        {
            _log.Write(StudioLogLevel.Warning, "Plugins", $"Плагина {pluginId} больше нет в папке плагинов");
            return null;
        }

        var lower = dependents.Append(pluginId).ToList();
        var raise = new List<InstalledPlugin> { installed };

        foreach (var dependentId in dependents)
        {
            if (_installed.FirstOrDefault(plugin => plugin.Id == dependentId) is { } dependent)
                raise.Add(dependent);
            else
                _log.Write(StudioLogLevel.Warning, "Plugins",
                    $"{Named(dependentId)} зависел от {installed.DisplayName}, но пропал с диска — опущен и не поднят");
        }

        foreach (var id in lower)
        {
            await _release.LetGoAsync(id);

            // Счёт сбоев обнуляется только здесь: обновлённый плагин отвечает за
            // себя, а не за грехи прежней копии. Отключённому упавшему такого
            // прощения не полагается — потому это и не в общей уборке.
            _guard.Forget(id);
        }

        // Снятые контролы отпускает не список, а дерево: пока проход раскладки
        // и отрисовки не прошёл, они ещё чьи-то. Ждём его — иначе проверка
        // выгрузки увидит помеху, которой через миг не будет. Проход один на
        // всех: дерево тоже одно.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        var cascade = host.Reload(lower, raise);

        foreach (var skipped in cascade.Skipped)
            _log.Write(StudioLogLevel.Warning, "Plugins", skipped.Value);

        foreach (var note in cascade.Notes)
            _log.Write(StudioLogLevel.Warning, "Plugins", note);

        foreach (var loaded in cascade.Raised)
            Accept(loaded);

        // Выгрузка кооперативная, и не удаться она может по вине любого из
        // опущенных: подписка на событие студии, оставленный таймер,
        // работающий поток. Каждый невыгрузившийся называется своим именем —
        // безымянное предупреждение не говорит, кого чинить.
        var stuck = cascade.Released
            .Where(pair => !pair.Value)
            .Select(pair => Named(pair.Key))
            .ToList();

        if (stuck.Count == 0)
            return null;

        var warning = $"{string.Join(", ", stuck)}: прежняя копия осталась в памяти — надёжнее перезапустить студию";

        _log.Write(StudioLogLevel.Warning, "Plugins", warning);

        return warning;
    }

    /// <summary>Как расширение называется в сообщениях.</summary>
    private string Named(string pluginId) =>
        _modules.Concat(_installed).FirstOrDefault(plugin => plugin.Id == pluginId)?.DisplayName ?? pluginId;

    /// <summary>
    /// Ставит в полосу всё, что объявлено манифестами, — не поднимая никого.
    /// </summary>
    /// <remarks>
    /// Кнопка и меню сборки не требуют: студия рисует их сама, а щелчок будит
    /// хозяина через реестр команд. Свой контрол здесь только занимает место —
    /// придёт он, когда плагин поднимут.
    /// </remarks>
    private void MountDeclared(IEnumerable<InstalledPlugin> plugins)
    {
        foreach (var plugin in plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }))
        {
            foreach (var declared in plugin.Manifest!.Contributions.ToolBar)
                ToolBar.Add(plugin, declared);
        }
    }

    /// <summary>Принимает поднятый модуль или плагин: вклады и панели.</summary>
    private void Accept(LoadedPlugin loaded)
    {
        if (loaded.Error is { } error)
        {
            _log.Write(StudioLogLevel.Error, "Plugins", $"{loaded.Installed.DisplayName}: {error}");

            // Кнопки несостоявшегося плагина стоять не должны: команда за ними
            // не найдётся никогда.
            ToolBar.RemoveOwnedBy(loaded.Installed.Id);
            return;
        }

        _log.Write(StudioLogLevel.Info, "Plugins", $"{loaded.Installed.DisplayName} поднят");

        _contributions.Add(loaded);
        MountPanels(loaded);
        MountToolBar(loaded);
    }

    /// <summary>
    /// Отключает расширение, падающее раз за разом.
    /// </summary>
    /// <remarks>
    /// Три падения подряд — это не случайность, а сломанный плагин, и звать его
    /// дальше значит показывать человеку одну и ту же ошибку до конца сеанса.
    /// <para>
    /// Панели при этом уходят со стен, хотя раньше обещалось оставить вместо них
    /// заглушки. Обещание было невыполнимым: заглушка панели держит замыкание
    /// перезапуска, а оно — типы плагина и через них его контекст загрузки.
    /// Студия выгружала бы плагин только на словах и сама же потом жаловалась,
    /// что прежняя копия осталась в памяти. О случившемся говорит журнал и
    /// менеджер плагинов, а не пустая рамка на экране.
    /// </para>
    /// </remarks>
    private void Disable(PluginFailure failure)
    {
        _log.Write(StudioLogLevel.Error, "Plugins",
            $"{Named(failure.PluginId)}: отключён после {failure.Count} сбоев подряд");

        // Всё откладывается. Сюда попадают в том числе из прохода раскладки —
        // барьер панели сообщает о сбое прямо на замере, — а ни ждать задачи, ни
        // вынимать контролы из дерева окна во время его же прохода нельзя.
        //
        // Ждать не страшно: гвард пометил плагина сбойным раньше, чем позвал
        // сюда, и звать его код он уже отказывается.
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await _release.LetGoAsync(failure.PluginId);

                // Снятые контролы отпускает дерево, а не список: ждём его
                // проход, иначе выгрузка упрётся в помеху, которой через миг
                // не будет.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            finally
            {
                // Через хост, а не Unload напрямую: только он снимет запись со
                // счёта и разошлёт уборку реестрам. В finally, потому что выше
                // закрываются документы плагина — его же кодом, и упасть он
                // волен и здесь; бросить плагина неснятым нельзя.
                _host?.Drop(failure.PluginId);
            }
        });
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

            if (Build(loaded, declared, type, studio) is not { } content)
                continue;

            // Панель живёт не прямо в дереве окна, а в своей поверхности: сбой
            // на замере или раскладке иначе унёс бы весь проход, а с ним и окно
            // студии со всеми открытыми документами.
            PluginSurface? surface = null;

            surface = new PluginSurface(
                content,
                error => _guard.Report(loaded.Installed.Id, $"раскладка панели {declared.Id}", error),
                () => Reload(loaded, declared, type, studio, surface!));

            Mount(loaded.Installed, declared, surface);
        }
    }

    /// <summary>
    /// Строит панель плагина: создать, подключить, спросить содержимое.
    /// </summary>
    /// <remarks>
    /// Три чужих вызова подряд, и упасть плагин может на любом. Идут они одним
    /// куском: панель, построенная наполовину, студии не нужна.
    /// </remarks>
    private Control? Build(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolWindow declared,
        Type type,
        IStudioContext studio) =>
        _guard.Get(loaded.Installed.Id, $"панель {declared.Id}", () =>
        {
            if (Activator.CreateInstance(type) is not ToolWindow panel)
                return null;

            panel.Attach(studio);

            return panel.Content;
        });

    /// <summary>
    /// Строит упавшую панель заново по кнопке в заглушке.
    /// </summary>
    /// <remarks>
    /// Счёт падений при этом обнуляется: человек попросил новую попытку, и
    /// отказать ему на том основании, что прежняя копия падала, значит сделать
    /// кнопку бессмысленной.
    /// </remarks>
    private void Reload(
        LoadedPlugin loaded,
        Sdk.Plugins.PluginToolWindow declared,
        Type type,
        IStudioContext studio,
        PluginSurface surface)
    {
        _guard.Forget(loaded.Installed.Id);

        if (Build(loaded, declared, type, studio) is { } content)
            surface.Reset(content);
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
    private void Mount(InstalledPlugin plugin, Sdk.Plugins.PluginToolWindow declared, Control content)
    {
        var id = Panel(plugin.Id, declared.Id);

        Dock.Add(plugin.Id, id, declared.Wanted, declared.Title, plugin.Strings, content);

        _log.Write(StudioLogLevel.Debug, "Plugins",
            $"Панель «{plugin.Strings.Resolve(declared.Title)}» встала в раскладку");
    }

    /// <summary>Имя панели в раскладке.</summary>
    private static string Panel(string pluginId, string toolWindowId) => $"{pluginId}:{toolWindowId}";

    /// <summary>Снимает со стен и с полосы всё, что поставило расширение.</summary>
    private void Unmount(string pluginId)
    {
        Dock.RemoveOwnedBy(pluginId);
        ToolBar.RemoveOwnedBy(pluginId);
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
                ToolBar.Add(loaded.Installed, declared);
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
                    Dispatcher.UIThread.Post(() => ToolBar.Remove(id, declared.Id));
                });

            ToolBar.Add(loaded.Installed, declared, surface);
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

            return item.Content;
        });

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
