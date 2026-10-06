using System.Runtime.CompilerServices;
using ArxisStudio.Sdk;
using Avalonia.Threading;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Холст впереди — вкладки формы или доски: тот, с которым работали последним, пока его видно. За ним идут
/// панели модуля — иерархия.
/// </summary>
/// <remarks>
/// <para>
/// <b>Работали — значит, фокус пришёл внутрь вида его хозяина</b> (<see cref="IFormCanvasHost.HostView"/>) — полосы,
/// холста или XAML — <b>или там нажали указатель</b>: холст, у которого фокус остался, пока рядом открыли вкладку,
/// второй раз его не получает. Фокус в самой иерархии или в окне проекта холст впереди не меняет — так Document
/// Outline у Visual Studio идёт за документом, а не за панелью, в которой щёлкнули.
/// </para>
/// <para>
/// <b>Видно — значит, вид хозяина стоит в дереве окна.</b> Ушёл с экрана — впереди следующий из тех, с кем
/// работали; только что открытый считается тем, с кем работали последним. Не видно никого — впереди никого.
/// </para>
/// <para>
/// <b>Решает после прохода диспетчера.</b> Док на каждый щелчок по вкладке перестраивает окно, и видимое
/// снимается и ставится за один проход: решение на ходу гоняло бы панели туда и обратно.
/// </para>
/// <para>
/// Один на контекст модуля: вкладки, доску и панели студия строит порознь и подключает к одному контексту, и по
/// нему они находят один и тот же учёт — без статики, которая пережила бы контекст.
/// </para>
/// </remarks>
internal sealed class FormFront
{
    private static readonly ConditionalWeakTable<IStudioContext, FormFront> Owners = new();

    private readonly List<FormCanvas> _recent = [];
    private bool _reviewing;

    private FormFront()
    {
    }

    /// <summary>Холст впереди; null — дизайнера на экране нет.</summary>
    public FormCanvas? Canvas { get; private set; }

    /// <summary>Впереди другой холст или никакого.</summary>
    public event EventHandler? CanvasChanged;

    /// <summary>Учёт контекста модуля.</summary>
    /// <param name="context">Контекст модуля.</param>
    public static FormFront Of(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Owners.GetValue(context, static _ => new FormFront());
    }

    /// <summary>
    /// Холст заведён — вкладку или доску только что открыли, и работать начнут с неё: он встаёт первым и впереди
    /// окажется, когда его вид встанет на экран.
    /// </summary>
    internal void Add(FormCanvas canvas)
    {
        _recent.Remove(canvas);
        _recent.Insert(0, canvas);
        Review();
    }

    /// <summary>Холст убран; стоял впереди — впереди сразу следующий: панели не держат убранного.</summary>
    internal void Remove(FormCanvas canvas)
    {
        if (!_recent.Remove(canvas))
            return;

        if (ReferenceEquals(Canvas, canvas))
            Decide();
        else
            Review();
    }

    /// <summary>С холстом работают: фокус пришёл внутрь вида его хозяина или в нём нажали указатель.</summary>
    /// <remarks>Каждое нажатие на холсте впереди — не перемена: пересчёт заводится, только когда порядок сменился.</remarks>
    internal void Worked(FormCanvas canvas)
    {
        if (_recent.Count > 0 && ReferenceEquals(_recent[0], canvas) && ReferenceEquals(Canvas, canvas))
            return;

        _recent.Remove(canvas);
        _recent.Insert(0, canvas);
        Review();
    }

    /// <summary>Вид хозяина встал на экран или ушёл с него: кто впереди, решится после прохода.</summary>
    internal void Review()
    {
        if (_reviewing)
            return;

        _reviewing = true;
        Dispatcher.UIThread.Post(Decide, DispatcherPriority.Background);
    }

    private void Decide()
    {
        _reviewing = false;

        var front = _recent.Find(static canvas => canvas.IsOnScreen);

        if (ReferenceEquals(front, Canvas))
            return;

        Canvas = front;
        CanvasChanged?.Invoke(this, EventArgs.Empty);
    }
}
