using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.ViewModels;

/// <summary>
/// Строка настройки расширения в экране настроек.
/// </summary>
/// <remarks>
/// Строится по манифесту, а не по коду расширения: студия читает манифесты, не
/// загружая сборок, и настройки должны быть видны и у плагина, который в этом
/// сеансе ни разу не поднимался.
/// <para>
/// Проектная настройка в этом экране только показывается: проекта здесь нет, и
/// записать её некуда. Прятать её при этом нельзя — человек искал бы её и не
/// нашёл, решив, что расширение её не объявляет.
/// </para>
/// <para>
/// Правка не уходит на диск сразу: строка копит её у себя и отдаёт по
/// <see cref="Commit"/>. Так у окна настроек появляются «Сохранить» и
/// «Отмена» — без этого «Отмена» отменяла бы только последнее движение мыши, а
/// всё сделанное до неё уже лежало бы в файле.
/// </para>
/// </remarks>
/// <param name="pluginId">Чья настройка.</param>
/// <param name="pluginName">Как называется расширение.</param>
/// <param name="declared">Объявление из манифеста.</param>
/// <param name="store">Общее хранилище настроек.</param>
/// <param name="strings">Словари расширения: по ним переводится подпись.</param>
public sealed class PluginSettingRow(
    string pluginId,
    string pluginName,
    PluginSetting declared,
    PluginSettingsStore store,
    PluginStrings strings) : INotifyPropertyChanged
{
    /// <summary>
    /// Правка, ещё не ушедшая в хранилище; null — правки нет.
    /// </summary>
    /// <remarks>
    /// Отдельного признака «есть правка» не нужно: настройку нельзя выставить
    /// в null — пустая строка и снятый флажок это значения, а не отсутствие.
    /// </remarks>
    private object? _pending;

    private bool _shown = true;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Строка видна: поиска нет или он её нашёл.</summary>
    public bool IsShown
    {
        get => _shown;
        private set
        {
            if (_shown == value)
                return;

            _shown = value;
            PropertyChanged.Raise(this, nameof(IsShown));
        }
    }

    /// <summary>Чьё это расширение — по нему студия говорит ему об изменении.</summary>
    public string PluginId => pluginId;

    /// <summary>Как называется расширение, которому принадлежит настройка.</summary>
    public string PluginName => pluginName;

    /// <summary>Подпись настройки.</summary>
    public string Label => strings.Resolve(declared.Label);

    /// <summary>Ключ — по нему настройку правят в файле и находит поиск.</summary>
    public string Key => declared.Key;

    /// <summary>Настройка — переключатель.</summary>
    public bool IsToggle => declared.IsBool;

    /// <summary>Настройка — строка или число: показывается полем ввода.</summary>
    public bool IsText => !declared.IsBool;

    /// <summary>
    /// Настройку можно править здесь и сейчас.
    /// </summary>
    /// <remarks>
    /// Спрашивается хранилище, а не манифест: проектная настройка неправима не
    /// потому, что она проектная, а потому, что писать её некуда, пока проект
    /// не открыт. Разница станет видной, когда работа с проектами приедет
    /// модулем, — а закрыть глаза на неё сейчас значит написать правило,
    /// которое придётся переписывать.
    /// </remarks>
    public bool IsEditable => !declared.IsProject || store.ProjectFile is not null;

    /// <summary>Пояснение к неправимой строке; пусто у обычной.</summary>
    public string Note => IsEditable ? string.Empty : Localizer.Instance["settings.project.note"];

    /// <summary>
    /// Строку правили, и правка отличается от записанного.
    /// </summary>
    /// <remarks>
    /// Сравнение, а не «трогали ли»: набрать прежнее значение обратно — то же
    /// самое, что не трогать вовсе, и спрашивать за это «правки не сохранены»
    /// было бы неправдой.
    /// </remarks>
    public bool HasChanges => _pending switch
    {
        bool flag => flag != Read(),
        string text => !string.Equals(text, Stored(), StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>Значение переключателя.</summary>
    /// <remarks>
    /// Читается терпимо, потому что спрашивают его и о том, что переключателем
    /// не является. Привязка тумблера живёт в одном шаблоне с полем ввода и
    /// вычисляется у каждой строки — у скрытой тоже, — так что строковая
    /// настройка приходит сюда обычным путём. Без охраны студия писала бы на
    /// каждой такой строке ошибку привязки про тумблер, которого человек не
    /// видит.
    /// <para>
    /// Правило то же, что у <see cref="PluginSettings.Get{T}"/>: тип в файле
    /// правят руками, и разойтись с объявленным в манифесте он может всегда.
    /// </para>
    /// </remarks>
    public bool Flag
    {
        get => _pending is bool pending ? pending : Read();
        set => Stage(value);
    }

    /// <summary>Значение строкой — им же показывается число.</summary>
    /// <remarks>
    /// Копится строкой, как её набрали, а числом становится при сохранении:
    /// переводя её в число на каждое нажатие клавиши, поле переписывало бы
    /// набранное под курсором.
    /// </remarks>
    public string Text
    {
        get => _pending as string ?? Stored();
        set => Stage(value);
    }

    /// <summary>
    /// Отдаёт накопленную правку хранилищу.
    /// </summary>
    /// <param name="problems">Куда сложить причину, если записать не вышло.</param>
    /// <returns><c>true</c>, если значение записано и о нём стоит сказать расширению.</returns>
    /// <remarks>
    /// Причина отказа не выбрасывается, как было раньше, а возвращается
    /// наверх: «настройка проектная, а проект не открыт» и «настройки не
    /// записались» — это то, что человек обязан прочитать, а не то, о чём
    /// студия молчит.
    /// </remarks>
    public bool Commit(ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (!HasChanges || _pending is not { } value)
            return false;

        // Разбор и запись — по инвариантной культуре, а не по машинной. Число
        // уезжает в JSON, где точка десятичная всегда; на русской машине
        // разбор по текущей культуре принял бы «13,5» и отверг «13.5» — то
        // самое, что человек видит в файле и набирает обратно.
        if (value is string text && declared.IsNumber
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            value = number;
        }

        if (store.Write(pluginId, declared, value) is { } error)
        {
            problems.Add(error);
            return false;
        }

        _pending = null;
        PendingChanged();
        return true;
    }

    /// <summary>
    /// Оставляет строку на виду, если поиск её нашёл.
    /// </summary>
    /// <param name="query">Что ищут; <c>null</c> — строка видна всегда.</param>
    /// <remarks>
    /// Ищется и подпись, и ключ — тем же правилом, по которому поиск находит страницу: ключ
    /// человек видит в файле настроек и ищет именно его.
    /// </remarks>
    public void Narrow(string? query) =>
        IsShown = query is null
            || SettingsSearch.Matches(Label, query)
            || SettingsSearch.Matches(Key, query);

    /// <summary>Забывает накопленную правку.</summary>
    public void Revert()
    {
        if (_pending is null)
            return;

        _pending = null;
        PendingChanged();
    }

    /// <summary>
    /// Перечитывает подписи: язык сменили.
    /// </summary>
    /// <remarks>
    /// Подпись строки приходит из словаря расширения через
    /// <c>PluginStrings.Resolve</c>, а он разрешает ключ на месте и о смене
    /// языка не знает — в отличие от <c>{ax:Loc}</c>, который держит привязку.
    /// Пересобрать строку вместо этого нельзя: вместе с ней пропала бы
    /// накопленная правка.
    /// </remarks>
    public void Relabel()
    {
        PropertyChanged.Raise(this, nameof(Label), nameof(PluginName), nameof(Note));
    }

    /// <summary>Записанное значение строкой; пусто, если ничего не записано.</summary>
    private string Stored() => store.Read(pluginId, declared)?.ToString() ?? string.Empty;

    /// <summary>Флажок из хранилища; ложь, если там лежит не флажок.</summary>
    private bool Read()
    {
        var value = store.Read(pluginId, declared);

        if (value is null)
            return false;

        try
        {
            return value.GetValue<bool>();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private void Stage(object value)
    {
        _pending = value;
        PendingChanged();
    }

    /// <summary>Накопленная правка сменилась: извещает о показанном значении и о несохранённом.</summary>
    private void PendingChanged() => PropertyChanged.Raise(this, nameof(Flag), nameof(Text), nameof(HasChanges));
}
