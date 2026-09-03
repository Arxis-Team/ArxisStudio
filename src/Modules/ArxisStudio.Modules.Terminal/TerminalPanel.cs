using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Панель терминала: сеансы вкладками, кнопка «ещё один» и меню оболочек.
/// </summary>
/// <remarks>
/// Полоса сеансов стоит первой строкой содержимого, а не в шапке панели: в
/// доке студии шапка принадлежит группе вкладок — там живут вкладки соседних
/// панелей и кнопки студии, — и делить её с ними панель не может. Устроена
/// полоса теми же вкладками <c>compact</c>, что и шапка, и читается так же.
/// <para>
/// Первый сеанс открывается не при построении, а при первом показе: студия
/// строит содержимое панели при подъёме модуля, и оболочка, запущенная в
/// свёрнутую панель, работала бы впустую. Дальше сеансы открывают человек и
/// команды модуля — через <see cref="TerminalHub"/>.
/// </para>
/// </remarks>
[ToolWindow(TerminalModule.PanelId)]
public sealed class TerminalPanel : ToolWindow
{
    private readonly List<Entry> _entries = [];
    private AxTabStrip _tabs = null!;
    private Border _body = null!;
    private Control _empty = null!;
    private Control _root = null!;
    private bool _shown;

    /// <summary>Открытые сеансы в порядке вкладок.</summary>
    public IReadOnlyList<TerminalSession> Sessions =>
        _entries.Select(entry => entry.Session).OfType<TerminalSession>().ToList();

    /// <inheritdoc/>
    protected override Control Build()
    {
        var strings = Context.Strings;

        _tabs = new AxTabStrip { Classes = { "compact" }, VerticalAlignment = VerticalAlignment.Center };
        _tabs.SelectionChanged += (_, _) => ShowSelected();

        var add = IconButton(AxIcons.Plus, strings["terminal.new"]);

        add.Click += (_, _) => Open(TerminalModule.DefaultProfile(Context.Settings));

        var shells = IconButton(AxIcons.ChevronDownSmall, strings["terminal.shells"]);

        shells.Flyout = BuildMenu(strings);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { add, shells },
        };

        DockPanel.SetDock(actions, Dock.Right);

