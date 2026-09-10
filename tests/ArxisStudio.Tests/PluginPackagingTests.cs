using System.Text.RegularExpressions;
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
    /// Список руками был дырой: сборку, добавленную к общим, забывали дописать
    /// сюда — и проверка молча переставала её касаться, а плагин увозил её с собой.
    /// Имена вычитываются из <c>IsShared</c>: дописать список и не заметить
    /// этого больше нельзя. Общей сборка бывает по приставке имени или по имени
    /// целиком — модель проектов общая точно, чтобы её движки общими не стали.
    /// </remarks>
    private static (string Name, bool Exact)[] Shared()
    {
        var found = Regex.Matches(Resolver(), @"name\.(StartsWith|Equals)\(([^,]+),")
            .Select(match => (match.Groups[2].Value.Trim('"'), match.Groups[1].Value == "Equals"))
            .ToArray();

        Assert.NotEmpty(found);

        return found;
    }

    /// <summary>Считает ли резолвер сборку общей — по тем же правилам, что он сам.</summary>
    private static bool IsShared(string name) =>
        Shared().Any(shared => shared.Exact
            ? string.Equals(name, shared.Name, StringComparison.Ordinal)
            : name.StartsWith(shared.Name, StringComparison.Ordinal));

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

        Assert.True(File.Exists(Path.Combine(lang, "strings.json")), "нет словаря по умолчанию");
        Assert.True(File.Exists(Path.Combine(lang, "strings.en.json")), "нет перевода");
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
        var archive = Path.Combine(Sample(), "arxis.hello.axplugin");

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

            Assert.Null(catalog.InstallFromArchive(Path.Combine(Sample(), "arxis.hello.axplugin")).Error);

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
        var targets = File.ReadAllText(
            Path.Combine(Repository(), "src", "ArxisStudio.Sdk", "build", "ArxisStudio.Sdk.targets"));

        Assert.Contains($"'{name}'", targets, StringComparison.Ordinal);
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
            Path.Combine(Repository(), "src", "ArxisStudio.Sdk", "ArxisStudio.Sdk.csproj"))
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

    /// <summary>Текст резолвера: список общих сборок объявлен в нём.</summary>
    private static string Resolver() => File.ReadAllText(
        Path.Combine(Repository(), "src", "ArxisStudio.Extensibility", "PluginHost.cs"));

    private static string Package() => Path.Combine(Sample(), "package");

    /// <summary>Корень репозитория: над ним лежит решение.</summary>
    private static string Repository()
    {
        for (var directory = new DirectoryInfo(Sample()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArxisStudio.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Не найден корень репозитория: рядом нет ArxisStudio.slnx");
    }

    /// <summary>
    /// Папка примера плагина.
    /// </summary>
    /// <remarks>
    /// Ищется подъёмом от папки сборки тестов: путь от репозитория до неё
    /// зависит от конфигурации и платформы, а сам пример узнаётся по файлу
    /// проекта, который в нём заведомо есть.
    /// </remarks>
    private static string Sample()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Plugins", "Arxis.HelloPlugin");

            if (File.Exists(Path.Combine(candidate, "Arxis.HelloPlugin.csproj")))
                return candidate;
        }

        throw new InvalidOperationException("Не найден пример плагина src/Plugins/Arxis.HelloPlugin");
    }
}
