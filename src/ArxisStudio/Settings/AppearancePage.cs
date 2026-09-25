using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Icons;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia.Media;

namespace ArxisStudio.Settings;

/// <summary>
/// Страница «Оформление»: тема студии, плотность и язык интерфейса.
/// </summary>
/// <remarks>
/// Единственная страница, чьи правки видны до сохранения, и это не небрежность,
/// а условие задачи: тему, плотность и язык выбирают глазами. Поэтому все три
/// применяются сразу — предпросмотром, — а записываются только по «Сохранить»;
/// «Отмена» возвращает то, что было при открытии окна или с последней записи:
/// удавшееся «Сохранить» и есть новое «как было».
/// <para>
/// Язык при открытии снимается <b>действующий</b>, а не записанный в
/// настройках: словарь могли удалить, и студия осталась на запасном. Вернув по
/// «Отмене» записанное имя, окно вернуло бы язык, которого нет.
/// </para>
/// </remarks>
public sealed class AppearancePage : ISettingsPage, INotifyPropertyChanged
{
    private readonly ISettingsStore _studio;

    private StudioTheme _themeAtOpen;
    private StudioDensity _densityAtOpen;
    private string _languageAtOpen;
    private StudioTheme _theme;
    private StudioDensity _density;
    private string _language;
    private string? _query;

