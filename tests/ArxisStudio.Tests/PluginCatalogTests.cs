using ArxisStudio.Extensibility;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Каталог плагинов: чтение манифестов, состояние «включён», установка папкой.
/// </summary>
public class PluginCatalogTests : IDisposable
{
    private const string Manifest =
        """
        {
          "id": "arxis.figma-import",
          "name": "Figma Import",
          "version": "2.4.0",
          "publisher": "Arxis Labs",
          "description": "Импорт макетов Figma в дизайнер форм",
          "entry": "bin/Arxis.FigmaImport.dll",
          "contributions": {
            "commands": [ { "id": "figma.import", "title": "Импорт из Figma" } ],
            "toolWindows": [ { "id": "figma.panel", "title": "Figma", "zone": "bottom" } ]
          },
          "activation": [ "onCommand:figma.import" ]
        }
        """;

    private const string Placed =
        """
        {
          "id": "arxis.placed",
          "name": "Placed",
          "version": "1.0.0",
          "publisher": "Arxis Labs",
          "entry": "bin/Arxis.Placed.dll",
          "contributions": {
            "toolWindows": [
              {
                "id": "placed.panel",
                "title": "Placed",
                "placement": { "side": "bottom", "size": 0.3, "near": "arxis.git:git.changes" }
              },
              { "id": "placed.silent", "title": "Silent" }
            ]
          }
        }
        """;

