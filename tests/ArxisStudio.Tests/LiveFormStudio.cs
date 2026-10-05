using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Panels;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Живая вкладка формы и живая доска в окне: службы проектов и XAML, дизайнер рядом — всё поднято хостом,
/// как в студии.
/// </summary>
/// <remarks>
/// Вкладку открывает редактор документов модуля, подключённый к его контексту, — той же дорогой, что
/// студия, — и ставит в окно; доску ставит её панель. Документ дизайнер берёт у службы XAML экспортом, как
/// взял бы плагин.
/// </remarks>
internal sealed class LiveFormStudio : IAsyncDisposable
{
    private readonly List<LiveFormDocument> _opened = [];
    private readonly List<BoardPanel> _boards = [];

    /// <summary>Поднимает службы и окно.</summary>
    /// <param name="autoSave">Пауза автосохранения; по умолчанию по паузе форма не сохраняется.</param>
    /// <param name="snapshots">Папка снимков форм; по умолчанию снимки выключены, как всему процессу тестов.</param>
    /// <param name="snapshotShown">Что ждёт фоновый снимок, взяв показ формы; по умолчанию ничего.</param>
    /// <param name="hideDelay">Пауза, после которой форма доски, ушедшая с виду, отдаёт показ; по умолчанию — никогда.</param>
    /// <param name="formShown">Что ждёт показ формы вкладки или доски, взяв документ; по умолчанию ничего.</param>
    /// <param name="codeHighlighted">Что ждёт перечитывание XAML, посчитав роли текста; по умолчанию ничего.</param>
    public LiveFormStudio(
        TimeSpan? autoSave = null,
        string? snapshots = null,
        Func<CancellationToken, Task>? snapshotShown = null,
        TimeSpan? hideDelay = null,
        Func<FormShowRank, CancellationToken, Task>? formShown = null,
        Func<CancellationToken, Task>? codeHighlighted = null)
    {
        // Фоновые снимки — без паузы после просьбы: в тесте плитки не листают, и ждать тишины незачем. Приложение
        // у решения теста строится сразу, а у большинства его нет вовсе — долго его ждать незачем.
        Options = new UiDesignerOptions
        {
            AutoSaveDelay = autoSave ?? Timeout.InfiniteTimeSpan,
            SnapshotsFolder = snapshots,
            SnapshotQuiet = TimeSpan.Zero,
            SnapshotApplicationWait = TimeSpan.FromMilliseconds(500),
            SnapshotShown = snapshotShown,
            BoardHideDelay = hideDelay ?? Timeout.InfiniteTimeSpan,
            FormShown = formShown,
            CodeHighlighted = codeHighlighted,
        };
        Xaml = new XamlStudio(services: new Dictionary<Type, object> { [typeof(UiDesignerOptions)] = Options });

        Designer = Xaml.Host.LoadBuiltIn(typeof(UiDesignerModule).Assembly);
        Assert.True(Designer.IsLoaded, Designer.Error);

        Context = Designer.Studio!;
        Window = new Window { Width = 1200, Height = 800 };
        Window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Службы проектов и XAML.</summary>
    public XamlStudio Xaml { get; }

    /// <summary>Поднятый дизайнер.</summary>
    public LoadedPlugin Designer { get; }

    /// <summary>Контекст дизайнера.</summary>
    public IStudioContext Context { get; }

    /// <summary>Шов дизайнера.</summary>
    public UiDesignerOptions Options { get; }

    /// <summary>Окно, в котором стоит вкладка.</summary>
    public Window Window { get; }

    /// <summary>Словарь дизайнера.</summary>
    public IStudioStrings Strings => Context.Strings;

    /// <summary>Редактор документов модуля, подключённый к его контексту, — как его подключает студия.</summary>
    public FormEditor Editor()
    {
        var editor = new FormEditor();

        editor.Attach(Context);

        return editor;
    }

    /// <summary>
    /// Кладёт форму в проект, открывает решение и вкладку и ждёт, пока форма встанет на холст живой.
    /// </summary>
    /// <param name="name">Файл от папки проекта.</param>
    /// <param name="text">Разметка.</param>
    public async Task<LiveFormDocument> OpenAsync(string name, string text)
    {
        var path = Xaml.Write(name, text);

        await Xaml.OpenAsync();

        var editor = Editor();

        Assert.True(editor.CanOpen(path.Value), "дизайнер не взял форму");

        var (view, error) = await editor.OpenAsync(path.Value);

        Assert.Null(error);

        var document = Assert.IsType<LiveFormDocument>(view);

        _opened.Add(document);
        Window.Content = document.Content;
        Dispatcher.UIThread.RunJobs();

        await document.Opening;
        await XamlStudio.UntilAsync(() => document.Form.Root is not null, "форма не встала на холст");
        await XamlStudio.UntilAsync(() => document.Code is not null, "XAML не показан");
        Frame();

        return document;
    }

    /// <summary>
    /// Кладёт формы в проект, открывает решение и доску в окне и ждёт, пока формы на виду встанут живыми.
    /// </summary>
    /// <param name="forms">Файлы форм от папки проекта и их разметка.</param>
    public async Task<BoardPanel> OpenBoardAsync(params (string Name, string Text)[] forms)
    {
        foreach (var (name, text) in forms)
            Xaml.Write(name, text);

        await Xaml.OpenAsync();

        var board = new BoardPanel();

        board.Attach(Context);
        _boards.Add(board);
        Window.Content = board.Content;
        Dispatcher.UIThread.RunJobs();

        await XamlStudio.UntilAsync(() => board.Model is { IsReady: true } model && model.Cards.Count == forms.Length, "доска не встала");
        await board.Model!.Settled;
        await UntilLiveAsync(board);

        return board;
    }

    /// <summary>Ждёт, пока формы на виду встанут на доске живыми.</summary>
    /// <param name="board">Доска.</param>
    public static async Task UntilLiveAsync(BoardPanel board)
    {
        Frame();
        board.Sight!.Update();

        await XamlStudio.UntilAsync(
            () => board.Sight.Seen.Count > 0 && board.Sight.Seen.All(board.Forms!.IsLive),
            "формы на виду не встали живыми");

        Frame();
    }

    /// <summary>Раскладка и кадр: холст выбирает только то, у чего уже есть рамка.</summary>
    public static void Frame()
    {
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Нажимает клавишу там, где стоит каретка, — как её доставило бы окно.</summary>
    public static void Press(Control where, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        where.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = where,
        });
        Frame();
    }

    /// <summary>Окно теряет фокус: человек ушёл в другое приложение.</summary>
    /// <remarks>
    /// Так, как об этом говорит платформа, — вызовом, который окно Avalonia поставило своей реализации, —
    /// а не событием, поднятым мимо окна. Член у Avalonia 12 закрытый, поэтому отражением.
    /// </remarks>
    public void Deactivate()
    {
        var platform = Assert.IsAssignableFrom<IWindowBaseImpl>(Window.PlatformImpl);
        var property = typeof(IWindowBaseImpl).GetProperty(
            "Deactivated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var deactivated = Assert.IsType<Action>(property?.GetValue(platform), exactMatch: false);

        deactivated();
        Dispatcher.UIThread.RunJobs();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Вкладки и доски прощаются раньше служб — как в студии, где документы и панели закрываются до модулей.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        foreach (var document in _opened)
            await document.DisposeAsync();

        foreach (var board in _boards)
            board.Release();

        Dispatcher.UIThread.RunJobs();

        Window.Content = null;
        Window.Close();
        await Xaml.DisposeAsync();
    }
}
