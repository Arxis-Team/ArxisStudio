using ArxisStudio.Controls;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Клавиатура в вопросе студии: на каком ответе она стоит, когда вопрос открылся.
/// </summary>
/// <remarks>
/// Вопрос открывался без фокуса: Esc до него доходил через корень окна, а Enter и пробел не
/// отвечали ничем, и видно не было, что сделает нажатие.
/// <para>
/// Очередь общая с остальными: подпись «Отмены» берётся у словаря, а <c>Localizer</c> один на
/// процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class QuestionFocusTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>
    /// На необратимом клавиатура стоит на «Отмене», на обратимом — на согласии, и кольцо видно.
    /// </summary>
    /// <remarks>
    /// Случайный Enter не должен соглашаться на то, чего не вернуть. Кольцо видно сразу, без
    /// нажатия Tab: человек должен знать, что сделает Enter, до того как нажмёт.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_question_opens_with_the_keyboard_on_the_answer_it_is_safe_to_give(bool danger)
    {
        var owner = Owner();
        var asking = StudioAsk.ConfirmAsync(owner, "Удалить", "Точно?", "Удалить", danger);

        Dispatcher.UIThread.RunJobs();

        var dialog = Assert.Single(owner.OwnedWindows.OfType<AxDialog>());
        var focused = Assert.IsType<AxButton>(dialog.FocusManager?.GetFocusedElement());
        var expected = danger ? Localizer.Instance["common.cancel"] : "Удалить";

        Assert.Equal(expected, focused.Content);
        Assert.True(focused.Classes.Contains(":focus-visible"), "кольца фокуса на ответе не видно");

        await Answer(dialog, asking);

        owner.Close();
    }

    /// <summary>Окно с кнопкой, из которой спрашивают.</summary>
    private static Window Owner()
    {
        var owner = new Window { Width = 400, Height = 300, Content = new AxButton { Content = "Удалить плагин" } };

        owner.Show();
        Dispatcher.UIThread.RunJobs();

        return owner;
    }

    /// <summary>Отвечает Esc и ждёт ответа — со сторожем: вопрос модальный.</summary>
    private static async Task Answer(Window dialog, Task<bool> asking)
    {
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(asking, await Task.WhenAny(asking, Task.Delay(Patience)));
    }
}
