using System.Text.RegularExpressions;

namespace ArxisStudio.Tests;

/// <summary>
/// Общие сборки студии — тем правилом, каким их считает резолвер плагинов.
/// </summary>
/// <remarks>
/// Список руками был дырой: сборку, добавленную к общим, забывали дописать в проверку, и
/// проверка молча переставала её касаться. Правила вычитываются из <c>IsShared</c> в исходнике
/// резолвера: общей сборка бывает по приставке имени или по имени целиком — модель проектов общая
/// точно, чтобы её движки общими не стали. Читается исходник, а не зовётся метод: внутренности
/// Extensibility тестам закрыты. Правилом пользуются и упаковка плагина, и раскладка студии —
/// общей сборке место и не в пакете плагина, и не в папке модулей.
/// </remarks>
internal static class SharedAssemblies
{
    private static readonly Lazy<(string Name, bool Exact)[]> Parsed = new(Parse);

    /// <summary>Правила резолвера: имя и то, целиком ли оно сравнивается.</summary>
    public static IReadOnlyList<(string Name, bool Exact)> Rules => Parsed.Value;

    /// <summary>Считает ли резолвер сборку общей — по тем же правилам, что он сам.</summary>
    /// <param name="name">Простое имя сборки.</param>
    public static bool IsShared(string name) =>
        Rules.Any(rule => rule.Exact
            ? string.Equals(name, rule.Name, StringComparison.Ordinal)
            : name.StartsWith(rule.Name, StringComparison.Ordinal));

    /// <summary>Корень репозитория: над папкой тестов лежит решение.</summary>
    public static string Repository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArxisStudio.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Не найден корень репозитория: выше папки тестов нет ArxisStudio.slnx");
    }

    private static (string Name, bool Exact)[] Parse()
    {
        var resolver = File.ReadAllText(Path.Combine(Repository(), "src", "ArxisStudio.Extensibility", "PluginHost.cs"));

        return
        [
            .. Regex.Matches(resolver, @"name\.(StartsWith|Equals)\(([^,]+),")
                .Select(match => (match.Groups[2].Value.Trim('"'), match.Groups[1].Value == "Equals")),
        ];
    }
}
