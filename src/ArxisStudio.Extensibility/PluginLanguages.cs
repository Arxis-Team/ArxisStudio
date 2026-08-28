using System.Collections.Frozen;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Языки интерфейса, которые принесли установленные плагины.
/// </summary>
/// <remarks>
/// Языковой пакет — плагин без единой сборки: студия читает его манифест,
/// как читает любой другой, и берёт словарь файлом из его папки. Ничего
/// поднимать при этом не нужно, и события активации у пакета не объявлены —
/// он так и остаётся лежать на диске.
/// <para>
/// Раздача, установка, обновление, включение и удаление достаются пакету
/// даром: это тот же менеджер плагинов. Ради языка не заведено ни своего
/// каталога, ни своего формата архива, ни своего места на диске.
/// </para>
/// </remarks>
public sealed class PluginLanguages : ILanguageSource
{
    private readonly Dictionary<string, Declared> _declared = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrozenDictionary<string, string>> _read =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _problems = [];

    /// <summary>
    /// Собирает языки по установленным плагинам.
    /// </summary>
    /// <param name="plugins">Установленные плагины.</param>
    /// <remarks>
    /// Выключенный плагин языка не даёт: выключение — это способ убрать
    /// принесённое им, не удаляя его самого, и на язык оно обязано
    /// распространяться так же, как на панели и команды.
    /// </remarks>
    public PluginLanguages(IEnumerable<InstalledPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        foreach (var plugin in plugins.Where(candidate => candidate is { IsEnabled: true, IsValid: true }))
        {
            foreach (var declared in plugin.Manifest!.Contributions.Languages)
                Add(plugin, declared);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Codes => _declared.Keys;

    /// <summary>
    /// О чём стоит сказать человеку: занятый код, потерянный словарь.
    /// </summary>
    /// <remarks>
    /// Молчание здесь хуже всего: пакет установлен, языка в списке нет, и
    /// человеку неоткуда узнать, почему.
    /// </remarks>
    public IReadOnlyList<string> Problems => _problems;

    /// <inheritdoc/>
    /// <remarks>
    /// Название языка приходит из манифеста, если сам словарь себя не
    /// назвал: манифест студия прочитала и так, а открывать ради имени
    /// каждый словарь незачем.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Read(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);

        if (!_declared.TryGetValue(language, out var declared))
            return FrozenDictionary<string, string>.Empty;

        if (_read.TryGetValue(language, out var cached))
            return cached;

        var strings = StringFile.Read(declared.Path);

        if (!strings.ContainsKey(Localizer.NameKey) && declared.Name is { Length: > 0 } name)
            strings[Localizer.NameKey] = name;

        var frozen = strings.ToFrozenDictionary(StringComparer.Ordinal);

        _read[language] = frozen;
        return frozen;
    }

    private void Add(InstalledPlugin plugin, Sdk.Plugins.PluginLanguage declared)
    {
        if (declared.Code is not { Length: > 0 } code)
        {
            _problems.Add($"{plugin.DisplayName}: язык объявлен без кода");
            return;
        }

        // Занятый код — не выбор, а гонка: выиграл бы тот, чья папка
        // раньше попалась при обходе каталога.
        if (_declared.TryGetValue(code, out var taken))
        {
            _problems.Add($"{plugin.DisplayName}: язык {code} уже принёс {taken.PluginName}");
            return;
        }

        // Пакет, сделанный под словарь новее нашего, не отвергается:
        // лишние ключи студия просто не спросит, а недостающие возьмёт из
        // английского. Но сказать об этом стоит — иначе человек будет
        // гадать, почему переведено не всё.
        if (!StudioSdk.Satisfies(plugin.Manifest?.Sdk?.Min))
        {
            _problems.Add(
                $"{plugin.DisplayName}: пакет сделан под SDK {plugin.Manifest!.Sdk!.Min}, " +
                $"у этой студии {StudioSdk.Version} — часть строк может быть непереведена");
        }

        var path = Path.Combine(plugin.Directory, declared.File ?? string.Empty);

        if (declared.File is not { Length: > 0 } || !File.Exists(path))
        {
            _problems.Add($"{plugin.DisplayName}: словаря {declared.File} нет — язык {code} не предлагается");
            return;
        }

        _declared[code] = new Declared(plugin.Id, plugin.DisplayName, declared.Name, path);
    }

    private sealed record Declared(string PluginId, string PluginName, string Name, string Path);
}
