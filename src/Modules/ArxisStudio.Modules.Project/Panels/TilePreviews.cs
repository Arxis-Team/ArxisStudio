using System.Collections.Specialized;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Project.Browse;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Кто и когда просит превью для плиток правой колонки.
/// </summary>
/// <remarks>
/// Плитки стоят на <c>WrapPanel</c> без виртуализации: контейнер есть у каждой, и просить превью
/// всей папки разом значило бы декодировать её целиком, пока человек смотрит на первый экран.
/// Поэтому превью просит колонка, а не плитка, и только для видимых — с запасом в экран вниз и
/// вверх, чтобы прокрутка встречала готовое. Скрытый список плиток — ступень «список» или одна
/// колонка — не просит ничего.
/// <para>
/// Проход один на несколько поводов: прокрутка, перекладка колонки, смена ступени или выключателя,
/// возвращение окна. Поводы, после которых файл мог измениться, — перечтение модели и возвращение
/// окна — сверяют с диском и уже показанные превью. Так делает Unity: ассеты перечитываются, когда
/// редактор снова в фокусе. Слежения за содержимым папки здесь нет: перезапись картинки не даёт
/// событий ни одному наблюдателю студии, а свой наблюдатель ради этого — лишний поток на каждое окно.
/// </para>
/// </remarks>
internal sealed class TilePreviews : IDisposable
{
    private readonly AxListBox _tiles;
    private readonly Browser _browser;
    private readonly Previews _previews = new();

    /// <summary>Плитки, чьё превью закреплено в службе: снимая превью, его отпускают.</summary>
    private readonly HashSet<Tile> _holding = [];

    private ScrollViewer? _scroll;
    private WindowBase? _window;
    private CancellationTokenSource? _pass;
    private bool _enabled;
    private bool _pending;
    private bool _revalidate;

    /// <summary>Заводит превью над списком плиток.</summary>
    /// <param name="tiles">Список плиток.</param>
    /// <param name="browser">Колонка, чьи предметы он показывает.</param>
    public TilePreviews(AxListBox tiles, Browser browser)
    {
        _tiles = tiles;
        _browser = browser;

        _tiles.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        _tiles.AttachedToVisualTree += OnAttached;
        _tiles.DetachedFromVisualTree += OnDetached;
        _tiles.PropertyChanged += OnTilesPropertyChanged;
        _browser.Items.CollectionChanged += OnItemsChanged;
        _browser.Refreshed += OnRefreshed;

        Watch(TopLevel.GetTopLevel(_tiles) as WindowBase);
    }

    /// <summary>Проход, запущенный последним, со всеми его просьбами, — тестам: ждать, а не спать.</summary>
    internal Task Settled { get; private set; } = Task.CompletedTask;

    /// <summary>Служба декода — тестам: сколько прочитано и сколько держится.</summary>
    internal Previews Service => _previews;

    /// <summary>Включает или выключает превью.</summary>
    /// <param name="enabled">Показывать ли картинку вместо силуэта.</param>
    /// <remarks>
    /// Выключенные превью снимаются с плиток сразу, а память отдаётся следом: растры держит служба, и
    /// освобождать их раньше, чем их перестали рисовать, нельзя.
    /// </remarks>
    public void Show(bool enabled)
    {
        if (_enabled == enabled)
        {
            Schedule(false);
            return;
        }

        _enabled = enabled;

        if (enabled)
        {
            Schedule(false);
            return;
        }

        Cancel();
        Drop(_holding.ToList());
        _previews.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _tiles.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
        _tiles.AttachedToVisualTree -= OnAttached;
        _tiles.DetachedFromVisualTree -= OnDetached;
        _tiles.PropertyChanged -= OnTilesPropertyChanged;
        _browser.Items.CollectionChanged -= OnItemsChanged;
        _browser.Refreshed -= OnRefreshed;
        Watch(null);

        _enabled = false;
        Cancel();
        Drop(_holding.ToList());
        _previews.Dispose();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is ScrollViewer scroll)
            _scroll = scroll;

