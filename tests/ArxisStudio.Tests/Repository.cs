using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Репозиторий, из которого собраны тесты: его корень и пути под ним.
/// </summary>
/// <remarks>
/// Тесты бегут из своей выходной папки, а сверяют файлы, лежащие выше, — примеры, шаблоны,
/// манифесты, записи поверхностей. Корень узнаётся по решению: папка с ним одна, а путь от неё до
/// выхода тестов зависит от конфигурации и платформы. Двадцать пять наборов искали его сами,
/// каждый своим признаком, и расходились в том, чем кончается ненайденное.
/// </remarks>
internal static class Repository
{
    /// <summary>Корень репозитория — папка с <c>ArxisStudio.slnx</c>.</summary>
    public static string Root { get; } = Find();

    /// <summary>Путь под корнем.</summary>
    /// <param name="parts">Части пути от корня.</param>
    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary>Файл под корнем, который обязан быть: его кладёт сборка или он лежит в репозитории.</summary>
    /// <param name="parts">Части пути от корня.</param>
    public static string File(params string[] parts)
    {
        var path = Path(parts);

        Assert.True(System.IO.File.Exists(path), $"нет файла {string.Join('/', parts)}");

        return path;
    }

    /// <summary>Выход проекта той же конфигурации и платформы, что у тестов.</summary>
    /// <param name="project">Части пути от корня до папки проекта.</param>
    /// <remarks>
    /// Конфигурация берётся из пути самих тестов: прогон бывает и Debug, и Release, а сверять
    /// Release-тесты с Debug-студией значит сверять с тем, чего в этом прогоне не собирали.
    /// </remarks>
    public static string Output(params string[] project)
    {
        var tests = new DirectoryInfo(AppContext.BaseDirectory);

        return System.IO.Path.Combine([Root, .. project, "bin", tests.Parent!.Name, tests.Name]);
    }

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "ArxisStudio.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Не найден корень репозитория: выше папки тестов нет ArxisStudio.slnx");
    }
}
