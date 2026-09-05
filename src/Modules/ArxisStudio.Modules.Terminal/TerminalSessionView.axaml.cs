using ArxisStudio.Controls;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Вид одного сеанса: экран оболочки и строка о её конце.
/// </summary>
/// <remarks>
/// Строку показывают один раз и насовсем: оболочка, которая вышла или не
/// поднялась, второго слова о себе не скажет, а первое человек должен успеть
/// прочитать.
/// <para>
/// Меню экрана живёт здесь же: всё, что оно делает, оно делает с этим экраном
/// и ни о чём вокруг не знает.
/// </para>
/// </remarks>
public partial class TerminalSessionView : AxUserControl
{
    /// <summary>Собирает вид из разметки.</summary>
    public TerminalSessionView()
    {
        InitializeComponent();

        Copy.Click += (_, _) => _ = Screen.CopyAsync();
        Paste.Click += (_, _) => _ = Screen.PasteAsync();
        SelectAll.Click += (_, _) => Screen.SelectAll();
        Clear.Click += (_, _) => Screen.ClearScreen();

        // Копировать нечего, пока ничего не выделено, а чистить — пока экраном
        // распоряжается полноэкранная программа. Пункты об этом говорят.
        if (Screen.ContextFlyout is AxMenuFlyout menu)
        {
            menu.Opening += (_, _) =>
            {
                Copy.IsEnabled = Screen.HasSelection;
                Clear.IsEnabled = Screen.CanClear;
            };
        }
    }

    /// <summary>Показывает, что стало с оболочкой.</summary>
    /// <param name="reason">Что сказать человеку.</param>
    internal void Say(string reason)
    {
        Reason.Text = reason;
        Note.IsVisible = true;
    }
}
