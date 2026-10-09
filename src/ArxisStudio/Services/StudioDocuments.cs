using System.Globalization;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Shell;
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
/// <para>
/// Редактор и представление документа — код плагина, и зовутся они через шов: открытие, заголовок
/// и содержимое, показ и скрытие — рабочей дорогой, закрытие — прощальной, которая доходит и до
/// отключённого за сбои. Иначе бросающий <c>DisposeAsync</c> обрывал перезагрузку плагина на
/// середине: задачи уже остановлены, остальные документы открыты, хост никого не поднял.
/// </para>
/// <para>
/// <b>Несохранённое и закрытие</b> (SDK 7.14). Отметку несохранённого документ ставит сам, а вкладка
/// носит её точкой. Закрыть документ — крестиком, Ctrl+W, вместе с окном или ради перезапуска — можно,
/// только если он согласен (<see cref="DocumentView.CanCloseAsync"/>), а оставшееся несохранённым
/// человек решает одним вопросом на все (<see cref="Ask"/>). Перед перезапуском вопроса нет:
/// несохранённое сохраняется, потому что новая копия вернёт вкладки.
/// </para>
/// </remarks>
public sealed class StudioDocuments
{
    private readonly List<OpenDocument> _open = [];
    private readonly StudioDock _dock;
    private readonly Func<string, EditorMatch?> _editorFor;
    private readonly IStudioStatus _status;
    private readonly PluginGuard _guard;

    private OpenDocument? _shown;

    /// <summary>
    /// Заводит службу над полосой вкладок и вкладами плагинов.
    /// </summary>
    /// <param name="dock">Раскладка, в которой стоят вкладки документов.</param>
    /// <param name="editorFor">Кто возьмётся за файл; null — никто.</param>
    /// <param name="status">Куда говорить о ходе открытия.</param>
    /// <param name="guard">Шов вызовов плагина; null — завести свой, без счёта на всю студию.</param>
    /// <remarks>
    /// Выбор и закрытие вкладки служба слушает сама: связь «вкладка —
    /// документ» её и есть, и разнеси её по двум местам, эти два места
    /// разъедутся.
    /// </remarks>
    public StudioDocuments(
        StudioDock dock,
        Func<string, EditorMatch?> editorFor,
        IStudioStatus status,
        PluginGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(dock);
        ArgumentNullException.ThrowIfNull(editorFor);
        ArgumentNullException.ThrowIfNull(status);

        _dock = dock;
        _editorFor = editorFor;
        _status = status;
        _guard = guard ?? new PluginGuard();

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
    public DocumentView? Shown => _shown?.View;

    /// <summary>
    /// Вопрос человеку о несохранённых документах — по их именам.
    /// </summary>
    /// <remarks>
    /// Шов ради тестов и ради хозяина вопроса: модальный диалог нужен окну-владельцу, и ставит его
    /// главное окно. Не поставлен — студия сохраняет: молча потерять правку хуже, чем молча её записать.
    /// </remarks>
    public Func<IReadOnlyList<string>, Task<StudioSaveChoice>>? Ask { get; set; }

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

        // Редактор, который держит файл на своей панели, показывает его там, и вкладки нет. Упавший показ
        // — его сбой, и файл открывается вкладкой, как у редактора, не умеющего показывать у себя.
        var revealed = false;

        if (await _guard.RunAsync(match.PluginId, $"показ {Path.GetFileName(filePath)}", async () => revealed = await match.Editor.RevealAsync(filePath))
            && revealed)
        {
            return;
        }

        _status.Show(Localizer.Instance["editor.loading"]);

        // Три чужих вызова одним куском: открытие, заголовок и содержимое. Документ, у которого
        // удалось первое и упало третье, студии не нужен — вкладку из него не собрать.
        DocumentView? view = null;
        string? error = null;
        string? title = null;
        Avalonia.Controls.Control? content = null;
        Avalonia.Controls.Control? focus = null;

        var opened = await _guard.RunAsync(match.PluginId, $"открытие {Path.GetFileName(filePath)}", async () =>
        {
            (view, error) = await match.Editor.OpenAsync(filePath);

            if (view is null)
                return;

            title = view.Title;
            content = view.Content;

            // Цель каретки спрашивается здесь же и один раз — как у панели: документ, отвечающий
            // разное в разное время, получил бы разное поведение на ровном месте.
            focus = view.FocusTarget;
        });

        if (!opened || view is null || title is null || content is null)
        {
            // Представление, построенное и не показанное, отпускается: за ним уже может стоять
            // открытый файл. Причину сбоя назвал шов — в журнале, с именем плагина.
            if (view is not null)
                await _guard.FarewellAsync(match.PluginId, "закрытие недооткрытого документа", async () => await view.DisposeAsync());

            _status.Show(error is null
                ? Localizer.Instance["editor.loadfailed"]
                : $"{Localizer.Instance["editor.loadfailed"]}: {error}");

            return;
        }

        // Показывать отдельно нечего: раскладка кончает открытие показом, а
        // показ приходит сюда же выбором вкладки. Свой вызов рядом был бы
        // вторым источником того же правила — и разошёлся бы с первым.
        var document = new OpenDocument(id, filePath, view, match.PluginId);

        // Цель каретки кладётся раньше, чем вкладка встанет, — как панели, которой её кладут до
        // раскладки. Вкладка, открытая на месте панели с кареткой, получает каретку тут же, на
        // перестройке раскладки, и цель, положенная после, опоздала бы: каретка ушла бы первому
        // внутри, а не туда, где в документе работают.
        if (focus is not null)
            DockFocus.SetTarget(content, focus);

        _open.Add(document);
        _dock.Open(match.PluginId, id, title, content);

        // Отметку документ ставит сам и сам о ней говорит; читается она свойством SDK, а не кодом
        // плагина, и шов ей не нужен.
        view.ModifiedChanged += (_, _) => _dock.Modified(id, document.View.IsModified);
        _dock.Modified(id, view.IsModified);
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

        if (document is null || ReferenceEquals(_shown, document))
            return;

        Hide();

        _shown = document;
        _guard.Run(document.PluginId, "показ документа", document.View.OnActivated);

        _status.Show(document.Path);
    }

