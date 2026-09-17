using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Реестр вкладов: редакторы документов.
/// </summary>
/// <remarks>
/// Реализаций у этого контракта в репозитории нет — их приносил модуль
/// дизайнера, — но сам контракт остался: это то, чем плагин расширяет студию,
/// а не то, чем пользовался модуль. Заявка строится прямо здесь, в тестовой
/// сборке: реестру всё равно, откуда пришла сборка с вкладом.
/// </remarks>
public class ContributionsTests
{
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

        var match = registry.EditorFor(Path.Combine(Path.GetTempPath(), "Список.note"));

        Assert.NotNull(match);
        Assert.IsType<NoteEditor>(match!.Editor);
        Assert.Null(registry.EditorFor(Path.Combine(Path.GetTempPath(), "Список.txt")));

        // Хозяин нужен документу: когда плагин перезагрузят, его документы
        // придётся закрыть, а по самому редактору этого не узнать.
        Assert.Equal("notes", match.PluginId);

        registry.Remove("notes");
        Assert.Null(registry.EditorFor(Path.Combine(Path.GetTempPath(), "Список.note")));
    }

    /// <summary>
    /// Редактор, упавший на постройке или на вопросе, соседям ничего не стоит.
    /// </summary>
    /// <remarks>
    /// Реестр зовётся при приёме каждого поднятого плагина, в общем цикле. Пока редактор строился
    /// напрямую, его конструктор обрывал приём всех, кто поднялся после: хост их уже активировал, а
    /// панелей, полосы и редакторов они не получали. Бросающий <c>CanOpen</c> валил открытие любого
    /// файла у всех редакторов сразу. Сборка с такими редакторами собирается здесь же, а не лежит в
    /// тестовой: соседний тест перебирает тестовую сборку целиком.
    /// </remarks>
    [Fact]
    public void An_editor_that_falls_costs_the_others_nothing()
    {
        var guard = new PluginGuard();
        var failures = new List<PluginFailure>();

        guard.Failed += (_, failure) => failures.Add(failure);

        var registry = new PluginContributionRegistry(guard);

        var broken = TestAssembly.Emit("Probe.BrokenEditors", """
            using System.Threading.Tasks;
            using ArxisStudio.Sdk;

            namespace Probe;

            public sealed class FallsWhenBuilt : DocumentEditor
            {
                public FallsWhenBuilt() => throw new System.InvalidOperationException("редактор не построился");

                public override bool CanOpen(string filePath) => true;

                public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath) =>
                    Task.FromResult<(DocumentView?, string?)>((null, null));
            }

            public sealed class FallsWhenAsked : DocumentEditor
            {
                public override bool CanOpen(string filePath) =>
                    throw new System.InvalidOperationException("редактор упал на вопросе");

                public override Task<(DocumentView? View, string? Error)> OpenAsync(string filePath) =>
                    Task.FromResult<(DocumentView?, string?)>((null, null));
            }
            """);

        registry.Add("arxis.broken", "Сломанный", [broken]);
        registry.Add("notes", "Заметки", [typeof(NoteEditor).Assembly]);

        var match = registry.EditorFor(Path.Combine(Path.GetTempPath(), "Список.note"));

        Assert.IsType<NoteEditor>(match?.Editor);

        Assert.Contains(failures, failure =>
            failure.PluginId == "arxis.broken" && failure.What.StartsWith("редактор ", StringComparison.Ordinal));

        Assert.Contains(failures, failure =>
            failure.PluginId == "arxis.broken" && failure.What.StartsWith("вопрос редактору", StringComparison.Ordinal));
    }
}

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
