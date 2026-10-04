using System.Security.Cryptography;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Snapshots;
using ArxisStudio.Sdk;
using ArxisStudio.Surface.UiDesigner;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>
/// Форма на холсте: сессия формы и карточка, которая её показывает.
/// </summary>
/// <remarks>
/// <para>
/// Карточка у формы может смениться и пропасть: у вкладки она одна на всю жизнь, а доска держит карточки
/// только у видимых форм, и холст отдаёт их другим формам. Поэтому всё, что ставится на карточку, —
/// корень, приложение, метки объявленного, — ставится заново на каждую новую (<see cref="FormCanvas.Place"/>),
/// а снимается раньше, чем карточку отдадут.
/// </para>
/// <para>
/// <b>Снимок для плиток</b> — картинка того, что показывает карточка, в хранилище снимков: почти даром,
/// форма уже построена и разложена.
/// </para>
/// </remarks>
internal sealed class FormSlot : IFormTarget
{
    private readonly FormCanvas _canvas;
    private readonly FormSnapshots? _snapshots;
    private bool _snapshotQueued;
    private string? _pictureWritten;

    /// <summary>Ставит форму на холст.</summary>
    /// <param name="canvas">Холст.</param>
    /// <param name="session">Сессия формы.</param>
    /// <param name="snapshots">Хранилище снимков; null — снимки выключены.</param>
    public FormSlot(FormCanvas canvas, FormSession session, FormSnapshots? snapshots)
    {
        _canvas = canvas;
        _snapshots = snapshots;
        Session = session;
    }

    /// <summary>Сессия формы.</summary>
    public FormSession Session { get; }

    /// <summary>Карточка, которая показывает форму сейчас; null — форма не на виду.</summary>
    public UiDesignerFormItem? Item { get; private set; }

    /// <summary>Последняя запись снимка — тестам: дождаться, а не спать.</summary>
    public Task Snapshotting { get; private set; } = Task.CompletedTask;

    /// <inheritdoc/>
    public IReadOnlyList<XamlElementPath> Selection => _canvas.SelectionOf(this);

    /// <inheritdoc/>
    public IXamlDocumentHandle? Document => Session.Document;

    /// <inheritdoc/>
    public IXamlDesignView? Shown => Session.Shown;

    /// <summary>Тема, к которой пришло бы приложение формы при работе.</summary>
    /// <param name="application">Приложение формы.</param>
    /// <remarks>
    /// Объявленную сторону приложение называет само — <c>RequestedThemeVariant</c> в <c>App.axaml</c>, — а
    /// «по умолчанию» при работе решает платформа, а не студия: тёмная студия — сведение о студии, а не о
    /// проекте. Без приложения тему не знает никто, и карточка наследует студийную. Ту же тему берёт и
    /// фоновый снимок (<see cref="FormCaptures"/>): форма на плитке та же, что на холсте.
    /// </remarks>
    public static ThemeVariant VariantOf(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.RequestedThemeVariant is { } requested && requested != ThemeVariant.Default
            ? requested
            : Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
    }

