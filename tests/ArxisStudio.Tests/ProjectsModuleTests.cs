using ArxisStudio.Extensibility;
using ArxisStudio.Modules.Projects;
using ArxisStudio.Projects;
using ArxisStudio.Sdk.Plugins;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Манифест модуля «Проекты» и то, что студия по нему строит.
/// </summary>
/// <remarks>
/// Интерфейса у модуля нет, поэтому и сверять почти нечего — и тем важнее, чтобы его не
/// появилось: служба без панели обещает, что её потребитель — чужой код, а не окно самого модуля.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class ProjectsModuleTests
{
    /// <summary>Идентификаторы команд в коде — те же, что в манифесте.</summary>
    [Fact]
    public void The_commands_in_code_are_the_commands_in_the_manifest()
    {
        string[] code =
        [
            ProjectsModule.ReloadCommand,
            ProjectsModule.CloseCommand,
            ProjectsModule.RestoreCommand,
            ProjectsModule.BuildCommand,
            ProjectsModule.RebuildCommand,
            ProjectsModule.CleanCommand,
        ];

        Assert.Equal(Manifest().Contributions.Commands.Select(command => command.Id).Order(), code.Order());
    }

    /// <summary>Пункты меню зовут объявленные команды.</summary>
    [Fact]
    public void Every_menu_item_names_a_declared_command()
    {
        var manifest = Manifest();
        var declared = manifest.Contributions.Commands.Select(command => command.Id).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(manifest.Contributions.Menus);

        foreach (var item in manifest.Contributions.Menus)
            Assert.True(declared.Contains(item.Command), $"пункт {item.Path} зовёт неизвестную команду");
    }

    /// <summary>Модуль ничего не рисует: ни панелей, ни кнопок, ни контролов.</summary>
    [Fact]
    public void The_projects_module_draws_nothing()
    {
        var manifest = Manifest();

        Assert.Empty(manifest.Contributions.ToolWindows);
        Assert.Empty(manifest.Contributions.ToolBar);
        Assert.DoesNotContain(
            typeof(ProjectsModule).Assembly.GetTypes(),
            type => typeof(Avalonia.Controls.Control).IsAssignableFrom(type));
    }

    /// <summary>Манифест объявляет ровно те настройки, которые модуль читает.</summary>
    [Fact]
    public void The_manifest_declares_exactly_the_settings_the_module_reads()
    {
        var declared = Manifest().Contributions.Settings;

        Assert.Equal(ProjectsSettings.Keys.Order(), declared.Select(setting => setting.Key).Order());

        foreach (var setting in declared)
        {
            Assert.True(setting.IsBool, $"{setting.Key} объявлена не переключателем");
            Assert.False(setting.IsProject, $"{setting.Key} объявлена проектной: слежение — выбор человека, а не проекта");
            Assert.False(string.IsNullOrWhiteSpace(setting.Title), $"у настройки {setting.Key} нет подписи");
        }
    }

    /// <summary>Модуль поднимается, публикует службу и заявляет команды.</summary>
    [Fact]
    public void The_module_rises_publishes_the_service_and_registers_its_commands()
    {
        using var studio = new ProjectsStudio();

        Assert.Null(studio.Module.Context);
        Assert.Equal("arxis.projects", studio.Module.Installed.Id);
        Assert.Equal(ProjectsState.Closed, studio.Projects.Status.State);

        foreach (var command in Manifest().Contributions.Commands)
            Assert.Contains(command.Id, studio.Commands.Registered);
    }

    /// <summary>Команда «перезагрузить» без открытого проекта говорит об этом человеку.</summary>
    [Fact]
    public void Reload_with_nothing_open_says_so_in_the_status_bar()
    {
        using var studio = new ProjectsStudio();

        Assert.True(studio.Commands.Invoke(ProjectsModule.ReloadCommand));
        Assert.Single(studio.Status.Said);
        Assert.Equal(0, studio.Provider.Loads);
    }

    /// <summary>
    /// Своего MSBuild рядом со студией нет: он приходит из SDK, который найдёт локатор.
    /// </summary>
    /// <remarks>
    /// Провайдер ссылается на пакеты MSBuild только ради компиляции, и это условие обязано дойти
    /// по ссылкам до приложения. Копия рядом — второй MSBuild в процессе, и падает это позже и в
    /// чужом месте.
    /// </remarks>
    [Fact]
    public void The_studio_ships_no_msbuild_of_its_own()
    {
        var copies = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Microsoft.Build*.dll")
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "Microsoft.Build.Locator.dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(copies.Count == 0, $"рядом со студией лежит свой MSBuild: {string.Join(", ", copies)}");
        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.Build.Locator.dll")),
            "локатора нет — искать MSBuild будет некому");
    }

    private static PluginManifest Manifest()
    {
        var (manifest, error) = ModuleManifest.Load(typeof(ProjectsModule).Assembly);

        Assert.Null(error);
        Assert.NotNull(manifest);

        return manifest;
    }
}
