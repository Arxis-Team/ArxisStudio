using System.ComponentModel;
using ArxisStudio.Extensibility;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Модель главного окна: строка состояния и полоса идущей задачи.
/// </summary>
/// <remarks>
/// Правила показа прежде жили в методе окна и присваивали значения именованным
/// частям разметки. Проверить их было нечем: чтобы дойти до метода, надо было
/// поднять главное окно со всеми плагинами. Здесь та же работа проверяется без
/// окна вообще — в этом и была цель переноса.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class MainWindowViewModelTests : IDisposable
{
    /// <summary>Возвращает студии язык, на котором её застали.</summary>
    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        GC.SuppressFinalize(this);
    }

    /// <summary>Молчащая студия говорит, что готова, — на своём языке.</summary>
    /// <remarks>
    /// Умолчание держит модель, а не разметка: привязку, заданную в разметке,
    /// первое же присваивание текста снимало навсегда — так строка и переставала
    /// следовать за языком после первого сообщения.
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_status_says_the_studio_is_ready()
    {
        var model = new MainWindowViewModel(new StudioTaskRegistry());

        Assert.Equal(Localizer.Instance["studio.ready"], model.Status);

        model.Say("открываю проект");
        Assert.Equal("открываю проект", model.Status);

        model.Say(null);
        Assert.Equal(Localizer.Instance["studio.ready"], model.Status);
    }

    /// <summary>Сообщение объявляется — иначе привязка о нём не узнает.</summary>
    [AvaloniaFact]
    public void Saying_something_announces_the_status()
    {
        var model = new MainWindowViewModel(new StudioTaskRegistry());
        var said = Heard(model);

        model.Say("готово наполовину");

        Assert.Contains(nameof(model.Status), said);
    }

    /// <summary>Пока задач нет, полосы задачи нет.</summary>
    [AvaloniaFact]
    public void Without_tasks_there_is_no_task_strip()
    {
        var model = new MainWindowViewModel(new StudioTaskRegistry());

        Assert.False(model.HasTask);
        Assert.False(model.HasMoreTasks);
        Assert.Equal(string.Empty, model.MoreTasks);
    }

    /// <summary>
    /// Показывается свежая задача, а об остальных говорит счётчик.
    /// </summary>
    /// <remarks>
    /// Свежая — та, ради которой человек только что что-то нажал. Показывать
    /// первую значило бы отвечать не на то действие.
    /// </remarks>
    [AvaloniaFact]
    public void The_freshest_task_is_the_one_shown()
    {
        var tasks = new StudioTaskRegistry();
        var model = new MainWindowViewModel(tasks);

        tasks.Start("arxis.first", "Обход проекта");
        tasks.Start("arxis.second", "Загрузка пакетов");

        Assert.True(model.HasTask);
        Assert.Equal("Загрузка пакетов", model.TaskTitle);
        Assert.True(model.HasMoreTasks);
        Assert.Equal("+1", model.MoreTasks);
    }

    /// <summary>Кончилась задача — ушла и полоса.</summary>
    [AvaloniaFact]
    public void A_finished_task_takes_the_strip_with_it()
    {
        var tasks = new StudioTaskRegistry();
        var model = new MainWindowViewModel(tasks);
        var task = tasks.Start("arxis.demo", "Обход проекта");

        Assert.True(model.HasTask);

        tasks.Finish(task);

        Assert.False(model.HasTask);
        Assert.Equal(string.Empty, model.TaskTitle);
    }

    /// <summary>Ход задачи доходит до полосы долей и словами.</summary>
    [AvaloniaFact]
    public void The_progress_of_a_task_reaches_the_strip()
    {
        var tasks = new StudioTaskRegistry();
        var model = new MainWindowViewModel(tasks);
        var task = tasks.Start("arxis.demo", "Обход проекта");

        Assert.True(model.IsTaskIndeterminate);

        task.Report(0.25, "четверть");

        Assert.False(model.IsTaskIndeterminate);
        Assert.Equal(25d, model.TaskProgress);
        Assert.Equal("четверть", model.TaskMessage);
    }

    /// <summary>
    /// Отмена уходит той задаче, которую человек видит.
    /// </summary>
    /// <remarks>
    /// Отменять невидимую значило бы отвечать не на то нажатие: на полосе одно
    /// имя, и человек просит остановить именно его.
    /// </remarks>
    [AvaloniaFact]
    public void Cancelling_asks_the_task_that_is_shown()
    {
        var tasks = new StudioTaskRegistry();
        var model = new MainWindowViewModel(tasks);
        var older = tasks.Start("arxis.first", "Обход проекта");
        var shown = tasks.Start("arxis.second", "Загрузка пакетов");

        Assert.True(model.CanCancelTask);

        model.CancelTask();

        Assert.True(shown.IsCancelling);
        Assert.False(older.IsCancelling);
        Assert.False(model.CanCancelTask);
    }

    /// <summary>Отменять нечего — просьба ничего не ломает.</summary>
    [AvaloniaFact]
    public void Cancelling_nothing_is_not_a_failure()
    {
        var model = new MainWindowViewModel(new StudioTaskRegistry());

        model.CancelTask();

        Assert.False(model.HasTask);
    }

    /// <summary>
    /// Весть о задаче, пришедшая со стороны, доходит до привязок.
    /// </summary>
    /// <remarks>
    /// Задачи идут не в потоке интерфейса — на то они и фоновые, — а привязки
    /// читают модель только в нём. Объявление, сделанное с чужого потока,
    /// Avalonia не примет; поэтому модель переносит его на поток интерфейса
    /// сама, и проверяется здесь именно перенос: объявления ждут очереди
    /// потока, а не приходят на месте.
    /// </remarks>
    [AvaloniaFact]
    public void News_from_another_thread_reaches_the_bindings()
    {
        var tasks = new StudioTaskRegistry();
        var model = new MainWindowViewModel(tasks);
        var heard = Heard(model);

        // Своим потоком, а не пулом: ожидание задачи пула вернулось бы сюда
        // через очередь того же потока интерфейса и заодно прокрутило бы её —
        // тогда «объявление ещё ждёт» проверить было бы нечем.
        var thread = new Thread(() => tasks.Start("arxis.demo", "Обход проекта"))
        {
            IsBackground = true,
        };

        thread.Start();
        thread.Join();

        Assert.Empty(heard);

        Dispatcher.UIThread.RunJobs();

        Assert.Contains(nameof(model.HasTask), heard);
        Assert.True(model.HasTask);
    }

    /// <summary>
    /// Смена языка возвращает строку состояния на новый язык.
    /// </summary>
    /// <remarks>
    /// Ровно этого не умела прежняя строка: «готово» стояло привязкой в
    /// разметке и следовало за языком, пока первое сообщение эту привязку не
    /// снимало. Дальше окно молчало на языке, выбранном до запуска.
    /// </remarks>
    [AvaloniaFact]
    public void A_change_of_language_reaches_the_ready_line()
    {
        var model = new MainWindowViewModel(new StudioTaskRegistry());
        var heard = Heard(model);

        Localizer.Instance.SetLanguage("ru");

        Assert.Contains(nameof(model.Status), heard);
        Assert.Equal(Localizer.Instance["studio.ready"], model.Status);
    }

    /// <summary>Записывает всё, что модель объявляет о себе.</summary>
    /// <param name="model">За кем слушать.</param>
    private static List<string> Heard(MainWindowViewModel model)
    {
        var heard = new List<string>();

        model.PropertyChanged += (_, e) => heard.Add(e.PropertyName ?? string.Empty);

        return heard;
    }
}
