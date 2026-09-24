using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia.Media;

namespace ArxisStudio.Settings;

/// <summary>
/// Ветка «Расширения»: под ней стоят страницы модулей и плагинов.
/// </summary>
/// <remarks>
/// Своих настроек у ветки нет, и всё, что её просят, она складывает из детей.
/// Так окну не приходится знать, что узел бывает двух родов: сохранение и
/// отмена идут одной дорогой по всему дереву.
/// <para>
/// Показывает ветка своих детей — ссылками, как страница раздела в Rider: подсказка «выберите
/// слева» говорила, куда идти, а ссылка туда ведёт.
/// </para>
/// </remarks>
public sealed class ExtensionsPage : ISettingsPage
{
    private readonly IReadOnlyList<ISettingsPage> _children;

    /// <summary>Собирает ветку над страницами расширений.</summary>
    /// <param name="children">Страницы расширений в порядке показа.</param>
    public ExtensionsPage(IReadOnlyList<ISettingsPage> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        _children = children;

        // Правленая страница расширения — правленая ветка: точка в дереве стоит на обеих, и
        // сказать о ней ветке некому, кроме детей.
        foreach (var child in children)
            child.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public string Id => "studio.extensions";

    /// <inheritdoc/>
    public string Title => Localizer.Instance["settings.plugins"];

    /// <inheritdoc/>
    public Geometry? Icon => AxIcons.Package;

    /// <inheritdoc/>
    public IReadOnlyList<ISettingsPage> Children => _children;

    /// <inheritdoc/>
    public IEnumerable<string> Terms => [Title];

    /// <inheritdoc/>
    public bool HasChanges => _children.Any(child => child.HasChanges);

    /// <inheritdoc/>
    public async Task CommitAsync(ICollection<string> problems)
    {
        foreach (var child in _children)
            await child.CommitAsync(problems);
    }

    /// <inheritdoc/>
    public void Revert()
    {
        foreach (var child in _children)
            child.Revert();
    }

    /// <inheritdoc/>
    /// <remarks>Строк у ветки нет: детей окно сужает само, каждого своим вызовом.</remarks>
    public void Narrow(string? query)
    {
    }
}

/// <summary>
/// Страница одного расширения: строки, объявленные его манифестом.
/// </summary>
/// <remarks>
/// Собирается по манифесту, а не по коду: студия читает манифесты, не загружая
/// сборок, и настройки видны у расширения, которое в этом сеансе ни разу не
/// поднималось. Модулю и плагину здесь одна дорога — секция манифеста у них
/// одна.
/// <para>
/// О записанном расширению говорят сразу: оно слышит свои настройки через
/// <c>IStudioSettings.Changed</c>, а окно пишет мимо него — прямо в хранилище.
/// Без этого панель осталась бы с прежним значением до перезапуска.
/// </para>
/// </remarks>
public sealed class ExtensionPage : ISettingsPage
{
    private readonly InstalledPlugin _extension;
    private readonly Action<string, string>? _announce;

    /// <summary>Собирает страницу по записи каталога.</summary>
    /// <param name="extension">Модуль или плагин.</param>
    /// <param name="store">Общее хранилище настроек студии.</param>
    /// <param name="announce">Кому сказать о записанном; null — молча.</param>
    public ExtensionPage(
        InstalledPlugin extension,
        PluginSettingsStore store,
        Action<string, string>? announce = null)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(store);

        _extension = extension;
        _announce = announce;

        Rows =
        [
            .. (extension.Manifest?.Contributions.Settings ?? [])
                .Select(declared => new PluginSettingRow(
                    extension.Id, extension.DisplayName, declared, store, extension.Strings)),
        ];

        foreach (var row in Rows)
            row.PropertyChanged += OnRowChanged;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>Строки настроек в порядке манифеста.</summary>
    public IReadOnlyList<PluginSettingRow> Rows { get; }

    /// <inheritdoc/>
    public string Id => $"extension:{_extension.Id}";

    /// <inheritdoc/>
    public string Title => _extension.DisplayName;

    /// <inheritdoc/>
    public Geometry? Icon => _extension.IsBuiltIn ? AxIcons.Component : AxIcons.Plugin;

    /// <inheritdoc/>
    public IReadOnlyList<ISettingsPage> Children => [];

    /// <inheritdoc/>
    /// <remarks>
    /// Метки расширения тоже: «tools» приводит к терминалу так же, как «кегль». У
    /// встроенного модуля строка в менеджере плагинов теперь есть — справочная, — и
    /// по меткам поиск находит обе страницы.
    /// </remarks>
    public IEnumerable<string> Terms =>
        Rows.SelectMany(row => new[] { row.Label, row.Key })
            .Concat(_extension.Tags)
            .Prepend(Title);

    /// <inheritdoc/>
    public bool HasChanges => Rows.Any(row => row.HasChanges);

    /// <inheritdoc/>
    public Task CommitAsync(ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        foreach (var row in Rows)
        {
            if (row.Commit(problems))
                _announce?.Invoke(row.PluginId, row.Key);
        }

        // Строки пишутся в память хранилища и на диск — ждать здесь нечего.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Revert()
    {
        foreach (var row in Rows)
            row.Revert();
    }

    /// <inheritdoc/>
    public void Narrow(string? query)
    {
        foreach (var row in Rows)
            row.Narrow(query);
    }

    /// <summary>Перечитывает подписи строк: язык сменили.</summary>
    public void Relabel()
    {
        foreach (var row in Rows)
            row.Relabel();
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginSettingRow.HasChanges))
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
