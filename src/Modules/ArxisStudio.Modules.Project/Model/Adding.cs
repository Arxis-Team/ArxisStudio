using System.Text;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>Набранное в диалоге создания: каталоги по дороге и имя.</summary>
/// <param name="Directories">Каталоги от цели, которые лягут по дороге; пусто — имя без пути.</param>
/// <param name="Name">Имя — последний сегмент набранного.</param>
internal sealed record TypedName(IReadOnlyList<string> Directories, string Name)
{
    /// <summary>Каталог, в который ляжет пункт: цель и каталоги по дороге.</summary>
    /// <param name="folder">Цель — каталог, на котором позвали меню.</param>
    public CanonicalPath In(CanonicalPath folder) => Directories.Aggregate(folder, (at, segment) => at.Combine(segment));
}

/// <summary>Ветка меню «Добавить ▸»: пункты и ветки в порядке показа.</summary>
/// <param name="Title">Строка ветки; у корня — пусто.</param>
/// <param name="Owner">Расширение, чей пункт завёл ветку, — по нему ставится черта между группами.</param>
internal sealed record AddBranch(string Title, string Owner)
{
    /// <summary>Пункты и ветки — <see cref="StudioNewItem"/> или <see cref="AddBranch"/>.</summary>
    public List<object> Entries { get; } = [];

    /// <summary>Чей вход: у пункта — его расширение, у ветки — расширение, её заведшее.</summary>
    public static string OwnerOf(object entry) => entry switch
    {
        StudioNewItem item => item.Owner,
        AddBranch branch => branch.Owner,
        _ => string.Empty,
    };
}

/// <summary>
/// Создание по пункту «Добавить ▸»: меню, разбор набранного, проверка имени и переменные шаблона.
/// </summary>
/// <remarks>
/// <para>
/// <b>Меню.</b> Пункты идут, как их отдала служба: модули первыми, затем по идентификатору
/// расширения. Ветки сходятся по тексту — две «Образцы» от двух расширений становятся одной, — и
/// стоит сошедшаяся там, где её завёл первый. Черта ставится между соседями от разных расширений: у
/// Rider группы «Add» разделены так же, и чей пункт — видно без подписи.
/// </para>
/// <para>
/// <b>Путь в имени</b> — как у IntelliJ: <c>Models/Person</c> кладёт <c>Person</c> в каталог
/// <c>Models</c>, заводя его, если нужно. Путь раскладывает окно, а не служба создания: пространство
/// имён считается от каталога, и у двух зовущих оно не должно расходиться.
/// </para>
/// <para>
/// <b>Пространство имён</b> — как у Rider и Visual Studio: корневое пространство проекта и каталоги
/// от проекта через точку, каждый приведённый к идентификатору: знак, которого в имени нет, —
/// подчёркивание, цифра в начале — подчёркивание впереди. Точка в имени каталога остаётся точкой:
/// <c>Foo.Bar</c> — это два уровня, так их и пишут.
/// </para>
/// </remarks>
internal static class Adding
{
    private static readonly char[] Slashes = ['/', '\\'];

    /// <summary>Строит дерево меню из пунктов в порядке показа.</summary>
    /// <param name="items">Пункты, уже отобранные для проекта.</param>
    public static AddBranch Menu(IEnumerable<StudioNewItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var root = new AddBranch(string.Empty, string.Empty);

        foreach (var item in items)
        {
            var at = root;

            foreach (var segment in item.Menu)
            {
                var next = at.Entries.OfType<AddBranch>().FirstOrDefault(branch => string.Equals(branch.Title, segment, StringComparison.Ordinal));

                if (next is null)
                {
                    next = new AddBranch(segment, item.Owner);
                    at.Entries.Add(next);
                }

                at = next;
            }

            at.Entries.Add(item);
        }

        return root;
    }

    /// <summary>Раскладывает набранное на каталоги и имя.</summary>
    /// <param name="typed">Что набрано.</param>
    public static TypedName Split(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);

        var segments = typed.Split(Slashes);

