using System.Reflection;
using System.Runtime.Loader;

namespace ArxisStudio.Services;

/// <summary>
/// Папка сборок рядом со студией и дорога к ней для основного контекста загрузки.
/// </summary>
/// <remarks>
/// <para>
/// У корня остаётся сама студия: exe, её сборка и два файла среды. Остальное разложено по двум
/// папкам — <see cref="Library"/> держит платформу, на которой студия стоит, а <see cref="Modules"/>
/// встроенные модули и то, что везут только они. Основной контекст загрузки сам туда не
/// заглядывает: он ищет рядом с exe и в том, что перечислил файл зависимостей. Дорогу ему
/// показывают первыми строками <c>Main</c>.
/// </para>
/// <para>
/// <b>Форм у папки две, правило поиска одно.</b> Платформа лежит плоско, а модули — каждый в своей
/// папке, той же формы, что у установленного плагина: манифест в корне, сборки в <c>bin</c>. Искать
/// от этого приходится не в одном месте, а в нескольких, но ищут в каждом одинаково — по простому
/// имени, сборку ресурсов в подпапке её культуры, а имя, похожее на путь, не ищут вовсе: из папки
/// дорога уводить не должна.
/// </para>
/// <para>
/// <b>Порядок решает, кто отвечает первым.</b> Платформа подключается раньше модулей, и общая сборка
/// приходит из <see cref="Library"/>, даже если копия её лежала бы у модуля. Между собой папки
/// модулей обходятся в порядковом порядке имён — не в порядке подъёма модулей: чтобы узнать его,
/// пришлось бы назвать <c>StudioModules</c>, а это загрузило бы все сборки модулей внутри резолвера,
/// до того как студия начала подниматься. Одноимённых сборок в двух папках не бывает — их запрещает
/// <c>AXL1002</c> при сборке, — и порядок здесь нужен на случай выхода, собранного не нами.
/// </para>
/// <para>
/// <b>Файл зависимостей по-прежнему называет сборки у корня.</b> Существование он не обещает:
/// список доверенных сборок — это пути, и хост их при старте не проверяет, а промах по ним
/// становится обычным событием <see cref="AssemblyLoadContext.Resolving"/>. На нём и держится
/// раскладка; измерено на игрушечном приложении, а не предположено.
/// </para>
/// <para>
/// <b>Нативные библиотеки остаются у корня</b>, в <c>runtimes/</c>: их находит сама среда по файлу
/// зависимостей приложения, откуда бы ни пришла управляемая сборка, которая их зовёт. Сюда
/// переезжают только управляемые.
/// </para>
/// </remarks>
internal sealed class StudioAssemblyFolder
{
    /// <summary>Платформа студии: Avalonia, оболочка, контролы, SDK, ядро модели проектов.</summary>
    public static StudioAssemblyFolder Library { get; } = new("lib", Shape.Flat);

    /// <summary>Встроенные модули и то, что везут только они.</summary>
    public static StudioAssemblyFolder Modules { get; } = new("modules", Shape.Folders);

    private readonly Lazy<string[]> _places;

    private int _attached;

    private StudioAssemblyFolder(string name, Shape shape)
    {
        Name = name;
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, name);

        _places = new Lazy<string[]>(() => Places(Path, shape));
    }

    /// <summary>Как разложена папка.</summary>
    private enum Shape
    {
        /// <summary>Сборки лежат в самой папке.</summary>
        Flat,

        /// <summary>Папка держит папки, у каждой сборки в <c>bin</c>, — форма установленного плагина.</summary>
        Folders,
    }

    /// <summary>Имя папки в выходе студии.</summary>
    public string Name { get; }

    /// <summary>Папка рядом со студией.</summary>
    public string Path { get; }

    /// <summary>Показывает основному контексту дорогу к папке; повторный вызов ничего не делает.</summary>
    public void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) == 0)
            AssemblyLoadContext.Default.Resolving += OnResolving;
    }

    /// <summary>
    /// Файл сборки в папке.
    /// </summary>
    /// <param name="folder">Где искать.</param>
    /// <param name="name">Какую сборку ищут.</param>
    /// <returns>Путь к файлу или null, если там такой нет.</returns>
    /// <remarks>
    /// Сборка ресурсов лежит в подпапке своей культуры — так же, как у корня. Имя, которое само
    /// похоже на путь, не ищется вовсе: из папки дорога не должна уводить.
    /// </remarks>
    internal static string? Locate(string folder, AssemblyName name)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentNullException.ThrowIfNull(name);

        if (name.Name is not { Length: > 0 } simple || simple.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            return null;

        var directory = string.IsNullOrEmpty(name.CultureName)
            ? folder
            : System.IO.Path.Combine(folder, name.CultureName);

        var path = System.IO.Path.Combine(directory, simple + ".dll");

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Места, где эта папка ищет сборки.
    /// </summary>
    /// <param name="root">Папка рядом со студией.</param>
    /// <param name="shape">Как она разложена.</param>
    /// <returns>Папки в том порядке, в каком их спрашивают; пустой набор — искать негде.</returns>
    /// <remarks>
    /// Открыто ради теста: обход считается один раз на процесс, и проверять его надо на своём дереве,
    /// а не на выходе студии.
    /// </remarks>
    internal static string[] Places(string root, bool folders)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);

        if (!folders)
            return [root];

        if (!Directory.Exists(root))
            return [];

        return
        [
            .. Directory.EnumerateDirectories(root)
                .Select(folder => System.IO.Path.Combine(folder, "bin"))
                .Where(Directory.Exists)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string[] Places(string root, Shape shape) => Places(root, shape == Shape.Folders);

    /// <summary>
    /// Основной контекст не нашёл сборку сам.
    /// </summary>
    /// <remarks>
    /// Обход папок считается один раз, при первом промахе: событие приходит на каждое имя, которого
    /// нет в файле зависимостей, — а папка при работе студии не меняется, её раскладывает сборка.
    /// Ленивое, а не при <see cref="Attach"/>: дорогу показывают из <c>Main</c>, и чтения диска там
    /// быть не должно.
    /// </remarks>
    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (var place in _places.Value)
        {
            if (Locate(place, name) is { } path)
                return context.LoadFromAssemblyPath(path);
        }

        return null;
    }
}
