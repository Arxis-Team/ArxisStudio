using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Docking;

/// <summary>
/// Оторванное окно: своё дерево доков и своя полоса заголовка.
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

        View = new DockView();

        Content = new DockPanel
        {
            Children =
            {
                new AxTitleBar { [DockPanel.DockProperty] = Dock.Top, Height = 38 },
                View,
            },
        };

        // Заголовок окна идёт за выбранной вкладкой: она в нём и показана.
        View.Chosen += (_, _) => Retitle();
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

    /// <summary>Берёт заголовок у показанной вкладки.</summary>
    public void Retitle()
    {
        if (View.Root is not { } root)
            return;

        var shown = root.Groups().Select(group => group.Selected).FirstOrDefault(item => item is not null);

        Title = View.Items?.Find(shown)?.Title ?? Title;
    }
}
