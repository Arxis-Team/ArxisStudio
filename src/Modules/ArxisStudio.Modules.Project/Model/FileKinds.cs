namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Вид файла по его расширению.
/// </summary>
/// <remarks>
/// По расширению, а не по типу элемента MSBuild: один и тот же <c>app.manifest</c> бывает и
/// <c>None</c>, и <c>Content</c>, а человек узнаёт файл по имени, как узнаёт его Rider.
/// </remarks>
public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = FileKind.CSharp,
        [".axaml"] = FileKind.Markup,
        [".xaml"] = FileKind.Markup,
        [".csproj"] = FileKind.Xml,
        [".props"] = FileKind.Xml,
        [".targets"] = FileKind.Xml,
        [".xml"] = FileKind.Xml,
        [".config"] = FileKind.Xml,
        [".manifest"] = FileKind.Xml,
        [".resx"] = FileKind.Xml,
        [".nuspec"] = FileKind.Xml,
        [".slnx"] = FileKind.Xml,
        [".json"] = FileKind.Json,
        [".png"] = FileKind.Image,
        [".jpg"] = FileKind.Image,
        [".jpeg"] = FileKind.Image,
        [".gif"] = FileKind.Image,
        [".bmp"] = FileKind.Image,
        [".ico"] = FileKind.Image,
        [".svg"] = FileKind.Image,
        [".webp"] = FileKind.Image,
        [".md"] = FileKind.Text,
        [".txt"] = FileKind.Text,
    };

    /// <summary>Вид файла с таким расширением.</summary>
    /// <param name="extension">Расширение с точкой.</param>
    public static FileKind Of(string? extension) =>
        extension is { Length: > 0 } && Known.TryGetValue(extension, out var kind) ? kind : FileKind.Other;
}
