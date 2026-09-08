using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.ViewModels;

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
/// </remarks>
public sealed class PluginCard : INotifyPropertyChanged
{
    private bool _on;

    /// <summary>Собирает строку поверх записи каталога.</summary>
    /// <param name="plugin">Запись каталога.</param>
    /// <param name="dependencies">Зависимости с состоянием каждой цели.</param>
    public PluginCard(InstalledPlugin plugin, IReadOnlyList<PluginDependencyState> dependencies)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        Plugin = plugin;
        Dependencies = dependencies;
        _on = plugin.IsEnabled;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Запись каталога.</summary>
    public InstalledPlugin Plugin { get; }

    /// <summary>Зависимости с состоянием каждой цели.</summary>
    public IReadOnlyList<PluginDependencyState> Dependencies { get; }

    /// <summary>Строка зависимостей показывается только тем, у кого они есть.</summary>
    public bool HasDependencies => Dependencies.Count > 0;

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
        }
    }

    /// <summary>Галочка разошлась с тем, что записано на диске.</summary>
    public bool IsChanged => _on != Plugin.IsEnabled;

    /// <summary>Возвращает галочку к записанному.</summary>
    public void Revert() => IsOn = Plugin.IsEnabled;

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
