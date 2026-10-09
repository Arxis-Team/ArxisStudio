using System.Reflection;
using System.Runtime.Loader;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Что расширения добавили к студии и к контрактам модулей экземплярами: редакторы документов, код
/// пунктов создания и вклады, которые собирают модули.
/// </summary>
/// <remarks>
/// Реестр один на студию и заполняется при поднятии плагинов. Здесь живут
/// вклады, которые нельзя объявить манифестом: студия спрашивает редактор о
/// каждом открываемом файле, а ответить может только его код. Пункт создания
/// вида <c>code</c> объявлен манифестом, но файлы собирает его класс — и он
/// живёт здесь же, экземпляром, до выгрузки хозяина.
/// <para>
/// Рисовальщики свойств и свои инспекторы жили здесь же и сняты вместе со своим контрактом, когда
/// дизайнер уехал из репозитория (этап 17). Вернулись они вкладами в контракт модуля
/// (<see cref="Collect{T}"/>, запись 358): инспектор теперь модуль со своим контрактом, а модуль не
/// знает, кто его зовёт, — контекст не называет расширение, — и потому собирает, строит, зовёт через шов
/// и снимает чужие классы студия, как свои. Прежний изъян ушёл вместе с прежним контрактом: вклад
/// получает контекст хозяина и берёт свои строки.
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
    private readonly List<RaisedExtension> _raised = [];
    private readonly List<ContributionSet> _sets = [];

    // Ручки к чужим объектам — слабыми ссылками: ручку держит тот, кто её взял, а реестру она нужна
    // только затем, чтобы отпустить объект с выгрузкой хозяина. Сильная ссылка отсюда держала бы и
    // ручку, и объект, и его контекст загрузки — дольше того, кому они были нужны.
    private readonly List<WeakReference<IReleasable>> _guarded = [];

    /// <summary>
    /// О чём сказать в журнал, ничего не роняя: тип не годится в набор, класс вклада не построить.
    /// </summary>
    public event EventHandler<string>? Note;

    /// <summary>
    /// Чем будить спящих, ждущих сбора вкладов в контракт; аргумент — идентификатор хозяина контракта.
    /// </summary>
    /// <remarks>
    /// Будит служба расширений: подъём ставит панели и раздаёт вклады, и дорога у него одна.
    /// </remarks>
    public Action<string>? Awaken { get; set; }

    /// <summary>Шов, через который зовутся вклады и чужие объекты.</summary>
    internal PluginGuard Seam => _guard;

    /// <summary>
    /// Собирает вклады плагина из его сборок.
    /// </summary>
    /// <param name="plugin">Поднятый плагин.</param>
    public void Add(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        Add(
            plugin.Installed.Id,
            plugin.Installed.DisplayName,
            plugin.Assemblies,
            plugin.Studio,
            plugin.Installed.IsBuiltIn,
            plugin.Context);
    }

    /// <summary>
    /// Собирает вклады из перечисленных сборок.
    /// </summary>
    /// <param name="pluginId">Идентификатор плагина.</param>
    /// <param name="displayName">Как плагин называется в сообщениях.</param>
    /// <param name="assemblies">Сборки, в которых искать вклады.</param>
    /// <param name="studio">Контекст, который получат редакторы документов и вклады в контракты.</param>
    /// <param name="builtIn">Встроенный модуль: его вклады в наборе стоят раньше плагинов.</param>
    /// <param name="loadContext">
    /// Выгружаемый контекст загрузки расширения; по нему узнаётся хозяин объекта из приватной сборки,
    /// подгруженной по требованию.
    /// </param>
    /// <remarks>
    /// Тот же идентификатор второй раз — новая копия расширения: прежние вклады снимаются раньше, чем
    /// встанут новые, иначе в наборах стояли бы обе.
    /// </remarks>
    public void Add(
        string pluginId,
        string displayName,
        IEnumerable<Assembly> assemblies,
        IStudioContext? studio = null,
        bool builtIn = false,
        AssemblyLoadContext? loadContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(assemblies);

        if (_raised.Any(raised => PluginIds.Same(raised.Id, pluginId)))
            Remove(pluginId);

        var list = assemblies.ToList();

        AddDeclared(pluginId, list, studio);

        var joined = new RaisedExtension(pluginId, displayName, list, studio, builtIn, loadContext);

        _raised.Add(joined);

        // Набор правится копией списка: обработчик Changed волен собрать новый набор, и перебор живого
        // списка упал бы на этом.
        foreach (var set in _sets.ToList())
            set.Join(joined, announce: true);
    }

    /// <summary>Редакторы документов и код пунктов создания — вклады, которые спрашивает сама студия.</summary>
    private void AddDeclared(string pluginId, IReadOnlyList<Assembly> assemblies, IStudioContext? studio)
    {
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
    /// <remarks>
    /// Зовётся до выгрузки: вклады прощаются, пока их код ещё есть, а собравшие узнают об уходе
    /// (<c>Released</c>, затем <c>Changed</c>) и снимают построенное ими с экрана. Ушёл сам собирающий —
    /// его наборы закрываются: вклады в них нужны были только ему.
    /// </remarks>
    public void Remove(string pluginId)
    {
        _editors.RemoveAll(registration => registration.PluginId == pluginId);
        _makers.RemoveAll(registration => registration.PluginId == pluginId);
        _raised.RemoveAll(raised => PluginIds.Same(raised.Id, pluginId));

        foreach (var set in _sets.ToList())
        {
            if (PluginIds.Same(set.CollectorId, pluginId))
            {
                _sets.Remove(set);
                set.Close();
            }
            else
            {
                set.Leave(pluginId);
            }
        }

        foreach (var reference in _guarded.ToList())
        {
            if (!reference.TryGetTarget(out var handle))
            {
                _guarded.Remove(reference);
            }
            else if (PluginIds.Same(handle.OwnerId, pluginId))
            {
                _guarded.Remove(reference);
                handle.Let();
            }
        }
    }

    /// <summary>Собирает вклады в контракт для расширения, которое об этом просит.</summary>
    /// <typeparam name="T">Тип вклада — класс из контрактной сборки модуля.</typeparam>
    /// <param name="collectorId">Кто собирает.</param>
    /// <returns>Живой набор; повторная просьба того же расширения отдаёт тот же.</returns>
    /// <remarks>
    /// Спящих, ждущих сбора в этот контракт, реестр будит раньше, чем соберёт набор: подъём каждого
    /// приходит сюда же, через <see cref="Add(LoadedPlugin)"/>, и набор, заведённый до пробуждения,
    /// получил бы проснувшихся дважды — при подъёме и при сборе.
    /// </remarks>
    public IStudioContributionSet<T> Collect<T>(string collectorId) where T : StudioContribution
    {
        ArgumentException.ThrowIfNullOrEmpty(collectorId);

        foreach (var known in _sets)
        {
            if (known is ContributionSet<T> same && PluginIds.Same(same.CollectorId, collectorId))
                return same;
        }

        var contract = typeof(T);

        // Тот же запрет и по той же причине, что у экспортов: тип из выгружаемого контекста никто, кроме
        // собравшего, не назовёт, а набор держал бы его контекст загрузки.
        if (AssemblyLoadContext.GetLoadContext(contract.Assembly) != AssemblyLoadContext.Default)
        {
            Note?.Invoke(this,
                $"{collectorId}: вклады в {contract.Name} не собираются — тип живёт в сборке плагина, а не в " +
                "контрактной. Вынесите его в сборку, объявленную в provides.contracts");

            return ContributionSet<T>.Empty(this, collectorId);
        }

        var owner = PluginContracts.OwnerOf(contract.Assembly);

        if (owner is not null)
            Awaken?.Invoke(owner);

        var set = new ContributionSet<T>(this, collectorId, owner);

        _sets.Add(set);

        foreach (var raised in _raised.ToList())
            set.Join(raised, announce: false);

        return set;
    }

    /// <summary>Собирает ли кто-нибудь вклады в контракт этого хозяина.</summary>
    /// <param name="ownerId">Идентификатор модуля — хозяина контракта.</param>
    /// <remarks>
    /// Нужен подъёму плагинов: плагин, ждущий сбора, отложен на старте, а модуль мог начать собирать ещё
    /// раньше — при своём подъёме, когда будить было некого.
    /// </remarks>
    public bool IsCollected(string ownerId) =>
        _sets.Any(set => set.ContractOwner is { } owner && PluginIds.Same(owner, ownerId));

    /// <summary>Ждёт ли плагин сбора, который уже идёт: его надо будить сейчас.</summary>
    /// <param name="manifest">Манифест плагина.</param>
    public bool Awaited(PluginManifest? manifest) =>
        PluginActivation.ContributionOwners(manifest).Any(IsCollected);

    /// <summary>Ручка к объекту, отданному расширением; хозяин — расширение, загрузившее его сборку.</summary>
    /// <typeparam name="T">Тип объекта.</typeparam>
    /// <param name="foreign">Объект.</param>
    public IStudioGuarded<T> Guard<T>(T foreign) where T : class
    {
        ArgumentNullException.ThrowIfNull(foreign);

        return Adopt(OwnerOf(foreign.GetType().Assembly) ?? string.Empty, foreign);
    }

    /// <summary>Ручка к объекту на названного хозяина — отпускаемая с его выгрузкой.</summary>
    internal IStudioGuarded<T> Adopt<T>(string ownerId, T value) where T : class
    {
        var handle = new GuardedHandle<T>(this, ownerId, value);

        if (ownerId.Length > 0)
            Track(handle);

        return handle;
    }

    /// <summary>
    /// Строит вклады типа <typeparamref name="T"/> из сборок поднятого расширения — через шов.
    /// </summary>
    /// <remarks>
    /// Класс без открытого конструктора без параметров не строится и не засчитывается хозяину сбоем: это
    /// ошибка сборки, а не вызова, и третья такая ошибка отключала бы плагин, ни разу не позванный.
    /// </remarks>
    internal List<ContributionHandle<T>> Build<T>(RaisedExtension raised) where T : StudioContribution
    {
        var built = new List<ContributionHandle<T>>();

        foreach (var type in PluginTypes.Concrete<T>(raised.Assemblies))
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                Note?.Invoke(this,
                    $"{raised.Name}: у {type.Name} нет открытого конструктора без параметров — вклад в " +
                    $"{typeof(T).Name} не построить");

                continue;
            }

            // Постройка и подключение — одним куском и через шов, как у редактора документов: вклад,
            // построенный наполовину, никому не нужен, а упавший не должен стоить соседям ничего.
            var contribution = _guard.Get(raised.Id, $"вклад {type.Name}", () =>
            {
                var instance = (T)Activator.CreateInstance(type)!;

                if (raised.Studio is not null)
                    instance.Attach(raised.Studio);

                return instance;
            });

            if (contribution is null)
                continue;

            var handle = new ContributionHandle<T>(this, raised.Id, raised.IsBuiltIn, contribution);

            built.Add(handle);
        }

        return built;
    }

    /// <summary>Хозяин сборки: поднятое расширение, загрузившее её, или хозяин контракта.</summary>
    /// <remarks>
    /// Сверяется и список сборок расширения, и его контекст загрузки — тем же правилом, каким хост
    /// приписывает исключение по стеку: приватную зависимость, подгруженную по требованию, знает только
    /// контекст.
    /// </remarks>
    private string? OwnerOf(Assembly assembly)
    {
        var context = AssemblyLoadContext.GetLoadContext(assembly);

        foreach (var raised in _raised)
        {
            if (raised.Assemblies.Contains(assembly) || (raised.LoadContext is not null && context == raised.LoadContext))
                return raised.Id;
        }

        return PluginContracts.OwnerOf(assembly);
    }

    /// <summary>Запоминает ручку, чтобы отпустить её объект с выгрузкой хозяина.</summary>
    private void Track(IReleasable handle)
    {
        // Мёртвые ссылки чистятся по ходу: модуль берёт ручки на каждое построенное поле, и без уборки
        // список рос бы весь сеанс.
        if (_guarded.Count >= 64)
            _guarded.RemoveAll(reference => !reference.TryGetTarget(out _));

        _guarded.Add(new WeakReference<IReleasable>(handle));
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
