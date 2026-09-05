using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Shell.Localization;

namespace ArxisStudio.Services;

/// <summary>
/// Открытые документы студии: кто открыт, кто показан и кого закрыть.
/// </summary>
/// <remarks>
/// Оболочка не знает ни одного расширения: какой модуль возьмётся за файл,
/// решает объявленный им тип файла. Панель проекта просит «открой этот путь» —
/// и на этом её знание о содержимом кончается.
/// <para>
/// Служба, а не часть окна. Правил здесь много — «файл уже открыт», «показан
/// ровно один», «документы выгружаемого плагина закрываются», — и каждое
/// прежде проверялось только глазами: чтобы дойти до кода, надо было поднять
/// главное окно со всеми плагинами. Список документов и полоса вкладок при
/// этом жили порознь и однажды разъехались.
/// </para>
/// </remarks>
public sealed class StudioDocuments
{
    private readonly List<OpenDocument> _open = [];
    private readonly StudioDock _dock;
    private readonly Func<string, EditorMatch?> _editorFor;
    private readonly IStudioStatus _status;

    private DocumentView? _shown;

    /// <summary>
    /// Заводит службу над полосой вкладок и вкладами плагинов.
    /// </summary>
    /// <param name="dock">Раскладка, в которой стоят вкладки документов.</param>
    /// <param name="editorFor">Кто возьмётся за файл; null — никто.</param>
    /// <param name="status">Куда говорить о ходе открытия.</param>
    /// <remarks>
    /// Выбор и закрытие вкладки служба слушает сама: связь «вкладка —
    /// документ» её и есть, и разнеси её по двум местам, эти два места
    /// разъедутся.
    /// </remarks>
    public StudioDocuments(StudioDock dock, Func<string, EditorMatch?> editorFor, IStudioStatus status)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ArgumentNullException.ThrowIfNull(editorFor);
        ArgumentNullException.ThrowIfNull(status);

        _dock = dock;
        _editorFor = editorFor;
        _status = status;