    private const string Legacy =
        """
        {
          "id": "arxis.legacy",
          "name": "Из прошлой версии",
          "entry": "bin/Arxis.Legacy.dll",
          "contributions": {
            "commands": [ { "id": "legacy.run", "title": "Запустить" } ],
            "fileTypes": [ { "ext": ".fig", "name": "Figma Document" } ]
          },
          "activation": [ "onCommand:legacy.run" ]
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-plugins-{Guid.NewGuid():N}");

    public PluginCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Reads_the_manifest_of_an_installed_plugin()
    {
        Install("arxis.figma-import", Manifest);

        var plugin = Assert.Single(new PluginCatalog(_root).Scan());

        Assert.Equal("arxis.figma-import", plugin.Id);
        Assert.Equal("Figma Import", plugin.DisplayName);
        Assert.Equal("2.4.0", plugin.Manifest!.Version);
        Assert.True(plugin.IsEnabled);
    }

    [Fact]
    public void Reads_contributions_without_loading_the_assembly()
    {
        Install("arxis.figma-import", Manifest);

        var plugin = Assert.Single(new PluginCatalog(_root).Scan());
        var contributions = plugin.Manifest!.Contributions;

        Assert.Equal("figma.import", Assert.Single(contributions.Commands).Id);
        // Сторона нарочно не «right»: она же — сторона по умолчанию, и на ней
        // проверка прошла бы даже с выключенным чтением старого поля.
        Assert.Equal("bottom", Assert.Single(contributions.ToolWindows).Wanted.Side);
        Assert.Equal("onCommand:figma.import", Assert.Single(plugin.Manifest.Activation));
    }

    /// <summary>
    /// Место панели читается из нового поля, а без него — из старого.
    /// </summary>
    /// <remarks>
    /// Старое поле оставлено ради манифестов, написанных до <c>placement</c>:
    /// <c>zone: "left"</c> и есть <c>placement: { side: "left" }</c>. Панель,
    /// не сказавшая ни того ни другого, тоже получает место — сторону по
    /// умолчанию, а не пустую строку, из-за которой её потом ищут по всему окну.
    /// </remarks>
    [Fact]
    public void A_panel_says_where_it_wants_to_stand()
    {
        Install("arxis.placed", Placed);

        var panels = Assert.Single(new PluginCatalog(_root).Scan()).Manifest!.Contributions.ToolWindows;

        Assert.Equal("bottom", panels[0].Wanted.Side);
        Assert.Equal(0.3, panels[0].Wanted.Size);
        Assert.Equal("arxis.git:git.changes", panels[0].Wanted.Near);

        Assert.Equal("right", panels[1].Wanted.Side);
        Assert.Equal(0, panels[1].Wanted.Size);
        Assert.Null(panels[1].Wanted.Near);
    }

    /// <summary>
    /// Манифест со снятыми полями читается по-прежнему.
    /// </summary>
    /// <remarks>
    /// Убранное из контракта осталось у людей на дисках: плагин, собранный
    /// вчера, объявляет и название команды, и типы файлов. Отказать ему
    /// значило бы сломать установленное ради полей, которых студия и раньше
    /// не читала; лишнее в манифесте она просто не замечает.
    /// </remarks>
    [Fact]
    public void A_manifest_with_removed_fields_still_loads()
    {
        Install("arxis.legacy", Legacy);

        var plugin = Assert.Single(new PluginCatalog(_root).Scan());

        Assert.Null(plugin.Error);
        Assert.Equal("Из прошлой версии", plugin.DisplayName);
        Assert.Equal("legacy.run", Assert.Single(plugin.Manifest!.Contributions.Commands).Id);
    }

    [Fact]
    public void A_broken_manifest_is_listed_with_its_error_rather_than_hidden()
    {
        Install("broken.plugin", "{ not json at all");

        var plugin = Assert.Single(new PluginCatalog(_root).Scan());

        Assert.False(plugin.IsValid);
        Assert.NotNull(plugin.Error);
        Assert.Equal("broken.plugin", plugin.Id);
    }

    [Fact]
    public void A_folder_without_a_manifest_is_not_a_plugin()
    {
        Directory.CreateDirectory(Path.Combine(_root, "just-a-folder"));

        Assert.Empty(new PluginCatalog(_root).Scan());
    }

    [Fact]
    public void Disabling_a_plugin_survives_a_restart()
    {
        Install("arxis.figma-import", Manifest);

        new PluginCatalog(_root).SetEnabled("arxis.figma-import", false);

        Assert.False(Assert.Single(new PluginCatalog(_root).Scan()).IsEnabled);
    }

    [Fact]
    public void Installing_copies_the_folder_into_the_catalog()
    {
        var source = Path.Combine(Path.GetTempPath(), $"arxis-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(source, "bin"));
        File.WriteAllText(Path.Combine(source, "plugin.json"), Manifest);
        File.WriteAllText(Path.Combine(source, "bin", "Arxis.FigmaImport.dll"), "not really a dll");

        try
        {
            var (plugin, error) = new PluginCatalog(_root).InstallFromDirectory(source);

            Assert.Null(error);
            Assert.NotNull(plugin);
            Assert.True(File.Exists(Path.Combine(_root, "arxis.figma-import", "bin", "Arxis.FigmaImport.dll")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void Installing_the_same_plugin_twice_is_refused()
    {
        var source = Path.Combine(Path.GetTempPath(), $"arxis-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "plugin.json"), Manifest);

        try
        {
            var catalog = new PluginCatalog(_root);
            catalog.InstallFromDirectory(source);

            var (plugin, error) = catalog.InstallFromDirectory(source);

            Assert.Null(plugin);
            Assert.Contains("arxis.figma-import", error);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>
    /// Установка поверх разрешается явно — и заменяет папку целиком.
    /// </summary>
    /// <remarks>
    /// Обновиться иначе нельзя: каталог плагина после установки неизменяем, и
    /// класть новую версию поверх старой значило бы оставить в папке файлы,
    /// которых новая сборка не знает. Проверяется именно это: файл прежней
    /// версии из папки исчез.
    /// </remarks>
    [Fact]
    public void Installing_over_a_plugin_replaces_it_whole()
    {
        var source = Source();

        try
        {
            var catalog = new PluginCatalog(_root);

            catalog.InstallFromDirectory(source);
            File.WriteAllText(Path.Combine(_root, "arxis.figma-import", "bin", "Old.dll"), "прошлая версия");

            var (plugin, error) = catalog.InstallFromDirectory(source, replace: true);

            Assert.Null(error);
            Assert.NotNull(plugin);
            Assert.False(File.Exists(Path.Combine(_root, "arxis.figma-import", "bin", "Old.dll")));
            Assert.True(File.Exists(Path.Combine(_root, "arxis.figma-import", "bin", "Arxis.FigmaImport.dll")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>
    /// Выключённость переживает обновление.
    /// </summary>
    /// <remarks>
    /// Человек выключил этот плагин, а не эту его версию: включившись сам после
    /// обновления, плагин сделал бы за человека выбор, который тот уже сделал.
    /// </remarks>
    [Fact]
    public void An_update_does_not_switch_a_disabled_plugin_back_on()
    {
        var source = Source();

        try
        {
            var catalog = new PluginCatalog(_root);

            catalog.InstallFromDirectory(source);
            catalog.SetEnabled("arxis.figma-import", false);

            var (plugin, _) = catalog.InstallFromDirectory(source, replace: true);

            Assert.NotNull(plugin);
            Assert.False(plugin!.IsEnabled);
            Assert.False(Assert.Single(new PluginCatalog(_root).Scan()).IsEnabled);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>Удаление сносит папку и забывает выключённость.</summary>
    /// <remarks>
    /// Пометка живёт не в папке плагина, а в общем файле рядом, и, оставшись
    /// там, выключила бы плагин, поставленный заново, — а причину этого человек
    /// уже не вспомнит.
    /// </remarks>
    [Fact]
    public void Uninstalling_takes_the_folder_and_the_disabled_mark_with_it()
    {
        var source = Source();

        try
        {
            var catalog = new PluginCatalog(_root);
            var (plugin, _) = catalog.InstallFromDirectory(source);

            catalog.SetEnabled("arxis.figma-import", false);

            Assert.Null(catalog.Uninstall(plugin!));
            Assert.False(Directory.Exists(Path.Combine(_root, "arxis.figma-import")));

            var (installed, _) = catalog.InstallFromDirectory(source);

            Assert.True(installed!.IsEnabled);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>
    /// Занятую папку удалить нельзя — и об этом надо сказать словами.
    /// </summary>
    /// <remarks>
    /// Сборки плагина держит запущенная студия, пока он поднят. Исключение из
    /// каталога означало бы, что кнопка «Удалить» роняет окно; молчание — что
    /// она ничего не делает. Оба ответа человеку одинаково бесполезны.
    /// </remarks>
    [Fact]
    public void A_busy_plugin_folder_is_refused_with_a_word()
    {
        var source = Source();

        try
        {
            var catalog = new PluginCatalog(_root);
            var (plugin, _) = catalog.InstallFromDirectory(source);

            using var hold = File.Open(
                Path.Combine(_root, "arxis.figma-import", "bin", "Arxis.FigmaImport.dll"),
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);

            var error = catalog.Uninstall(plugin!);

            Assert.NotNull(error);
            Assert.Contains("занята", error);
            Assert.True(Directory.Exists(Path.Combine(_root, "arxis.figma-import")));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>Папка-источник с манифестом и сборкой.</summary>
    private static string Source()
    {
        var source = Path.Combine(Path.GetTempPath(), $"arxis-source-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Path.Combine(source, "bin"));
        File.WriteAllText(Path.Combine(source, "plugin.json"), Manifest);
        File.WriteAllText(Path.Combine(source, "bin", "Arxis.FigmaImport.dll"), "not really a dll");

        return source;
    }

    [Fact]
    public void Installing_from_a_folder_without_a_manifest_is_refused()
    {
        var source = Path.Combine(Path.GetTempPath(), $"arxis-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(source);

        try
        {
            var (plugin, error) = new PluginCatalog(_root).InstallFromDirectory(source);

            Assert.Null(plugin);
            Assert.Contains("plugin.json", error);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    private void Install(string id, string manifest)
    {
        var directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plugin.json"), manifest);
    }
}