        var header = new Border
        {
            Padding = new Thickness(4, 2),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { Children = { actions, _tabs } },
        };

        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("AxBrdBrush"));
        DockPanel.SetDock(header, Dock.Top);

        _empty = BuildEmpty(strings);
        _body = new Border { Child = _empty };
        _root = new DockPanel { Children = { header, _body } };

        _root.AttachedToVisualTree += (_, _) =>
        {
            if (_shown)
                return;

            _shown = true;

            if (_entries.Count == 0)
                Open(TerminalModule.DefaultProfile(Context.Settings));
        };

        Context.Settings.Changed += (_, _) => ApplySettings();
        TerminalHub.Attach(Handle);

        return _root;
    }

    /// <summary>Открывает сеанс оболочки новой вкладкой и делает её текущей.</summary>
    /// <param name="profile">Какую оболочку.</param>
    public void Open(ShellProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var strings = Context.Strings;
        var settings = TerminalSettings.Read(Context.Settings);

        var view = new TerminalView { FontSize = settings.FontSize, CursorBlink = settings.CursorBlink };

        view.Describe(string.Format(CultureInfo.CurrentCulture, strings["terminal.view"], profile.Title));
        view.ContextFlyout = BuildContextMenu(view, strings);

        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };

        var footer = new Border
        {
            IsVisible = false,
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = message,
        };

        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("AxBrdBrush"));
        DockPanel.SetDock(footer, Dock.Bottom);

        var tab = new AxTabItem { Classes = { "compact" }, Content = Title(profile), IsClosable = true };

        var entry = new Entry
        {
            Profile = profile,
            Tab = tab,
            View = view,
            Host = new DockPanel { Children = { footer, view } },
            Footer = footer,
            Message = message,
        };

        tab.CloseRequested += (_, _) => Close(entry);

        _entries.Add(entry);
        _tabs.Items.Add(tab);
        _tabs.SelectedItem = tab;

        _ = StartAsync(entry, settings);
    }

    /// <summary>Закрывает сеанс вместе с оболочкой.</summary>
    /// <param name="session">Какой.</param>
    public void Close(TerminalSession session)
    {
        if (_entries.FirstOrDefault(entry => ReferenceEquals(entry.Session, session)) is { } found)
            Close(found);
    }

    private async Task StartAsync(Entry entry, TerminalSettings settings)
    {
        var strings = Context.Strings;

        try
        {
            var session = await TerminalSession.StartAsync(
                entry.Profile, WorkingDirectory(), settings, entry.View.Columns, entry.View.Rows, CancellationToken.None);

            // Вкладку могли закрыть, пока оболочка поднималась.
            if (!_entries.Contains(entry))
            {
                session.Dispose();
                return;
            }

            entry.Session = session;

            session.Exited += (_, code) =>
            {
                entry.Message.Text = string.Format(CultureInfo.CurrentCulture, strings["terminal.exited"], code);
                entry.Footer.IsVisible = true;
            };

            // Оболочка называет себя сама — путём, командой; подпись вкладки
            // при этом остаётся именем оболочки, иначе вкладки не отличить.
            session.TitleChanged += (_, title) => ToolTip.SetTip(entry.Tab, title);

            entry.View.Session = session;

            if (ReferenceEquals(_tabs.SelectedItem, entry.Tab))
                entry.View.Focus();

            Context.Log.Write(StudioLogLevel.Debug, TerminalModule.LogSource, $"Открыт сеанс {entry.Profile.Title}");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            entry.Message.Text = string.Format(CultureInfo.CurrentCulture, strings["terminal.failed"], entry.Profile.App, e.Message);
            entry.Footer.IsVisible = true;

            Context.Log.Write(StudioLogLevel.Error, TerminalModule.LogSource, entry.Message.Text);
            Context.GetService<IStudioStatus>()?.Show(entry.Message.Text);
        }
    }

    private void Close(Entry entry)
    {
        var index = _tabs.Items.IndexOf(entry.Tab);

        entry.View.Session = null;
        entry.Session?.Dispose();
        entry.Session = null;
        _entries.Remove(entry);
        _tabs.Items.Remove(entry.Tab);

        if (_tabs.Items.Count > 0)
            _tabs.SelectedIndex = Math.Clamp(index, 0, _tabs.Items.Count - 1);
        else
            ShowSelected();
    }

    private void ShowSelected()
    {
        var entry = _entries.FirstOrDefault(candidate => ReferenceEquals(candidate.Tab, _tabs.SelectedItem));

        _body.Child = entry?.Host ?? _empty;

        if (entry is not null)
            Dispatcher.UIThread.Post(() => entry.View.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Отвечает на просьбу команды; просьбы приходят с потока интерфейса, но проверить не вредно.</summary>
    private void Handle(TerminalRequest request)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Handle(request));
            return;
        }

        switch (request.Kind)
        {
            case TerminalRequestKind.Open:
                if (_entries.Count == 0)
                    Open(TerminalModule.DefaultProfile(Context.Settings));
                else
                    ShowSelected();
                break;

            case TerminalRequestKind.NewSession:
                Open(request.Profile ?? TerminalModule.DefaultProfile(Context.Settings));
                break;

            case TerminalRequestKind.NewSsh:
                _ = AskSshAsync();
                break;

            case TerminalRequestKind.Settings:
                _ = EditSettingsAsync();
                break;
        }
    }

    private async Task AskSshAsync()
    {
        if (Owner() is not { } owner)
            return;

        if (await SshDialog.AskAsync(owner, Context.Strings) is { } profile)
            Open(profile);
    }

    private async Task EditSettingsAsync()
    {
        if (Owner() is not { } owner)
            return;

        await SettingsDialog.EditAsync(owner, Context.Strings, Context.Settings, ShellCatalog.Available());
    }

    /// <summary>Разносит изменённые настройки по открытым сеансам; история — только у новых.</summary>
    private void ApplySettings()
    {
        var settings = TerminalSettings.Read(Context.Settings);

        foreach (var entry in _entries)
        {
            entry.View.FontSize = settings.FontSize;
            entry.View.CursorBlink = settings.CursorBlink;
        }
    }

    private Window? Owner() => TopLevel.GetTopLevel(_root) as Window;

    /// <summary>Где начинать оболочку: в папке проекта, а без проекта — дома.</summary>
    private string WorkingDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (Context.ProjectPath is not { Length: > 0 } path)
            return home;

        if (Directory.Exists(path))
            return path;

        return Path.GetDirectoryName(path) is { Length: > 0 } folder && Directory.Exists(folder) ? folder : home;
    }

    /// <summary>Подпись вкладки: имя оболочки, а у второй такой же — с номером.</summary>
    private string Title(ShellProfile profile)
    {
        var same = _entries.Count(entry => string.Equals(entry.Profile.Title, profile.Title, StringComparison.Ordinal));

        return same == 0 ? profile.Title : $"{profile.Title} ({same + 1})";
    }

    private AxMenuFlyout BuildMenu(IStudioStrings strings)
    {
        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        foreach (var profile in ShellCatalog.Available())
        {
            var item = new AxMenuItem { Header = profile.Title };
            var chosen = profile;

            item.Click += (_, _) => Open(chosen);
            flyout.Items.Add(item);
        }

        var ssh = new AxMenuItem { Header = strings["terminal.ssh"] };
        var settings = new AxMenuItem { Header = strings["terminal.settings"] };

        ssh.Click += (_, _) => _ = AskSshAsync();
        settings.Click += (_, _) => _ = EditSettingsAsync();

        flyout.Items.Add(ssh);
        flyout.Items.Add(settings);

        return flyout;
    }

    private static AxMenuFlyout BuildContextMenu(TerminalView view, IStudioStrings strings)
    {
        var copy = new AxMenuItem { Header = strings["terminal.copy"] };
        var paste = new AxMenuItem { Header = strings["terminal.paste"] };
        var selectAll = new AxMenuItem { Header = strings["terminal.selectAll"] };
        var clear = new AxMenuItem { Header = strings["terminal.clear"] };

        copy.Click += (_, _) => _ = view.CopyAsync();
        paste.Click += (_, _) => _ = view.PasteAsync();
        selectAll.Click += (_, _) => view.SelectAll();
        clear.Click += (_, _) => view.ClearScreen();

        var flyout = new AxMenuFlyout();

        flyout.Items.Add(copy);
        flyout.Items.Add(paste);
        flyout.Items.Add(selectAll);
        flyout.Items.Add(clear);

        // Копировать нечего, пока ничего не выделено, — пункт об этом говорит.
        flyout.Opening += (_, _) => copy.IsEnabled = view.HasSelection;

        return flyout;
    }

    private Control BuildEmpty(IStudioStrings strings)
    {
        var open = new AxButton { Content = strings["terminal.new"], HorizontalAlignment = HorizontalAlignment.Center };

        open.Click += (_, _) => Open(TerminalModule.DefaultProfile(Context.Settings));

        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = strings["terminal.empty"], TextAlignment = TextAlignment.Center },
                open,
            },
        };
    }

    /// <summary>
    /// Кнопка со значком: подпись — подсказка и имя для средств доступности.
    /// </summary>
    /// <remarks>
    /// Класс один: <c>icon</c> делает кнопку квадратной по высоте строки, а
    /// добавленный к нему <c>compact</c> вернул бы минимальную ширину кнопки с
    /// текстом — 64 пикселя под значок 12×12.
    /// </remarks>
    private static AxButton IconButton(Geometry icon, string title)
    {
        var button = new AxButton
        {
            Classes = { "icon" },
            Content = new AxIcon { Classes = { "small" }, Data = icon },
        };

        Avalonia.Automation.AutomationProperties.SetName(button, title);
        ToolTip.SetTip(button, title);

        return button;
    }

    /// <summary>Вкладка и всё, что за ней: сеанс, его экран и строка о завершении.</summary>
    private sealed class Entry
    {
        public required ShellProfile Profile { get; init; }

        public required AxTabItem Tab { get; init; }

        public required TerminalView View { get; init; }

        public required DockPanel Host { get; init; }

        public required Border Footer { get; init; }

        public required TextBlock Message { get; init; }

        public TerminalSession? Session { get; set; }
    }
}