    /// <summary>Заводит страницу поверх настроек студии.</summary>
    /// <param name="studio">Хранилище настроек студии.</param>
    public AppearancePage(ISettingsStore studio)
    {
        ArgumentNullException.ThrowIfNull(studio);

        _studio = studio;
        _theme = _themeAtOpen = studio.Current.Theme;
        _density = _densityAtOpen = studio.Current.Density;
        _language = _languageAtOpen = Localizer.Instance.Language;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <summary>Куда сказать о том, что не вышло.</summary>
    public Action<string>? Complain { get; init; }

    /// <summary>Кого позвать, когда сменился язык: подписи расширений сами не перечитаются.</summary>
    public Action? Relabelled { get; init; }

    /// <inheritdoc/>
    public string Id => "studio.appearance";

    /// <inheritdoc/>
    public string Title => Localizer.Instance["settings.appearance"];

    /// <inheritdoc/>
    public Geometry? Icon => AxIcons.Settings;

    /// <inheritdoc/>
    public IReadOnlyList<ISettingsPage> Children => [];

    /// <inheritdoc/>
    /// <remarks>
    /// Подписи строк и их вариантов: «светлая» ищут чаще, чем «тема», — человек помнит, что
    /// хочет получить, а не как это называется.
    /// </remarks>
    public IEnumerable<string> Terms => [Title, .. ThemeTerms, .. DensityTerms, .. LanguageTerms];

    /// <summary>Строка темы видна: поиска нет или он её нашёл.</summary>
    public bool ShowsTheme => Shows(ThemeTerms);

    /// <summary>Строка плотности видна: поиска нет или он её нашёл.</summary>
    public bool ShowsDensity => Shows(DensityTerms);

    /// <summary>Строка языка видна: поиска нет или он её нашёл.</summary>
    public bool ShowsLanguage => Shows(LanguageTerms);

    private static string[] ThemeTerms =>
    [
        Localizer.Instance["settings.theme"],
        Localizer.Instance["settings.theme.dark"],
        Localizer.Instance["settings.theme.light"],
    ];

    private static string[] DensityTerms =>
    [
        Localizer.Instance["settings.density"],
        Localizer.Instance["settings.density.compact"],
        Localizer.Instance["settings.density.normal"],
        Localizer.Instance["settings.density.comfortable"],
    ];

    private string[] LanguageTerms =>
        [Localizer.Instance["settings.language"], .. Languages.Select(language => language.Name)];

    /// <inheritdoc/>
    public bool HasChanges =>
        _theme != _themeAtOpen ||
        _density != _densityAtOpen ||
        !string.Equals(_language, _languageAtOpen, StringComparison.Ordinal);

    /// <summary>Языки, которые студия сейчас умеет показать.</summary>
    public IReadOnlyList<StudioLanguage> Languages => Localizer.Instance.Languages;

    /// <summary>
    /// Тема номером сегмента: 0 — тёмная, 1 — светлая.
    /// </summary>
    /// <remarks>
    /// Номером, а не значением: сегментный переключатель отвечает
    /// <c>SelectedIndex</c>, и переводить одно в другое лучше здесь, чем
    /// заводить ради этого преобразователь.
    /// </remarks>
    public int ThemeIndex
    {
        get => _theme == StudioTheme.Light ? 1 : 0;
        set
        {
            var wanted = value == 1 ? StudioTheme.Light : StudioTheme.Dark;

            if (_theme == wanted)
                return;

            _theme = wanted;
            StudioTheming.Apply(wanted);
            Edited();
        }
    }

    /// <summary>
    /// Плотность номером сегмента: 0 — плотная, 1 — обычная, 2 — просторная.
    /// </summary>
    /// <remarks>
    /// Номер совпадает с порядком объявления ступеней, и это не совпадение:
    /// порядок там выбран порядком на экране. Число вне ряда сегментный
    /// переключатель не отдаёт, но свойство открыто привязке, и отказ здесь
    /// дешевле, чем ступень, которой нет в теме.
    /// </remarks>
    public int DensityIndex
    {
        get => (int)_density;
        set
        {
            if (!Enum.IsDefined((StudioDensity)value))
                return;

            var wanted = (StudioDensity)value;

            if (_density == wanted)
                return;

            _density = wanted;
            StudioTheming.Apply(wanted);
            Edited();
        }
    }

    /// <summary>Выбранный язык; null — список ещё не собран.</summary>
    public StudioLanguage? SelectedLanguage
    {
        get => Languages.FirstOrDefault(language => language.Code == _language);
        set
        {
            if (value is not { } wanted || string.Equals(wanted.Code, _language, StringComparison.Ordinal))
                return;

            // Ответ проверяется: словарь могли удалить между сборкой списка и
            // выбором, и записать в настройки язык, на котором студия говорить
            // не умеет, значит увести её на запасной при следующем запуске.
            if (!Localizer.Instance.SetLanguage(wanted.Code))
            {
                Complain?.Invoke(Localizer.Instance["settings.language.missing"]);
                Edited();
                return;
            }

            _language = wanted.Code;
            Edited();
            Relabelled?.Invoke();
        }
    }

    /// <inheritdoc/>
    public Task CommitAsync(ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (!HasChanges)
            return Task.CompletedTask;

        _studio.Current.Theme = _theme;
        _studio.Current.Density = _density;
        _studio.Current.Language = _language;

        // Запись одна на все три настройки: хранилище сериализует весь объект,
        // и три вызова записали бы один и тот же файл трижды. Своей охраны у
        // него нет — исключение из WriteAllText вышло бы наружу и уронило бы
        // сохранение целиком, поэтому ловим здесь.
        try
        {
            _studio.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Localizer.Instance["settings.title"]}: {e.Message}");
            return Task.CompletedTask;
        }

        // Записанное становится тем, «что было при открытии». Иначе страница
        // остаётся изменённой после «Сохранить», и закрытие — а «Сохранить»
        // закрывает окно — спрашивает о потере правок, которые уже в файле;
        // ответив «закрыть», человек получал бы откат оформления к прежнему
        // при уже переписанном файле.
        //
        // Только после удавшейся записи: не легло — значит не сохранено, и
        // окно обязано остаться при своих правках.
        _themeAtOpen = _theme;
        _densityAtOpen = _density;
        _languageAtOpen = _language;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Revert()
    {
        if (_theme != _themeAtOpen)
        {
            _theme = _themeAtOpen;
            StudioTheming.Apply(_themeAtOpen);
        }

        if (_density != _densityAtOpen)
        {
            _density = _densityAtOpen;
            StudioTheming.Apply(_densityAtOpen);
        }

        if (!string.Equals(_language, _languageAtOpen, StringComparison.Ordinal))
        {
            // Отказ здесь молчаливый намеренно: словарь исчез посреди сеанса,
            // студия осталась на запасном, и сказать об этом окну, которое
            // сейчас закроется, некуда.
            Localizer.Instance.SetLanguage(_languageAtOpen);
            _language = _languageAtOpen;
            Relabelled?.Invoke();
        }

        // Страница остаётся на экране после «Сбросить» в шапке, и её контролы обязаны показать
        // вернувшееся. Прежде откат шёл только с закрытием окна, и сегменты, оставшиеся на
        // прежнем выборе, видно не было.
        Edited(nameof(ThemeIndex));
        Edited(nameof(DensityIndex));
        Edited(nameof(SelectedLanguage));
    }

    /// <inheritdoc/>
    public void Narrow(string? query)
    {
        _query = SettingsSearch.Normalize(query);

        PropertyChanged.Raise(this, nameof(ShowsTheme), nameof(ShowsDensity), nameof(ShowsLanguage));
    }

    private bool Shows(IEnumerable<string> terms) =>
        _query is not { } query || SettingsSearch.Matches(terms, query);

    /// <summary>Свойство сменилось правкой: извещает о нём, о несохранённом и окно.</summary>
    /// <param name="property">Сменившееся свойство.</param>
    private void Edited([CallerMemberName] string? property = null)
    {
        PropertyChanged.Raise(this, property, nameof(HasChanges));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
