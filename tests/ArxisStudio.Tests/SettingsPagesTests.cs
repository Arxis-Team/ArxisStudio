using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Settings;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static ArxisStudio.Tests.SettingsHarness;

namespace ArxisStudio.Tests;

/// <summary>
/// Страницы окна настроек: плоские, с колонкой подписей, с флажками и с отбором строк поиском.
/// </summary>
/// <remarks>
/// Страница была карточкой во всю высоту, подпись стояла у левого края, контрол — у правого, а
/// флаг расширения был тумблером. Теперь она устроена, как у Rider и Unity: контролы — линией
/// возле подписей, флаг — флажок, потому что правка ждёт «Сохранить», а поиск оставляет на странице
/// только то, что нашёл.
/// <para>
/// Очередь общая с остальными: подписи берутся из словарей, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsPagesTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Язык пакета, поставленного после запуска, окно предлагает — из какой двери его ни открой.
    /// </summary>
    /// <remarks>
    /// Языки пакетов собирали запуск и Welcome перед своим окном настроек. Открытое из студии
    /// показывало список, каким он был на запуске: пакет, поставленный менеджером, появлялся в нём
    /// только после перезапуска, а удалённый оставался. Теперь их собирает само окно — дорога у обеих
    /// дверей одна, и харнесс открывает его ею.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_language_pack_installed_after_start_is_offered_by_the_window()
    {
        _harness.InstallLanguage("arxis.lang-de", "de", "Deutsch");

        Assert.DoesNotContain(Localizer.Instance.Languages, language => language.Code == "de");

        var (owner, settings, shown) = _harness.Open();

        Assert.Contains(Localizer.Instance.Languages, language => language is { Code: "de", Name: "Deutsch" });

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Флаг расширения — флажок, подписанный своей настройкой, а не тумблер; щелчок копит правку.
    /// </summary>
    /// <remarks>
    /// Тумблер обещает, что сработает сразу, а правка в этом окне ждёт «Сохранить», — так Rider
    /// показывает свои флаги флажками. Подпись — содержимое флажка: щелчок по ней ставит галочку, а
    /// длинная подпись переносится.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_flag_setting_is_a_check_box_labelled_with_its_title()
    {
        var (owner, settings, shown) = _harness.Open(page: "extension:arxis.terminal", declaring: [Terminal()]);
        // Страницу держат двое: сама страница и слот действий в шапке. Строки — у первой.
        var page = settings.GetVisualDescendants()
            .OfType<ContentControl>()
            .Where(host => host.Content is ExtensionPage)
            .Single(host => host.GetVisualDescendants().OfType<ItemsControl>().Any());

        Assert.DoesNotContain(page.GetVisualDescendants().OfType<AxToggleSwitch>(), toggle => toggle.IsEffectivelyVisible);

        var flag = Assert.Single(page.GetVisualDescendants().OfType<AxCheckBox>(), box => box.IsEffectivelyVisible);
        var row = Assert.IsType<PluginSettingRow>(flag.DataContext);

        Assert.Equal("Мигающий курсор", flag.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.True(flag.IsChecked, "флажок не показал записанное по умолчанию значение");

        // Щелчок по подписи, а не по коробке: подпись — часть флажка, и нажимать её можно так же.
        var label = flag.GetVisualDescendants().OfType<TextBlock>().Single();
        var at = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), settings)!.Value;

        settings.MouseDown(at, Avalonia.Input.MouseButton.Left);
        settings.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(row.Flag, "щелчок по флажку не дошёл до строки");
        Assert.True(row.HasChanges, "снятая галочка не стала правкой");

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>Поиск по строке прячет на странице всё, что не нашёл, и возвращает, когда его стирают.</summary>
    [AvaloniaFact]
    public async Task A_search_hides_the_rows_it_did_not_find()
    {
        var (owner, settings, shown) = _harness.Open(page: "extension:arxis.terminal", declaring: [Terminal()]);
        var model = (SettingsViewModel)settings.DataContext!;

        model.Search = "курсор";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Мигающий курсор"], Shown(settings));

        model.Search = string.Empty;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Кегль", "Мигающий курсор"], Shown(settings));

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Контролы страницы стоят одной линией возле подписей, а не у правого края окна.
    /// </summary>
    /// <remarks>
    /// Колонка подписей одна на всё окно, как ширина подписей у Unity: взгляд идёт от подписи к
    /// контролу через зазор строки, а не через половину широкого окна.
    /// </remarks>
    [AvaloniaFact]
    public async Task Controls_of_a_page_stand_in_one_column()
    {
        var (owner, settings, shown) = _harness.Open();

        Assert.True(Application.Current!.TryFindResource("AxSettingsLabelWidth", out var column), "в теме нет колонки подписей");
        Assert.True(Application.Current!.TryFindResource("AxGapFormRow", out var gap), "в теме нет зазора строки формы");

        var rows = settings.GetVisualDescendants()
            .OfType<WrapRow>()
            .Where(row => row.Classes.Contains("settings-row") && row.IsEffectivelyVisible)
            .ToList();

        Assert.Equal(3, rows.Count);

        foreach (var row in rows)
        {
            var start = row.TranslatePoint(default, settings)!.Value.X;
            var control = row.Children.Last(child => child.IsVisible).TranslatePoint(default, settings)!.Value.X;

            Assert.Equal(start + (double)column! + (double)gap!, control, 0.5);
        }

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>Подписи строк страницы расширения, которые сейчас видно.</summary>
    private static List<string> Shown(SettingsWindow settings) =>
    [
        .. settings.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.DataContext is PluginSettingRow && control is AxCheckBox or WrapRow && control.IsEffectivelyVisible)
            .Select(control => ((PluginSettingRow)control.DataContext!).Label),
    ];

    /// <summary>Модуль с двумя настройками: числом и флагом.</summary>
    /// <remarks>Папка — настоящая папка терминала: из неё берутся подписи настроек.</remarks>
    private static InstalledPlugin Terminal() => new(
        ModuleManifest.FolderOf(typeof(ArxisStudio.Modules.Terminal.TerminalModule).Assembly),
        new PluginManifest
        {
            Id = "arxis.terminal",
            Name = "Терминал",
            Contributions = new PluginContributions
            {
                Settings =
                {
                    new PluginSetting("terminal.fontSize", "number", "user", "Кегль", 13),
                    new PluginSetting("terminal.cursorBlink", "bool", "user", "Мигающий курсор", true),
                },
            },
        },
        Error: null,
        IsEnabled: true,
        IsBuiltIn: true);
}
