using ArxisStudio.Modules.Designer;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Один открытый документ подопытного проекта на весь прогон тестов.
/// </summary>
/// <remarks>
/// Открыть его дважды в одном процессе нельзя: каждое открытие поднимает сборки
/// проекта в своём контексте, и загрузчик натыкается на два разных типа с одним
/// именем — <c>Unable to substitute DesignFixtureApp.MainWindow with
/// DesignFixtureApp.MainWindow</c>. Ровно то же ограничение мешает студии
/// открыть проект, чьи сборки она уже загрузила в себя.
/// <para>
/// Поэтому документ открывается один раз и переиспользуется. Тесты, которые его
/// правят, откатывают за собой правки, а утверждения об истории делаются не о
/// её длине, а об изменении относительно того, что было до теста.
/// </para>
/// </remarks>
internal static class DesignerFixture
{
    private static Task<DesignerFixtureState>? _opening;

    /// <summary>
    /// Возвращает открытый документ, открывая его при первом обращении.
    /// </summary>
    /// <remarks>
    /// Вызывается только из тестов Avalonia, то есть с потока интерфейса: живые
    /// объекты документа другого потока не примут.
    /// </remarks>
    public static Task<DesignerFixtureState> OpenAsync() => _opening ??= OpenCoreAsync();

    /// <summary>Откатывает правки теста, чтобы следующий начал с чистого документа.</summary>
    /// <param name="document">Документ.</param>
    public static async Task RollbackAsync(DesignDocument document)
    {
        while (document.CanUndo)
            await document.UndoAsync();
    }

    private static async Task<DesignerFixtureState> OpenCoreAsync()
    {
        var project = FindProject();
        var file = Path.Combine(Path.GetDirectoryName(project)!, "MainWindow.axaml");

        var workspace = new StudioWorkspace();
        var openError = await workspace.OpenAsync(project);

        Assert.Null(openError);

        var snapshot = workspace.FindProjectForFile(file);
        Assert.NotNull(snapshot);

        var (document, error) = await DesignDocument.OpenAsync(file, workspace.Snapshot!, snapshot);

        if (error is not null)
            Assert.Fail(error);

        return new DesignerFixtureState(workspace, snapshot, document!);
    }

    private static string FindProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures", "DesignFixtureApp", "DesignFixtureApp.csproj");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Не найден подопытный проект tests/fixtures/DesignFixtureApp");
    }
}

/// <summary>Что тестам досталось от открытия подопытного проекта.</summary>
/// <param name="Workspace">Модель решения.</param>
/// <param name="Project">Снапшот подопытного проекта.</param>
/// <param name="Document">Открытый документ дизайнера.</param>
internal sealed record DesignerFixtureState(
    StudioWorkspace Workspace,
    ArxisStudio.ProjectSystem.ProjectSnapshot Project,
    DesignDocument Document)
{
    /// <summary>Находит узел дерева по значению <c>x:Name</c>.</summary>
    /// <param name="name">Имя элемента в разметке.</param>
    public HierarchyNode Node(string name) =>
        Flatten(Document.Nodes).First(node => node.Identity == name);

    /// <summary>Разворачивает дерево в плоскую последовательность.</summary>
    public IEnumerable<HierarchyNode> All() => Flatten(Document.Nodes);

    private static IEnumerable<HierarchyNode> Flatten(IEnumerable<HierarchyNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var descendant in Flatten(node.Children))
                yield return descendant;
        }
    }
}
