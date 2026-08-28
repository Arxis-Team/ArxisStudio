using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Реестр вкладов: рисовальщики свойств, свои инспекторы и редакторы
/// документов.
/// </summary>
/// <remarks>
/// Реализаций у этих контрактов в репозитории больше нет — их приносил модуль
/// дизайнера, — но сами контракты остались: это то, чем плагин расширяет
/// студию, а не то, чем пользовался модуль. Заявки строятся прямо здесь, в
/// тестовой сборке: реестру всё равно, откуда пришла сборка с вкладом.
/// </remarks>
public class ContributionsTests
{
    /// <summary>
    /// Два рисовальщика на один тип — это не выбор, а гонка: выиграл бы тот,
    /// кого раньше загрузили, поэтому второй отвергается со словом о том, кто
    /// занял тип.
    /// </summary>
    [Fact]
    public void A_type_that_is_already_taken_is_refused_with_a_word()
    {
        var registry = new PluginContributionRegistry();
        var conflicts = new List<string>();
        var assembly = typeof(FirstDrawer).Assembly;

        registry.Add("first", "Первый", [assembly]);

        var winner = registry.DrawerFor(typeof(int));

        Assert.NotNull(winner);

        registry.Conflict += (_, message) => conflicts.Add(message);
        registry.Add("second", "Второй", [assembly]);

        Assert.NotEmpty(conflicts);
        Assert.All(conflicts, message => Assert.Contains("first", message));
        Assert.All(conflicts, message => Assert.Contains("Второй", message));
        Assert.Equal("first", registry.DrawerFor(typeof(int))!.PluginId);
        Assert.IsType(winner.Drawer.GetType(), registry.DrawerFor(typeof(int))!.Drawer);
    }

    /// <summary>
    /// Инспектор, заявленный на базовый тип, должен доставаться и наследнику:
    /// иначе своя кнопка в библиотеке отменяла бы чужую работу.
    /// </summary>
    [Fact]
    public void An_inspector_declared_on_a_base_type_serves_its_heirs()
    {
        var registry = new PluginContributionRegistry();

        registry.Add("editor", "Редактор", [typeof(ButtonInspector).Assembly]);

        Assert.IsType<ButtonInspector>(registry.InspectorFor(typeof(Button))!.Editor);
        Assert.IsType<ButtonInspector>(registry.InspectorFor(typeof(HeirButton))!.Editor);
        Assert.Null(registry.InspectorFor(typeof(TextBlock)));
    }

    [Fact]
    public void A_disabled_plugin_takes_its_contributions_with_it()
    {
        var registry = new PluginContributionRegistry();

        registry.Add("first", "Первый", [typeof(FirstDrawer).Assembly]);
        Assert.NotNull(registry.DrawerFor(typeof(int)));

        registry.Remove("first");
        Assert.Null(registry.DrawerFor(typeof(int)));
    }

    /// <summary>
    /// За файл берётся тот редактор, который объявил его тип.
    /// </summary>
    /// <remarks>
    /// Оболочка не знает ни одного расширения: открывая путь, она спрашивает
    /// реестр, и ответ «никто» — обычный ответ, а не ошибка. Проверка нужна
    /// именно теперь: реализаций редактора в репозитории не осталось, и без
    /// теста этот контракт молча зарос бы.
    /// </remarks>
    [Fact]
    public void A_document_editor_takes_the_files_it_claimed()
    {
        var registry = new PluginContributionRegistry();

        registry.Add("notes", "Заметки", [typeof(NoteEditor).Assembly]);

        Assert.NotNull(registry.EditorFor(Path.Combine(Path.GetTempPath(), "Список.note")));
        Assert.Null(registry.EditorFor(Path.Combine(Path.GetTempPath(), "Список.txt")));
    }
}

/// <summary>Рисовальщик-пустышка, занимающий тип.</summary>
[PropertyDrawer(typeof(int))]
public sealed class FirstDrawer : PropertyDrawer
{
    /// <inheritdoc/>
    public override Control Build(IPropertyContext property) => new TextBlock { Text = "первый" };
}

/// <summary>Свой инспектор для кнопок.</summary>
[CustomInspector(typeof(Button))]
public sealed class ButtonInspector : InspectorEditor
{
    /// <inheritdoc/>
    public override Control Build(IInspectorContext element) => new TextBlock { Text = element.TypeName };
}

/// <summary>Наследник кнопки: инспектор базового типа должен доставаться и ему.</summary>
public sealed class HeirButton : Button;

/// <summary>Редактор документов-пустышка: берётся за файлы своего типа.</summary>
public sealed class NoteEditor : DocumentEditor
{
    /// <inheritdoc/>
    public override bool CanOpen(string filePath) =>
        Path.GetExtension(filePath).Equals(".note", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath) =>
        Task.FromResult<(DocumentView?, string?)>((null, "пример: открывать нечего"));
}
