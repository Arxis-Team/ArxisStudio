using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Modules.Project.History;

/// <summary>
/// Открытые окна локальной истории — по окну на путь: второе открытие того же пути выводит первое.
/// </summary>
/// <remarks>
/// Окна держат службу истории и словари модуля, и панель, прощаясь, закрывает их все: иначе окно
/// пережило бы того, кто его открыл.
/// </remarks>
internal sealed class HistoryWindows
{
    private readonly Dictionary<CanonicalPath, HistoryWindow> _open = [];

    /// <summary>Открытые окна.</summary>
    public IReadOnlyCollection<HistoryWindow> All => _open.Values;

    /// <summary>Выводит окно истории пути; нет его — открывает новое.</summary>
    /// <param name="path">Чья история.</param>
    /// <param name="open">Как открыть окно, если его нет.</param>
    public void Show(CanonicalPath path, Func<HistoryWindow> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        if (_open.TryGetValue(path, out var shown))
        {
            shown.Activate();
            return;
        }

        var window = open();

        _open[path] = window;
        window.Closed += (_, _) => _open.Remove(path);
    }

    /// <summary>Закрывает все окна.</summary>
    public void CloseAll()
    {
        foreach (var window in _open.Values.ToList())
            window.Close();

        _open.Clear();
    }
}
