using System.Reflection;
using System.Runtime.Loader;

namespace ArxisStudio.Services;

/// <summary>
/// Папка встроенных модулей рядом со студией и дорога к ней для основного контекста загрузки.
/// </summary>
/// <remarks>
/// <para>
/// Сборка студии кладёт модули и то, что везут только они, в <c>Modules</c>, а не в корень:
/// корень — это студия и её платформа, у модулей свой угол. Основной контекст сам туда не
/// заглядывает — он ищет рядом с exe и в том, что перечислил файл зависимостей, — поэтому
/// дорогу ему показывают до первого обращения к модулю: <see cref="Attach"/> зовётся первой
/// строкой <c>Main</c>.
/// </para>
/// <para>
/// <b>Корень всегда впереди.</b> Событие <see cref="AssemblyLoadContext.Resolving"/> приходит,
/// только когда обычный поиск сборку не нашёл, поэтому копия общей сборки в папке модулей
/// подменить настоящую не может, и второго экземпляра того же типа не возникнет.
/// </para>
/// <para>
/// <b>Нативные библиотеки модулей остаются у корня</b>, в <c>runtimes/</c>: их находит сама
/// среда по файлу зависимостей приложения, откуда бы ни пришла управляемая сборка, которая
/// их зовёт. Сюда переезжают только управляемые сборки.
/// </para>
/// </remarks>
public static class StudioModuleFolder
{
    /// <summary>Имя папки модулей в выходе студии.</summary>
    public const string Name = "Modules";

    private static int _attached;

    /// <summary>Папка модулей рядом со студией.</summary>
    public static string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, Name);

    /// <summary>Показывает основному контексту дорогу к модулям; повторный вызов ничего не делает.</summary>
    public static void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) == 0)
            AssemblyLoadContext.Default.Resolving += OnResolving;
    }

    /// <summary>
    /// Файл сборки в папке модулей.
    /// </summary>
    /// <param name="folder">Папка модулей.</param>
    /// <param name="name">Какую сборку ищут.</param>
    /// <returns>Путь к файлу или null, если там такой нет.</returns>
    /// <remarks>
    /// Сборка ресурсов лежит в подпапке своей культуры — так же, как у корня. Имя, которое
    /// само похоже на путь, не ищется вовсе: из папки модулей дорога не должна уводить.
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

    private static Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name) =>
        Locate(Path, name) is { } path ? context.LoadFromAssemblyPath(path) : null;
}