        return new TypedName(segments[..^1], segments[^1]);
    }

    /// <summary>
    /// Проверяет набранное: каждый сегмент пути, имя типа там, где его просит пункт, и занятость всего,
    /// что пункт положит.
    /// </summary>
    /// <param name="typed">Что набрано.</param>
    /// <param name="item">Пункт.</param>
    /// <param name="folder">Цель — каталог, на котором позвали меню.</param>
    /// <param name="outputs">Что положит пункт под именем — пути от каталога, в который он ляжет.</param>
    /// <param name="isFile">Лежит ли по пути файл.</param>
    /// <param name="exists">Лежит ли по пути что-нибудь.</param>
    public static NameCheck Check(
        string? typed,
        StudioNewItem item,
        CanonicalPath folder,
        Func<string, IReadOnlyList<string>> outputs,
        Func<string, bool> isFile,
        Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(isFile);
        ArgumentNullException.ThrowIfNull(exists);

        if (string.IsNullOrWhiteSpace(typed))
            return new NameCheck(NameProblem.Empty);

        if (typed.IndexOfAny(Slashes) is var slash and >= 0 && !item.Nested)
            return new NameCheck(NameProblem.Invalid, typed[slash].ToString());

        var name = Split(typed);

        // Пустой сегмент — «a//b» или косая на краю — не имя каталога, а лишняя косая.
        if (name.Directories.Append(name.Name).Any(segment => segment.Length == 0))
            return new NameCheck(NameProblem.Invalid, "/");

        foreach (var segment in name.Directories.Append(name.Name))
        {
            if (FileNames.Check(segment) is { IsFine: false } wrong)
                return wrong;
        }

        if (item.NameRule == NewItemNameRule.Identifier && FileNames.Identifier(name.Name) is { IsFine: false } type)
            return type;

        var at = folder;

        foreach (var directory in name.Directories)
        {
            at = at.Combine(directory);

            // Каталог по дороге может уже быть — в него и ляжет; файл на его месте — занято.
            if (isFile(at.Value))
                return new NameCheck(NameProblem.Taken, directory);
        }

        var laid = outputs(name.Name) is { Count: > 0 } paths ? paths : [name.Name];

        foreach (var path in laid)
        {
            if (exists(Path.Combine(at.Value, path)))
                return new NameCheck(NameProblem.Taken, Path.GetFileName(path));
        }

        return new NameCheck(NameProblem.None);
    }

    /// <summary>Корневое пространство имён проекта: его свойство, а без него — имя проекта.</summary>
    /// <param name="project">Проект.</param>
    public static string RootNamespace(ProjectSnapshot project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var root = project.Properties.GetValueOrDefault("RootNamespace") is { Length: > 0 } declared ? declared : project.Name;

        return Dotted(root.Split('.'));
    }

    /// <summary>Пространство имён каталога: корневое и каталоги от проекта.</summary>
    /// <param name="root">Корневое пространство имён проекта.</param>
    /// <param name="project">Каталог проекта.</param>
    /// <param name="directory">Каталог, в который ляжет пункт.</param>
    /// <remarks>Каталог вне проекта берёт корневое: считать пространство от чужого места не от чего.</remarks>
    public static string Namespace(string root, CanonicalPath project, CanonicalPath directory)
    {
        ArgumentNullException.ThrowIfNull(root);

        var relative = Path.GetRelativePath(project.Value, directory.Value);

        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return root;

        // Корень режется по точкам, как и каталоги: точка в нём — уровень, а не знак, который
        // приводят к подчёркиванию.
        return Dotted([.. root.Split('.'), .. relative.Split(Slashes, StringSplitOptions.RemoveEmptyEntries).SelectMany(segment => segment.Split('.'))]);
    }

    /// <summary>Часть пространства имён: знак не из имени — подчёркивание, цифра впереди — подчёркивание перед ней.</summary>
    /// <param name="part">Часть — имя каталога между точками.</param>
    public static string Identifier(string part)
    {
        ArgumentNullException.ThrowIfNull(part);

        var built = new StringBuilder(part.Length + 1);

        foreach (var symbol in part)
            built.Append(char.IsLetterOrDigit(symbol) || symbol == '_' ? symbol : '_');

        if (built.Length == 0 || char.IsDigit(built[0]))
            built.Insert(0, '_');

        return built.ToString();
    }

    private static string Dotted(IEnumerable<string> parts) =>
        string.Join('.', parts.Where(part => part.Length > 0).Select(Identifier));
}