        Schedule(false);
    }

    private void OnTilesPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Плитки стали видны — ступень ушла со «списка» или окно вернулось к двум колонкам.
        if (e.Property == Visual.IsVisibleProperty)
            Schedule(false);
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        Watch(TopLevel.GetTopLevel(_tiles) as WindowBase);

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => Watch(null);

    /// <summary>Следит за окном, в котором стоит колонка: панель отрывают в своё окно и возвращают.</summary>
    private void Watch(WindowBase? window)
    {
        if (ReferenceEquals(_window, window))
            return;

        if (_window is not null)
            _window.Activated -= OnActivated;

        _window = window;

        if (_window is not null)
            _window.Activated += OnActivated;
    }

    private void OnActivated(object? sender, EventArgs e) => Schedule(true);

    private void OnRefreshed(object? sender, EventArgs e) => Schedule(true);

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Ушедшая плитка отпускает своё превью: вернётся та же картинка — служба отдаст его из кэша.
        if (_holding.Count > 0)
        {
            var shown = new HashSet<Tile>(_browser.Items);

            Drop(_holding.Where(tile => !shown.Contains(tile)).ToList());
        }

        Schedule(false);
    }

    /// <summary>Собирает поводы в один проход после раскладки.</summary>
    private void Schedule(bool revalidate)
    {
        if (!_enabled)
            return;

        _revalidate |= revalidate;

        if (_pending)
            return;

        _pending = true;
        Dispatcher.UIThread.Post(Pass, DispatcherPriority.Background);
    }

    private void Pass()
    {
        _pending = false;

        var revalidate = _revalidate;

        _revalidate = false;

        if (!_enabled || !_tiles.IsEffectivelyVisible || TopLevel.GetTopLevel(_tiles) is not { } top)
            return;

        _scroll ??= _tiles.FindDescendantOfType<ScrollViewer>();

        if (_scroll is not { } scroll)
            return;

        // Растр — в размер крупнейшей ступени и точек экрана: ползунок уменьшает его, а не декодирует
        // заново, и на каждом шаге лестницы плитка рисует одно и то же превью.
        var ladder = TileLadder.Of(_tiles);
        var pixels = (int)Math.Ceiling(ladder.Glyphs[^1] * top.RenderScaling);
        var reach = scroll.Viewport.Height;

        Cancel();
        _pass = new CancellationTokenSource();

        var token = _pass.Token;
        var asked = new List<Task>();

        foreach (var tile in _browser.Items)
        {
            if (!Previews.Decodes(tile.Node) || (tile.Preview is not null && !revalidate))
                continue;

            if (_tiles.ContainerFromItem(tile) is not Control container
                || container.TranslatePoint(default, scroll) is not { } at
                || at.Y + container.Bounds.Height < -reach
                || at.Y > reach * 2)
            {
                continue;
            }

            asked.Add(Fill(tile, pixels, token));
        }

        Settled = Task.WhenAll(asked);
    }

    private async Task Fill(Tile tile, int pixels, CancellationToken token)
    {
        Bitmap? bitmap;

        try
        {
            bitmap = await _previews.RequestAsync(tile.Node.Path, pixels, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested || !_enabled || !_browser.Items.Contains(tile))
            return;

        Put(tile, bitmap);
    }

    /// <summary>Ставит плитке превью, закрепив новое и отпустив прежнее.</summary>
    private void Put(Tile tile, Bitmap? bitmap)
    {
        if (ReferenceEquals(tile.Preview, bitmap))
            return;

        var old = tile.Preview;

        if (bitmap is not null)
        {
            _previews.Hold(bitmap);
            _holding.Add(tile);
        }
        else
        {
            _holding.Remove(tile);
        }

        tile.Preview = bitmap;

        if (old is not null)
            _previews.Release(old);
    }

    /// <summary>Снимает превью с плиток и отпускает их растры.</summary>
    private void Drop(IReadOnlyList<Tile> tiles)
    {
        foreach (var tile in tiles)
        {
            _holding.Remove(tile);

            if (tile.Preview is not { } old)
                continue;

            tile.Preview = null;
            _previews.Release(old);
        }
    }

    private void Cancel()
    {
        _pass?.Cancel();
        _pass?.Dispose();
        _pass = null;
    }
}
