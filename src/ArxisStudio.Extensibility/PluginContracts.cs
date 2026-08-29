using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Контрактные сборки плагинов: типы, которые обязаны быть одними на всех.
/// </summary>
/// <remarks>
/// Тип в .NET — это имя внутри сборки, загруженной в конкретный контекст. У
/// каждого плагина контекст свой, и интерфейс, загруженный дважды, — это два
/// разных типа с одним именем: приведение падает с бессмысленным «IFoo не
/// приводится к IFoo». Поэтому сборка, объявленная контрактом, загружается
/// один раз в общий контекст — как SDK и контролы — и раздаётся всем
/// контекстам плагинов по имени.
/// <para>
/// Цена решения: контракт не выгружается до конца процесса. Обновление
/// плагина, изменившее контракт, требует перезапуска студии — и об этом
/// говорится словами, а не делается вид, что перезагрузка полная.
/// </para>
/// <para>
/// Реестр на процесс, а не на хост: сборки в общем контексте живут с
/// процессом, и второй хост — а в тестах их десятки — обязан видеть уже
/// загруженное, а не пытаться загрузить то же имя второй раз.
/// </para>
/// </remarks>
public static class PluginContracts
{
    private static readonly ConcurrentDictionary<string, Known> Loaded =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _swept;

    /// <summary>
    /// Загружает контракты плагина в общий контекст, если ещё не загружены.
    /// </summary>
    /// <param name="plugin">Чьи контракты.</param>
    /// <param name="notes">Куда писать о неожиданном, не отказывая.</param>
    /// <returns>Причина отказа или null, если всё загрузилось.</returns>
    /// <remarks>
    /// Объявленный и отсутствующий файлом контракт — отказ владельцу: это
    /// обещание манифеста, и зависимые на него рассчитывают. Изменившийся на
    /// диске файл — не отказ, а заметка: студия держит прежнюю копию до
    /// перезапуска, и молчать об этом нельзя.
    /// </remarks>
    public static string? EnsureLoaded(InstalledPlugin plugin, ICollection<string> notes)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(notes);

        foreach (var declared in plugin.Manifest?.Provides?.Contracts ?? [])
        {
            if (declared is not { Length: > 0 })
                continue;

            var path = Path.Combine(plugin.Directory, declared);

            if (!File.Exists(path))
                return $"{plugin.DisplayName}: объявленный контракт не найден: {declared}";

            var name = Path.GetFileNameWithoutExtension(path);
            var file = new FileInfo(path);

            if (Loaded.TryGetValue(name, out var known))
            {
                // Прежняя копия остаётся: выгрузить её из общего контекста
                // нечем. Изменившийся файл — повод сказать, а не повод
                // молча притвориться, что новые типы уже видны.
                if (known.Length != file.Length || known.Written != file.LastWriteTimeUtc)
                {
                    notes.Add(
                        $"{plugin.DisplayName}: контракт {name} изменился на диске — " +
                        "студия держит прежнюю копию до перезапуска");
                }

                continue;
            }

            Loaded.TryAdd(name, Load(name, file));
        }

        return null;
    }

    /// <summary>
    /// Контрактная сборка по имени; null — такого контракта нет.
    /// </summary>
    /// <param name="assemblyName">Имя, которое просит контекст плагина.</param>
    /// <remarks>
    /// Отсюда контексты плагинов берут контракт вместо собственной копии:
    /// даже если файл с тем же именем лежит в их <c>bin/</c> — автор забыл
    /// исключить, — тип обязан остаться одним на всех.
    /// </remarks>
    public static Assembly? Find(AssemblyName assemblyName) =>
        assemblyName.Name is { Length: > 0 } name && Loaded.TryGetValue(name, out var known)
            ? known.Assembly
            : null;

    private static Known Load(string name, FileInfo file)
    {
        // Сборка с этим именем может уже жить в общем контексте: её загрузил
        // тот, кто встраивает студию, или тестовый прогон своей ссылкой.
        // Тогда контракт — она: вторая копия того же имени в одном контексте
        // невозможна, да и не нужна.
        if (AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            is { } adopted)
        {
            return new Known(adopted, file.Length, file.LastWriteTimeUtc);
        }

        // Грузится теневая копия, а не сам файл: общий контекст держит файл
        // открытым до конца процесса, и без копии автор не смог бы
        // пересобрать плагин, не закрыв студию, а тест — прибрать за собой
        // временную папку.
        var shadow = Path.Combine(ShadowRoot, $"{name}-{Guid.NewGuid():N}.dll");

        File.Copy(file.FullName, shadow);

        return new Known(
            AssemblyLoadContext.Default.LoadFromAssemblyPath(shadow),
            file.Length,
            file.LastWriteTimeUtc);
    }

    /// <summary>
    /// Папка теневых копий контрактов; при первом обращении за процесс
    /// выметаются копии прежних запусков.
    /// </summary>
    private static string ShadowRoot
    {
        get
        {
            var root = Path.Combine(Path.GetTempPath(), "arxis-contract-shadow");

            if (_swept)
                return root;

            _swept = true;
            Directory.CreateDirectory(root);

            foreach (var stale in Directory.EnumerateFiles(root))
            {
                try
                {
                    File.Delete(stale);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Файл держит другая студия — её контракт, её право.
                }
            }

            return root;
        }
    }

    private readonly record struct Known(Assembly Assembly, long Length, DateTime Written);
}
