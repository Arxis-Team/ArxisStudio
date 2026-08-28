using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using Avalonia.Input;

namespace ArxisStudio.Modules.Designer;

/// <summary>
/// Общее состояние дизайнера: активный документ и выделение.
/// </summary>
/// <remarks>
/// Панели и представления документов — отдельные контролы в разных зонах окна,
/// и говорить им друг с другом больше не через кого: иерархия показывает дерево
/// активного документа, палитра вставляет в него, инспектор правит выделенный
/// узел. Состояние — их общая доска, и оно одно на модуль, потому что и
/// активный документ в студии один.
/// </remarks>
internal sealed class DesignerState
{
    /// <summary>Единственное состояние модуля.</summary>
    public static DesignerState Instance { get; } = new();

    /// <summary>
    /// Под каким именем контрол палитры едет в перетаскивании.
    /// </summary>
    /// <remarks>
    /// Формат внутрипроцессный: палитра и канва — одно окно, и превращать
    /// контрол в текст ради дороги длиной в сантиметр незачем.
    /// </remarks>
    public static readonly DataFormat<ToolboxItem> ToolboxFormat =
        DataFormat.CreateInProcessFormat<ToolboxItem>("arxis.toolbox.item");

    // Выбор в дереве, на канве и в инспекторе синхронизируются по кругу, и
    // каждый поднимает событие другого. Флаг гасит это эхо.
    private bool _syncing;

    /// <summary>Активный документ сменился или перечитал своё дерево.</summary>
    public event Action? ActiveChanged;

    /// <summary>Выделение сменилось; аргумент — кто его сменил.</summary>
    public event Action<object?>? SelectionChanged;

    /// <summary>Документ изменился: значения, история, признак несохранённого.</summary>
    public event Action? Mutated;

    /// <summary>Что студия дала модулю; null до активации.</summary>
    public IStudioContext? Context { get; private set; }

    /// <summary>Представление активного документа; null, если вкладок нет.</summary>
    public DesignerDocumentView? Active { get; private set; }

    /// <summary>Выделенный узел активного документа.</summary>
    public HierarchyNode? Selected { get; private set; }

    /// <summary>Модель решения; null, пока студия её не дала.</summary>
    public IDesignerWorkspace? Workspace => Context?.GetService<IDesignerWorkspace>();

    /// <summary>Вклады плагинов — рисовальщики и свои инспекторы.</summary>
    public PluginContributionRegistry? Contributions => Context?.GetService<PluginContributionRegistry>();

    /// <summary>
    /// Шов вызовов плагинов; null, если студия его не дала.
    /// </summary>
    /// <remarks>
    /// Инспектор — единственное место модуля, где чужой код строит контрол, и
    /// зовёт он его тем же швом, что и оболочка: падение рисовальщика должно
    /// считаться там же, где падение панели, иначе сломанный плагин отключат
    /// только за половину своих сбоев.
    /// </remarks>
    public PluginGuard? Guard => Context?.GetService<PluginGuard>();

    /// <summary>Принимает контекст студии при активации модуля.</summary>
    /// <param name="context">Что студия даёт модулю.</param>
    public void Attach(IStudioContext context) => Context = context;

    /// <summary>Отпускает контекст при выключении модуля.</summary>
    public void Detach()
    {
        Context = null;
        Active = null;
        Selected = null;
    }

    /// <summary>Пишет сообщение в строку состояния студии.</summary>
    /// <param name="message">Что показать.</param>
    public void Status(string message) => Context?.GetService<IStudioStatus>()?.Show(message);

    /// <summary>Пишет строку в журнал студии.</summary>
    /// <param name="level">Уровень записи.</param>
    /// <param name="message">Сообщение.</param>
    public void Log(StudioLogLevel level, string message) =>
        Context?.Log.Write(level, "Designer", message);

    /// <summary>
    /// Сообщает студии, что не так с разметкой документа.
    /// </summary>
    /// <remarks>
    /// Источник назван по файлу: документов открыто несколько, и находка про
    /// один не должна снимать находку про другой. Пустое сообщение снимает
    /// прежнюю — разметка разобралась.
    /// </remarks>
    /// <param name="filePath">Файл документа.</param>
    /// <param name="found">Находки разбора; пустой список снимает прежние.</param>
    public void Problems(string filePath, IEnumerable<DocumentProblem> found)
    {
        if (Context?.GetService<IStudioProblems>() is not { } problems)
            return;

        problems.Report(
            $"designer:{filePath}",
            found.Select(problem => new StudioProblem(
                problem.IsError ? StudioProblemSeverity.Error : StudioProblemSeverity.Warning,
                problem.Code,
                problem.Message,
                filePath,
                problem.Line,
                problem.Column)));
    }

    /// <summary>Объявляет активным другое представление документа.</summary>
    /// <param name="view">Представление или null, когда вкладок не осталось.</param>
    public void SetActive(DesignerDocumentView? view)
    {
        Active = view;
        Selected = null;
        ActiveChanged?.Invoke();
        SelectionChanged?.Invoke(null);
    }

    /// <summary>Дерево активного документа пересобрано.</summary>
    public void NotifyReloaded()
    {
        Selected = null;
        ActiveChanged?.Invoke();
        SelectionChanged?.Invoke(null);
    }

    /// <summary>Документ изменился — панели перечитывают значения.</summary>
    public void NotifyMutated() => Mutated?.Invoke();

    /// <summary>
    /// Меняет выделение.
    /// </summary>
    /// <param name="node">Выделяемый узел или null.</param>
    /// <param name="origin">
    /// Кто выделил: получатели пропускают собственные события, иначе выделение
    /// ходило бы по кругу без конца.
    /// </param>
    public void Select(HierarchyNode? node, object? origin)
    {
        if (_syncing)
            return;

        _syncing = true;
        try
        {
            Selected = node;
            SelectionChanged?.Invoke(origin);
        }
        finally
        {
            _syncing = false;
        }
    }
}
