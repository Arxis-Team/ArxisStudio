namespace ArxisStudio.Modules.Project.Model;

/// <summary>Что не так с новым именем.</summary>
internal enum NameProblem
{
    /// <summary>Имя годится.</summary>
    None,

    /// <summary>Имя то же, что было: переименовывать нечего.</summary>
    Unchanged,

    /// <summary>Имени нет.</summary>
    Empty,

    /// <summary>В имени знак, которого в имени файла быть не может.</summary>
    Invalid,

    /// <summary>Имя кончается точкой или пробелом — Windows их молча отрезает.</summary>
    Trailing,

    /// <summary>Имя устройства Windows: <c>CON</c>, <c>NUL</c>, <c>COM1</c> и соседи.</summary>
    Reserved,

    /// <summary>Такое имя в папке уже есть.</summary>
    Taken,
}

/// <summary>Ответ проверки имени.</summary>
/// <param name="Problem">Что не так.</param>
/// <param name="Subject">О чём сказать: знак, зарезервированное слово, занятое имя.</param>
internal readonly record struct NameCheck(NameProblem Problem, string? Subject = null)
{
    /// <summary>Имя годится.</summary>
    public bool IsFine => Problem == NameProblem.None;
}

/// <summary>
/// Переименование, как у Rider: новое имя вложенных, выделение в поле и проверка имени.
/// </summary>
/// <remarks>
/// <para>
/// Вложенный файл получает новое имя владельца, если его имя начинается с имени владельца:
/// <c>MainWindow.axaml.cs</c> при <c>MainWindow.axaml</c> → <c>Main.axaml</c> становится
/// <c>Main.axaml.cs</c>. Если начинается только с основы — имени без последнего расширения, — как
/// <c>Resources.Designer.cs</c> при <c>Resources.resx</c>, меняется основа. Иначе вложенный своё
/// имя сохраняет, а ссылку на владельца служба файлов перепишет сама.
/// </para>
/// <para>
/// Проверка отвечает раньше, чем имя уйдёт службе: занятое имя, знак, недопустимый в имени файла, и
/// имя устройства Windows человек видит под полем, пока печатает, а не отказом после. Имена
/// устройств запрещены на любой системе — решение, в котором лежит <c>con.cs</c>, не откроется у
/// соседа с Windows.
/// </para>
/// </remarks>
internal static class Renaming
{
    private static readonly char[] Forbidden = [.. Path.GetInvalidFileNameChars().Union(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])];

    private static readonly HashSet<string> Devices = new(
        ["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" })],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Имя без последнего расширения; у имени, начинающегося с точки, — всё имя.</summary>
    /// <param name="name">Имя.</param>
    public static string Stem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var dot = name.LastIndexOf('.');

        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>Сколько знаков от начала выделить в поле: у файла — основу, у папки — всё.</summary>
    /// <param name="name">Имя.</param>
    /// <param name="folder">Это папка.</param>
    public static int Selected(string name, bool folder) => folder ? name.Length : Stem(name).Length;

    /// <summary>Новое имя вложенного при переименовании владельца.</summary>
    /// <param name="owner">Имя владельца до.</param>
    /// <param name="renamed">Имя владельца после.</param>
    /// <param name="nested">Имя вложенного.</param>
    /// <returns>Новое имя; null — вложенный своё имя сохраняет.</returns>
    public static string? Companion(string owner, string renamed, string nested)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(renamed);
        ArgumentNullException.ThrowIfNull(nested);

        if (Continues(nested, owner))
            return renamed + nested[owner.Length..];

        var stem = Stem(owner);

        return stem.Length < owner.Length && Continues(nested, stem) ? Stem(renamed) + nested[stem.Length..] : null;
    }

    /// <summary>
    /// Что переименуется: сам узел и вложенные, чьё имя идёт за ним, — каждый со своим новым именем.
    /// </summary>
    /// <param name="root">Узел, который переименовывают.</param>
    /// <param name="renamed">Его новое имя.</param>
    /// <remarks>
    /// Вложенный за вложенным следует за своим владельцем, а не за корнем: у цепочки
    /// <c>A.xaml</c> → <c>A.xaml.cs</c> → <c>A.xaml.cs.map</c> каждое звено тянет следующее.
    /// </remarks>
    public static IReadOnlyList<(Node Node, string Name)> Plan(Node root, string renamed)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(renamed);

        var plan = new List<(Node Node, string Name)> { (root, renamed) };
        var names = new Dictionary<Node, (string Before, string After)> { [root] = (root.Name, renamed) };

        foreach (var node in EditSelection.Nested(root))
        {
            if (node.Parent is not { } parent || !names.TryGetValue(parent, out var owner))
                continue;

            if (Companion(owner.Before, owner.After, node.Name) is not { } name)
                continue;

            plan.Add((node, name));
            names[node] = (node.Name, name);
        }

        return plan;
    }

    /// <summary>Проверяет новое имя и имена, которые получат вложенные.</summary>
    /// <param name="typed">Что набрано.</param>
    /// <param name="root">Узел, который переименовывают.</param>
    /// <param name="exists">Есть ли на диске такой путь.</param>
    public static NameCheck Check(string? typed, Node root, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(exists);

        if (string.IsNullOrWhiteSpace(typed))
            return new NameCheck(NameProblem.Empty);

        if (string.Equals(typed, root.Name, StringComparison.Ordinal))
            return new NameCheck(NameProblem.Unchanged);

        if (typed.IndexOfAny(Forbidden) is var at and >= 0)
            return new NameCheck(NameProblem.Invalid, typed[at].ToString());

        if (typed is "." or "..")
            return new NameCheck(NameProblem.Invalid, typed);

        if (typed[^1] is '.' or ' ')
            return new NameCheck(NameProblem.Trailing);

        if (typed.Split('.')[0].TrimEnd() is var device && Devices.Contains(device))
            return new NameCheck(NameProblem.Reserved, device);

        var plan = Plan(root, typed);
        var leaving = plan.Select(step => step.Node.Path.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (node, name) in plan)
        {
            if (Path.GetDirectoryName(node.Path.Value) is not { } folder)
                continue;

            var target = Path.Combine(folder, name);

            // Своё место не занято: смена одного регистра и обмен именами внутри одной правки.
            if (!taken.Add(target) || (exists(target) && !leaving.Contains(target)))
                return new NameCheck(NameProblem.Taken, name);
        }

        return new NameCheck(NameProblem.None);
    }

    /// <summary>Имя идёт за другим: начинается с него и продолжается точкой.</summary>
    private static bool Continues(string name, string head) =>
        name.Length > head.Length && name[head.Length] == '.' && name.StartsWith(head, StringComparison.OrdinalIgnoreCase);
}
