using System.Text.RegularExpressions;

namespace ArxisStudio.Tests;

/// <summary>
/// Общие сборки студии — тем правилом, каким их считает резолвер плагинов.
/// </summary>
/// <remarks>
/// Список руками был дырой: сборку, добавленную к общим, забывали дописать в проверку, и
/// проверка молча переставала её касаться. Правила вычитываются из <c>IsShared</c> в исходнике
/// резолвера: общей сборка бывает семейством — имя целиком или с точкой за ним — либо только по
/// имени целиком: модель проектов общая точно, чтобы её движки общими не стали. По первым буквам
/// общую не узнают: под «Avalonia» без точки попадала бы чужая <c>AvaloniaEdit</c>. Читается
/// исходник, а не зовётся метод: внутренности Extensibility тестам закрыты. Правилом пользуются и
/// упаковка плагина, и раскладка студии — общей сборке место и не в пакете плагина, и не в папке
/// модулей.
/// </remarks>
internal static class SharedAssemblies
{
    private static readonly Lazy<(string Name, bool Exact)[]> Parsed = new(Parse);

    /// <summary>Правила резолвера: имя и то, только ли целиком оно сравнивается.</summary>
    public static IReadOnlyList<(string Name, bool Exact)> Rules => Parsed.Value;

    /// <summary>Считает ли резолвер сборку общей — по тем же правилам, что он сам.</summary>
    /// <param name="name">Простое имя сборки.</param>
    public static bool IsShared(string name) =>
        Rules.Any(rule =>
            string.Equals(name, rule.Name, StringComparison.Ordinal) ||
            (!rule.Exact && name.StartsWith(rule.Name + ".", StringComparison.Ordinal)));

    /// <summary>
    /// Выражение, которым общие сборки узнаёт таргет упаковки плагина.
    /// </summary>
    /// <remarks>
    /// Берётся из самого таргета и исполняется, а не ищется в нём глазами: согласие двух записей
    /// одного правила проверяется тем, что они отвечают одинаково, а не тем, что похоже написаны.
    /// </remarks>
    public static Regex PackPattern()
    {
        var targets = File.ReadAllText(
            Repository.Path("src", "ArxisStudio.Sdk", "build", "ArxisStudio.Sdk.targets"));

        var match = Regex.Match(targets, @"<_AxSharedName>([^<]+)</_AxSharedName>");

        if (!match.Success)
            throw new InvalidOperationException("В таргете упаковки нет свойства _AxSharedName");

        return new Regex(match.Groups[1].Value, RegexOptions.CultureInvariant);
    }

    private static (string Name, bool Exact)[] Parse()
    {
        var resolver = File.ReadAllText(Repository.Path("src", "ArxisStudio.Extensibility", "PluginHost.cs"));

        return
        [
            .. Regex.Matches(resolver, @"Family\(name, ""([^""]+)""\)")
                .Select(match => (match.Groups[1].Value, false)),

            .. Regex.Matches(resolver, @"name\.Equals\(""([^""]+)"",")
                .Select(match => (match.Groups[1].Value, true)),
        ];
    }
}
