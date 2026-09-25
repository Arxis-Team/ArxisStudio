using System.Reflection;
using System.Runtime.Loader;
using Avalonia;
using Avalonia.Platform;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Контекст загрузки одного плагина.
/// </summary>
/// <remarks>
/// Сборки студии сюда не тянутся: общий тип должен быть один на всех, иначе
/// <c>StudioPlugin</c> плагина и <c>StudioPlugin</c> студии окажутся разными
/// типами. Поэтому разрешаются только те сборки, что лежат рядом с плагином, а
/// всё остальное отдаётся основному контексту.
/// </remarks>
internal sealed class PluginLoadContext(string name, string entryPath)
    : AssemblyLoadContext($"arxis-plugin:{name}", isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Контракт один на всех: даже если копия с тем же именем лежит в
        // bin/ плагина — автор забыл исключить, — тип обязан остаться общим.
        // Иначе вернулась бы двойная идентичность, от которой контракты и
        // заведены.
        if (PluginContracts.Find(assemblyName) is { } contract)
            return contract;

        return _resolver.ResolveAssemblyToPath(assemblyName) is { } path &&
               assemblyName.Name is { } name && !IsShared(name)
            ? LoadFromAssemblyPath(path)
            : null;
    }

    /// <summary>
    /// Сборка, которая обязана быть одной на всех и приходит из общего контекста.
    /// </summary>
    /// <remarks>
    /// Спрашивается не только резолвером: под этими именами нельзя объявить и
    /// контракт — иначе файл плагина подменил бы общую сборку и студии, и всем
    /// соседям.
    /// <para>
    /// Семейство узнаётся по имени целиком или по имени с точкой, а не по первым буквам: под
    /// «Avalonia» без точки попадали и чужие библиотеки — <c>AvaloniaEdit</c>, <c>AvaloniaHex</c>, —
    /// которые в студии не лежат. Плагину с такой зависимостью отказывали в его же файле, основной
    /// контекст её не находил, и плагин падал на первом обращении к редактору.
    /// </para>
    /// </remarks>
    internal static bool IsShared(string name) =>
        Family(name, "Avalonia") ||
        Family(name, "ArxisStudio.Sdk") ||
        Family(name, "ArxisStudio.Controls") ||
        Family(name, "ArxisStudio.Icons") ||
        // Модель проектов — точным именем, а не семейством: семейство отдало бы
        // плагинам и её движки — MSBuild, NuGet, адаптер разметки, — а их держит
        // служба проектов, и второй экземпляр движка в процессе был бы бедой.
        name.Equals("ArxisStudio.ProjectSystem", StringComparison.Ordinal);

    /// <summary>Само имя или имя из его семейства: <c>Avalonia</c>, <c>Avalonia.Base</c>, но не <c>AvaloniaEdit</c>.</summary>
    private static bool Family(string name, string root) =>
        name.Equals(root, StringComparison.Ordinal) ||
        name.StartsWith(root + ".", StringComparison.Ordinal);

    /// <summary>
    /// Выгружает контекст, отпустив прежде то, что держит его снаружи.
    /// </summary>
    /// <remarks>
    /// Дорог выгрузки три — прощание поднятого плагина, сбой загрузки сборки
    /// и сбой активации, — и уборка стоит здесь, на общем шве, а не у каждой
    /// из них. Забытая на одной дороге, она означала бы плагин, который
    /// выгружается при перезагрузке и остаётся в памяти, упав на подъёме.
    /// <para>
    /// Выгрузка идёт в <c>finally</c>: уборка перед ней — дело полезное, но не
    /// обязательное, и сорвись она непредвиденным образом, контекст не должен
    /// остаться неотпущенным. Это было бы хуже той беды, ради которой уборку и
    /// завели.
    /// </para>
    /// </remarks>
    public void Release()
    {
        try
        {
            Forget();
            Unregister();
        }
        finally
        {
            Unload();
        }
    }

    /// <summary>
    /// Снимает с реестра свойств Avalonia всё, что свойства запомнили о типах плагина.
    /// </summary>
    /// <remarks>
    /// Свойство Avalonia кэширует свои метаданные для каждого типа, у которого их спросили, —
    /// словарём с сильным ключом-типом, а спрашивает оформление. Тип-контрол плагина, побывавший на
    /// экране, оставался ключом у свойств, которые ему назначила тема, — в замере это
    /// <c>Border.Background</c>, <c>Visual.ClipToBounds</c>, <c>Visual.IsVisible</c> и
    /// <c>TemplatedControl.Template</c>, — и контекст загрузки не собирался никогда. Так терял
    /// перезагрузку на ходу всякий плагин со своим классом-контролом, а значит и всякий с разметкой
    /// <c>x:Class</c>.
    /// <para>
    /// Средство у Avalonia открытое: <c>UnregisterByModule</c> забывает по списку типов их
    /// переопределения метаданных и кэши. Типы спрашиваются у всех сборок контекста: приватная
    /// зависимость плагина тоже может завести свой контрол.
    /// </para>
    /// <para>
    /// Свойств и событий, заведённых самим типом, он не снимает — ни в 12.1.1, ни в основной ветке
    /// Avalonia, — а у реестра событий снятия нет вовсе. Такой плагин остаётся в памяти до
    /// перезапуска, и узнать это до спуска можно только спросив: <see cref="Pinned"/>. По ответу
    /// студия решает, ждать ли застрявшую копию или сразу ставить плагин в ждущие перезапуска.
    /// </para>
    /// </remarks>
    private void Unregister() => AvaloniaPropertyRegistry.Instance.UnregisterByModule(Types());

    /// <summary>
    /// Кто из типов контекста держится реестрами Avalonia; null — никто из видимых.
    /// </summary>
    /// <returns>Причина словами: чья сборка завела свои свойства и события и у каких типов.</returns>
    /// <remarks>
    /// Avalonia помнит свойство и маршрутизируемое событие статическим реестром до конца процесса, а
    /// ключом там стоит тип-владелец. Свой <c>StyledProperty</c> у контрола плагина — обычное дело, и
    /// AvaloniaEdit у просмотрщика заводит их десятки, а с ними события и присоединённое свойство.
    /// Причина называет сборку: у просмотрщика держит не его код, а его библиотека, — и это автору
    /// нужнее списка типов.
    /// <para>
    /// Свойства спрашиваются закрытым словарём реестра <c>_registered</c>, только на чтение: под
    /// типом-владельцем Avalonia кладёт и обычное свойство, и прямое, и присоединённое, и принятое
    /// <c>AddOwner</c>. Открытый <c>GetRegistered</c> прогоняет статические конструкторы всей цепочки
    /// типов и сам завёл бы свойства, которых плагин ещё не трогал: проверка приковала бы к памяти тот
    /// плагин, о котором спрашивает. Словарь пропал при обновлении Avalonia — ответ беднеет, а не врёт,
    /// и падает тест. События спрашиваются открытым <c>GetAllRegistered</c>: он конструкторов не зовёт.
    /// </para>
    /// <para>
    /// Классовый обработчик на чужом событии так не виден вовсе: его подписка лежит внутри события
    /// Avalonia, и хозяина у неё нет. Его выдаёт только проверка выгрузки после спуска.
    /// </para>
    /// </remarks>
    internal string? Pinned()
    {
        var mine = Assemblies.ToHashSet();
        var owners = new List<Type>();

        foreach (var routed in Avalonia.Interactivity.RoutedEventRegistry.Instance.GetAllRegistered())
        {
            if (mine.Contains(routed.OwnerType.Assembly))
                owners.Add(routed.OwnerType);
        }

        try
        {
            if (typeof(AvaloniaPropertyRegistry)
                    .GetField("_registered", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(AvaloniaPropertyRegistry.Instance) is System.Collections.IDictionary registered)
            {
                owners.AddRange(registered.Keys.OfType<Type>().Where(key => mine.Contains(key.Assembly)));
            }
        }
        catch (InvalidOperationException)
        {
            // Словарь переписали посреди обхода: свойство завели из фонового потока. Сказано то, что
            // успели увидеть, — проверка выгрузки после спуска всё равно скажет своё.
        }

        if (owners.Count == 0)
            return null;

        var assemblies = owners.Select(owner => owner.Assembly.GetName().Name).Distinct().Order(StringComparer.Ordinal).ToList();
        var names = owners.Select(owner => owner.Name).Distinct().Order(StringComparer.Ordinal).ToList();
        var shown = names.Count > 3 ? $"{string.Join(", ", names.Take(3))} и ещё {names.Count - 3}" : string.Join(", ", names);

        return $"{string.Join(", ", assemblies)} {(assemblies.Count > 1 ? "заводят" : "заводит")} свои свойства и события " +
               $"Avalonia — {shown}, — а снимать их Avalonia не умеет";
    }

    /// <summary>Все типы сборок контекста, включая те, что загрузились не все.</summary>
    private List<Type> Types()
    {
        var types = new List<Type>();

        foreach (var assembly in Assemblies)
        {
            try
            {
                types.AddRange(assembly.GetTypes());
            }
            catch (ReflectionTypeLoadException e)
            {
                // Типы, которые не загрузились, ни у кого метаданных не спрашивали.
                types.AddRange(e.Types.OfType<Type>());
            }
        }

        return types;
    }

    /// <summary>
    /// Убирает сборки плагина из кэша загрузчика ресурсов Avalonia.
    /// </summary>
    /// <remarks>
    /// Кэш держит сборку сильной ссылкой и по <b>простому</b> имени, а попасть
    /// в него хватает одного вопроса про <c>avares://</c>-адрес с этим именем:
    /// в замере даже <c>Exists</c>, ответивший «такого ресурса нет», оставлял
    /// сборку в кэше — и контекст плагина не собирался никогда.
    /// <para>
    /// Беда при этом сама себя поддерживает: живая прежняя копия находится по
    /// простому имени первой, и следующий подъём того же плагина получал бы
    /// ресурсы предыдущего. Порядок «сперва забыть, потом выгрузить» её и
    /// разрывает.
    /// </para>
    /// <para>
    /// Спрашиваются сборки контекста, а не одна entry: приватная зависимость
    /// плагина, подгруженная по требованию, везёт свои ресурсы и попадает в
    /// тот же кэш под своим именем.
    /// </para>
    /// <para>
    /// Кэша может не быть вовсе — студию собирают и без платформы Avalonia, и
    /// так же живёт половина тестов расширений. Спросить об этом заранее
    /// нечем: <c>AvaloniaLocator</c> из открытой поверхности убран, и
    /// единственный ответ службы — исключение. Ловится оно здесь: платформы
    /// нет, значит и кэш пуст, и выгрузке это не помеха.
    /// </para>
    /// </remarks>
    private void Forget()
    {
        try
        {
            foreach (var assembly in Assemblies)
            {
                if (assembly.GetName().Name is { Length: > 0 } simple)
                    AssetLoader.InvalidateAssemblyCache(simple);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
