using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Docking;

/// <summary>
/// Оторванное окно: своё дерево доков, а вместо полосы заголовка — полоса
/// вкладок.
/// </summary>
/// <remarks>
/// Живые панели у него общие с главным окном — тот же
/// <see cref="DockView.Items"/>. Контрол не копируется и не строится заново:
/// у контрола Avalonia ровно один родитель, и панель просто переезжает вместе с
/// именем, сохраняя прокрутку, выделение и всё, что помнит о себе сама.
/// <para>
/// Заголовок окна — подпись показанной вкладки: другого имени у окна с одной
/// панелью нет, а «ArxisStudio» в третий раз человеку ничего не говорит.
/// Своего текста у окна при этом нет — подпись приходит из панели.
/// </para>
/// <para>
/// Отдельной полосы заголовка у окна нет: она стояла бы пустой поверх полосы
/// вкладок и съедала бы четверть невысокого окна ради трёх кнопок. Кнопки
/// окна стоят в правом краю полосы вкладок, за её пустое место окно двигают,
/// а двойным щелчком по нему разворачивают — так же это устроено и у Unity.
/// </para>
/// </remarks>
public class DockFloat : AxWindow
{
    /// <summary>Заводит окно с деревом внутри.</summary>
    public DockFloat()
    {
        Width = 420;
        Height = 320;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        View = new DockView { Actions = () => new AxWindowControls() };

        // Своей полосы заголовка у окна нет: полоса вкладок и есть его
        // заголовок, а кнопки окна стоят в её правом краю. Отдельная полоса
        // поверх неё стояла бы пустой и съедала бы четверть невысокого окна
        // ради трёх кнопок — так же это устроено и у Unity.
        Content = View;

        // Заголовок окна идёт за выбранной вкладкой: она в нём и показана.
        View.Chosen += (_, _) => Retitle();
        View.Grabbed += (_, e) => Grab(e);
    }

    /// <summary>Дерево этого окна.</summary>
    public DockView View { get; }

    /// <summary>Место и размер окна — в том виде, в каком они лягут в файл.</summary>
    /// <remarks>
    /// Точка экрана, а не окна-владельца: у оторванного окна владельца нет, и
    /// человек волен унести его на второй монитор.
    /// </remarks>
    public DockWindow Snapshot() => new()
    {
        Root = View.Root ?? new DockGroup { Id = "float" },
        X = Position.X,
        Y = Position.Y,
        Width = Width,
        Height = Height,
    };

    /// <summary>Ставит окно туда и такого размера, как записано.</summary>
    /// <param name="window">Запись из файла раскладки.</param>
    public void Restore(DockWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        View.Root = window.Root;
        Width = window.Width;
        Height = window.Height;
        Position = new PixelPoint((int)window.X, (int)window.Y);

        Retitle();
    }

    /// <summary>
    /// Двигает окно за пустое место шапки, двойным щелчком разворачивает.
    /// </summary>
    /// <remarks>
    /// То же, что делает <c>AxTitleBar</c> у прочих окон: своей полосы
    /// заголовка здесь нет, и её работу берёт полоса вкладок.
    /// </remarks>
    private void Grab(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

            e.Handled = true;

            return;
        }

        BeginMoveDrag(e);
    }

    /// <summary>Берёт заголовок у показанной вкладки.</summary>
    public void Retitle()
    {
        if (View.Root is not { } root)
            return;

        var shown = root.Groups().Select(group => group.Selected).FirstOrDefault(item => item is not null);

        Title = View.Items?.Find(shown)?.Title ?? Title;
    }
}
