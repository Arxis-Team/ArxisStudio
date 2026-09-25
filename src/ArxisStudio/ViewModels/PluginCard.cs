using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.ViewModels;

/// <summary>Строка сведений о плагине: что это и какое оно.</summary>
/// <param name="Label">Подпись: «Панели», «Требует SDK».</param>
/// <param name="Value">Значение, готовое к показу.</param>
public sealed record PluginFact(string Label, string Value);

/// <summary>
/// Строка списка плагинов: запись каталога, состояние её зависимостей и
/// галочка «включён», которую человек только что поставил.
/// </summary>
/// <remarks>
/// Состояние зависимостей считается при сборке списка, а не в привязке: ему
/// нужны все установленные разом — цели ищутся среди соседей и модулей.
/// <para>
/// Галочка живёт здесь, а не в записи каталога: до «Сохранить» она ничья, и
/// запись с диска знать о ней не должна. Сравнение с записью и есть ответ на
/// вопрос «есть ли несохранённое».
/// </para>
/// <para>
/// Строка несёт и подробности — то, что панель справа от списка показывает о выбранном
/// плагине: что он добавляет в студию, кому он нужен, какой SDK требует и где лежит. Всё это
/// читается из манифеста, не загружая сборки, — у спящего плагина подробности те же, что у
/// поднятого.
/// </para>
/// </remarks>
public sealed class PluginCard : INotifyPropertyChanged, IPluginRow
{
    private bool _on;
    private bool _shown = true;
    private IReadOnlyList<string> _dependents = [];

