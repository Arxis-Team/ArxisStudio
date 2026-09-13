using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Esc в диалоге.
/// </summary>
/// <remarks>
/// Обещание стояло в описании <c>AxDialog</c> с первого дня и не выполнялось ни
/// разу: обработчика клавиши в классе не было вовсе. Документация врала, и
/// заметить это можно было только попробовав.
/// <para>
/// Тестов у подмодуля контролов нет, поэтому проверка живёт здесь — там же, где
/// диалогами пользуется студия.
/// </para>
/// </remarks>
public class DialogEscapeTests
{
    /// <summary>Esc закрывает диалог, из которого можно уйти не ответив.</summary>
    [AvaloniaFact]
    public void Escape_closes_a_dialog_one_may_leave()
    {
        var (dialog, closed) = Shown(dismissable: true);

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed(), "Esc обещан описанием класса и обязан работать");
    }

    /// <summary>
    /// Обязательный выбор Esc не отменяет.
    /// </summary>
    /// <remarks>
    /// Крестик у такого диалога снят нарочно: решать придётся кнопкой. Клавиша,
    /// делающая то, чего не делает кнопка, была бы не вторым выходом, а дырой
    /// мимо принятого решения.
    /// </remarks>
    [AvaloniaFact]
    public void Escape_does_not_undo_a_choice_that_must_be_made()
    {
        var (dialog, closed) = Shown(dismissable: false);

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.False(closed(), "из обязательного выбора Esc вывел мимо кнопок");

        dialog.Close();
    }

    /// <summary>
    /// Алерт Esc отпускает: у него есть «Отмена».
    /// </summary>
    /// <remarks>
    /// Шапка у алерта скрыта вместе с крестиком, но это вёрстка, а не смысл:
    /// обязательность объявляет <c>IsCloseVisible</c>. Вопрос «вы уверены?»
    /// строится алертом, и Esc там делает ровно то же, что кнопка «Отмена».
    /// </remarks>
    [AvaloniaFact]
    public void An_alert_still_answers_escape()
    {
        var (dialog, closed) = Shown(dismissable: true, alert: true);

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed(), "у алерта с «Отменой» Esc обязан делать то же, что она");
    }

    /// <summary>
    /// Esc отвечает «нет» на вопрос о необратимом.
    /// </summary>
    /// <remarks>
    /// Это и есть то обещание, которое читает человек: «Esc и Отмена оставляют
    /// всё как было». Держится оно на том, что <c>Close</c> без результата даёт
    /// <c>null</c>, а <c>null == true</c> — это <c>false</c>; проверять такое
    /// чтением кода мало, потому что сломать его может правка в любом из двух
    /// мест.
    /// <para>
    /// Со сторожем: диалог модальный, и не отозвавшийся на клавишу повесил бы
    /// весь прогон вместо того, чтобы честно упасть.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public async Task Escape_answers_no_to_a_question_about_the_irreversible()
    {
        var owner = new Window { Width = 400, Height = 300 };

        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var asking = StudioAsk.ConfirmAsync(owner, "Удалить", "Точно?", "Удалить", danger: true);

        Dispatcher.UIThread.RunJobs();

        var dialog = Assert.Single(owner.OwnedWindows.OfType<AxDialog>());

        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        var guarded = await Task.WhenAny(asking, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(asking, guarded);
        Assert.False(await asking, "Esc согласился на необратимое");

        owner.Close();
    }

    /// <summary>Показанный диалог и способ спросить, закрылся ли он.</summary>
    /// <param name="dismissable">Можно ли уйти, не ответив.</param>
    /// <param name="alert">Диалог со значком: шапка уступает место колонке.</param>
    private static (AxDialog Dialog, Func<bool> Closed) Shown(bool dismissable, bool alert = false)
    {
        var closed = false;

        var dialog = new AxDialog
        {
            Title = "Вопрос",
            Content = new TextBlock { Text = "Так или иначе?" },
            IsCloseVisible = dismissable,
            AlertIcon = alert ? new AxIcon { Data = AxIcons.Warning } : null,
            Width = 320,
            Height = 160,
        };

        dialog.Closed += (_, _) => closed = true;

        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        return (dialog, () => closed);
    }
}
