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
/// Всё, что приходит из манифеста, здесь проверяется до того, как коснётся
/// общего контекста: путь обязан остаться внутри папки плагина, файл — быть
/// сборкой, имя — не совпадать с общими сборками студии и не спорить с чужим
/// контрактом. Из общего контекста ничего не выгрузить, поэтому ошибка,
/// пропущенная сюда, живёт до перезапуска.
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

    private static readonly Lock Gate = new();

    private static bool _swept;

    /// <summary>
    /// Загружает контракты плагина в общий контекст, если ещё не загружены.
    /// </summary>
    /// <param name="plugin">Чьи контракты.</param>
    /// <param name="notes">Куда писать о неожиданном, не отказывая.</param>
    /// <returns>Причина отказа или null, если всё загрузилось.</returns>
    /// <remarks>
    /// Любая беда с объявленным контрактом — отказ владельцу, а не исключение
    /// наружу: контракты грузятся раньше всех подъёмов, и брошенное отсюда
    /// исключение унесло бы с собой загрузку всех плагинов и модулей сразу.
    /// Правило то же, что у entry-сборки: сломанный плагин становится записью
    /// с ошибкой, а не падением студии.
    /// </remarks>
    public static string? EnsureLoaded(InstalledPlugin plugin, ICollection<string> notes)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(notes);

        // Сперва проверяются все объявленные, и только потом грузится хоть
        // один. Иначе беда во втором контракте оставляла бы первый в общем
        // контексте навсегда — у плагина, который так и не поднялся: автор
        // чинит манифест, пересобирает, а студия отвечает ему «держу прежнюю
        // копию до перезапуска» про сборку, ни разу не работавшую.
        var declared = new List<(string Name, AssemblyName Identity, FileInfo File)>();

        foreach (var path in plugin.Manifest?.Provides?.Contracts ?? [])
        {
            if (path is not { Length: > 0 })
                continue;

            if (Examine(plugin, path, out var checked_) is { } refusal)
                return refusal;

            declared.Add(checked_);
        }

        foreach (var (name, identity, file) in declared)
        {
            if (Claim(name, identity, file, plugin, notes) is { } refusal)
                return refusal;
        }

        return null;
    }

    /// <summary>
    /// Проверяет один объявленный контракт, ничего не загружая.
    /// </summary>
    /// <returns>Причина отказа или null, если контракт годен.</returns>
    private static string? Examine(
        InstalledPlugin plugin, string declared, out (string, AssemblyName, FileInfo) checked_)
    {
        checked_ = default;

        if (Inside(plugin.Directory, declared) is not { } path)
            return $"контракт уводит за пределы папки плагина: {declared}";

        if (!File.Exists(path))
            return $"объявленный контракт не найден: {declared}";

        // Имя берётся из самой сборки, а не из имени файла: резолвер
        // спрашивает контракт по имени сборки, и файл, названный иначе,
        // молча не нашёлся бы — тип раскололся бы ровно там, где контракты
        // его и сращивают. Заодно это первая проверка, что файл вообще
        // сборка: манифест читается, ничего не загружая в процесс.
        AssemblyName identity;

        try
        {
            identity = AssemblyName.GetAssemblyName(path);
        }
        catch (Exception e) when (e is BadImageFormatException or FileLoadException
            or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"контракт не читается как сборка: {declared} — {e.Message}";
        }

        if (identity.Name is not { Length: > 0 } name)
            return $"у контракта нет имени сборки: {declared}";

        // Общие сборки студии под контракт не отдаются. Резолвер спрашивает
        // контракт раньше всего остального, и файл плагина, назвавшийся
        // Avalonia.Controls, достался бы вместо настоящего и студии, и всем
        // соседям — без возможности это отменить.
        if (PluginLoadContext.IsShared(name))
            return $"имя {name} занято общими сборками студии";

        checked_ = (name, identity, new FileInfo(path));
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
    public static Assembly? Find(AssemblyName assemblyName)
    {
        // Контрактов нет у подавляющего большинства установок, а спрашивают
        // отсюда на каждой сборке каждого плагина: пустой реестр обязан
        // отвечать даром, не считая хеш имени.
        if (Loaded.IsEmpty)
            return null;

        return assemblyName.Name is { Length: > 0 } name && Loaded.TryGetValue(name, out var known)
            ? known.Assembly
            : null;
    }

    /// <summary>
    /// Занимает имя контракта за плагином либо объясняет, почему не вышло.
    /// </summary>
    /// <remarks>
    /// Под замком: два хоста в одном процессе — обычное дело в тестах, а
    /// «посмотрели и добавили» двумя действиями дало бы двум потокам
    /// загрузить одно имя дважды, и второй <c>LoadFromAssemblyPath</c> упал бы
    /// на чужой уже занятой идентичности.
    /// </remarks>
    private static string? Claim(
        string name, AssemblyName identity, FileInfo file, InstalledPlugin plugin, ICollection<string> notes)
    {
        lock (Gate)
        {
            if (Loaded.TryGetValue(name, out var known))
            {
                // Тот же контракт у второго плагина — не беда: обе стороны
                // ссылаются на одну сборку, делить им нечего. А вот чужая
                // сборка под занятым именем — беда: она досталась бы всем
                // контекстам вместо своей, и виновника потом не найти.
                if (!string.Equals(known.Identity, identity.FullName, StringComparison.Ordinal))
                {
                    return $"имя {name} уже занято контрактом " +
                           $"плагина {known.OwnerId} — {known.Identity}";
                }

                // Прежняя копия остаётся: выгрузить её из общего контекста
                // нечем. Изменившийся файл — повод сказать, а не повод
                // молча притвориться, что новые типы уже видны.
                if (known.Length != file.Length || known.Written != file.LastWriteTimeUtc)
                {
                    notes.Add(
                        $"{plugin.DisplayName}: контракт {name} изменился на диске — " +
                        "студия держит прежнюю копию до перезапуска");
                }

                return null;
            }

            try
            {
                Loaded[name] = Load(name, identity, file, plugin.Id);
                return null;
            }
            catch (Exception e) when (e is BadImageFormatException or FileLoadException
                or IOException or UnauthorizedAccessException)
            {
                return $"контракт {name} не загрузился: {e.Message}";
            }
        }
    }

    private static Known Load(string name, AssemblyName identity, FileInfo file, string ownerId)
    {
        // Сборка с этой идентичностью может уже жить в общем контексте: её
        // загрузил тот, кто встраивает студию, или тестовый прогон своей
        // ссылкой. Тогда контракт — она: вторая копия того же имени в одном
        // контексте невозможна, да и не нужна.
        if (AssemblyLoadContext.Default.Assemblies
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            is { } adopted)
        {
            // Записывается идентичность усыновлённой сборки, а не файла с
            // диска: реестр обязан описывать то, что он раздаёт. Иначе
            // следующий плагин сверялся бы с версией, которой у него на руках
            // нет, и получал бы отказ или пропуск не по делу.
            return new Known(adopted, adopted.GetName().FullName, file.Length, file.LastWriteTimeUtc, ownerId);
        }

        // Грузится теневая копия, а не сам файл: общий контекст держит файл
        // открытым до конца процесса, и без копии автор не смог бы
        // пересобрать плагин, не закрыв студию, а тест — прибрать за собой
        // временную папку.
        var shadow = Path.Combine(ShadowRoot, $"{name}-{Guid.NewGuid():N}.dll");

        File.Copy(file.FullName, shadow);

        return new Known(
            AssemblyLoadContext.Default.LoadFromAssemblyPath(shadow),
            identity.FullName,
            file.Length,
            file.LastWriteTimeUtc,
            ownerId);
    }

    /// <summary>
    /// Полный путь к объявленному контракту либо null, если он уводит наружу.
    /// </summary>
    /// <remarks>
    /// <see cref="Path.Combine(string, string)"/> отбрасывает папку плагина
    /// целиком, если объявленный путь абсолютный, а «..» уводит куда угодно.
    /// Контракт грузится в общий контекст навсегда и достаётся всем — пускать
    /// туда файл со стороны нельзя. Та же проверка стоит на распаковке архива.
    /// </remarks>
    private static string? Inside(string directory, string declared)
    {
        string root, full;

        try
        {
            root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            full = Path.GetFullPath(Path.Combine(directory, declared));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
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

            Directory.CreateDirectory(root);

            // Флаг ставится последним: выставь мы его раньше, второй вызов
            // получил бы дорогу к папке, которую ещё не создали, а сорвавшееся
            // создание запомнилось бы как успешное на весь процесс.
            _swept = true;

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

    private readonly record struct Known(
        Assembly Assembly, string Identity, long Length, DateTime Written, string OwnerId);
}
