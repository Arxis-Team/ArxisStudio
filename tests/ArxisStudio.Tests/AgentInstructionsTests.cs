using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Инструкции агентам — корневой <c>CLAUDE.md</c>, подключённые к нему правила кода и <c>CLAUDE.md</c> в папках
/// подсистем: корень не растёт молча, ссылки и имена ведут в живое, до каждого файла папки доходят от корня, а
/// счёта, который правят каждым коммитом, в них нет.
/// </summary>
/// <remarks>
/// Инструкции не собирает компилятор и не читает ни один другой тест, поэтому стареют они молча: тип
/// переименовали — в инструкции осталось мёртвое имя, папку перенесли — ссылка ведёт в никуда, а корень, который
/// грузится в каждую сессию, растёт по строчке. Счёт тестов и записей правили каждым коммитом, и о него
/// спотыкалось каждое слияние параллельных веток (запись 356).
/// </remarks>
public class AgentInstructionsTests
{
    /// <summary>
    /// Сколько строк грузится в каждую сессию: корень и то, что он подключает импортом.
    /// </summary>
    /// <remarks>
    /// Храповик, и сверка точная, как у отступов: запас под потолком потратили бы. Выросло — подробность одной
    /// подсистемы уходит в <c>CLAUDE.md</c> её папки, а общее правило пишут короче; поднимают число осознанно,
    /// тем же коммитом, что и строки. Убавилось — число опускают.
    /// </remarks>
    private const int Budget = 251;

    /// <summary>
    /// Имена не из кода студии и её подмодулей: папка Windows, ключ PowerShell и внутренность Avalonia, названная,
    /// чтобы по ней искали причину.
    /// </summary>
    private static readonly HashSet<string> Foreign = new(StringComparer.Ordinal) { "AppData", "PassThru", "ExecuteArrangePass" };

