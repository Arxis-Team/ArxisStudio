using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ArxisStudio.Extensibility;
using ArxisStudio.Shell.Localization;
using Avalonia.Threading;

namespace ArxisStudio;

/// <summary>
/// Модель главного окна: то, что окно рассказывает о себе самом.
/// </summary>
/// <remarks>
/// Панели, документы и вклады сюда не входят — ими распоряжаются службы, а
/// окно их только размещает. Здесь остаётся строка состояния и полоса
/// идущей задачи: два места, которые прежде окно обновляло вручную, доставая
/// именованные части разметки и присваивая им значения по одному.
/// <para>
/// Разница не в красоте. Правило «показываем свежую задачу, об остальных
/// говорит счётчик» жило в методе, который нельзя вызвать без окна, и
/// проверить его было нечем; теперь оно живёт в объекте, который собирается
/// одной строкой.
/// </para>
/// </remarks>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly StudioTaskRegistry _tasks;
    private string? _status;

    /// <summary>
    /// Заводит модель над реестром задач студии.
    /// </summary>
    /// <param name="tasks">Реестр, из которого берётся идущая задача.</param>
    public MainWindowViewModel(StudioTaskRegistry tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        _tasks = tasks;
        _tasks.Changed += (_, _) => Publish();

        // «Готово» — тоже текст студии, и смену языка он обязан пережить.
        Localizer.Instance.PropertyChanged += (_, _) => Notify(nameof(Status));
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Строка состояния; пусто — «готово» на языке студии.
    /// </summary>
    /// <remarks>
    /// Умолчание держит модель, а не разметка: привязка, заданная в разметке,
    /// умирает от первого же присваивания текста — так эта строка и переставала
    /// следовать за языком после первого сообщения.
    /// </remarks>
    public string Status => _status is { Length: > 0 } said ? said : Localizer.Instance["studio.ready"];

    /// <summary>Идёт ли сейчас задача.</summary>
    public bool HasTask => Current is not null;

    /// <summary>Чем занята задача — её имя.</summary>
    public string TaskTitle => Current?.Title ?? string.Empty;

    /// <summary>Что задача говорит о себе сейчас.</summary>
    public string TaskMessage => Current?.Message ?? string.Empty;

    /// <summary>Задача не сообщает доли — полоса идёт бегущей.</summary>
    public bool IsTaskIndeterminate => Current?.Fraction is null;

    /// <summary>Доля выполненного в процентах.</summary>
    public double TaskProgress => (Current?.Fraction ?? 0) * 100;

    /// <summary>Отмену уже попросили — второй раз просить нечего.</summary>
    public bool CanCancelTask => Current is { IsCancelling: false };

    /// <summary>Есть ли задачи, кроме показанной.</summary>
    public bool HasMoreTasks => _tasks.Running.Count > 1;

    /// <summary>Сколько задач осталось за кадром — «+2».</summary>
    public string MoreTasks => HasMoreTasks
        ? string.Create(CultureInfo.InvariantCulture, $"+{_tasks.Running.Count - 1}")
        : string.Empty;

    /// <summary>
    /// Показывает сообщение в строке состояния.
    /// </summary>
    /// <param name="message">Что сказать; пусто — вернуться к «готово».</param>
    public void Say(string? message)
    {
        _status = message;
        Notify(nameof(Status));
    }

    /// <summary>
    /// Отменяет задачу, которую человек видит.
    /// </summary>
    /// <remarks>
    /// Ту же, что показана: отменять невидимую значило бы отвечать не на то
    /// нажатие.
    /// </remarks>
    public void CancelTask() => Current?.Cancel();

    /// <summary>
    /// Показывается свежая задача.
    /// </summary>
    /// <remarks>
    /// Она та, ради которой человек только что что-то нажал. Об остальных
    /// говорит счётчик — строка состояния узкая, а список задач студии пока не
    /// нужен: заводить его стоит, когда задач станет столько, что счётчик
    /// перестанет отвечать на вопрос.
    /// </remarks>
    private StudioTask? Current => _tasks.Running is { Count: > 0 } running ? running[^1] : null;

    /// <summary>
    /// Объявляет заново всё, что зависит от задач.
    /// </summary>
    /// <remarks>
    /// Задачи идут не в потоке интерфейса, а привязки читают только в нём —
    /// поэтому объявление переносится на него, если пришло со стороны.
    /// </remarks>
    private void Publish()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Publish);
            return;
        }

        Notify(nameof(HasTask));
        Notify(nameof(TaskTitle));
        Notify(nameof(TaskMessage));
        Notify(nameof(IsTaskIndeterminate));
        Notify(nameof(TaskProgress));
        Notify(nameof(CanCancelTask));
        Notify(nameof(HasMoreTasks));
        Notify(nameof(MoreTasks));
    }

    private void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
