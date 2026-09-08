using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Icons;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using ArxisStudio.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

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
        Closing += OnClosing;
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
        string? page = null)
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
            new PluginsPage(catalog, extensions, window));

        window.Attach(model, page);

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

    /// <summary>
    /// Вторичные действия менеджера: поставить из папки, из архива, открыть папку.
    /// </summary>
    /// <remarks>
    /// Меню, а не три кнопки в ряд: ставят плагин редко, а место наверху нужно
    /// подписи раздела. Собирается на каждый щелчок — тем же приёмом, что меню
    /// шестерёнки в полосе: подписи переводятся вместе с языком.
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

    /// <summary>Открытая страница плагинов; null — открыта другая.</summary>
    private PluginsPage? Plugins => _model?.Page as PluginsPage;
}
