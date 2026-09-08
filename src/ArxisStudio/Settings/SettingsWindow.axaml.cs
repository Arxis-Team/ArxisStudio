using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.Shell.Settings;
using Avalonia.Controls;
using Avalonia.Interactivity;

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
public partial class SettingsWindow : AxWindow
{
    private readonly SettingsViewModel _model;

    private bool _closing;

    /// <summary>Собирает окно поверх готовой модели.</summary>
    /// <param name="model">Что показывать и что сохранять.</param>
    public SettingsWindow(SettingsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        DataContext = model;

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
    /// <remarks>
    /// Хранилище берётся у студии, а не заводится своё: оно читает файл в
    /// память при создании и переписывает его целиком, поэтому второй
    /// экземпляр рядом — это не копия, а гонка, в которой правка пропадает
    /// молча.
    /// </remarks>
    public static Task ShowAsync(
        Window owner,
        ISettingsStore studio,
        StudioPlugins extensions,
        IReadOnlyList<InstalledPlugin> declaring)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(extensions);

        var model = new SettingsViewModel(studio, extensions.Settings, declaring, extensions.Announce);

        return new SettingsWindow(model).ShowDialog(owner);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_model.Save())
            Close();
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

        if (_model.HasChanges && !await StudioAsk.ConfirmAsync(
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
        _model.Revert();
        _closing = true;
        Close();
    }
}
