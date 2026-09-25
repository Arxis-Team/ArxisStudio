using System.Text;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Решение на диске, открытое службой «Проекты»: проект с вложенным файлом, папкой и ссылкой
/// <c>Update</c> в файле проекта и второй проект рядом.
/// </summary>
/// <remarks>
/// Провайдер теста MSBuild не зовёт — снимок он собирает сам, но папки проектов в нём настоящие, и
/// диск правится настоящий. Историю служба ведёт во временной папке теста.
/// <para>
/// Наборы службы файлов и службы истории правят одно и то же решение и спрашивают его одними
/// словами — эти слова здесь. Базовым классом, а не полем: пути в наборах названы сотнями, и
/// приставка к каждому ничего бы не прояснила.
/// </para>
/// </remarks>
public abstract class ProjectsOnDisk : IDisposable
{
    private const string Project = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <None Update="Views\Readme.txt" CopyToOutputDirectory="Always" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>Заводит временную папку набора.</summary>
    /// <param name="name">Чья папка — часть её имени.</param>
    private protected ProjectsOnDisk(string name) =>
        Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"arxis-{name}-{Guid.NewGuid():N}")).FullName;

    /// <summary>Временная папка набора.</summary>
    private protected string Root { get; }

    /// <summary>Папка решения.</summary>
    private protected string Solution => Path.Combine(Root, "solution");

    /// <summary>Проект, в котором лежит всё, что правят.</summary>
    private protected string Lib => Path.Combine(Solution, "Lib");

    /// <summary>Второй проект.</summary>
    private protected string App => Path.Combine(Solution, "App");

    /// <summary>Папка локальной истории.</summary>
    private protected string HistoryRoot => Path.Combine(Root, "history");

    private protected static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Раскладывает решение на диске.</summary>
    private protected virtual void Lay()
    {
        Directory.CreateDirectory(Path.Combine(Lib, "Views"));
        File.WriteAllText(Path.Combine(Solution, "Hello.slnx"), "<Solution />");
        File.WriteAllText(At("Lib.csproj"), Project.ReplaceLineEndings("\r\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(At("Greeter.cs"), "class Greeter { }");
        File.WriteAllText(At("appsettings.json"), "{ }");
        File.WriteAllText(At("Views/MainWindow.axaml"), "<Window />");
        File.WriteAllText(At("Views/MainWindow.axaml.cs"), "partial class MainWindow { }");
        File.WriteAllText(At("Views/Readme.txt"), "прочти");
        Directory.CreateDirectory(App);
        File.WriteAllText(Path.Combine(App, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(App, "Program.cs"), "class Program { }");
    }

    /// <summary>Решение на диске, открытое службой, — и опорный снимок истории снят.</summary>
    /// <param name="history">Вести ли историю.</param>
    /// <param name="write">Раскладывать ли решение заново: второй запуск открывает то, что оставил первый.</param>
    private protected async Task<ProjectsStudio> OpenAsync(bool history = true, bool write = true)
    {
        if (write)
            Lay();

        var studio = new ProjectsStudio(historyRoot: history ? HistoryRoot : null);

        studio.Provider.Projects =
        [
            ("Lib", ["Greeter.cs", "appsettings.json", "Views/MainWindow.axaml", "Views/MainWindow.axaml.cs"]),
            ("App", ["Program.cs"]),
        ];

        var opened = await studio.Projects.OpenAsync(CanonicalPath.Create(Path.Combine(Solution, "Hello.slnx")), Token);

        Assert.True(opened.HasSnapshot, "решение не открылось");

        await studio.SettleHistoryAsync();

        return studio;
    }

    /// <summary>Полный путь файла проекта <c>Lib</c>.</summary>
    /// <param name="relative">Путь от папки проекта, через прямую черту.</param>
    private protected string At(string relative) =>
        Path.GetFullPath(Path.Combine(Lib, relative.Replace('/', Path.DirectorySeparatorChar)));

    private protected CanonicalPath Canon(string relative) => CanonicalPath.Create(At(relative));

    private protected FileMove Pair(string from, string to) => new(Canon(from), Canon(to));

    /// <summary>Код первой диагностики итога; null — диагностик нет.</summary>
    private protected static string? Code(ProjectOperationResult result) => result.Diagnostics.FirstOrDefault()?.Code;

    /// <summary>Диагностики итога одной строкой — чтобы падение говорило, что случилось.</summary>
    private protected static string Said(ProjectOperationResult result) =>
        string.Join("; ", result.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.Message}"));
}
