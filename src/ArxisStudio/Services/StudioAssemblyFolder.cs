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
/// <b>Правило поиска одно на обе папки.</b> Сборка ищется по простому имени, сборка ресурсов — в
/// подпапке своей культуры, а имя, похожее на путь, не ищется вовсе: из папки дорога уводить не
/// должна. Порядок решает, кто отвечает первым: платформа подключается раньше модулей, и общая
/// сборка приходит из <see cref="Library"/>, даже если копия её лежала бы в <see cref="Modules"/>.
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
    public static StudioAssemblyFolder Library { get; } = new("Lib");

    /// <summary>Встроенные модули и то, что везут только они.</summary>
    public static StudioAssemblyFolder Modules { get; } = new("Modules");

    private int _attached;

    private StudioAssemblyFolder(string name)
    {
        Name = name;
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, name);
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

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name) =>
        Locate(Path, name) is { } path ? context.LoadFromAssemblyPath(path) : null;
}
