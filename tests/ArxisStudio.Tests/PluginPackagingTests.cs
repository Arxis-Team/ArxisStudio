using System.IO.Compression;
using System.Text.Json;
using ArxisStudio.Extensibility;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Упаковка плагина: что оставляет после себя таргет сборки.
/// </summary>
/// <remarks>
/// Пример плагина собирается вместе с тестами, поэтому проверять есть что и
/// запускать сборку отсюда не нужно: раскладка и архив уже лежат на диске.
/// <para>
/// Главное здесь — чего в пакете быть не должно. Общие контракты студия всегда
/// берёт из своего контекста загрузки, и сборка, приехавшая в плагине, не
/// заменит их, а разойдётся с ними: тип из другой сборки — другой тип, и панель
/// плагина в интерфейс не встанет. Заметить это по работающей студии почти
/// невозможно — она просто скажет, что панели нет.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class PluginPackagingTests
{
    /// <summary>
    /// Общие контракты — не списком здесь, а тем, что считает общим резолвер.
    /// </summary>
    /// <remarks>
    /// Правила вычитывает <see cref="SharedAssemblies"/> из исходника резолвера —
    /// те же, по которым раскладка студии держит общие сборки у корня: общей
    /// сборке место и не в пакете плагина, и не в папке модулей.
    /// </remarks>
    private static IReadOnlyList<(string Name, bool Exact)> Shared()
    {
        var rules = SharedAssemblies.Rules;

        Assert.NotEmpty(rules);

        return rules;
    }

    /// <summary>Считает ли резолвер сборку общей — по тем же правилам, что он сам.</summary>
    private static bool IsShared(string name) => SharedAssemblies.IsShared(name);

    public static TheoryData<string> SharedNames
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var (name, _) in Shared())
                data.Add(name);

            return data;
        }
    }

    /// <summary>Каталог собран по формату: манифест в корне, сборка в bin/.</summary>
    [Fact]
    public void The_layout_is_the_directory_format_from_the_plan()
    {
        var package = Package();

        Assert.True(File.Exists(Path.Combine(package, "plugin.json")), "манифеста нет в корне пакета");
        Assert.True(File.Exists(Path.Combine(package, "bin", "Arxis.HelloPlugin.dll")), "нет entry-сборки");
    }

    /// <summary>
    /// Словари едут вместе с плагином.
    /// </summary>
    /// <remarks>
    /// Без них весь текст, который студия показывает за плагин, — заголовок
    /// панели, пункт меню, подпись настройки — превратится у человека в
    /// <c>!ключ!</c>: манифест ссылается на строки, а взять их будет неоткуда.
    /// </remarks>
    [Fact]
    public void The_dictionaries_travel_with_the_plugin()
    {
        var lang = Path.Combine(Package(), "lang");

        Assert.True(File.Exists(Path.Combine(lang, "en.json")), "нет словаря запасного языка");
        Assert.True(File.Exists(Path.Combine(lang, "ru.json")), "нет словаря языка");
    }

    /// <summary>
    /// Файл зависимостей едет вместе со сборкой.
    /// </summary>
    /// <remarks>
    /// По нему плагин разрешает свои приватные сборки: без него
    /// <c>AssemblyDependencyResolver</c> не найдёт ничего, и плагин со своей
    /// зависимостью упадёт при первом обращении к ней — уже у человека.
    /// </remarks>
    [Fact]
    public void The_deps_file_travels_with_the_assembly()
    {
        Assert.True(
            File.Exists(Path.Combine(Package(), "bin", "Arxis.HelloPlugin.deps.json")),
            "нет файла зависимостей");
    }

    /// <summary>Общих контрактов в пакете нет.</summary>
    [Fact]
    public void Shared_contracts_do_not_travel_in_the_package()
    {
        var strays = Directory
            .GetFiles(Path.Combine(Package(), "bin"), "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => IsShared(name!))
            .ToList();

        Assert.True(strays.Count == 0, $"в пакете общие контракты: {string.Join(", ", strays)}");
    }

    /// <summary>
    /// Своя зависимость плагина едет с ним — и сборкой, и переводом.
    /// </summary>
    /// <remarks>
    /// Общему контракту в пакете места нет, а своей зависимости — ровно
    /// наоборот: без неё плагин упадёт на первом же типе из неё, и упадёт уже у
    /// человека. На машине автора беду не видно вовсе: там сборку находит кэш
    /// NuGet, по которому <c>AssemblyDependencyResolver</c> и ищет.
    /// <para>
    /// Перевод пакета — сборка-сателлит, и живёт она в папке своего языка:
    /// сложенный в корень <c>bin/</c>, он не находится ничем. Просмотрщик —
    /// единственный плагин репозитория с пакетной зависимостью, и потому
    /// правило проверяется на нём.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_package_dependency_travels_with_the_plugin()
    {
        var bin = Path.Combine(Package("Arxis.CodeViewer"), "bin");

        Assert.True(File.Exists(Path.Combine(bin, "AvaloniaEdit.dll")), "своя зависимость не уехала в пакет");

        Assert.True(
            File.Exists(Path.Combine(bin, "zh-Hans", "AvaloniaEdit.resources.dll")),
            "перевод зависимости лежит не в папке своего языка");
    }

    /// <summary>
    /// Того, что студия везёт сама, в пакете плагина нет.
    /// </summary>
    /// <remarks>
    /// Список общих сборок отвечает за контракты, которыми студия и плагин
    /// обмениваются типами, а рядом с Avalonia в <c>lib/</c> едут её спутники —
    /// SkiaSharp, HarfBuzzSharp, MicroCom. Типы их границу не переходят, и
    /// объявлять их общими незачем; копия в плагине при этом всё равно лишняя —
    /// сборку, которой у него нет, контекст загрузки берёт у основного, а
    /// своя копия SkiaSharp пришла бы без нативной библиотеки.
    /// <para>
    /// Набор берётся у собранной студии, а не списком в тесте: список пришлось
    /// бы править вслед за каждой новой зависимостью платформы, и он бы отстал.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Arxis.HelloPlugin")]
    [InlineData("Arxis.HelloFriend")]
    [InlineData("Arxis.CodeViewer")]
    public void Nothing_the_studio_carries_travels_in_the_package(string plugin)
    {
        var studio = Directory
            .GetFiles(Studio(), "*.dll")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(studio);

        var strays = Directory
            .GetFiles(Path.Combine(Package(plugin), "bin"), "*.dll", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .Where(studio.Contains)
            .ToList();

        Assert.True(strays.Count == 0, $"плагин везёт то, что есть у студии: {string.Join(", ", strays)}");
    }

    /// <summary>
    /// Каждый плагин, которого собирает проект, оставляет свой архив — и тот
    /// же, что его раскладка.
    /// </summary>
    /// <remarks>
    /// Архив включают строкой в csproj, и новый плагин её забывает: сборка
    /// проходит, раскладка ложится, а поставить плагин менеджером нечем. Так
    /// вышло у просмотрщика (запись 278). Правило сверяет все плагины разом, а
    /// не называет их поимённо: список отстал бы от первого же нового.
    /// <para>
    /// Одного наличия файла мало. Архив в <c>.gitignore</c>, и выключенная
    /// упаковка оставляет на диске прежний: на машине автора он лежит, пока
    /// его не удалят, и ставится вместо сегодняшней сборки. Поэтому архив
    /// сверяется с раскладкой побайтно — отставший расходится с ней на первой
    /// же правке.
    /// </para>
    /// <para>
    /// Пакет, собранный без проекта, — словарь перевода вроде
    /// <c>Arxis.Lang.De</c> — сборка не собирает вовсе, и правило его не
    /// касается.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_plugin_project_leaves_its_archive()
    {
        var projects = Directory
            .GetDirectories(Repository.Path("src", "Plugins"))
            .Where(folder => File.Exists(Path.Combine(folder, "plugin.json")) &&
                             Directory.GetFiles(folder, "*.csproj").Length > 0)
            .ToList();

        Assert.NotEmpty(projects);

        var broken = new List<string>();

        foreach (var folder in projects)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "plugin.json")));

            var id = manifest.RootElement.GetProperty("id").GetString();
            var archive = Path.Combine(folder, $"{id}.axplugin");

            if (!File.Exists(archive))
                broken.Add($"{Path.GetFileName(folder)}: нет {id}.axplugin");
            else if (Differs(archive, Path.Combine(folder, "package")) is { } difference)
                broken.Add($"{Path.GetFileName(folder)}: {difference}");
        }

        Assert.True(broken.Count == 0, $"архив не собран или отстал от раскладки — {string.Join("; ", broken)}");
    }

    /// <summary>
    /// Архив собран и ставится студией.
    /// </summary>
    /// <remarks>
    /// Проверка конца в конец: то, что оставил таргет, принимает тот же
    /// каталог, которым студия ставит плагины из менеджера. Разойтись эти двое
    /// могут молча — архив соберётся, а при установке окажется, что манифест
    /// лежит не там, где его ищут.
    /// </remarks>
    [Fact]
    public void The_archive_installs_the_way_the_studio_installs_it()
    {
        var archive = HelloArchive.Path;

        Assert.True(File.Exists(archive), "архива .axplugin нет");

        var root = Path.Combine(Path.GetTempPath(), $"arxis-packaging-{Guid.NewGuid():N}");

        try
        {
            var (plugin, error) = new PluginCatalog(root).InstallFromArchive(archive);

            Assert.Null(error);
            Assert.NotNull(plugin);
            Assert.Equal("arxis.hello", plugin!.Id);
            Assert.Equal(Path.Combine(root, "arxis.hello"), plugin.Directory);

            // Entry-сборка лежит там, где её объявил манифест: иначе плагин
            // установится и не поднимется.
            Assert.NotNull(plugin.Manifest?.Entry);
            Assert.True(
                File.Exists(Path.Combine(plugin.Directory, plugin.Manifest!.Entry!)),
                $"по пути {plugin.Manifest.Entry} сборки нет");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Установленный из архива плагин поднимается и заявляет свою команду.
    /// </summary>
    /// <remarks>
    /// Дальний конец всей дороги: собранное таргетом ставится каталогом и
    /// поднимается хостом — тем же путём, каким это делает студия. Проверять
    /// только раскладку значило бы проверять форму, а не то, что она работает.
    /// </remarks>
    [Fact]
    public void A_packed_plugin_installs_and_raises()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arxis-raising-{Guid.NewGuid():N}");
        var commands = new StudioCommands();

        try
        {
            var catalog = new PluginCatalog(root);

            Assert.Null(catalog.InstallFromArchive(HelloArchive.Path).Error);

            // Контекст закрывается здесь, а не в конце метода: пока он жив,
            // сборка плагина открыта, и папку не удалить.
            using (var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null)))
            {
                // Пример объявляет onToolWindow:, а панель без поднятого плагина
                // показать нечем — такой манифест поднимается сразу.
                var loaded = Assert.Single(host.LoadStartup(catalog.Scan()));

                Assert.True(loaded.IsLoaded, loaded.Error);
                Assert.Equal("arxis.hello", loaded.Installed.Id);
                Assert.NotEmpty(loaded.Entries);
                Assert.Contains("hello.greet", commands.Registered);
            }
        }
        finally
        {
            Forget(root);
        }
    }

    /// <summary>
    /// Убирает за собой временную папку плагинов.
    /// </summary>
    /// <remarks>
    /// Выгружаемый контекст отпускает файлы не в момент выгрузки, а когда до
    /// них доберётся сборщик мусора, — отсюда и явный вызов. Если и после него
    /// файл занят, папка остаётся во временном каталоге: уронить из-за этого
    /// тест значило бы проверять сборщик мусора, а не упаковку.
    /// </remarks>
    private static void Forget(string root)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Документация автору в пакет не едет.</summary>
    /// <remarks>
    /// XML-файл документации нужен тому, кто пишет плагин, а не студии: она его
    /// не читает никогда. В пакете он только весит.
    /// </remarks>
    [Fact]
    public void Documentation_stays_with_the_author()
    {
        Assert.Empty(Directory.GetFiles(Path.Combine(Package(), "bin"), "*.xml"));
    }

    /// <summary>
    /// Таргет и резолвер понимают под общим контрактом одно и то же.
    /// </summary>
    /// <remarks>
    /// Список общих сборок записан дважды: в
    /// <see cref="PluginHost"/> — чтобы брать их из своего контекста, и в
    /// таргете упаковки — чтобы не класть их в пакет. Разъехаться они могут
    /// молча, и тогда плагин увезёт с собой сборку, которую студия всё равно
    /// возьмёт свою: тип из другой сборки — другой тип, и панель не встанет.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SharedNames))]
    public void The_target_and_the_resolver_mean_the_same_by_shared(string name)
    {
        var pack = SharedAssemblies.PackPattern();
        var exact = Shared().Single(rule => rule.Name == name).Exact;

        Assert.True(pack.IsMatch(name), $"таргет не считает общей сборку {name}");

        Assert.True(
            pack.IsMatch(name + ".Inner") == IsShared(name + ".Inner") && IsShared(name + ".Inner") == !exact,
            $"о сборке {name}.Inner таргет и резолвер отвечают по-разному");

        Assert.False(pack.IsMatch(name + "Edit"), $"таргет узнаёт {name} по первым буквам: {name}Edit — чужая сборка");
        Assert.False(IsShared(name + "Edit"), $"резолвер узнаёт {name} по первым буквам: {name}Edit — чужая сборка");
    }

    /// <summary>
    /// Библиотека, которая только начинается как общая, — своя у плагина.
    /// </summary>
    /// <remarks>
    /// <c>AvaloniaEdit</c> в студии не лежит. Пока общую узнавали по первым буквам, упаковка не клала
    /// её в пакет, резолвер отказывался брать её из папки плагина, основной контекст не находил — и
    /// плагин с редактором кода падал на первом обращении к нему.
    /// </remarks>
    [Fact]
    public void A_library_that_only_starts_like_a_shared_one_is_the_plugins_own()
    {
        Assert.True(IsShared("Avalonia"));
        Assert.True(IsShared("Avalonia.Base"));
        Assert.True(IsShared("ArxisStudio.Sdk"));

        Assert.False(IsShared("AvaloniaEdit"), "чужая библиотека объявлена общей");
        Assert.False(IsShared("AvaloniaHex"), "чужая библиотека объявлена общей");
        Assert.False(SharedAssemblies.PackPattern().IsMatch("AvaloniaEdit"), "упаковка не положит библиотеку в пакет");
    }

    /// <summary>
    /// Модель проектов общая, а её движки — нет.
    /// </summary>
    /// <remarks>
    /// Ядро ProjectSystem плагин видит напрямую — снимки решения из него, — и
    /// оно обязано быть одним на всех. Движки же держит служба проектов: MSBuild
    /// регистрируется на процесс один раз, и второй его экземпляр в контексте
    /// плагина был бы бедой, а не удобством. Поэтому общим объявлено имя ядра
    /// целиком, и приставка, которая захватила бы и движки, здесь запрещена.
    /// </remarks>
    [Fact]
    public void The_project_model_is_shared_and_its_engines_are_not()
    {
        Assert.True(IsShared("ArxisStudio.ProjectSystem"), "ядро модели проектов не объявлено общим");

        Assert.False(IsShared("ArxisStudio.ProjectSystem.MSBuild"), "провайдер MSBuild объявлен общим — плагин получил бы движок вместо модели");
        Assert.False(IsShared("ArxisStudio.ProjectSystem.NuGet"), "правка пакетов объявлена общей — это дело службы, а не плагина");
        Assert.False(IsShared("ArxisStudio.ProjectSystem.Markup.Xaml"), "адаптер разметки объявлен общим — он тащит за собой Markup и Avalonia");
    }

    /// <summary>
    /// Всё, что плагин видит через SDK, объявлено общим.
    /// </summary>
    /// <remarks>
    /// Плагин ссылается на SDK, а через него — на то, на что ссылается сам SDK.
    /// Каждая такая сборка обязана быть одной на всех: иначе копия рядом с
    /// плагином даст второй экземпляр того же типа, и ни панель, ни иконка
    /// плагина в интерфейс не встанет.
    ///
    /// Это и есть корень списка общих сборок: список пишется руками, а ссылки
    /// SDK — решением, и порваться они могут молча: добавили ссылку — обязаны
    /// объявить её общей.
    /// </remarks>
    [Fact]
    public void Everything_the_sdk_shows_a_plugin_is_shared()
    {
        var exposed = File.ReadAllLines(
            Repository.Path("src", "ArxisStudio.Sdk", "ArxisStudio.Sdk.csproj"))
            .Where(line => line.Contains("ProjectReference", StringComparison.Ordinal))
            .Select(line => line.Split('"'))
            .Where(parts => parts.Length > 1)
            .Select(parts => Path.GetFileNameWithoutExtension(parts[1]))
            .Where(name => name.StartsWith("ArxisStudio.", StringComparison.Ordinal)
                // Анализатор приходит без ссылки на сборку — плагин его типов не видит.
                && !name.EndsWith(".Analyzers", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(exposed);
        Assert.All(exposed, name => Assert.True(IsShared(name), $"{name} виден плагину через SDK, но общим не объявлен"));
    }

    private static string Package() => Repository.Path("src", "Plugins", "Arxis.HelloPlugin", "package");

    /// <summary>Чем архив расходится с раскладкой; пусто — ничем.</summary>
    /// <remarks>
    /// Пути сравниваются прямой чертой: в архиве она такая всегда, а на диске —
    /// какая у системы.
    /// </remarks>
    private static string? Differs(string archive, string package)
    {
        using var zip = ZipFile.OpenRead(archive);

        var packed = zip.Entries
            .Where(entry => !entry.FullName.EndsWith('/'))
            .ToDictionary(entry => entry.FullName.Replace('\\', '/'), StringComparer.Ordinal);

        var laid = Directory
            .GetFiles(package, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(package, path).Replace('\\', '/'), StringComparer.Ordinal);

        if (laid.Keys.Except(packed.Keys).FirstOrDefault() is { } missing)
            return $"в архиве нет {missing}";

        if (packed.Keys.Except(laid.Keys).FirstOrDefault() is { } extra)
            return $"в архиве лишний {extra}";

        foreach (var (path, entry) in packed)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();

            stream.CopyTo(copy);

            if (!copy.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(laid[path])))
                return $"{path} в архиве не тот, что в раскладке";
        }

        return null;
    }

    /// <summary>Раскладка названного плагина репозитория.</summary>
    private static string Package(string plugin) =>
        Repository.Path("src", "Plugins", plugin, "package");

    /// <summary>
    /// Папка платформы у собранной студии — той же конфигурации, что у тестов.
    /// </summary>
    /// <remarks>
    /// Конфигурация берётся из пути самих тестов: прогон бывает и Debug, и
    /// Release, а сверять Release-плагин с Debug-студией значит сверять с тем,
    /// чего в этом прогоне не собирали.
    /// </remarks>
    private static string Studio()
    {
        var library = Path.Combine(Repository.Output("src", "ArxisStudio"), StudioAssemblyFolder.Library.Name);

        Assert.True(Directory.Exists(library), $"студия не собрана: нет {library}");

        return library;
    }
}