    /// <summary>Отдаёт форме карточку или забирает её.</summary>
    /// <param name="item">Карточка; null — форма уходит с виду.</param>
    /// <remarks>
    /// Уходящая карточка отдаёт корень и приложение формы сразу: карточку холст отдаст другой форме, а корень
    /// держит одна карточка.
    /// </remarks>
    internal void Bind(UiDesignerFormItem? item)
    {
        if (ReferenceEquals(item, Item))
            return;

        if (Item is { } old)
        {
            old.RemoveHandler(InputElement.GettingFocusEvent, OnGettingFocus);
            old.Root = null;
            old.ApplicationRoot = null;
            old.ApplicationThemeVariant = ThemeVariant.Default;
        }

        Item = item;
        _pictureWritten = null;

        item?.AddHandler(InputElement.GettingFocusEvent, OnGettingFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Ставит карточке корень показа и помечает объявленное документом.</summary>
    /// <returns>Корень есть.</returns>
    internal bool TakeRoot()
    {
        if (Item is not { } item)
            return false;

        var root = Session.Shown?.Root;

        if (!ReferenceEquals(item.Root, root))
            item.Root = root;

        Mark();

        return root is not null;
    }

    /// <summary>Ставит карточке приложение формы: его стили, ресурсы, шаблоны данных и тему.</summary>
    /// <returns>Приложение сменилось — содержимое формы встало в дерево заново.</returns>
    /// <remarks>
    /// Со сменой приложения карточка ставит содержимое формы в дерево заново — тему своего типа контрол ищет
    /// при входе, — и выбор холста на миг уходит с контролов: его возвращают пути. Вариант темы ставится
    /// раньше приложения, чтобы содержимое вошло в дерево уже под ним.
    /// </remarks>
    internal bool TakeApplication()
    {
        if (Item is not { } item)
            return false;

        var application = Session.Shown?.Application;

        item.ApplicationThemeVariant = application is null ? ThemeVariant.Default : VariantOf(application);

        if (ReferenceEquals(item.ApplicationRoot, application))
            return false;

        using (_canvas.Syncing())
            item.ApplicationRoot = application;

        return true;
    }

    /// <summary>Отдаёт корень и приложение — на замену поколения.</summary>
    internal void Release()
    {
        if (Item is not { } item)
            return;

        item.Root = null;
        item.ApplicationRoot = null;
    }

    /// <summary>
    /// Помечает контролы, которые документ объявил сам: только их холст предлагает к выбору.
    /// </summary>
    /// <remarks>
    /// Корень не помечается: за него стоит карточка, и её ручки и есть размер формы. Метка внутри кнопки
    /// построена её шаблоном и элемента не имеет — показ её не называет.
    /// </remarks>
    internal void Mark()
    {
        if (Item is null || Session.Shown is not { } shown)
            return;

        foreach (var declared in shown.GetDeclaredObjects())
        {
            if (declared is Control control && !Layout.GetIsTracked(control))
                Layout.SetIsTracked(control, true);
        }
    }

    /// <summary>
    /// Что на холсте стоит за элементом: корень — карточка, остальное — его контрол, а элемент, построивший
    /// не контрол (определение строки, ресурс), — ближайший контрол над ним.
    /// </summary>
    /// <param name="path">Путь элемента.</param>
    internal Control? TargetOf(XamlElementPath path)
    {
        if (Item is not { } item || Session.Shown is not { Root: not null } shown)
            return null;

        for (XamlElementPath? current = path; current is not null; current = current.Parent)
        {
            if (current.Equals(XamlElementPath.Root))
                return item;

            if (shown.ObjectAt(current) is Control control)
                return control;
        }

        return null;
    }

    /// <summary>Путь того, что выбрано на холсте: карточка — корень документа.</summary>
    /// <param name="target">Выбранное.</param>
    internal XamlElementPath? PathOf(Control target) =>
        ReferenceEquals(target, Item) ? XamlElementPath.Root : Session.Shown?.PathOf(target);

    /// <summary>
    /// Снимает форму, когда она встанет: снимок почти даром — форма уже построена и разложена.
    /// </summary>
    /// <remarks>
    /// Зовут его всё, после чего форма выглядит иначе: встал корень, сменилось приложение, текст сохранён или
    /// принят с диска, холст вернулся в окно или снова виден. Просьбы сливаются в одну, после раскладки.
    /// </remarks>
    internal void QueueSnapshot()
    {
        if (_snapshots is null || _snapshotQueued || Item is null)
            return;

        _snapshotQueued = true;
        Dispatcher.UIThread.Post(TakeSnapshot, DispatcherPriority.Background);
    }

    /// <summary>
    /// Снимает форму, если она такая же, как на диске: снимок отвечает файлу, а несохранённое показывает
    /// только холст.
    /// </summary>
    /// <remarks>
    /// Не снимается замороженный кадр замены — его корень уже отпущен, — карточка вне окна и холст, спрятанный
    /// видом «XAML», — их не разложить, и снимок вышел бы прежней раскладкой, — и форма, у которой нет показа.
    /// Картинка, совпавшая с записанной, не пишется: показ формы снова и снова не будит плитки зря.
    /// </remarks>
    private void TakeSnapshot()
    {
        _snapshotQueued = false;

        if (_canvas.IsDisposed
            || _snapshots is not { } snapshots
            || _canvas.IsFrozen
            || Item is not { } item
            || Session.Document is not { IsModified: false, HasConflict: false, IsDeleted: false, IsClosed: false } document
            || Session.Shown is not { Root: { } root } shown
            || !ReferenceEquals(item.Root, root)
            || TopLevel.GetTopLevel(item) is null
            || Face(item) is not { IsEffectivelyVisible: true } face
            || FormPicture.Take(face, root, FormSnapshots.Pixels) is not { } picture)
        {
            return;
        }

        var written = Convert.ToHexString(SHA256.HashData(picture));

        if (written == _pictureWritten)
            return;

        _pictureWritten = written;

        var snapshot = FormSnapshots.Describe(Session.Path.Value, document, shown, item.ApplicationThemeVariant, face.Bounds.Size);

        Snapshotting = WriteSnapshotAsync(Snapshotting, snapshots, snapshot, picture, _canvas.Lifetime);
    }

    /// <summary>Пишет снимок вслед за прежним и говорит показывающим, что превью формы сменилось.</summary>
    /// <remarks>
    /// Записи формы идут по очереди: каждая пишет те же два файла через те же временные, и встречные
    /// разошлись бы картинкой со сведениями. Отмена — жизнь холста, взятая, пока он жив: убранный холст
    /// недописанное бросает.
    /// </remarks>
    private async Task WriteSnapshotAsync(Task previous, FormSnapshots snapshots, FormSnapshot snapshot, byte[] picture, CancellationToken lifetime)
    {
        try
        {
            await previous;
            await snapshots.WriteAsync(snapshot, picture, lifetime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Не записалось — превью остаётся прежним, а следующий показ снимет снова.
            _pictureWritten = null;
            return;
        }

        _canvas.Context.GetService<IStudioFilePreviews>()?.Invalidate(Session.Path.Value);
    }

    /// <summary>
    /// Фокус в форму не заходит: кнопка формы не нажимается, поле не берёт текст, а Delete и Ctrl+A
    /// остаются у холста.
    /// </summary>
    /// <remarks>
    /// Обход Tab запирает стиль холста на хосте формы; сюда доходит фокус, пришедший иначе, — из кода самого
    /// контрола формы. Неотменимую перемену уводят на холст: он в фокусе ничего не печатает.
    /// </remarks>
    private void OnGettingFocus(object? sender, FocusChangingEventArgs e)
    {
        if (Item is not { } item
            || e.NewFocusedElement is not Visual target
            || ReferenceEquals(target, item)
            || !item.IsVisualAncestorOf(target))
        {
            return;
        }

        if (!e.TryCancel())
            e.TrySetNewFocusedElement(_canvas.Sheet);
    }

    /// <summary>
    /// Область формы на карточке — её фон и содержимое, без рамки карточки и заголовка окна.
    /// </summary>
    /// <remarks>Часть шаблона карточки Surface: <c>PART_FormBackground</c>.</remarks>
    private static Border? Face(UiDesignerFormItem item) =>
        item.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "PART_FormBackground");
}
