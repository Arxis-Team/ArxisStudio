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
/// </remarks>
/// <param name="children">Страницы расширений в порядке показа.</param>
public sealed class ExtensionsPage(IReadOnlyList<ISettingsPage> children) : ISettingsPage
{
    /// <inheritdoc/>
    public string Id => "studio.extensions";

    /// <inheritdoc/>
    public string Title => Localizer.Instance["settings.plugins"];

    /// <inheritdoc/>
    public Geometry? Icon => AxIcons.Package;

    /// <inheritdoc/>
    public IReadOnlyList<ISettingsPage> Children => children;

    /// <inheritdoc/>
    public IEnumerable<string> Terms => [Title];

    /// <inheritdoc/>
    public bool HasChanges => children.Any(child => child.HasChanges);

    /// <inheritdoc/>
    public void Commit(ICollection<string> problems)
    {
        foreach (var child in children)
            child.Commit(problems);
    }

    /// <inheritdoc/>
    public void Revert()
    {
        foreach (var child in children)
            child.Revert();
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
    }

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
    public IEnumerable<string> Terms =>
        Rows.SelectMany(row => new[] { row.Label, row.Key }).Prepend(Title);

    /// <inheritdoc/>
    public bool HasChanges => Rows.Any(row => row.HasChanges);

    /// <inheritdoc/>
    public void Commit(ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        foreach (var row in Rows)
        {
            if (row.Commit(problems))
                _announce?.Invoke(row.PluginId, row.Key);
        }
    }

    /// <inheritdoc/>
    public void Revert()
    {
        foreach (var row in Rows)
            row.Revert();
    }

    /// <summary>Перечитывает подписи строк: язык сменили.</summary>
    public void Relabel()
    {
        foreach (var row in Rows)
            row.Relabel();
    }
}
