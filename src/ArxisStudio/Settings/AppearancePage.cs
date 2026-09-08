using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Icons;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia.Media;

namespace ArxisStudio.Settings;

/// <summary>
/// Страница «Оформление»: тема студии и язык интерфейса.
/// </summary>
/// <remarks>
/// Единственная страница, чьи правки видны до сохранения, и это не небрежность,
/// а условие задачи: тему и язык выбирают глазами. Поэтому обе применяются
/// сразу — предпросмотром, — а записываются только по «Сохранить»; «Отмена»
/// возвращает то, что было при открытии окна или с последней записи: удавшееся
/// «Сохранить» и есть новое «как было».
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
    private string _languageAtOpen;
    private StudioTheme _theme;
    private string _language;

    /// <summary>Заводит страницу поверх настроек студии.</summary>
    /// <param name="studio">Хранилище настроек студии.</param>
    public AppearancePage(ISettingsStore studio)
    {
        ArgumentNullException.ThrowIfNull(studio);

        _studio = studio;
        _theme = _themeAtOpen = studio.Current.Theme;
        _language = _languageAtOpen = Localizer.Instance.Language;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

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
    public IEnumerable<string> Terms =>
    [
        Title,
        Localizer.Instance["settings.theme"],
        Localizer.Instance["settings.language"],
    ];

    /// <inheritdoc/>
    public bool HasChanges => _theme != _themeAtOpen || !string.Equals(_language, _languageAtOpen, StringComparison.Ordinal);

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
            Notify();
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
                Notify();
                return;
            }

            _language = wanted.Code;
            Notify();
            Relabelled?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void Commit(ICollection<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);

        if (!HasChanges)
            return;

        _studio.Current.Theme = _theme;
        _studio.Current.Language = _language;

        // Запись одна на обе настройки: хранилище сериализует весь объект, и
        // два вызова записали бы один и тот же файл дважды. Своей охраны у
        // него нет — исключение из WriteAllText вышло бы наружу и уронило бы
        // сохранение целиком, поэтому ловим здесь.
        try
        {
            _studio.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{Localizer.Instance["settings.title"]}: {e.Message}");
            return;
        }

        // Записанное становится тем, «что было при открытии». Иначе страница
        // остаётся изменённой после «Сохранить», и закрытие — а «Сохранить»
        // закрывает окно — спрашивает о потере правок, которые уже в файле;
        // ответив «закрыть», человек получал бы откат темы и языка к прежним
        // при уже переписанном файле.
        //
        // Только после удавшейся записи: не легло — значит не сохранено, и
        // окно обязано остаться при своих правках.
        _themeAtOpen = _theme;
        _languageAtOpen = _language;
    }

    /// <inheritdoc/>
    public void Revert()
    {
        if (_theme != _themeAtOpen)
        {
            _theme = _themeAtOpen;
            StudioTheming.Apply(_themeAtOpen);
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

        Notify();
    }

    private void Notify([CallerMemberName] string? property = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChanges)));
    }
}
