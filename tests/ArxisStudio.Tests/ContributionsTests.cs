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