    /// <summary>Собирает строку поверх записи каталога.</summary>
    /// <param name="plugin">Запись каталога.</param>
    /// <param name="dependencies">Зависимости с состоянием каждой цели.</param>
    /// <param name="riseError">Почему плагин не поднялся на этом запуске; null — поднялся или не пробовал.</param>
    /// <param name="needsRestart">Изменения плагина применит только перезапуск студии.</param>
    public PluginCard(
        InstalledPlugin plugin,
        IReadOnlyList<PluginDependencyState> dependencies,
        string? riseError = null,
        bool needsRestart = false)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        Plugin = plugin;
        Dependencies = dependencies;
        RiseError = riseError;
        NeedsRestart = needsRestart;
        _on = plugin.IsEnabled;
    }

    /// <summary>
    /// Изменения плагина применит только перезапуск студии.
    /// </summary>
    /// <remarks>
    /// Причину человеку не называют — как Rider, у которого у такого плагина стоит «Restart IDE», и
    /// только: владельцы свойств Avalonia и пересобранные контракты — разговор для автора плагина,
    /// и ведётся он в журнале.
    /// </remarks>
    public bool NeedsRestart { get; }

    /// <summary>Почему плагин не поднялся; null — поднялся или его и не поднимали.</summary>
    /// <remarks>
    /// Отдельно от ошибки манифеста: манифест может быть цел, а сборка — не читаться или точка
    /// входа падать. Такой плагин включён и в списке выглядел исправным, хотя не работал.
    /// </remarks>
    public string? RiseError { get; }

    /// <summary>Отметка «не поднялся» показывается только тому, кто не поднялся.</summary>
    public bool HasRiseError => RiseError is not null;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public bool IsHeader => false;

    /// <inheritdoc/>
    public string Label => Plugin.DisplayName;

    /// <summary>Запись каталога.</summary>
    public InstalledPlugin Plugin { get; }

    /// <summary>Зависимости с состоянием каждой цели.</summary>
    public IReadOnlyList<PluginDependencyState> Dependencies { get; }

    /// <summary>Строка зависимостей показывается только тем, у кого они есть.</summary>
    public bool HasDependencies => Dependencies.Count > 0;

    /// <summary>
    /// Кто из установленных объявил зависимость на этот плагин — как в Unity Package Manager, где
    /// у пакета видно и «использует», и «нужен».
    /// </summary>
    /// <remarks>
    /// Задаёт список страница, когда собирает все строки разом: ответ ищется среди соседей, а
    /// строка о них не знает. Нужен он затем, чтобы видеть последствия выключения раньше вопроса.
    /// </remarks>
    public IReadOnlyList<string> Dependents
    {
        get => _dependents;
        internal set
        {
            _dependents = value;
            Notify();
            Notify(nameof(HasDependents));
            Notify(nameof(HasLinks));
        }
    }

    /// <summary>Этот плагин кому-то нужен.</summary>
    public bool HasDependents => _dependents.Count > 0;

    /// <summary>Раздел «Зависимости» есть что показать: и в ту, и в другую сторону.</summary>
    public bool HasLinks => HasDependencies || HasDependents;

    /// <summary>
    /// Метки расширения так, как их показывают человеку.
    /// </summary>
    /// <remarks>
    /// Несколько тегов студия знает в лицо и переводит: <c>tools</c> в русском
    /// становится «Инструментами». Остальные показываются как написаны — тег
    /// свободный, и придумывать за автора перевод его слова студия не вправе.
    /// <para>
    /// Ищут при этом по самому тегу, а не по подписи: <see cref="Tags"/> —
    /// показ, а поиск берёт список у записи каталога. Иначе найденное менялось
    /// бы вместе с языком интерфейса.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Tags =>
        Plugin.Tags
            .Select(tag => Localizer.Instance[$"tag.{tag}"] is { } label && !label.StartsWith('!') ? label : tag)
            .ToList();

    /// <summary>Строка меток показывается только тем, у кого они есть.</summary>
    public bool HasTags => Plugin.Tags.Count > 0;

    /// <summary>Модуль, приехавший со студией: его не выключают и не удаляют.</summary>
    public bool IsBuiltIn => Plugin.IsBuiltIn;

    /// <summary>Флажок строки есть: манифест цел, и это не модуль.</summary>
    public bool CanToggle => Plugin.IsValid && !Plugin.IsBuiltIn;

    /// <summary>Плагин можно снять с машины: модуль уходит только вместе со студией.</summary>
    public bool CanRemove => !Plugin.IsBuiltIn;

    /// <summary>Манифест не разобрался: включать нечего, остаётся удалить.</summary>
    public bool IsBroken => !Plugin.IsValid;

    /// <summary>
    /// Языковой пакет: несёт языки интерфейса и ни одной сборки.
    /// </summary>
    /// <remarks>
    /// Признак — отсутствие <c>entry</c>, а не только наличие языков: плагин с кодом, приносящий
    /// заодно свои переводы, — всё-таки плагин, и стоять ему среди плагинов.
    /// </remarks>
    public bool IsLanguagePack =>
        Plugin.Manifest is { } manifest
        && string.IsNullOrWhiteSpace(manifest.Entry)
        && manifest.Contributions.Languages.Count > 0;

    /// <summary>
    /// Что с плагином не так; null — всё в порядке.
    /// </summary>
    /// <remarks>
    /// Три беды по старшинству: манифест не прочитался, плагин не поднялся, у включённого не в
    /// порядке обязательная зависимость. Последнее спрашивается у записи с диска, а не у
    /// галочки: поднимался плагин по записанному, и зависимость мешает ему, а не решению, которое
    /// ещё не сохранено. Беда названа своим видом, а причина стоит за ним целиком: журнал, где она
    /// сказана, из окна настроек не виден.
    /// </remarks>
    public string? Problem
    {
        get
        {
            if (!Plugin.IsValid)
                return $"{Localizer.Instance["plugins.broken"]}: {Plugin.Error}";

            if (RiseError is not null)
                return $"{Localizer.Instance["plugins.unrisen"]}: {RiseError}";

            var broken = Dependencies.Where(dependency => dependency.IsProblem).Select(dependency => dependency.Label).ToList();

            return Plugin.IsEnabled && broken.Count > 0
                ? $"{Localizer.Instance["plugins.deps"]}: {string.Join(", ", broken)}"
                : null;
        }
    }

    /// <summary>Строка отмечена знаком ошибки.</summary>
    public bool IsProblem => Problem is not null;

    /// <summary>Под именем в строке: версия и издатель, у сломанного — папка.</summary>
    public string Subtitle =>
        Plugin.Manifest is { } manifest
            ? manifest.Publisher is { Length: > 0 } publisher ? $"{manifest.Version} · {publisher}" : manifest.Version
            : Path.GetFileName(Plugin.Directory);

    /// <inheritdoc/>
    /// <remarks>
    /// Точку, знаки ошибки и перезапуска и серый цвет глазом видно, а диктору о них надо сказать
    /// словами.
    /// </remarks>
    public string Status =>
        IsChanged ? Localizer.Instance["plugins.pending"]
        : IsProblem ? Localizer.Instance["common.error"]
        : NeedsRestart ? Localizer.Instance["restart.required"]
        : IsBuiltIn ? Localizer.Instance["plugins.builtin"]
        : string.Empty;

    /// <summary>Описание есть — раздел «Описание» показывается.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Plugin.Description);

    /// <summary>
    /// Что плагин добавляет в студию — по манифесту, не загружая сборки.
    /// </summary>
    /// <remarks>
    /// Панели названы заголовками: их человек и видит в студии. Остальное — числом: сорок команд
    /// поимённо не читают, а число отвечает на вопрос «много ли он трогает». Пустое не
    /// перечисляется — строка «Контракты: 0» ничего не сообщает.
    /// </remarks>
    public IReadOnlyList<PluginFact> Brings
    {
        get
        {
            if (Plugin.Manifest is not { } manifest)
                return [];

            var contributions = manifest.Contributions;
            var facts = new List<PluginFact>();

            if (contributions.ToolWindows.Count > 0)
            {
                facts.Add(Fact(
                    "plugins.details.panels",
                    string.Join(", ", contributions.ToolWindows.Select(window => Plugin.Strings.Resolve(window.Title)))));
            }

            Count("plugins.details.commands", contributions.Commands.Count);
            Count("plugins.details.toolbar", contributions.ToolBar.Count);
            Count("plugins.details.newitems", contributions.NewItems.Count);
            Count("plugins.details.settings", contributions.Settings.Count);

            if (Plugin.Coverage is { Count: > 0 } coverage)
                facts.Add(Fact("plugins.details.languages", string.Join(", ", coverage.Select(language => language.Label))));
            else if (contributions.Languages.Count > 0)
                facts.Add(Fact("plugins.details.languages", string.Join(", ", contributions.Languages.Select(language => language.Name))));

            Count("plugins.details.contracts", manifest.Provides?.Contracts.Count ?? 0);

            return facts;

            void Count(string label, int count)
            {
                if (count > 0)
                    facts.Add(Fact(label, count.ToString(CultureInfo.CurrentCulture)));
            }
        }
    }

    /// <summary>Раздел «Что добавляет» есть что показать.</summary>
    public bool HasBrings => Brings.Count > 0;

    /// <summary>
    /// Сведения о плагине: идентификатор, версия, издатель, требования и события подъёма.
    /// </summary>
    /// <remarks>Папка — отдельно: её показывают ссылкой, которая открывает её в системе.</remarks>
    public IReadOnlyList<PluginFact> Facts
    {
        get
        {
            var facts = new List<PluginFact> { Fact("plugins.details.id", Plugin.Id) };

            if (Plugin.Manifest is not { } manifest)
                return facts;

            facts.Add(Fact("plugins.details.version", manifest.Version));

            if (manifest.Publisher is { Length: > 0 } publisher)
                facts.Add(Fact("plugins.details.publisher", publisher));

            if (manifest.Sdk?.Min is { Length: > 0 } sdk)
            {
                facts.Add(Fact(
                    "plugins.details.sdk",
                    string.Format(CultureInfo.CurrentCulture, Localizer.Instance["plugins.details.sdk.value"], sdk)));
            }

            if (manifest.Activation.Count > 0)
                facts.Add(Fact("plugins.details.activation", Activation(manifest.Activation)));

            return facts;
        }
    }

    /// <summary>Папка плагина на диске.</summary>
    public string Folder => Plugin.Directory;

    /// <summary>
    /// Папка так, как её показывают: переносится по разделителям пути.
    /// </summary>
    /// <remarks>
    /// Путь без пробелов — одно слово, и перенос резал его где пришлось: «C:» оставалось одно на
    /// строке. Невидимый пробел нулевой ширины после каждого разделителя даёт переносу места по
    /// сегментам, а подсказка и открытие берут настоящий путь.
    /// </remarks>
    public string FolderShown =>
        Folder.Replace("\\", "\\​", StringComparison.Ordinal).Replace("/", "/​", StringComparison.Ordinal);

    /// <summary>
    /// События подъёма — словами: «при запуске; файлы .cs, .md; команды hello.greet».
    /// </summary>
    /// <remarks>
    /// Сырой список <c>onFileType:.cs, onFileType:.md, …</c> у просмотрщика кода занимал десять
    /// строк, а говорил одно: «файлы таких-то видов». События одного рода собраны вместе, незнакомые
    /// показаны как написаны.
    /// </remarks>
    private static string Activation(IEnumerable<string> events)
    {
        var startup = false;
        var kinds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var other = new List<string>();

        foreach (var activation in events)
        {
            if (string.Equals(activation, "onStartup", StringComparison.Ordinal))
            {
                startup = true;
                continue;
            }

            var colon = activation.IndexOf(':', StringComparison.Ordinal);
            var kind = colon > 0 ? activation[..colon] : string.Empty;

            if (kind is "onFileType" or "onCommand" or "onToolWindow" or "onNewItem")
            {
                if (!kinds.TryGetValue(kind, out var names))
                    kinds[kind] = names = [];

                names.Add(activation[(colon + 1)..]);
            }
            else
            {
                other.Add(activation);
            }
        }

        var parts = new List<string>();

        if (startup)
            parts.Add(Localizer.Instance["plugins.activation.startup"]);

        Add("onFileType", "plugins.activation.files");
        Add("onCommand", "plugins.activation.commands");
        Add("onToolWindow", "plugins.activation.panels");
        Add("onNewItem", "plugins.activation.newitems");

        parts.AddRange(other);

        return string.Join("; ", parts);

        void Add(string kind, string label)
        {
            if (kinds.TryGetValue(kind, out var names))
                parts.Add(string.Format(CultureInfo.CurrentCulture, Localizer.Instance[label], string.Join(", ", names)));
        }
    }

    /// <summary>Плагин объявил настройки — у него есть своя страница в ветке «Расширения».</summary>
    public bool DeclaresSettings => Plugin.Manifest?.Contributions.Settings.Count > 0;

    /// <summary>Имя страницы его настроек — туда ведёт кнопка «Настройки».</summary>
    public string SettingsPageId => ExtensionPage.IdFor(Plugin.Id);

    /// <summary>Включён ли плагин — с учётом непринятой ещё правки.</summary>
    public bool IsOn
    {
        get => _on;
        set
        {
            if (_on == value)
                return;

            _on = value;
            Notify();
            Notify(nameof(IsChanged));
            Notify(nameof(Pending));
            Notify(nameof(Status));
        }
    }

    /// <summary>Галочка разошлась с тем, что записано на диске.</summary>
    public bool IsChanged => _on != Plugin.IsEnabled;

    /// <summary>
    /// Что случится с плагином по «Сохранить»; null — ничего.
    /// </summary>
    /// <remarks>
    /// Правка копится до «Сохранить», и подробности говорят об этом словами: выключенный галочкой
    /// и выключенный на диске иначе выглядели бы одинаково.
    /// </remarks>
    public string? Pending =>
        IsChanged ? Localizer.Instance[_on ? "plugins.pending.on" : "plugins.pending.off"] : null;

    /// <summary>
    /// По чему плагин находит поиск: имя, идентификатор, издатель и метки.
    /// </summary>
    /// <remarks>
    /// Метки — сами теги, а не их подписи: тег один на все языки, и найденное по нему не должно
    /// меняться вместе с языком интерфейса.
    /// </remarks>
    public IEnumerable<string> Terms =>
        Plugin.Tags
            .Append(Plugin.DisplayName)
            .Append(Plugin.Id)
            .Append(Plugin.Manifest?.Publisher)
            .OfType<string>();

    /// <summary>Строка видна: поиска нет или он её нашёл.</summary>
    public bool IsShown
    {
        get => _shown;
        private set
        {
            if (_shown == value)
                return;

            _shown = value;
            Notify();
        }
    }

    /// <summary>Оставляет строку на виду, если поиск её нашёл.</summary>
    /// <param name="query">Что ищут; <c>null</c> — строка видна всегда.</param>
    public void Narrow(string? query) =>
        IsShown = query is null || SettingsSearch.Matches(Terms, query);

    /// <summary>Возвращает галочку к записанному.</summary>
    public void Revert() => IsOn = Plugin.IsEnabled;

    /// <summary>Имя плагина — по нему список ищет строку набором букв.</summary>
    public override string ToString() => Plugin.DisplayName;

    private static PluginFact Fact(string label, string value) => new(Localizer.Instance[label], value);

    private void Notify([CallerMemberName] string? property = null) => PropertyChanged.Raise(this, property);
}
