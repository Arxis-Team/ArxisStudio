using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Terminal.Shells;
using ArxisStudio.Sdk;
using Avalonia.Controls;
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
    private TerminalPanelView _view = null!;
    private bool _focusOnSelect = true;
    private bool _shown;

    /// <summary>Открытые сеансы в порядке вкладок.</summary>
    public IReadOnlyList<TerminalSession> Sessions =>
        _entries.Select(entry => entry.Session).OfType<TerminalSession>().ToList();

    /// <inheritdoc/>
    protected override Control Build()
    {
        var strings = Context.Strings;

        _view = new TerminalPanelView();

        _view.Tabs.SelectionChanged += (_, _) => ShowSelected();
        _view.Add.Click += (_, _) => Open(TerminalModule.DefaultProfile(Context.Settings));
        _view.Start.Click += (_, _) => Open(TerminalModule.DefaultProfile(Context.Settings));
        _view.Shells.Flyout = BuildMenu(strings);

        WireSessionMenu();

        _view.AttachedToVisualTree += (_, _) =>
        {
            if (_shown)
                return;

            _shown = true;

            if (_entries.Count == 0)
                Open(TerminalModule.DefaultProfile(Context.Settings), focus: false);
        };

        Context.Settings.Changed += (_, _) => ApplySettings();
        TerminalHub.Attach(Handle);

        return _view;
    }

    /// <summary>
    /// Открывает сеанс оболочки новой вкладкой и делает её текущей.
    /// </summary>
    /// <param name="profile">Какую оболочку.</param>
    /// <param name="focus">
    /// Ставить ли курсор в новый сеанс.
    /// </param>
    /// <remarks>
    /// Фокус идёт за действием человека, а не за появлением панели. Сеанс,
    /// открытый нажатием или командой, курсор забирает — за этим и нажимали.
    /// Первый сеанс, который панель заводит себе сама при подъёме студии, —
    /// нет: человек в этот миг ничего у терминала не просил, и отобранный
    /// курсор увёл бы его набор в чужую оболочку.
    /// </remarks>
    public void Open(ShellProfile profile, bool focus = true)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var strings = Context.Strings;
        var settings = TerminalSettings.Read(Context.Settings);

        var host = new TerminalSessionView();
        var view = host.Screen;

        view.FontSize = settings.FontSize;
        view.CursorBlink = settings.CursorBlink;
        view.Describe(string.Format(CultureInfo.CurrentCulture, strings["terminal.view"], profile.Title));

        var tab = new AxTabItem { Classes = { "compact" }, Content = Title(profile), IsClosable = true };

        var entry = new Entry
        {
            Profile = profile,
            Tab = tab,
            Host = host,
        };

        tab.CloseRequested += (_, _) => Close(entry);

        _entries.Add(entry);
        _view.Tabs.Items.Add(tab);

        // Выбор вкладки сам зовёт ShowSelected: ему и говорим, нужен ли курсор.
        _focusOnSelect = focus;
        _view.Tabs.SelectedItem = tab;
        _focusOnSelect = true;

        _ = StartAsync(entry, settings, focus);
    }

    /// <summary>Закрывает сеанс вместе с оболочкой.</summary>
    /// <param name="session">Какой.</param>
    public void Close(TerminalSession session)
    {
        if (_entries.FirstOrDefault(entry => ReferenceEquals(entry.Session, session)) is { } found)
            Close(found);
    }

    private async Task StartAsync(Entry entry, TerminalSettings settings, bool focus)
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
                entry.Host.Say(string.Format(CultureInfo.CurrentCulture, strings["terminal.exited"], code));

            // Оболочка называет себя сама — путём, командой; подпись вкладки
            // при этом остаётся именем оболочки, иначе вкладки не отличить.
            session.TitleChanged += (_, title) => ToolTip.SetTip(entry.Tab, title);

            entry.View.Session = session;

            if (focus && ReferenceEquals(_view.Tabs.SelectedItem, entry.Tab))
                entry.View.Focus();

            Context.Log.Write(StudioLogLevel.Debug, TerminalModule.LogSource, $"Открыт сеанс {entry.Profile.Title}");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var reason = string.Format(CultureInfo.CurrentCulture, strings["terminal.failed"], entry.Profile.App, e.Message);

            entry.Host.Say(reason);

            Context.Log.Write(StudioLogLevel.Error, TerminalModule.LogSource, reason);
            Context.GetService<IStudioStatus>()?.Show(reason);
        }
    }

    /// <summary>Вкладка, выбранная сейчас; null — ни одного сеанса.</summary>
    private Entry? Current =>
        _entries.FirstOrDefault(candidate => ReferenceEquals(candidate.Tab, _view.Tabs.SelectedItem));

    /// <summary>Закрывает сеанс; null — закрывать нечего.</summary>
    private void Close(Entry? entry)
    {
        if (entry is null)
            return;

        var index = _view.Tabs.Items.IndexOf(entry.Tab);

        entry.View.Session = null;
        entry.Session?.Dispose();
        entry.Session = null;
        _entries.Remove(entry);
        _view.Tabs.Items.Remove(entry.Tab);

        if (_view.Tabs.Items.Count > 0)
            _view.Tabs.SelectedIndex = Math.Clamp(index, 0, _view.Tabs.Items.Count - 1);
        else
            ShowSelected();
    }

    private void ShowSelected()
    {
        var entry = Current;

        // Приглашение не подменяют экраном, а прячут: экран держит живую
        // оболочку, и пересобирать его на каждом переключении вкладки нельзя.
        _view.Body.Child = entry?.Host;
        _view.Empty.IsVisible = entry is null;

        if (entry is not null && _focusOnSelect)
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

        if (await SshDialog.AskAsync(owner) is { } profile)
            Open(profile);
    }

    private async Task EditSettingsAsync()
    {
        if (Owner() is not { } owner)
            return;

        await SettingsDialog.EditAsync(owner, Context.Settings, ShellCatalog.Available());
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

    private Window? Owner() => TopLevel.GetTopLevel(_view) as Window;

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

    /// <summary>
    /// Меню шеврона: что открыть.
    /// </summary>
    /// <remarks>
    /// Единственное меню терминала, оставшееся в коде, и по причине: его
    /// начало — список оболочек, найденных в системе. Разметкой такой список
    /// не записать, а половина меню в разметке и половина в коде читалась бы
    /// хуже целого.
    /// </remarks>
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

    /// <summary>
    /// Связывает меню «⋮»: что сделать с открытым сеансом.
    /// </summary>
    /// <remarks>
    /// Пункты объявлены разметкой, а доступность и действия — здесь: над чем
    /// работать, знает панель. Два меню в шапке делят обязанности, а не
    /// повторяют друг друга: шеврон отвечает на «что открыть» — оболочки, SSH,
    /// настройки, — а это на «что сделать с тем, что открыто». Пунктов,
    /// которым не над чем работать, здесь не бывает: без сеанса они выключены,
    /// а не молча ничего не делают.
    /// </remarks>
    private void WireSessionMenu()
    {
        _view.Rename.Click += (_, _) => _ = RenameAsync();
        _view.Clear.Click += (_, _) => Current?.View.ClearScreen();
        _view.CloseSession.Click += (_, _) => Close(Current);

        if (_view.More.Flyout is not AxMenuFlyout menu)
            return;

        menu.Opening += (_, _) =>
        {
            var open = Current is not null;

            _view.Rename.IsEnabled = open;
            _view.CloseSession.IsEnabled = open;

            // Чистить нечего, пока экраном распоряжается полноэкранная
            // программа: она рисует его по своей модели и о чужой уборке не
            // узнает.
            _view.Clear.IsEnabled = Current?.View.CanClear == true;
        };
    }

    /// <summary>
    /// Спрашивает новое имя вкладки и ставит его.
    /// </summary>
    /// <remarks>
    /// Имя, данное человеком, за оболочкой больше не следует: подсказка на
    /// вкладке по-прежнему показывает то, чем себя называет она сама, а
    /// подпись остаётся той, которую выбрали.
    /// </remarks>
    private async Task RenameAsync()
    {
        if (Owner() is not { } owner || Current is not { } entry)
            return;

        var current = entry.Tab.Content as string ?? entry.Profile.Title;

        if (await RenameDialog.AskAsync(owner, current) is { } name)
            entry.Tab.Content = name;
    }

    /// <summary>Вкладка и всё, что за ней: сеанс и его вид.</summary>
    private sealed class Entry
    {
        public required ShellProfile Profile { get; init; }

        public required AxTabItem Tab { get; init; }

        public required TerminalSessionView Host { get; init; }

        /// <summary>Экран сеанса: он живёт в своём виде, но нужен на каждом шагу.</summary>
        public TerminalView View => Host.Screen;

        public TerminalSession? Session { get; set; }
    }
}
