using System.IO.Compression;
using System.Text.Json;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Каталог установленных плагинов: одна папка на плагин, манифест внутри.
/// </summary>
/// <remarks>
/// Каталог читает манифесты, хранит состояние «включён / выключен», ставит и снимает плагины.
/// Сборок он не грузит: это дело <see cref="PluginHost"/>, который работает поверх того же списка.
/// <para>
/// Отказ файловой системы здесь — ответ, а не исключение. Зовут каталог обработчики окна
/// настроек, и брошенное оттуда <see cref="IOException"/> доезжает до диспетчера как дефект самой
/// студии: занятый файл или папка только для чтения роняли бы её на нажатии «Сохранить». Поэтому
/// всё, что пишет на диск, возвращает слово о том, почему не вышло.
/// </para>
/// </remarks>
public sealed class PluginCatalog
{
    private readonly string _root;
    private readonly HashSet<string> _disabled;
    private readonly string _stateFile;

    /// <summary>Создаёт каталог над папкой плагинов.</summary>
    /// <param name="root">Папка плагинов; по умолчанию — <see cref="StudioPaths.Plugins"/>.</param>
    public PluginCatalog(string? root = null)
    {
        _root = root ?? StudioPaths.Plugins;
        _stateFile = Path.Combine(_root, ".disabled.json");
        _disabled = LoadDisabled(_stateFile);
    }

    /// <summary>Папка, в которой каталог ищет плагины.</summary>
    public string Root => _root;

    /// <summary>
    /// Перечитывает папку плагинов. Каждая подпапка с манифестом даёт запись;
    /// подпапка без манифеста игнорируется — там просто нет плагина.
    /// </summary>
    public IReadOnlyList<InstalledPlugin> Scan()
    {
        if (!Directory.Exists(_root))
            return [];

        var found = new List<InstalledPlugin>();

        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            var manifestPath = Path.Combine(directory, "plugin.json");
            if (!File.Exists(manifestPath))
                continue;

            found.Add(Read(directory, manifestPath));
        }

        return found
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Включает или выключает плагин; состояние переживает перезапуск.</summary>
    /// <param name="id">Идентификатор плагина.</param>
    /// <param name="enabled">Включить или выключить.</param>
    /// <returns><c>null</c>, если состояние записано; иначе — почему не записалось.</returns>
    /// <remarks>
    /// Не записалось — значит, и в памяти не поменялось: каталог, который помнит одно, а на диске
    /// держит другое, после перезапуска молча вернул бы прежнее, и человек не узнал бы, какое из
    /// двух состояний настоящее.
    /// </remarks>
    public string? SetEnabled(string id, bool enabled)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        if (!(enabled ? _disabled.Remove(id) : _disabled.Add(id)))
            return null;

        if (SaveDisabled() is not { } error)
            return null;

        // Откат: правка в памяти без файла не пережила бы перезапуск.
        if (enabled)
            _disabled.Add(id);
        else
            _disabled.Remove(id);

