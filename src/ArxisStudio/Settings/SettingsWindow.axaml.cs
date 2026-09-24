using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ArxisStudio.Settings;

/// <summary>
/// Окно настроек студии: разделы слева, страница справа, «Сохранить» и
/// «Отмена» внизу.
/// </summary>
/// <remarks>
/// Отдельное окно, а не раздел экрана Welcome, — потому что Welcome
/// закрывается навсегда, стоит войти в студию, и настройки вместе с ним
/// становились недостижимы. Открывают его оба входа, и оба одним и тем же
/// вызовом <see cref="ShowAsync"/>: два способа собрать одно окно разошлись бы.
/// <para>
/// Корень — <c>AxWindow</c>, а не <c>AxDialog</c>: диалог в конструкторе
/// прибивает <c>SizeToContent</c> и запрещает менять размер, а окно настроек
/// обязано тянуться. Модальность даёт <c>ShowDialog</c>, а не тип окна.
/// </para>
/// </remarks>
public partial class SettingsWindow : AxWindow, IPluginDialogs
{
    private SettingsViewModel? _model;

    private bool _closing;

    /// <summary>
    /// Собирает пустое окно — модель приходит следом.
    /// </summary>
    /// <remarks>
    /// Порознь, а не одним конструктором, потому что связь кольцевая:
    /// менеджеру плагинов нужны диалоги, диалоги — это окно, а окну нужна
    /// модель, в которой лежит менеджер. Кольцо разрывается здесь: окно
    /// рождается пустым и получает готовую модель через <see cref="Attach"/>.
    /// </remarks>
    public SettingsWindow()
    {
        InitializeComponent();

        Cancel.Click += OnCancelClick;
        Save.Click += OnSaveClick;
        Reset.Click += (_, _) => Then(() => _model?.Reset());
        Back.Click += (_, _) => Then(() => _model?.Back());
        Forward.Click += (_, _) => Then(() => _model?.Forward());
        Crumbs.Navigated += OnCrumbNavigated;
        Closing += OnClosing;

        // Боковые кнопки мыши ходят по истории, как в браузере и в настройках Rider. Слушаются
        // и отданные контролом нажатия: кнопка «назад» у мыши ничего не значит ни для одного
        // контрола окна, и взявший нажатие ради фокуса не должен её глушить.
        AddHandler(PointerPressedEvent, OnSideButton, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnHistoryKey, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnPluginRowKey, RoutingStrategies.Tunnel);

        // Каретка — в поиске, как в настройках Rider: окно открывают, чтобы найти настройку,
        // а без этого фокуса не было ни у кого, и первое нажатие уходило в пустоту.
        Opened += (_, _) => SearchBox.Focus();
    }

    /// <summary>
    /// Показывает настройки поверх окна-хозяина и ждёт, пока их закроют.
    /// </summary>
    /// <param name="owner">Окно, из которого настройки открыли.</param>
    /// <param name="studio">Настройки студии.</param>
    /// <param name="extensions">Расширения студии: у них общее хранилище и живые контексты.</param>
    /// <param name="declaring">Кто объявляет настройки: модули, затем плагины.</param>
    /// <param name="catalog">Каталог плагинов на диске.</param>
    /// <param name="page">На каком разделе открыть; null — на первом.</param>
    /// <param name="keys">
    /// Страница сочетаний клавиш; null — окно без неё. Окно отпускает страницу, закрываясь: она слушает
    /// реестр сочетаний, который живёт весь сеанс.
    /// </param>
    /// <remarks>
    /// Хранилище берётся у студии, а не заводится своё: оно читает файл в
    /// память при создании и переписывает его целиком, поэтому второй
    /// экземпляр рядом — это не копия, а гонка, в которой правка пропадает
    /// молча. По той же причине каталог плагинов приходит снаружи.
    /// <para>
    /// Раздел называется идентификатором страницы, а не номером: строка
    /// «Плагины» в полосе Welcome ведёт в менеджер, и звать его по месту в
    /// дереве значило бы ломать вход при каждой перестановке разделов.
    /// </para>
    /// </remarks>
    public static Task ShowAsync(
        Window owner,
        ISettingsStore studio,
        StudioPlugins extensions,
        IReadOnlyList<InstalledPlugin> declaring,
        PluginCatalog catalog,
        string? page = null,
        KeysPage? keys = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(catalog);

        var window = new SettingsWindow();
        var model = new SettingsViewModel(
            studio,
            extensions.Settings,
            declaring,
            extensions.Announce,
            new PluginsPage(catalog, extensions, window, [.. declaring.Where(extension => extension.IsBuiltIn)]),
            keys);

        window.Attach(model, page);
        window.Closed += (_, _) => keys?.Dispose();

        return window.ShowDialog(owner);
    }

