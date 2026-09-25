using System.Xml.Linq;
using ArxisStudio.Modules.Projects.Files;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Ссылки файла проекта на переехавшие и удалённые пути.
/// </summary>
/// <remarks>
/// Проект в стиле SDK берёт файлы масками, и переименованный файл найдёт сам, — но буквальная ссылка
/// остаётся на старом имени, и её метаданные пропадают молча: <c>appsettings.json</c> перестаёт
/// копироваться в выход. Здесь — что переписывается, что снимается и что остаётся нетронутым.
/// </remarks>
public class ItemReferencesTests
{
    private static readonly string Project = Path.Combine(Path.GetTempPath(), "arxis-item-references", "App");

    /// <summary>Переименованный файл уносит свою ссылку вместе с метаданными.</summary>
    [Fact]
    public void A_renamed_file_takes_its_reference_and_metadata_along()
    {
        var document = Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <None Update="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(Rewrite(document, Moved("appsettings.json", "settings.json")));

        var item = document.Descendants("None").Single();

        Assert.Equal("settings.json", item.Attribute("Update")?.Value);
        Assert.Equal("PreserveNewest", item.Attribute("CopyToOutputDirectory")?.Value);
    }

    /// <summary>
    /// Переименованная папка переписывает маску, чья буквальная часть в ней лежит, и пути под ней.
    /// </summary>
    [Fact]
    public void A_renamed_folder_rewrites_the_masks_and_paths_inside_it()
    {
        var document = Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaResource Include="Assets\**" />
                <None Update="Assets\logo.ico" Pack="true" />
                <Content Include="Other/**/*.png" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(Rewrite(document, Moved("Assets", "Images", folder: true)));

        Assert.Equal("Images\\**", document.Descendants("AvaloniaResource").Single().Attribute("Include")?.Value);
        Assert.Equal("Images\\logo.ico", document.Descendants("None").Single().Attribute("Update")?.Value);
        Assert.Equal("Other/**/*.png", document.Descendants("Content").Single().Attribute("Include")?.Value);
    }

    /// <summary>
    /// Удалённый путь снимается из списка, а элемент, у которого снялось всё, уходит вместе с отступом.
    /// </summary>
    [Fact]
    public void A_deleted_path_leaves_its_list_and_an_emptied_item_leaves_whole()
    {
        var document = Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="Old.cs;Keep.cs" />
                <None Update="Old.cs" CopyToOutputDirectory="Always" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(Rewrite(document, new PathChange(Full("Old.cs"), null, false)));

        Assert.Equal("Keep.cs", document.Descendants("Compile").Single().Attribute("Include")?.Value);
        Assert.Empty(document.Descendants("None"));
        Assert.DoesNotContain("\n    \n", Text(document).Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>DependentUpon</c> идёт за владельцем, а зависимый файл, переехавший вместе с ним, — за собой.
    /// </summary>
    [Fact]
    public void Dependent_upon_follows_its_owner_and_the_dependent_itself()
    {
        var document = Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Update="Views\MainWindow.axaml.cs">
                  <DependentUpon>MainWindow.axaml</DependentUpon>
                </Compile>
              </ItemGroup>
            </Project>
            """);

        Assert.True(Rewrite(document,
            Moved("Views\\MainWindow.axaml", "Views\\Main.axaml"),
            Moved("Views\\MainWindow.axaml.cs", "Views\\Main.axaml.cs")));

        var item = document.Descendants("Compile").Single();

        Assert.Equal("Views\\Main.axaml.cs", item.Attribute("Update")?.Value);
        Assert.Equal("Main.axaml", item.Element("DependentUpon")?.Value);
    }

    /// <summary>
    /// Незатронутое правкой остаётся написанным так, как написал человек, — и свойства, и разделитель.
    /// </summary>
    [Fact]
    public void What_the_change_does_not_touch_keeps_its_spelling()
    {
        const string text = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="$(Shared)\Tool.cs" />
                <Compile Update="Views/Other.axaml.cs" DependentUpon="./Other.axaml" />
                <None Include="Docs/readme.md;;Docs/notes.md" />
              </ItemGroup>
            </Project>
            """;

        var document = Parse(text);

        Assert.False(Rewrite(document, Moved("Unrelated.cs", "Renamed.cs")));
        Assert.Equal(Text(Parse(text)), Text(document));

        Assert.True(Rewrite(document, Moved("Docs/notes.md", "Docs/todo.md")));
        Assert.Equal("Docs/readme.md;Docs/todo.md", document.Descendants("None").Single().Attribute("Include")?.Value);
    }

    /// <summary>
    /// Папка с тем же началом имени — соседка, а не вложенная: переименование <c>Assets</c> не
    /// трогает <c>AssetsOld</c>.
    /// </summary>
    [Fact]
    public void A_sibling_folder_sharing_the_start_of_the_name_is_not_inside()
    {
        var document = Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <None Update="Assets\logo.ico" Pack="true" />
                <None Update="AssetsOld\logo.ico" Pack="true" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(Rewrite(document, Moved("Assets", "Images", folder: true)));

        Assert.Equal(
            ["Images\\logo.ico", "AssetsOld\\logo.ico"],
            document.Descendants("None").Select(item => item.Attribute("Update")?.Value));
    }

    /// <summary>
    /// Папка, записанная с разделителем на конце, — <c>&lt;Folder Include="Assets\"/&gt;</c>, как её
    /// пишет Visual Studio, — остаётся записанной так же, с разделителем того же вида.
    /// </summary>
    [Fact]
    public void A_folder_written_with_a_trailing_separator_keeps_it()
    {
        var document = Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Folder Include="Assets\" />
                <Folder Include="Docs/" />
              </ItemGroup>
            </Project>
            """);

        Assert.True(Rewrite(document, Moved("Assets", "Images", folder: true), Moved("Docs", "Notes", folder: true)));

        Assert.Equal(["Images\\", "Notes/"], document.Descendants("Folder").Select(item => item.Attribute("Include")?.Value));
    }

    private static bool Rewrite(XDocument document, params PathChange[] changes) =>
        ItemReferences.Rewrite(document, Project, changes);

    private static PathChange Moved(string from, string to, bool folder = false) => new(Full(from), Full(to), folder);

    private static string Full(string relative) =>
        Path.GetFullPath(Path.Combine(Project, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));

    private static XDocument Parse(string text) => XDocument.Parse(text, LoadOptions.PreserveWhitespace);

    private static string Text(XDocument document) => document.ToString(SaveOptions.DisableFormatting);
}