        _dock.Chosen += (_, id) => Show(id);
        _dock.Closing += async (_, id) => await CloseAsync(id);
    }

    /// <summary>
    /// Файл открывают: путь известен, редактор — ещё нет.
    /// </summary>
    /// <remarks>
    /// Сообщается до поиска редактора, потому что редактора может ещё и не
    /// быть: плагин, объявивший этот тип файла, ждёт как раз такого события,
    /// чтобы подняться.
    /// </remarks>
    public event EventHandler<string>? Opening;

    /// <summary>Открытые документы, в порядке открытия.</summary>
    public IReadOnlyList<OpenDocument> Opened => _open;

    /// <summary>Показанный документ; null — не показан ни один.</summary>
    public DocumentView? Shown => _shown;

    /// <summary>
    /// Имя документа в раскладке.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <remarks>
    /// Путь и есть имя: два документа одного файла студии не нужны, а сравнение
    /// путей — единственное, чем «этот файл уже открыт» и проверяется.
    /// </remarks>
    public static string Name(string filePath) => $"doc:{filePath}";

    /// <summary>
    /// Открывает файл во вкладке, спросив редактор у реестра вкладов.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    public async Task OpenAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var id = Name(filePath);

        if (_open.Any(document => string.Equals(document.Id, id, StringComparison.Ordinal)))
        {
            _dock.Show(id);
            return;
        }

        Opening?.Invoke(this, filePath);

        if (_editorFor(filePath) is not { } match)
        {
            _status.Show(Localizer.Instance["editor.noeditor"]);
            return;
        }

        _status.Show(Localizer.Instance["editor.loading"]);

        var (view, error) = await match.Editor.OpenAsync(filePath);

        if (view is null)
        {
            _status.Show($"{Localizer.Instance["editor.loadfailed"]}: {error}");
            return;
        }

        // Показывать отдельно нечего: раскладка кончает открытие показом, а
        // показ приходит сюда же выбором вкладки. Свой вызов рядом был бы
        // вторым источником того же правила — и разошёлся бы с первым.
        _open.Add(new OpenDocument(id, filePath, view, match.PluginId));
        _dock.Open(match.PluginId, id, view.Title, view.Content);
    }

    /// <summary>
    /// Показывает выбранный документ, если выбран документ.
    /// </summary>
    /// <param name="id">Имя выбранной вкладки; null — не выбрано ничего.</param>
    /// <remarks>
    /// Выбор приходит на любую вкладку, а не только на документную: щелчок по
    /// панели внизу документ не меняет и не обязан менять. Поэтому чужое имя
    /// здесь просто ни к чему не приводит.
    /// </remarks>
    public void Show(string? id)
    {
        var document = id is null
            ? null
            : _open.FirstOrDefault(open => string.Equals(open.Id, id, StringComparison.Ordinal));

        if (document is null || ReferenceEquals(_shown, document.View))
            return;

        _shown?.OnDeactivated();
        _shown = document.View;
        _shown.OnActivated();

        _status.Show(document.Path);
    }

    /// <summary>Закрывает документ по просьбе человека — крестиком на вкладке.</summary>
    /// <param name="id">Имя документа в раскладке.</param>
    public async Task CloseAsync(string id)
    {
        if (_open.FirstOrDefault(open => string.Equals(open.Id, id, StringComparison.Ordinal))
            is not { } document)
        {
            return;
        }

        await ReleaseAsync(document);

        // Место закрытого документа занял сосед — его и показываем.
        Show(_dock.Showing);
    }

    /// <summary>
    /// Закрывает документы, открытые редактором этого плагина.
    /// </summary>
    /// <param name="pluginId">Чьи документы.</param>
    /// <remarks>
    /// Представление документа построил плагин, и живёт оно в его контексте
    /// загрузки. Оставить вкладку открытой значит и держать контекст, и
    /// показывать человеку окно, за которым уже ничего нет.
    /// </remarks>
    public async Task CloseOwnedByAsync(string pluginId)
    {
        foreach (var document in _open.Where(document => document.PluginId == pluginId).ToList())
            await ReleaseAsync(document);

        Show(_dock.Showing);
    }

    /// <summary>
    /// Закрывает все документы — студию закрывают.
    /// </summary>
    /// <remarks>
    /// Вкладки при этом не убираются: раскладку студия сохраняет как есть, а
    /// окно уходит целиком. Отпустить надо сами представления — за ними стоят
    /// файлы и подписки редакторов.
    /// </remarks>
    public async Task CloseAllAsync()
    {
        _shown?.OnDeactivated();
        _shown = null;

        foreach (var document in _open)
            await document.View.DisposeAsync();

        _open.Clear();
    }

    /// <summary>Убирает документ отовсюду и отпускает его представление.</summary>
    private async Task ReleaseAsync(OpenDocument document)
    {
        if (ReferenceEquals(_shown, document.View))
        {
            _shown.OnDeactivated();
            _shown = null;
        }

        _open.Remove(document);
        _dock.Remove(document.Id);

        await document.View.DisposeAsync();
    }
}

/// <summary>Открытый документ.</summary>
/// <param name="Id">
/// Имя документа в раскладке. По имени, а не по номеру: номер разъезжается,
/// стоит отсеять хоть одну вкладку, — от этого и умирала прежняя связь
/// списка документов с полосой вкладок.
/// </param>
/// <param name="Path">Путь к файлу.</param>
/// <param name="View">Представление, построенное редактором.</param>
/// <param name="PluginId">
/// Чей редактор его открыл: при перезагрузке плагина документ придётся
/// закрыть — иначе останется вкладка, за которой стоит объект из
/// выгруженного контекста.
/// </param>
public sealed record OpenDocument(string Id, string Path, DocumentView View, string PluginId);
