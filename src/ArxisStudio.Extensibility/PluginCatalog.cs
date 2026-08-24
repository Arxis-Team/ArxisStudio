using System.IO.Compression;
using System.Text.Json;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Каталог установленных плагинов: одна папка на плагин, манифест внутри.
/// Каталог только читает манифесты и хранит состояние «включён / выключен» —
/// загрузка сборок в collectible-контексты придёт в M7 и встанет поверх этого же
/// списка, ничего в нём не меняя.
/// </summary>
public sealed class PluginCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

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
    public void SetEnabled(string id, bool enabled)
    {
        if (enabled ? _disabled.Remove(id) : _disabled.Add(id))
            SaveDisabled();
    }

    /// <summary>
    /// Устанавливает плагин копированием папки в каталог. Возвращает
    /// установленный плагин или сообщение, почему установка не состоялась.
    /// </summary>
    /// <param name="sourceDirectory">Папка с <c>plugin.json</c>.</param>
    public (InstalledPlugin? Plugin, string? Error) InstallFromDirectory(string sourceDirectory)
    {
        var manifestPath = Path.Combine(sourceDirectory, "plugin.json");
        if (!File.Exists(manifestPath))
            return (null, $"В папке нет plugin.json: {sourceDirectory}");

        var probe = Read(sourceDirectory, manifestPath);
        if (probe.Manifest is not { } manifest)
            return (null, probe.Error);

        if (string.IsNullOrWhiteSpace(manifest.Id))
            return (null, "В манифесте не указан id плагина");

        var target = Path.Combine(_root, manifest.Id);
        if (Directory.Exists(target))
            return (null, $"Плагин {manifest.Id} уже установлен");

        CopyDirectory(sourceDirectory, target);
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
    public (InstalledPlugin? Plugin, string? Error) InstallFromArchive(string archivePath)
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
                : InstallFromDirectory(source);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static string? Unpack(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);

            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));

                if (!target.StartsWith(root, StringComparison.Ordinal))
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

    /// <summary>Удаляет плагин вместе с его папкой.</summary>
    /// <param name="plugin">Установленный плагин.</param>
    public void Uninstall(InstalledPlugin plugin)
    {
        if (Directory.Exists(plugin.Directory))
            Directory.Delete(plugin.Directory, recursive: true);

        if (_disabled.Remove(plugin.Id))
            SaveDisabled();
    }

    private InstalledPlugin Read(string directory, string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(
                File.ReadAllText(manifestPath), Options);

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

    private void SaveDisabled()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_stateFile, JsonSerializer.Serialize(_disabled));
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
