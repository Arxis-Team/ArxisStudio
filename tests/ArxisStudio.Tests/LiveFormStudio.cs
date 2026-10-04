using System.Reflection;
using ArxisStudio.Extensibility;
using ArxisStudio.Modules.UiDesigner;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Живая вкладка формы в окне: службы проектов и XAML, дизайнер рядом — всё поднято хостом, как в студии.
/// </summary>
/// <remarks>
/// Вкладку открывает редактор документов модуля, подключённый к его контексту, — той же дорогой, что
/// студия, — и ставит в окно. Документ дизайнер берёт у службы XAML экспортом, как взял бы плагин.
/// </remarks>
internal sealed class LiveFormStudio : IAsyncDisposable
{
    private readonly List<LiveFormDocument> _opened = [];

    /// <summary>Поднимает службы и окно.</summary>
    /// <param name="autoSave">Пауза автосохранения; по умолчанию по паузе форма не сохраняется.</param>
    /// <param name="snapshots">Папка снимков форм; по умолчанию снимки выключены, как всему процессу тестов.</param>
    /// <param name="snapshotShown">Что ждёт фоновый снимок, взяв показ формы; по умолчанию ничего.</param>
    public LiveFormStudio(TimeSpan? autoSave = null, string? snapshots = null, Func<CancellationToken, Task>? snapshotShown = null)
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
    /// <remarks>Вкладки прощаются раньше служб — как в студии, где документы закрываются до модулей.</remarks>
    public async ValueTask DisposeAsync()
    {
        foreach (var document in _opened)
            await document.DisposeAsync();

        Window.Content = null;
        Window.Close();
        await Xaml.DisposeAsync();
    }
}
