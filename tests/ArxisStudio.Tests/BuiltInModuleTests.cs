using System.Reflection;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Sample;
using ArxisStudio.Sdk;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Встроенный модуль: второй способ доставки за тем же контрактом.
/// </summary>
/// <remarks>
/// Панель, приезжающая вместе со студией, поднимается тем же хостом, что и
/// внешний плагин, и отличается только тем, откуда взялся манифест и в каком
/// контексте живут сборки.
/// <para>
/// Проверяется это дважды и по разным причинам. Сам контракт — на сборке,
/// собранной прямо здесь, в память: она отвечает за случаи, которых у примера
/// нет, вроде забытого манифеста. Поставляемый модуль
/// <c>ArxisStudio.Modules.Sample</c> — на том, что его манифест и его код
/// говорят одно и то же: разойтись они могут молча, и человек увидит пустое
/// место в зоне вместо панели.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class BuiltInModuleTests
{
    private const string Manifest = """
        {
          "id": "arxis.probe",
          "name": "Проба",
          "version": "1.0.0",
          "contributions": {
            "toolWindows": [ { "id": "probe.panel", "title": "Проба" } ]
          },
          "activation": [ "onStartup" ]
        }
        """;

    /// <summary>
    /// Модуль, упавший на подъёме, становится записью, а не падением студии.
    /// </summary>
    /// <remarks>
    /// <c>Activate</c> и <c>Start</c> — чужой код, и зовутся они напрямую:
    /// <c>PluginGuard</c>, через который идут остальные вызовы плагина, на
    /// загрузке ни при чём. Швом загрузки работает фильтр <c>catch</c> в хосте,
    /// и пока он перечислял беды по именам, любая неназванная уносила студию.
    /// <see cref="NullReferenceException"/> названа не была — самая обычная
    /// беда в чужом коде и самая незаметная в этом списке.
    /// </remarks>
    [Fact]
    public void A_module_that_falls_while_rising_becomes_a_record()
    {
        using var host = new PluginHost(
            new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = host.LoadBuiltIn(Falling());

        Assert.False(loaded.IsLoaded, "упавший модуль не может считаться поднятым");
        Assert.NotNull(loaded.Error);
        Assert.Equal("arxis.probe", loaded.Installed.Id);
    }

    /// <summary>Манифест модуля читается из его папки.</summary>
    [Fact]
    public void The_manifest_of_a_built_in_module_is_read_from_its_folder()
    {
        var (manifest, error) = ModuleManifest.Load(Module());

        Assert.Null(error);
        Assert.NotNull(manifest);
        Assert.Equal("arxis.probe", manifest!.Id);
        Assert.Equal("probe.panel", Assert.Single(manifest.Contributions.ToolWindows).Id);
    }

    /// <summary>
    /// Сборка без манифеста объясняет, почему не поднялась.
    /// </summary>
    /// <remarks>
    /// Забыть положить <c>module.json</c> рядом — самая обычная ошибка при заведении модуля, и
    /// молчание в ответ означало бы панель, которой нет, без единого слова о причине. Дорога в
    /// сообщении обязательна: «нет манифеста» без ответа на «где искали» отправляет читателя
    /// гадать, а заодно озеленило бы этот тест, окажись чужой <c>module.json</c> в корне выхода.
    /// </remarks>
    [Fact]
    public void An_assembly_without_a_manifest_says_why()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(BuiltInModuleTests).Assembly);

        Assert.Null(manifest);
        Assert.NotNull(error);
        Assert.Contains("module.json", error);
        Assert.Contains(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), error);
    }

    /// <summary>
    /// Модуль поднимается тем же хостом и остаётся в основном контексте.
    /// </summary>
    /// <remarks>
    /// Своего выгружаемого контекста у встроенного модуля нет и быть не должно:
    /// он приезжает со студией, выключать его отдельно нечем, а лишний контекст
    /// раздвоил бы типы, которые он делит с оболочкой.
    /// </remarks>
    [Fact]
    public void A_built_in_module_rises_in_the_main_context()
    {
        using var host = new PluginHost(
            new StudioContextFactory(new StudioLog(), new StudioCommands(), null));

        var loaded = host.LoadBuiltIn(Module());

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(loaded.Context);
        Assert.NotEmpty(loaded.Entries);
        Assert.Equal("arxis.probe", loaded.Installed.Id);
    }

    /// <summary>
    /// У каждой панели, объявленной в манифесте примера, есть класс в сборке.
    /// </summary>
    /// <remarks>
    /// Манифест и код — две записи об одном, и разойтись они могут молча:
    /// панель переименовали в коде, а в манифесте забыли, — и человек увидит
    /// пустое место в зоне вместо панели. Оболочка ищет класс по
    /// идентификатору из манифеста, здесь тем же способом ищет и тест.
    /// </remarks>
    [Fact]
    public void The_sample_module_carries_every_panel_it_declares()
    {
        var assembly = typeof(SampleModule).Assembly;
        var (manifest, error) = ModuleManifest.Load(assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);

        var declared = manifest!.Contributions.ToolWindows.Select(panel => panel.Id).ToList();

        Assert.NotEmpty(declared);

        var built = assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<ToolWindowAttribute>()?.Id)
            .OfType<string>()
            .ToList();

        Assert.All(declared, id => Assert.Contains(id, built));
    }

    /// <summary>
    /// Пример поднимается как встроенный модуль и заявляет свою команду.
    /// </summary>
    /// <remarks>
    /// Это тот же путь, которым его поднимает студия: манифест из ресурса,
    /// сборка из основного контекста, команда — через контекст. Панель здесь
    /// не строится: её строит оболочка, когда ставит в зону.
    /// </remarks>
    [Fact]
    public void The_sample_module_rises_and_registers_its_command()
    {
        var commands = new StudioCommands();

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), commands, null));

        var loaded = host.LoadBuiltIn(typeof(SampleModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);
        Assert.Null(loaded.Context);
        Assert.Equal("arxis.sample", loaded.Installed.Id);
        Assert.Contains(SampleModule.AboutCommand, commands.Registered);
        Assert.Contains(SampleModule.VerboseCommand, commands.Registered);
        Assert.NotEmpty(loaded.Services);

        // Переключатель зовёт полосу, которой у этой студии нет, — и обязан
        // это пережить: службы контекста необязательны по контракту.
        Assert.True(commands.Invoke(SampleModule.VerboseCommand), "переключатель не вызвался");
    }

    /// <summary>
    /// Каждая кнопка модуля в полосе зовёт команду, которую модуль объявил.
    /// </summary>
    /// <remarks>
    /// Кнопка и команда — две записи об одном; разойдясь, они дали бы кнопку,
    /// за которой никого нет, и щелчок отвечал бы замечанием в журнал.
    /// </remarks>
    [Fact]
    public void Every_toolbar_button_of_the_sample_names_a_declared_command()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(SampleModule).Assembly);

        Assert.Null(error);

        var commands = manifest!.Contributions.Commands.Select(command => command.Id).ToList();
        var buttons = manifest.Contributions.ToolBar.Where(item => item.IsButton).ToList();

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Contains(button.Command, commands));
    }

    /// <summary>
    /// У всего, что студия рисует за пример, есть подпись.
    /// </summary>
    /// <remarks>
    /// Без подписи элемент не встанет вовсе: она же подсказка и она же имя для
    /// средств доступности, а в полосе из значков 24×24 узнать о кнопке больше
    /// неоткуда. Пропажа была бы тихой — кнопка просто не появилась бы, а
    /// замечание ушло бы в журнал.
    /// </remarks>
    [Fact]
    public void Everything_the_studio_draws_for_the_sample_has_a_title()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(SampleModule).Assembly);

        Assert.Null(error);

        var drawn = manifest!.Contributions.ToolBar.Where(item => !item.IsCustom).ToList();

        Assert.NotEmpty(drawn);
        Assert.All(drawn, item => Assert.False(string.IsNullOrEmpty(item.Title), item.Id));
    }

    /// <summary>
    /// Панель примера собирается из своей разметки и работает.
    /// </summary>
    /// <remarks>
    /// Разметка модуля проходит тот же путь, что и разметка плагина: она
    /// компилируется в сборку расширения и строится в чужом окне. Проверяется
    /// здесь он целиком — что панель построилась, что привязка нашла модель и
    /// что кнопка зовёт команду модуля.
    /// </remarks>
    [AvaloniaFact]
    public void The_panel_of_the_sample_module_is_built_from_its_markup()
    {
        var log = new StudioLog();

        using var host = new PluginHost(new StudioContextFactory(log, new StudioCommands(), null));

        var loaded = host.LoadBuiltIn(typeof(SampleModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);

        var panel = new SamplePanel();

        panel.Attach(loaded.Studio!);

        var view = Assert.IsType<SamplePanelView>(panel.Content);
        var lines = Assert.IsType<StackPanel>(view.Content);
        var strings = loaded.Studio!.Strings;

        // Каждая подпись — из словаря студии, а не строкой в разметке: образец со
        // вшитой строкой учил бы, что так и надо. Сверяется со словарём, а не с
        // написанным здесь текстом: он зависит от языка студии, и тест, знающий
        // его наизусть, проверял бы язык прогона.
        string[] keys =
        [
            "module.sample.heading", "module.sample.manifest", "module.sample.loadcontext",
            "module.sample.project.label", "module.sample.project.none",
        ];

        Assert.All(keys, key => Assert.False(strings[key].StartsWith('!'), $"строки {key} нет в словаре студии"));
        Assert.Equal(
            keys.Select(key => strings[key]),
            view.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text));

        // Проекта нет, и службы проектов у этой студии нет: строка говорит «не
        // открыт», а место под имя пусто и скрыто.
        var (name, none) = ProjectLine(lines);

        Assert.False(name.IsVisible, "имени проекта нет, а место под него показано");
        Assert.True(none.IsVisible);
        Assert.Equal(strings["module.sample.project.none"], none.Text);

        var button = Assert.IsType<AxButton>(lines.Children[^1]);

        Assert.Equal(strings["module.sample.log"], button.Content);

        var before = log.Records.Count;

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(log.Records.Count > before, "кнопка панели не позвала команду модуля");
    }

    /// <summary>
    /// Строка проекта в панели примера идёт за открытием и закрытием, пока панель жива.
    /// </summary>
    /// <remarks>
    /// Модель читала путь один раз, и панель до конца сеанса писала «проект не открыт», что бы ни
    /// открыл человек. Контракт говорит прямо: путь в контексте живой, но о перемене не сообщает, и
    /// тому, кому нужно событие, нужна служба проектов. Образец обязан показывать эту дорогу — и
    /// отпускать подписку, прощаясь: после прощания служба панели не держит.
    /// </remarks>
    [AvaloniaFact]
    public void The_project_line_of_the_sample_follows_the_project_while_the_panel_lives()
    {
        var projects = new ProjectsProbe();
        var exports = new StudioExportRegistry();

        exports.Publish(typeof(ArxisStudio.Projects.IStudioProjects), projects, "arxis.projects", "Проекты");

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null, exports: exports));

        var loaded = host.LoadBuiltIn(typeof(SampleModule).Assembly);

        Assert.True(loaded.IsLoaded, loaded.Error);

        var panel = new SamplePanel();

        panel.Attach(loaded.Studio!);

        var lines = Assert.IsType<StackPanel>(Assert.IsType<SamplePanelView>(panel.Content).Content);
        var (name, none) = ProjectLine(lines);
        var solution = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "Волна", "Волна.slnx"));

        projects.Publish(new ArxisStudio.Projects.ProjectsStatus
        {
            Sequence = 1,
            Session = 1,
            State = ArxisStudio.Projects.ProjectsState.Ready,
            EntryPoint = solution,
        });

        Assert.True(name.IsVisible, "проект открыли, а имени в панели нет");
        Assert.Equal("Волна.slnx", name.Text);
        Assert.False(none.IsVisible, "проект открыт, а панель пишет «не открыт»");

        projects.Publish(ArxisStudio.Projects.ProjectsStatus.Closed);

        Assert.False(name.IsVisible, "проект закрыли, а имя осталось");
        Assert.True(none.IsVisible);

        panel.Release();

        projects.Publish(new ArxisStudio.Projects.ProjectsStatus
        {
            Sequence = 2,
            Session = 2,
            State = ArxisStudio.Projects.ProjectsState.Ready,
            EntryPoint = solution,
        });

        Assert.False(name.IsVisible, "попрощавшаяся панель всё ещё слушает службу проектов");
    }

    /// <summary>Место под имя проекта и строка «не открыт» в панели примера.</summary>
    private static (TextBlock Name, TextBlock None) ProjectLine(StackPanel lines)
    {
        var line = Assert.IsType<WrapPanel>(lines.Children[3]);

        return (Assert.IsType<TextBlock>(line.Children[1]), Assert.IsType<TextBlock>(line.Children[2]));
    }

    /// <summary>
    /// Проект модуля подаёт сборке манифест и словарь — иначе правило молчит.
    /// </summary>
    /// <remarks>
    /// Анализатор может быть прав, а проект — забыть его накормить: <c>ARX0002</c>
    /// разбирает то, что пришло входом сборки, и без <c>AdditionalFiles</c>
    /// проверять ему нечего и не с чем. Молчание при этом неотличимо от «всё в
    /// порядке» — ровно так дыра и прожила: правило было, модули были, а
    /// встречались они только в намерении.
    /// <para>
    /// Читается сам проект: другого места, где эта связь записана, нет.
    /// </para>
    /// <para>
    /// Модуль — папка с манифестом. Рядом с модулями лежат и их контракты, у
    /// которых манифеста нет и кормить анализатор нечем; а модуль, забывший
    /// манифест, не пройдёт мимо — его сборку описывает <c>StudioModulesTests</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_module_project_feeds_the_analyzer()
    {
        var projects = Directory
            .EnumerateFiles(Modules(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "module.json")))
            .ToList();

        Assert.NotEmpty(projects);

        foreach (var project in projects)
        {
            var text = File.ReadAllText(project);
            var name = Path.GetFileName(project);

            Assert.Contains("<AdditionalFiles Include=\"module.json\"", text, StringComparison.Ordinal);

            // Манифест — файл в папке модуля, и второй его формы быть не должно: ресурс в сборке
            // разошёлся бы с файлом молча, а поймать это расхождение нечем.
            Assert.DoesNotContain("<EmbeddedResource Include=\"module.json\"", text, StringComparison.Ordinal);

            // Словарь студии модулю больше не словарь: свой лежит в его папке, и подаёт его
            // общий таргет. Строка, оставшаяся в csproj, добавила бы к ключам модуля ключи студии,
            // и ARX0002 промолчал бы о том, чего в словаре модуля нет.
            Assert.DoesNotContain("Localization/Strings", text, StringComparison.Ordinal);

            Assert.True(
                File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "lang", "en.json")),
                $"{name}: рядом с модулем нет lang/en.json — сверять ключи манифеста не с чем");
        }

        // Правило подачи одно на все модули и живёт в общем таргете: снятое, оно оставило бы
        // ARX0002 без словаря и без единого слова об этом.
        var targets = File.ReadAllText(Path.Combine(Modules(), "Directory.Build.targets"));

        Assert.Contains("lang/en.json", targets, StringComparison.Ordinal);
        Assert.Contains("AxStrings=\"default\"", targets, StringComparison.Ordinal);
        Assert.Contains("AxStrings=\"translation\"", targets, StringComparison.Ordinal);
    }

    /// <summary>Папка встроенных модулей в репозитории.</summary>
    private static string Modules()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Modules");

            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Не найдена папка src/Modules");
    }

    /// <summary>Сборка модуля со встроенным манифестом.</summary>
    /// <summary>Модуль, который падает ровно там, где студия зовёт чужой код.</summary>
    private static Assembly Falling() => TestAssembly.EmitModule(
        "Arxis.FallingModule",
        """
            using ArxisStudio.Sdk;

            namespace Falling;

            public sealed class FallingModule : StudioPlugin
            {
                public override void Activate(IStudioContext context) =>
                    throw new System.NullReferenceException("модуль уронил студию");
            }
            """,
        Manifest);

    private static Assembly Module() => TestAssembly.EmitModule(
        "Arxis.ProbeModule",
        """
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class ProbeModule : StudioPlugin
            {
                public override void Activate(IStudioContext context)
                {
                }
            }
            """,
        Manifest);
}
