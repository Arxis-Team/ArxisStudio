using ArxisStudio.Controls;
using ArxisStudio.Modules.Inspector.Editors;
using Avalonia.Media;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Библиотеки образцов окна «Цвет» файлами: в профиле человека и в <c>.arxis/colors</c> рядом с решением (запись 376).
/// </summary>
public sealed class ColorLibrariesTests : IDisposable
{
    private readonly string _folder = TempFolder.Create("colors");

    public void Dispose() => TempFolder.Erase(_folder);

    /// <summary>
    /// Библиотека переживает новое хранилище: образцы с прозрачностью, порядок и имя, а имя с запрещёнными знаками
    /// становится файлом без них.
    /// </summary>
    [Fact]
    public void A_library_survives_a_new_store()
    {
        var profile = Path.Combine(_folder, "profile");
        var first = new ColorLibraries(profile, () => null, "My colors");

        Assert.Equal("My colors", first.Current.Name);

        first.Current.Presets.Add(new AxColorPreset("Ocean", Color.FromUInt32(0x803C6ECF)));
        first.Current.Presets.Add(new AxColorPreset("Sand", Color.FromUInt32(0xFFE0A040)));
        first.Save(first.Current);

        var brand = first.Create("Brand / 2026", AxColorPresetLocation.User);

        Assert.Same(brand, first.Current);

        brand.Presets.Add(new AxColorPreset("Logo", Colors.Navy));
        first.Save(brand);

        Assert.True(File.Exists(Path.Combine(profile, "Brand _ 2026.json")), "имя с косой чертой не стало файлом");

        var team = first.Create("Команда", AxColorPresetLocation.User);

        team.Presets.Add(new AxColorPreset("Логотип", Colors.Navy));
        first.Save(team);

        // Файл читают глазами в просмотре изменений: кириллица в нём как есть, а не \u-записью.
        Assert.Contains("\"Логотип\"", File.ReadAllText(Path.Combine(profile, "Команда.json")), StringComparison.Ordinal);

        var second = new ColorLibraries(profile, () => null, "My colors");
        var mine = second.Libraries.Single(library => library.Name == "My colors");

        Assert.Equal(["Brand / 2026", "My colors", "Команда"], second.Libraries.Select(library => library.Name));
        Assert.Equal(
            [new AxColorPreset("Ocean", Color.FromUInt32(0x803C6ECF)), new AxColorPreset("Sand", Color.FromUInt32(0xFFE0A040))],
            mine.Presets);
        Assert.Equal(AxColorPresetLocation.User, mine.Location);
    }

    /// <summary>
    /// Библиотека решения лежит в <c>.arxis/colors</c> рядом с ним, а без решения её не завести; закрытое решение уносит
    /// свои библиотеки.
    /// </summary>
    [Fact]
    public void A_solution_library_lies_in_dot_arxis_colors()
    {
        var solution = Path.Combine(_folder, "App", "App.sln");
        string? open = solution;

        Directory.CreateDirectory(Path.GetDirectoryName(solution)!);

        var store = new ColorLibraries(null, () => open, "My colors");

        Assert.True(store.CanCreate(AxColorPresetLocation.Project));

        var team = store.Create("Team", AxColorPresetLocation.Project);

        team.Presets.Add(new AxColorPreset("Logo", Colors.Navy));
        store.Save(team);

        Assert.True(File.Exists(Path.Combine(_folder, "App", ".arxis", "colors", "Team.json")), "библиотека решения не в .arxis/colors");

        var other = new ColorLibraries(null, () => solution, "My colors");

        Assert.Equal(
            ("Team", AxColorPresetLocation.Project, "Logo"),
            other.Libraries.Where(library => library.Location == AxColorPresetLocation.Project)
                .Select(library => (library.Name, library.Location, library.Presets.Single().Name)).Single());

        open = null;

        Assert.DoesNotContain(store.Libraries, library => library.Location == AxColorPresetLocation.Project);
        Assert.Equal("My colors", store.Current.Name);
        Assert.False(store.CanCreate(AxColorPresetLocation.Project), "без решения можно завести библиотеку решения");
    }

    /// <summary>
    /// Процесс тестов держит библиотеки профиля в памяти (<see cref="TestColors"/>): сохранённый образец не оставляет
    /// файла нигде.
    /// </summary>
    [Fact]
    public void The_tests_keep_the_profile_libraries_in_memory()
    {
        Assert.Null(ColorLibraries.UserFolder());

        var store = new ColorLibraries(ColorLibraries.UserFolder(), () => null, "My colors");

        store.Current.Presets.Add(new AxColorPreset("Ocean", Colors.Teal));
        store.Save(store.Current);
        store.Create("Brand", AxColorPresetLocation.User);

        Assert.Equal(["My colors", "Brand"], store.Libraries.Select(library => library.Name));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    /// <summary>Непрочитанный файл пропускается с предупреждением, а остальные библиотеки на месте.</summary>
    [Fact]
    public void An_unreadable_file_is_skipped_with_a_warning()
    {
        var profile = Directory.CreateDirectory(Path.Combine(_folder, "profile")).FullName;
        var warnings = new List<string>();

        File.WriteAllText(Path.Combine(profile, "broken.json"), "{ not json");
        File.WriteAllText(
            Path.Combine(profile, "good.json"),
            """{ "version": 1, "name": "Good", "presets": [ { "name": "Red", "color": "#FF0000" }, { "color": "nonsense" } ] }""");

        var store = new ColorLibraries(profile, () => null, "My colors", warnings.Add);

        Assert.Equal(["Good"], store.Libraries.Select(library => library.Name));
        Assert.Equal([new AxColorPreset("Red", Colors.Red)], store.Current.Presets);
        Assert.Contains("broken.json", Assert.Single(warnings), StringComparison.Ordinal);
    }
}