        return error;
    }

    /// <summary>
    /// Устанавливает плагин копированием папки в каталог. Возвращает
    /// установленный плагин или сообщение, почему установка не состоялась.
    /// </summary>
    /// <param name="sourceDirectory">Папка с <c>plugin.json</c>.</param>
    /// <param name="replace">
    /// Заменять ли уже установленный плагин с тем же идентификатором. По
    /// умолчанию нет: тот, кто ставит второй раз, чаще ошибся, чем обновляет, —
    /// и молча стереть установленное было бы для него неожиданностью.
    /// </param>
    /// <remarks>
    /// Замена — это удаление и установка заново, а не наложение: каталог
    /// плагина после установки неизменяем, и оставить в нём файл от прошлой
    /// версии значит поставить плагин, которого не собирал никто. Пометка
    /// «выключен» замену переживает: человек выключил этот плагин, а не эту его
    /// версию.
    /// </remarks>
    public (InstalledPlugin? Plugin, string? Error) InstallFromDirectory(
        string sourceDirectory,
        bool replace = false)
    {
        var manifestPath = Path.Combine(sourceDirectory, "plugin.json");
        if (!File.Exists(manifestPath))
            return (null, $"В каталоге нет plugin.json: {sourceDirectory}");

        var probe = Read(sourceDirectory, manifestPath);
        if (probe.Manifest is not { } manifest)
            return (null, probe.Error);

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return (null, "В манифесте не указан id плагина");

        // Идентификатор становится именем папки, а манифест пришёл из чужих рук. Без этой
        // проверки «..» или полный путь уводили цель за пределы папки плагинов, и замена
        // стирала рекурсивно то, что там лежало, — вплоть до всей папки данных студии.
        if (!PluginPaths.IsFolderName(manifest.Id) || PluginPaths.Inside(_root, manifest.Id) is not { } target)
        {
            return (null,
                $"Идентификатор «{manifest.Id}» не годится именем каталога плагина: " +
                "допустимы латинские буквы, цифры, точка, дефис и подчёркивание");
        }

        if (Directory.Exists(target))
        {
            if (!replace)
                return (null, $"Плагин {manifest.Id} уже установлен");

            if (Remove(target) is { } busy)
                return (null, busy);
        }

        try
        {
            CopyDirectory(sourceDirectory, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Недокопированная папка — это плагин, которого не собирал никто: манифест в ней
            // может уже лежать, а сборки ещё нет. Убираем, что успели, и отвечаем словами.
            Remove(target);

            return (null, $"Плагин {manifest.Id} не скопировался: {e.Message}");
        }

        return (Read(target, Path.Combine(target, "plugin.json")), null);
    }

    /// <summary>
    /// Устанавливает плагин из архива <c>.axplugin</c> — это обычный zip с той
    /// же папкой внутри.
    /// </summary>
    /// <param name="archivePath">Путь к архиву.</param>
    /// <returns>Установленный плагин или сообщение, почему установка не состоялась.</returns>
    /// <remarks>
    /// Архив распаковывается во временную папку и оттуда устанавливается тем же
    /// путём, что и папка: проверка манифеста, занятости идентификатора и
    /// копирование — одни на оба способа, и расходиться им незачем.
    /// <para>
    /// Записи, ведущие за пределы папки назначения, отбрасываются: архив —
    /// файл из чужих рук, и путь вида <c>../../</c> в нём означает не установку,
    /// а запись куда попало.
    /// </para>
    /// </remarks>
    /// <param name="replace">Заменять ли уже установленный плагин с тем же идентификатором.</param>
    public (InstalledPlugin? Plugin, string? Error) InstallFromArchive(string archivePath, bool replace = false)
    {
        if (!File.Exists(archivePath))
            return (null, $"Архив не найден: {archivePath}");

        var staging = Path.Combine(Path.GetTempPath(), "arxis-plugin-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(staging);

            if (Unpack(archivePath, staging) is { } unpackError)
                return (null, unpackError);

            // Архив мог быть собран как «папка внутри архива» — тогда манифест
            // лежит на уровень глубже, и устанавливать надо именно её.
            var source = File.Exists(Path.Combine(staging, "plugin.json"))
                ? staging
                : Directory.GetDirectories(staging).FirstOrDefault(directory =>
                    File.Exists(Path.Combine(directory, "plugin.json")));

            return source is null
                ? (null, "В архиве нет plugin.json")
                : InstallFromDirectory(source, replace);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static string? Unpack(string archivePath, string destination)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            foreach (var entry in archive.Entries)
            {
                if (PluginPaths.Inside(destination, entry.FullName) is not { } target)
                    continue;

                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            return null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return $"Архив не читается: {e.Message}";
        }
    }

    /// <summary>
    /// Удаляет плагин вместе с его папкой.
    /// </summary>
    /// <param name="plugin">Установленный плагин.</param>
    /// <returns>null, если удалён, иначе — почему не вышло.</returns>
    /// <remarks>
    /// Папку может держать запущенная студия: пока плагин поднят, его сборки
    /// открыты, и файл не удалить. Это не исключение, а обычный ответ, который
    /// человеку надо показать словами — иначе нажатие на «Удалить» просто
    /// ничего не делает.
    /// <para>
    /// Пометка «выключен» снимается вместе с плагином: она живёт в общем файле
    /// рядом с папками, и, оставшись там, выключила бы плагин, поставленный
    /// заново, — а причины этого человек уже не вспомнит.
    /// </para>
    /// </remarks>
    public string? Uninstall(InstalledPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (Remove(plugin.Directory) is { } error)
            return error;

        // Пометка снимается молча: плагина уже нет, и отказ записать её — не отказ удалить.
        // Осиротевшая строка в файле ничего не выключит, пока плагин с этим именем не поставят
        // заново, — а тогда её видно в менеджере обычной галочкой.
        if (_disabled.Remove(plugin.Id))
            SaveDisabled();

        return null;
    }

    private static string? Remove(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        try
        {
            Directory.Delete(directory, recursive: true);

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Каталог плагина занят: {e.Message}. Закройте студию, если она открыта, и повторите.";
        }
    }

    private InstalledPlugin Read(string directory, string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(manifestPath), ManifestFormat.Options);

            return manifest is null
                ? new InstalledPlugin(directory, null, "Пустой манифест", false)
                : new InstalledPlugin(directory, manifest, null, !_disabled.Contains(manifest.Id));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new InstalledPlugin(directory, null, e.Message, false);
        }
    }

    private static HashSet<string> LoadDisabled(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path))
                       ?? new HashSet<string>(StringComparer.Ordinal);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Потерянное состояние означает лишь, что плагины снова включены.
        }

        return new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Записывает список выключенных.</summary>
    /// <returns><c>null</c>, если записан; иначе — почему не вышло.</returns>
    private string? SaveDisabled()
    {
        try
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_disabled));

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Состояние плагинов не записалось ({_stateFile}): {e.Message}";
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