    /// <summary>Говорит показанному документу, что его скрыли.</summary>
    private void Hide()
    {
        if (_shown is not { } shown)
            return;

        _shown = null;
        _guard.Run(shown.PluginId, "скрытие документа", shown.View.OnDeactivated);
    }

    /// <summary>Закрывает документ по просьбе человека — крестиком на вкладке или Ctrl+W.</summary>
    /// <param name="id">Имя документа в раскладке.</param>
    /// <remarks>Документ, не согласившийся закрыться, и несохранённое, которое человек не отпустил, оставляют вкладку.</remarks>
    public async Task CloseAsync(string id)
    {
        if (_open.FirstOrDefault(open => string.Equals(open.Id, id, StringComparison.Ordinal))
            is not { } document)
        {
            return;
        }

        if (!await ConfirmAsync([document], DocumentCloseReason.Tab))
            return;

        // Пока спрашивали, документ могли закрыть другой дорогой — выгрузкой его плагина.
        if (!_open.Contains(document))
            return;

        await ReleaseAsync(document);

        // Место закрытого документа занял сосед — его и показываем.
        Show(_dock.Showing);
    }

    /// <summary>
    /// Можно ли закрыть эти документы: каждый согласен, а несохранённое сохранено или отпущено.
    /// </summary>
    /// <param name="documents">Что закрывают.</param>
    /// <param name="reason">Почему.</param>
    /// <returns><c>false</c> — закрытие останавливается: документ отказал, человек нажал «Отмена» или не сохранилось.</returns>
    /// <remarks>
    /// <para>
    /// Сначала спрашивается каждый документ — здесь редактор с автосохранением сохраняет сам, — и
    /// только оставшееся несохранённым идёт человеку одним вопросом. Перед перезапуском вопроса нет:
    /// несохранённое сохраняется, потому что новая копия вернёт вкладки.
    /// </para>
    /// <para>
    /// Упавший вопрос — не отказ: шов называет виновника в журнале, а сломанный документ не держит
    /// открытым окно, которое человек закрывает. Упавшее сохранение — отказ: сохранить просили, и
    /// закрыть, не сохранив, значит потерять правку.
    /// </para>
    /// </remarks>
    public async Task<bool> ConfirmAsync(IReadOnlyList<OpenDocument> documents, DocumentCloseReason reason)
    {
        ArgumentNullException.ThrowIfNull(documents);

        foreach (var document in documents)
        {
            var agreed = true;

            await _guard.RunAsync(
                document.PluginId,
                $"вопрос о закрытии {Path.GetFileName(document.Path)}",
                async () => agreed = await document.View.CanCloseAsync(reason));

            if (!agreed)
                return false;
        }

        var modified = documents.Where(document => document.View.IsModified).ToList();

        if (modified.Count == 0)
            return true;

        var choice = reason == DocumentCloseReason.Restart || Ask is not { } ask
            ? StudioSaveChoice.Save
            : await ask([.. modified.Select(document => Path.GetFileName(document.Path))]);

        return choice switch
        {
            StudioSaveChoice.Save => await SaveEachAsync(modified),
            StudioSaveChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>
    /// Нужен ли вопрос перед закрытием: есть несохранённое или документ, решающий о закрытии сам.
    /// </summary>
    /// <remarks>
    /// Окно закрывается синхронно, а вопрос асинхронный: ради него первое закрытие отменяется и
    /// повторяется после ответа. Без нужды отменять его незачем, и документ, оставивший
    /// <see cref="DocumentView.CanCloseAsync"/> как есть, согласен всегда — об этом говорят
    /// метаданные его типа, а не вызов его кода.
    /// </remarks>
    public bool NeedsConfirmation => _open.Any(document => document.View.IsModified || Decides(document.View));

    /// <summary>Сохраняет показанный документ — по Ctrl+S.</summary>
    /// <returns><c>false</c> — показанного нет или сохранить не вышло.</returns>
    public Task<bool> SaveShownAsync() =>
        _shown is { } shown ? SaveAsync(shown) : Task.FromResult(false);

    /// <summary>Сохраняет все несохранённые документы — по Ctrl+Shift+S.</summary>
    /// <returns><c>false</c> — хоть один не сохранился.</returns>
    public Task<bool> SaveAllAsync() => SaveEachAsync([.. _open.Where(document => document.View.IsModified)]);

    private async Task<bool> SaveEachAsync(List<OpenDocument> documents)
    {
        var all = true;

        // Каждый, а не до первого отказа: отказ одного — не повод не сохранить соседей.
        foreach (var document in documents)
            all &= await SaveAsync(document);

        return all;
    }

    /// <summary>Сохраняет документ через шов; не вышло — строка состояния называет его.</summary>
    private async Task<bool> SaveAsync(OpenDocument document)
    {
        var saved = false;
        var name = Path.GetFileName(document.Path);

        var ran = await _guard.RunAsync(document.PluginId, $"сохранение {name}", async () => saved = await document.View.SaveAsync());

        if (ran && saved)
            return true;

        _status.Show(string.Format(CultureInfo.CurrentCulture, Localizer.Instance["documents.save.failed"], name));

        return false;
    }

    /// <summary>Документ решает о закрытии сам: его тип переопределил <see cref="DocumentView.CanCloseAsync"/>.</summary>
    private static bool Decides(DocumentView view) =>
        view.GetType().GetMethod(nameof(DocumentView.CanCloseAsync), [typeof(DocumentCloseReason)])?.DeclaringType
            != typeof(DocumentView);

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
        Hide();

        // Список снимается заранее: закрытие асинхронное, и открытый тем временем документ не
        // должен ни попасть под него, ни сломать перебор.
        var closing = _open.ToList();

        _open.Clear();

        foreach (var document in closing)
            await FarewellAsync(document);
    }

    /// <summary>Убирает документ отовсюду и отпускает его представление.</summary>
    private async Task ReleaseAsync(OpenDocument document)
    {
        if (ReferenceEquals(_shown, document))
            Hide();

        _open.Remove(document);
        _dock.Remove(document.Id);

        await FarewellAsync(document);
    }

    /// <summary>
    /// Отпускает представление документа прощальной дорогой шва.
    /// </summary>
    /// <remarks>
    /// Прощальной, потому что закрывают документы и у отключённого за сбои — как раз перед его
    /// выгрузкой, — а рабочая дорога ему отказывает. И через шов, потому что упавшее закрытие не
    /// должно обрывать того, кто закрывает: на нём стоит каскад перезагрузки.
    /// </remarks>
    private Task FarewellAsync(OpenDocument document) =>
        _guard.FarewellAsync(
            document.PluginId,
            $"закрытие {Path.GetFileName(document.Path)}",
            async () => await document.View.DisposeAsync());
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