    /// <summary>Папки, в которые не смотрят: выход сборок, раскладка плагина, служебное.</summary>
    private static readonly HashSet<string> Skipped =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".claude", "package", "node_modules", "TestResults" };

    /// <summary>Что считается кодом: исходники, разметка, манифесты, словари, файлы сборки и правила git и стиля.</summary>
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".axaml", ".xaml", ".csproj", ".props", ".targets", ".json", ".slnx", ".gitattributes", ".editorconfig",
    };

    /// <summary>Начала путей репозитория, которые инструкции называют в обратных кавычках.</summary>
    private static readonly string[] RepositoryRoots = ["src/", "tests/", "docs/", "templates/", "external/"];

    private static readonly Regex Fence = new("^```.*?^```", RegexOptions.Multiline | RegexOptions.Singleline);
    private static readonly Regex Span = new("`(?<code>[^`\n]+)`");
    private static readonly Regex Link = new(@"\]\((?<target>[^)\s]+)\)");
    private static readonly Regex Import = new(@"^@(?<path>\S+)\s*$", RegexOptions.Multiline);
    private static readonly Regex Heading = new(@"^#{1,6}\s+(?<text>.+?)\s*$", RegexOptions.Multiline);
    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*(\(\))?$");
    private static readonly Regex FileName = new(@"^[\w.-]+\.(cs|axaml|json|props|targets|csproj|slnx)$");
    private static readonly Regex Word = new("[A-Za-z_][A-Za-z0-9_]*");
    private static readonly Regex Hump = new("[a-z0-9][A-Z]");
    private static readonly Regex Count = new(@"\b\d+\s+(тест|запис)\w*", RegexOptions.IgnoreCase);

    private static string RootGuide => Path.GetFullPath(Repository.File("CLAUDE.md"));

    /// <summary>
    /// В каждую сессию грузится корень и всё, что он подключает импортом, — ровно столько строк, сколько записано
    /// в храповике; правила кода подключены, а не только названы.
    /// </summary>
    [Fact]
    public void The_root_and_what_it_imports_hold_the_budget()
    {
        var loaded = Imported(RootGuide).ToList();
        var lines = loaded.Sum(path => File.ReadAllLines(path).Length);

        Assert.True(
            loaded.Contains(Path.GetFullPath(Repository.Path("CODING_STANDARDS.md")), StringComparer.OrdinalIgnoreCase),
            "правила кода не подключены к корню: строки @CODING_STANDARDS.md в CLAUDE.md нет, и сессия их не увидит");
        Assert.True(
            lines <= Budget,
            $"в каждую сессию грузится строк: {lines} при потолке {Budget} ({string.Join(", ", loaded.Select(Name))}) — " +
            "подробности одной подсистемы место в CLAUDE.md её папки, общее правило пишут короче, " +
            "а потолок поднимают осознанно, тем же коммитом");
        Assert.True(lines >= Budget, $"строк стало {lines} — опустите потолок {Budget} до них: запас под потолком потратят");
    }

    /// <summary>Каждая ссылка инструкций ведёт на то, что есть, а якорь — на заголовок, который есть.</summary>
    [Fact]
    public void Every_link_in_the_instructions_leads_somewhere()
    {
        var broken = new List<string>();

        foreach (var guide in Guides())
        {
            foreach (var target in Links(guide))
            {
                var (path, anchor) = Split(target);
                var full = path.Length == 0 ? guide : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(guide)!, path));

                if (!Path.Exists(full))
                    broken.Add($"{Name(guide)}: {target} ведёт в никуда");
                else if (anchor.Length > 0 && full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !Anchors(full).Contains(anchor))
                    broken.Add($"{Name(guide)}: заголовка {target} нет");
            }
        }

        Assert.True(broken.Count == 0, string.Join(Environment.NewLine, broken));
    }

    /// <summary>
    /// Каждое имя инструкций в обратных кавычках — тип, член, код ошибки, файл, путь репозитория — есть в коде
    /// студии или её подмодулей: переименованное оставило бы в инструкции мёртвое имя.
    /// </summary>
    [Fact]
    public void Every_name_in_the_instructions_is_in_the_code()
    {
        var named = Guides().SelectMany(guide => Names(guide).Select(name => (Guide: guide, Name: name))).ToList();
        var code = Code.Read(named.Select(entry => entry.Name).Where(name => FileName.IsMatch(name)));
        var dead = named.Where(entry => !code.Knows(entry.Name)).Select(entry => $"{Name(entry.Guide)}: `{entry.Name}` в коде нет").ToList();

        Assert.True(
            dead.Count == 0,
            string.Join(Environment.NewLine, dead) + Environment.NewLine +
            $"имя переименовали — назовите новое; имя не из кода студии и подмодулей — допишите его в {nameof(Foreign)}");
    }

    /// <summary>
    /// До каждого <c>CLAUDE.md</c> папки доходят от корня по ссылкам инструкций: файл, о котором корень не знает,
    /// прочтёт только тот, кто уже открыл его папку.
    /// </summary>
    [Fact]
    public void Every_folder_guide_is_reachable_from_the_root()
    {
        var guides = Guides().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([RootGuide]);

        while (queue.TryDequeue(out var guide))
        {
            if (!reached.Add(guide))
                continue;

            var next = Imported(guide).Skip(1).Concat(Links(guide).Select(link => Resolve(guide, link)).OfType<string>());

            foreach (var file in next.Where(guides.Contains))
                queue.Enqueue(file);
        }

        var lost = guides.Where(guide => !reached.Contains(guide)).Select(Name).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            lost.Count == 0,
            $"до этих инструкций от корня не дойти: {string.Join(", ", lost)} — назовите их на карте корня " +
            "или в CLAUDE.md папки выше");
    }

    /// <summary>
    /// В инструкциях нет счёта тестов и записей: число берут у прогона и у журнала. Пример в обратных кавычках —
    /// не счёт.
    /// </summary>
    [Fact]
    public void The_instructions_hold_no_counts()
    {
        var counts = Guides()
            .SelectMany(guide => Count.Matches(Span.Replace(Prose(guide), string.Empty)).Select(match => $"{Name(guide)}: «{match.Value}»"))
            .ToList();

        Assert.True(counts.Count == 0, "счёт устаревает с каждым коммитом и ссорит ветки: " + string.Join("; ", counts));
    }

    /// <summary>Корень, правила кода и все <c>CLAUDE.md</c> в папках исходников и тестов.</summary>
    private static List<string> Guides()
    {
        var guides = new List<string> { Repository.File("CLAUDE.md"), Repository.File("CODING_STANDARDS.md") };

        foreach (var top in new[] { "src", "tests" })
            guides.AddRange(Files(Repository.Path(top)).Where(path => Path.GetFileName(path) == "CLAUDE.md"));

        return guides.Select(Path.GetFullPath).ToList();
    }

    /// <summary>Файл и всё, что он подключает импортом, — вглубь.</summary>
    private static IEnumerable<string> Imported(string guide)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([Path.GetFullPath(guide)]);

        while (pending.TryPop(out var file))
        {
            if (!seen.Add(file))
                continue;

            yield return file;

            foreach (Match import in Import.Matches(Prose(file)))
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, import.Groups["path"].Value)));
        }
    }

    /// <summary>Ссылки текста, кроме ссылок наружу; ссылка в обратных кавычках — пример, а не ссылка.</summary>
    private static IEnumerable<string> Links(string guide) =>
        Link.Matches(Span.Replace(Prose(guide), string.Empty))
            .Select(match => match.Groups["target"].Value)
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith("mailto:", StringComparison.Ordinal));

    /// <summary>Файл, на который ведёт ссылка: сам <c>.md</c> или <c>CLAUDE.md</c> названной папки.</summary>
    private static string? Resolve(string guide, string link)
    {
        var (path, _) = Split(link);

        if (path.Length == 0)
            return null;

        var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(guide)!, path));

        if (Directory.Exists(full))
            full = Path.Combine(full, "CLAUDE.md");

        return File.Exists(full) && full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Имена в обратных кавычках, которые можно сверить с кодом: идентификатор, файл и путь репозитория — целиком, а
    /// из выражения, атрибута, вызова с аргументами — слова с горбом: <c>StudioStateCollection</c> из
    /// <c>[Collection(StudioStateCollection.Name)]</c>, <c>SaveAsync</c> из <c>SaveAsync(live: false)</c>.
    /// </summary>
    private static IEnumerable<string> Names(string guide)
    {
        foreach (Match span in Span.Matches(Prose(guide)))
        {
            var code = span.Groups["code"].Value;

            if (Identifier.IsMatch(code) || FileName.IsMatch(code) || IsRepositoryPath(code))
            {
                yield return code;
                continue;
            }

            foreach (Match word in Word.Matches(code))
            {
                if (Hump.IsMatch(word.Value))
                    yield return word.Value;
            }
        }
    }

    private static bool IsRepositoryPath(string code) =>
        RepositoryRoots.Any(root => code.StartsWith(root, StringComparison.Ordinal))
        && code.IndexOfAny(['*', '<', '>', '{', '}', '…', ' ', '%']) < 0;

    /// <summary>Якоря заголовков, как их считает GitHub: строчные, без знаков, пробел — дефис.</summary>
    private static HashSet<string> Anchors(string file) =>
        Heading.Matches(Prose(file)).Select(match => Slug(match.Groups["text"].Value)).ToHashSet(StringComparer.Ordinal);

    private static string Slug(string heading)
    {
        var slug = new StringBuilder();

        foreach (var c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
                slug.Append(c);
            else if (c == ' ')
                slug.Append('-');
        }

        return slug.ToString();
    }

    private static (string Path, string Anchor) Split(string target)
    {
        var hash = target.IndexOf('#', StringComparison.Ordinal);

        return hash < 0
            ? (Uri.UnescapeDataString(target), string.Empty)
            : (Uri.UnescapeDataString(target[..hash]), Uri.UnescapeDataString(target[(hash + 1)..]));
    }

    /// <summary>Текст без блоков кода: имена и ссылки в них — пример, а не утверждение.</summary>
    private static string Prose(string file) =>
        Fence.Replace(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal), string.Empty);

    private static string Name(string file) => Path.GetRelativePath(Repository.Root, file).Replace('\\', '/');

    /// <summary>Файлы под папкой — мимо выхода сборок и служебного.</summary>
    private static IEnumerable<string> Files(string folder)
    {
        if (!Directory.Exists(folder))
            yield break;

        foreach (var file in Directory.EnumerateFiles(folder))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            if (Skipped.Contains(Path.GetFileName(child)))
                continue;

            foreach (var file in Files(child))
                yield return file;
        }
    }

    /// <summary>Код студии и её подмодулей: его слова, имена его файлов и названные им файлы.</summary>
    private sealed class Code
    {
        private readonly HashSet<string> _words = new(StringComparer.Ordinal);
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _mentioned = new(StringComparer.Ordinal);

        /// <summary>
        /// Читает код один раз и попутно ищет в его тексте файлы, которые инструкции называют, а в репозитории их
        /// нет: настройки, раскладку — то, что студия пишет сама.
        /// </summary>
        /// <param name="wanted">Имена файлов из инструкций.</param>
        public static Code Read(IEnumerable<string> wanted)
        {
            var code = new Code();
            var self = Path.GetFullPath(Repository.Path("tests", "ArxisStudio.Tests", $"{nameof(AgentInstructionsTests)}.cs"));
            var folders = new List<string> { Repository.Path("src"), Repository.Path("tests"), Repository.Path("templates") };

            folders.AddRange(Directory.EnumerateDirectories(Repository.Path("external")).Select(submodule => Path.Combine(submodule, "src")));

            var files = folders.SelectMany(Files)
                .Concat(Directory.EnumerateFiles(Repository.Root))
                .Where(path => CodeExtensions.Contains(Path.GetExtension(path)))
                .Where(path => !string.Equals(Path.GetFullPath(path), self, StringComparison.OrdinalIgnoreCase))
                .ToList();

            code._files.UnionWith(files.Select(Path.GetFileName).OfType<string>());

            var pending = wanted.Where(name => !code._files.Contains(name)).ToHashSet(StringComparer.Ordinal);

            foreach (var file in files)
            {
                var text = File.ReadAllText(file);

                foreach (Match word in Word.Matches(text))
                    code._words.Add(word.Value);

                foreach (var name in pending.Where(name => text.Contains(name, StringComparison.Ordinal)).ToList())
                {
                    code._mentioned.Add(name);
                    pending.Remove(name);
                }
            }

            return code;
        }

        /// <summary>Есть ли названное: путь — на диске, файл — в репозитории или назван кодом, имя — словами кода.</summary>
        public bool Knows(string name)
        {
            if (IsRepositoryPath(name))
            {
                return Path.Exists(Repository.Path(name))
                       || Directory.EnumerateDirectories(Repository.Path("external")).Any(submodule => Path.Exists(Path.Combine(submodule, name)));
            }

            if (FileName.IsMatch(name))
                return _files.Contains(name) || _mentioned.Contains(name);

            return name.TrimEnd('(', ')').Split('.').All(part => _words.Contains(part) || Foreign.Contains(part));
        }
    }
}
