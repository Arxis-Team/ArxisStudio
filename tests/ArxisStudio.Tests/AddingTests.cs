using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Правила создания окна проекта без окна: путь в имени, пространство имён и имя типа.
/// </summary>
/// <remarks>
/// Пространство имён — то, что человек увидит первой строкой нового файла, и ошибиться в нём значит
/// заставить править каждый созданный класс. Правило то же, что у Rider и Visual Studio: корневое
/// пространство проекта и каталоги от проекта, приведённые к идентификаторам.
/// </remarks>
public class AddingTests
{
    private static readonly CanonicalPath Project = CanonicalPath.Create(Path.Combine(Path.GetTempPath(), "arxis-adding", "App"));

    /// <summary>Набранное раскладывается на каталоги по дороге и имя — обе косые считаются.</summary>
    [Fact]
    public void A_typed_path_splits_into_directories_and_a_name()
    {
        var typed = Adding.Split("Models/Dto\\Person");

        Assert.Equal(["Models", "Dto"], typed.Directories);
        Assert.Equal("Person", typed.Name);
        Assert.Equal(Project.Combine(Path.Combine("Models", "Dto")), typed.In(Project));

        var plain = Adding.Split("Person");

        Assert.Empty(plain.Directories);
        Assert.Equal(Project, plain.In(Project));
    }

    /// <summary>
    /// Пространство имён — корневое и каталоги от проекта; знак не из имени — подчёркивание, цифра
    /// впереди — подчёркивание перед ней, точка в имени каталога — уровень.
    /// </summary>
    [Fact]
    public void The_namespace_is_the_root_and_the_directories_as_identifiers()
    {
        Assert.Equal("Hello.App", Adding.Namespace("Hello.App", Project, Project));
        Assert.Equal("Hello.App.Views", Adding.Namespace("Hello.App", Project, Project.Combine("Views")));
        Assert.Equal(
            "Hello.App.My_Folder._1st.Part",
            Adding.Namespace("Hello.App", Project, Project.Combine(Path.Combine("My Folder", "1st.Part"))));

        // Каталог вне проекта берёт корневое: считать пространство от чужого места не от чего.
        Assert.Equal("Hello.App", Adding.Namespace("Hello.App", Project, Project.Directory.Combine("Other")));
    }

    /// <summary>Корневое пространство — свойство проекта, а без него — имя проекта, приведённые к идентификаторам.</summary>
    [Fact]
    public void The_root_namespace_comes_from_the_property_or_the_project_name()
    {
        var declared = Snapshot("My-App", root: "My-App.Core");
        var named = Snapshot("7Days");

        Assert.Equal("My_App.Core", Adding.RootNamespace(declared));
        Assert.Equal("_7Days", Adding.RootNamespace(named));
    }

    /// <summary>Часть пространства имён: пустая и с цифрой впереди получают подчёркивание.</summary>
    [Fact]
    public void A_part_becomes_an_identifier()
    {
        Assert.Equal("a_b", Adding.Identifier("a b"));
        Assert.Equal("_1x", Adding.Identifier("1x"));
        Assert.Equal("_", Adding.Identifier(string.Empty));
        Assert.Equal("Пример", Adding.Identifier("Пример"));
    }

    /// <summary>
    /// Имя типа — буквы, цифры и подчёркивание, не с цифры и не ключевое слово; регистр ключевого
    /// слова строгий, как у компилятора.
    /// </summary>
    [Fact]
    public void A_type_name_follows_the_compiler()
    {
        foreach (var name in new[] { "Card", "_card1", "Пример", "Class" })
            Assert.True(FileNames.Identifier(name).IsFine, $"«{name}» — имя типа, а правило его не пустило");

        Assert.Equal(NameProblem.NotIdentifier, FileNames.Identifier("1Card").Problem);
        Assert.Equal(NameProblem.NotIdentifier, FileNames.Identifier("Ca-rd").Problem);
        Assert.Equal(NameProblem.NotIdentifier, FileNames.Identifier("Ca rd").Problem);
        Assert.Equal(new NameCheck(NameProblem.Keyword, "class"), FileNames.Identifier("class"));
    }

    /// <summary>Правила имени файла — одни у создания и переименования.</summary>
    [Fact]
    public void File_name_rules_are_one_for_creating_and_renaming()
    {
        Assert.Equal(NameProblem.Empty, FileNames.Check(" ").Problem);
        Assert.Equal(new NameCheck(NameProblem.Invalid, ":"), FileNames.Check("a:b"));
        Assert.Equal(new NameCheck(NameProblem.Invalid, ".."), FileNames.Check(".."));
        Assert.Equal(NameProblem.Trailing, FileNames.Check("Card.").Problem);
        Assert.Equal(new NameCheck(NameProblem.Reserved, "COM1"), FileNames.Check("COM1.txt"));
        Assert.True(FileNames.Check("Card.cs").IsFine);
    }

    private static ProjectSnapshot Snapshot(string name, string? root = null)
    {
        var file = Project.Combine(name + ".csproj");
        var builder = new ProjectSnapshotBuilder
        {
            Identity = ProjectIdentity.Create(WorkspaceIdentity.New(), file),
            ProjectFilePath = file,
            Name = name,
        };

        if (root is not null)
            builder.Properties["RootNamespace"] = root;

        return builder.ToSnapshot();
    }
}