    /// <summary>
    /// «Сохранить»: записать накопленное и закрыться, если всё легло.
    /// </summary>
    /// <remarks>
    /// Кнопки на время записи выключены: сохранение бывает долгим — страница
    /// плагинов опускает выключенных и ждёт, пока их отпустят, — и второй
    /// щелчок по «Сохранить» посреди первого начал бы всё заново.
    /// </remarks>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not { } model)
            return;

        Save.IsEnabled = Cancel.IsEnabled = false;

        try
        {
            if (await model.SaveAsync())
                Close();
        }
        finally
        {
            Save.IsEnabled = Cancel.IsEnabled = true;
        }
    }

    /// <summary>
    /// «Отмена»: забыть правки и закрыться.
    /// </summary>
    /// <remarks>
    /// Без вопроса — вопрос здесь и был бы вторым подряд: «Отмена» уже
    /// означает «я передумал». Спрашивает только крестик, где намерение не
    /// названо.
    /// </remarks>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Discard();

    /// <summary>
    /// Закрытие крестиком: спрашивает о несохранённом.
    /// </summary>
    /// <remarks>
    /// Закрытие отменяется и повторяется после ответа: спросить синхронно
    /// посреди <c>Closing</c> нельзя, а закрыть окно, потеряв правки, — не то,
    /// чего человек просил крестиком.
    /// </remarks>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing)
            return;

        e.Cancel = true;

        if (_model is { HasChanges: true } && !await StudioAsk.ConfirmAsync(
                this,
                Localizer.Instance["settings.unsaved.title"],
                Localizer.Instance["settings.unsaved"],
                Localizer.Instance["settings.unsaved.confirm"],
                danger: true))
        {
            return;
        }

        Discard();
    }

    /// <summary>
    /// Esc закрывает настройки — дорогой крестика, а не «Отмены».
    /// </summary>
    /// <remarks>
    /// Окно построено на <c>AxWindow</c>, а не на <c>AxDialog</c>, и Esc, которому
    /// запись 148 научила диалог, до настроек не доходил. Путь у клавиши крестика:
    /// намерение у неё не названо — Esc жмут и затем, чтобы закрыть подсказку или
    /// список, — поэтому несохранённое она не выбрасывает молча, а спрашивает о нём.
    /// <para>
    /// Нажатие, которое уже взял контрол внутри, — открытый список, например, — сюда
    /// не доходит. Пока идёт запись, клавиша не делает ничего: закрыть окно посреди
    /// сохранения значило бы спросить о правках, которые как раз ложатся на диск.
    /// </para>
    /// <para>
    /// В поиске, где что-то набрано, первый Esc очищает поиск, как в настройках Rider: человек
    /// бросает запрос, а не окно. Второй идёт обычной дорогой.
    /// </para>
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !Cancel.IsEnabled)
            return;

        e.Handled = true;

        if (_model is { Search.Length: > 0 } model && InSearch())
        {
            model.Search = string.Empty;
            return;
        }

        Close();
    }

    /// <summary>
    /// Alt+← и Alt+→ ходят по пройденным страницам, откуда бы их ни нажали.
    /// </summary>
    /// <remarks>
    /// На спуске, а не на подъёме: строка дерева берёт стрелки себе при любых модификаторах — Left
    /// сворачивает её или уводит к родителю, — и из дерева, где каретка стоит после выбора раздела
    /// мышью, «назад» не доходил бы.
    /// </remarks>
    private void OnHistoryKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || _model is not { } model)
            return;

        switch (e.Key)
        {
            case Key.Left:
                Then(model.Back);
                break;
            case Key.Right:
                Then(model.Forward);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Боковые кнопки мыши: «назад» и «вперёд».</summary>
    private void OnSideButton(object? sender, PointerPressedEventArgs e)
    {
        if (_model is not { } model)
            return;

        switch (e.GetCurrentPoint(this).Properties.PointerUpdateKind)
        {
            case PointerUpdateKind.XButton1Pressed:
                Then(model.Back);
                break;
            case PointerUpdateKind.XButton2Pressed:
                Then(model.Forward);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Каретка в поле поиска или внутри него.</summary>
    private bool InSearch() =>
        FocusManager?.GetFocusedElement() is Visual focused
        && (focused == SearchBox || SearchBox.IsVisualAncestorOf(focused));

    /// <summary>Сегмент пути в шапке ведёт на свою страницу.</summary>
    private void OnCrumbNavigated(object? sender, AxBreadcrumbNavigatedEventArgs e)
    {
        if (e.Item is SettingsNode node && _model is { } model)
            Then(() => model.Open(node.Page.Id));
    }

    /// <summary>Ссылка страницы ведёт на другую страницу — страница ветки ведёт так на детей.</summary>
    private void OnPageLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string pageId } && _model is { } model)
            Then(() => model.Open(pageId));
    }

    /// <summary>
    /// Делает дело шапки или страницы и не теряет каретку, если дело унесло её место.
    /// </summary>
    /// <param name="act">Что сделать.</param>
    /// <remarks>
    /// Ссылка ветки и сегмент пути уходят вместе со страницей, «Сбросить» прячется вместе с
    /// правкой, а крайняя стрелка гаснет — и каретка, стоявшая на них, оставалась ни на чём:
    /// следующая клавиша уходила в пустоту. Так же и с Alt+← из страницы: каретка стояла на её
    /// контроле, и страница унесла его с собой. Место каретки тогда — выбранный раздел дерева,
    /// откуда работают с окном. Ставит её туда программа, поэтому без кольца.
    /// </remarks>
    private void Then(Action act)
    {
        act();

        Dispatcher.UIThread.Post(
            () =>
            {
                if (FocusManager?.GetFocusedElement() is InputElement { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } focused
                    && this.IsVisualAncestorOf(focused))
                {
                    return;
                }

                if (_model?.Selected is { } node && Tree.TreeContainerFromItem(node) is Control row)
                    row.Focus(NavigationMethod.Unspecified);
            },
            DispatcherPriority.Loaded);
    }

    private void Discard()
    {
        _model?.Revert();
        _closing = true;
        Close();
    }

    /// <summary>Ставит готовую модель и открывает названный раздел.</summary>
    private void Attach(SettingsViewModel model, string? page)
    {
        _model = model;
        DataContext = model;

        if (page is { Length: > 0 })
            model.Select(page);
    }

    // -- диалоги страницы плагинов ------------------------------------------

    /// <inheritdoc/>
    public async Task<string?> AskFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    /// <inheritdoc/>
    public async Task<string?> AskArchiveAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ArxisStudio") { Patterns = ["*.axplugin"] },
            ],
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger) =>
        StudioAsk.ConfirmAsync(this, title, message, confirm, danger);

    /// <inheritdoc/>
    public void Reveal(string path) => StudioOpen.InShell(path);

    // -- кнопки страницы плагинов ------------------------------------------

    /// <summary>
    /// Кнопки страницы разбирает окно: у страницы нет ни одного контрола.
    /// </summary>
    /// <remarks>
    /// Разметка страницы живёт шаблоном в этом окне — там же, где шаблоны
    /// оформления и расширения, — а обработчик шаблона обязан быть в его
    /// code-behind. Строка модели приходит в <c>Tag</c>: у карточки нет
    /// команд, а заводить их ради двух кнопок значило бы принести в студию
    /// половину MVVM-фреймворка.
    /// </remarks>
    private async void OnPluginToggleClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is { } page && sender is Control { Tag: PluginCard card })
            await page.ToggleAsync(card);
    }

    private async void OnPluginRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is { } page && sender is Control { Tag: PluginCard card })
            await page.RemoveAsync(card);
    }

    private async void OnPluginInstallClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is { } page)
            await page.InstallFromFolderAsync();
    }

    private async void OnPluginInstallArchiveClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is { } page)
            await page.InstallFromArchiveAsync();
    }

    /// <summary>«Открыть keymap.json»: файл сочетаний — средствами системы.</summary>
    private void OnKeymapOpenClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is KeysPage keys)
            keys.OpenFile();
    }

    /// <summary>
    /// Вторичные действия менеджера: поставить из папки, из архива, открыть папку.
    /// </summary>
    /// <remarks>
    /// Меню, а не три кнопки в ряд: ставят плагин редко, и шапке страницы, где
    /// стоит путь, хватает одной кнопки. Собирается на каждый щелчок — тем же
    /// приёмом, что меню шестерёнки в полосе: подписи переводятся вместе с языком.
    /// </remarks>
    private void OnPluginActionsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control button)
            return;

        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };

        flyout.Items.Add(Item(Localizer.Instance["plugins.install"], AxIcons.FolderOpen, OnPluginInstallClick));
        flyout.Items.Add(Item(Localizer.Instance["plugins.installarchive"], AxIcons.Package, OnPluginInstallArchiveClick));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(Localizer.Instance["plugins.openfolder"], AxIcons.Folder, OnPluginFolderClick));

        flyout.ShowAt(button);

        static AxMenuItem Item(string header, Geometry glyph, EventHandler<RoutedEventArgs> click)
        {
            var item = new AxMenuItem { Header = header, Icon = new AxIcon { Data = glyph } };

            item.Click += click;

            return item;
        }
    }

    private void OnPluginFolderClick(object? sender, RoutedEventArgs e) => Plugins?.OpenFolder();

    /// <summary>
    /// Флажок строки: включение идёт той же дорогой, что кнопка в подробностях, — через вопрос о
    /// зависимых.
    /// </summary>
    /// <remarks>
    /// Флажок переключается сам раньше, чем приходит щелчок, а человек на вопрос может ответить
    /// «нет». Поэтому после ответа флажок сверяется с галочкой строки: отказ не оставит его снятым.
    /// </remarks>
    private async void OnPluginCheckClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is not { } page || sender is not AxCheckBox { DataContext: PluginCard card } box)
            return;

        await page.ToggleAsync(card);

        box.SetCurrentValue(ToggleButton.IsCheckedProperty, card.IsOn);
    }

    /// <summary>«Настройки» в подробностях ведут на страницу настроек плагина.</summary>
    private void OnPluginConfigureClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: PluginCard card } && _model is { } model)
            Then(() => model.Open(card.SettingsPageId));
    }

    /// <summary>«Вернуть» в подробностях: непринятая галочка плагина забывается.</summary>
    private void OnPluginUndoClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is { } page && sender is Control { Tag: PluginCard card })
            Then(() => page.Undo(card));
    }

    /// <summary>Папка плагина в подробностях — ссылкой, средствами системы.</summary>
    private void OnPluginRevealClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is { } page && sender is Control { Tag: PluginCard card })
            page.RevealFolder(card);
    }

    /// <summary>Итог действия прочитан и закрыт крестиком.</summary>
    private void OnPluginStatusClosed(object? sender, RoutedEventArgs e) => Plugins?.Dismiss();

    /// <summary>
    /// «…» в подробностях: открыть папку плагина, скопировать его идентификатор.
    /// </summary>
    /// <remarks>
    /// Идентификатор копируют, чтобы назвать плагин там, где его ищут по имени из манифеста: в
    /// зависимостях соседа, в <c>keymap.json</c>, в отчёте о сбое.
    /// </remarks>
    private void OnPluginMoreClick(object? sender, RoutedEventArgs e)
    {
        if (Plugins is not { } page || sender is not Control { Tag: PluginCard card } button)
            return;

        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var reveal = new AxMenuItem { Header = Localizer.Instance["plugins.revealfolder"], Icon = new AxIcon { Data = AxIcons.FolderOpen } };
        var copy = new AxMenuItem { Header = Localizer.Instance["plugins.copyid"], Icon = new AxIcon { Data = AxIcons.Copy } };

        reveal.Click += (_, _) => page.RevealFolder(card);
        copy.Click += (_, _) => _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(card.Plugin.Id);

        flyout.Items.Add(reveal);
        flyout.Items.Add(copy);
        flyout.ShowAt(button);
    }

    /// <summary>
    /// Заголовок группы не выбирается и мышью: список, выбравший его, возвращает выбор плагину.
    /// </summary>
    /// <remarks>
    /// Стрелки и набор букв мимо заголовка проходят сами. Щёлкнуть по нему можно, пока идёт
    /// поиск: тогда он не сворачивается и нажатие не берёт.
    /// </remarks>
    private void OnPluginListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is AxListBox { SelectedItem: PluginGroup } list && Plugins is { } page)
            Dispatcher.UIThread.Post(() => list.SelectedItem = page.Card);
    }

    /// <summary>
    /// Колонкам страницы плагинов — наименьшая ширина из темы: границу между ними тянут, и
    /// утянутая в ноль колонка пропала бы вместе с тем, за что её тянуть обратно.
    /// </summary>
    private void OnPluginsSplitLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid
            || !this.TryFindResource("AxSettingsPaneMinWidth", ActualThemeVariant, out var found)
            || found is not double least)
        {
            return;
        }

        grid.ColumnDefinitions[0].MinWidth = least;
        grid.ColumnDefinitions[2].MinWidth = least;
    }

    /// <summary>
    /// Пробел в строке плагина ставит и снимает флажок, Delete удаляет плагин — как в списке
    /// плагинов Rider.
    /// </summary>
    /// <remarks>
    /// На спуске: список сам берёт Пробел себе — им он выбирает строку. Отвечает только строка
    /// плагина: на флажке Пробел делает своё, а у встроенного оба нажатия ничего не значат.
    /// </remarks>
    private async void OnPluginRowKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None
            || e.Key is not (Key.Space or Key.Delete)
            || Plugins is not { } page
            || FocusManager?.GetFocusedElement() is not AxListBoxItem { DataContext: PluginCard card } row
            || row.FindAncestorOfType<AxListBox>() is not { Name: "PluginList" })
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Space)
            await page.ToggleAsync(card);
        else
            await page.RemoveAsync(card);
    }

    /// <summary>Открытая страница плагинов; null — открыта другая.</summary>
    private PluginsPage? Plugins => _model?.Page as PluginsPage;
}
