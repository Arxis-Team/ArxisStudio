using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.ViewModels;

/// <summary>
/// Модель заставки: что студия рассказывает о себе, пока поднимается.
/// </summary>
/// <remarks>
/// Это и есть договор между запуском и картинкой. Запуск знает про этапы и
/// ничего не знает про то, как они выглядят; оформление релиза знает про
/// свойства этой модели и ничего — про то, что за ними делается. Поэтому
/// заставку можно перерисовать к новой версии, не трогая ни строки запуска, и
/// наоборот.
/// <para>
/// Проверяется без окна: правила «доля считается по числу этапов», «до первого
/// этапа полоса бежит» и «версия пишется одной строкой» живут здесь, а не в
/// разметке.
/// </para>
/// </remarks>
public sealed class SplashViewModel : INotifyPropertyChanged, IDisposable
{
    private string _stage = string.Empty;
    private string? _failure;
    private int _done;
    private int _total;

    /// <summary>Заводит модель заставки.</summary>
    /// <remarks>
    /// Язык интерфейса выбирается одним из этапов запуска — то есть уже при
    /// показанной заставке. Строка версии на ней написана словом «сборка», и
    /// без этой подписки она осталась бы на языке, с которого студия начала.
    /// </remarks>
    public SplashViewModel() => Localizer.Instance.PropertyChanged += OnLanguageChanged;

    /// <summary>
    /// Отписывается от языка студии.
    /// </summary>
    /// <remarks>
    /// <c>Localizer</c> один на процесс и живёт дольше заставки. Лямбда,
    /// подписанная в конструкторе, не снималась никогда: модель оставалась жива
    /// подпиской на синглтон, и в прогоне тестов их копилось по одной на каждый.
    /// </remarks>
    public void Dispose() => Localizer.Instance.PropertyChanged -= OnLanguageChanged;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Чем студия занята сейчас.</summary>
    public string Stage => _stage;

    /// <summary>Доля пройденного в процентах.</summary>
    public double Progress => _total > 0 ? (double)_done / _total * 100 : 0;

    /// <summary>
    /// Полоса бежит, а не показывает долю.
    /// </summary>
    /// <remarks>
    /// Пока этапов не объявили, доли не существует: показать ноль значило бы
    /// сказать «сделано нисколько», а честный ответ — «считать пока не по чему».
    /// </remarks>
    public bool IsIndeterminate => _total == 0;

    /// <summary>Релиз и сборка одной строкой: <c>2026.1 · сборка 0.1.1</c>.</summary>
    public string Edition => string.Create(
        CultureInfo.InvariantCulture,
        $"{StudioRelease.Version} · {Localizer.Instance["splash.build"]} {StudioRelease.Build}");

    /// <summary>Права и набор инструментов: <c>© 2026 Arxis · Avalonia 12.1.1</c>.</summary>
    public string Credit => $"{StudioRelease.Copyright} · {StudioRelease.Toolkit}";

    /// <summary>Среда, на которой всё это работает: <c>.NET 10 · x64</c>.</summary>
    public string Runtime => StudioRelease.Runtime;

    /// <summary>
    /// Чем кончился запуск, если не студией; <c>null</c> — идёт как надо.
    /// </summary>
    /// <remarks>
    /// Здесь тип и сообщение исключения, а не перевод: строку эту человек несёт
    /// в отчёт о сбое, и переведённая она там бесполезна. Переводится заголовок
    /// над ней, а не она сама.
    /// </remarks>
    public string? Failure => _failure;

    /// <summary>Запуск кончился без студии.</summary>
    public bool HasFailed => _failure is not null;

    /// <summary>
    /// Студии не будет — и заставке больше нечего ждать.
    /// </summary>
    /// <param name="reason">Тип и сообщение исключения либо своё объяснение.</param>
    /// <remarks>
    /// Подпись этапа подменяется нарочно: имя этапа, на котором всё встало,
    /// читалось бы на экране как «идёт», хотя не идёт уже ничего. В журнале оно
    /// остаётся.
    /// </remarks>
    public void Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        _failure = reason;
        _stage = Localizer.Instance["splash.failed"];

        Notify(nameof(Failure));
        Notify(nameof(HasFailed));
        Notify(nameof(Stage));
    }

    /// <summary>
    /// Объявляет, сколько этапов впереди.
    /// </summary>
    /// <param name="total">Число этапов; ноль — полоса бежит.</param>
    public void Expect(int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        _total = total;
        _done = 0;

        Notify(nameof(IsIndeterminate));
        Notify(nameof(Progress));
    }

    /// <summary>
    /// Начинается очередной этап.
    /// </summary>
    /// <param name="stage">Что студия делает — на языке человека.</param>
    /// <remarks>
    /// Имя объявляется до работы, а доля растёт после неё: человек читает, чем
    /// студия занята, а не чем была занята. Полоса, обогнавшая подпись,
    /// показывала бы сделанным то, что ещё делается.
    /// </remarks>
    public void Begin(string stage)
    {
        _stage = stage ?? string.Empty;
        Notify(nameof(Stage));
    }

    /// <summary>Этап пройден — доля выросла.</summary>
    public void Done()
    {
        if (_total > 0 && _done < _total)
            _done++;

        Notify(nameof(Progress));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => Notify(nameof(Edition));

    private void Notify([CallerMemberName] string? property = null) => PropertyChanged.Raise(this, property);
}
