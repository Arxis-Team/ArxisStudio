using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using static ArxisStudio.Tests.SettingsHarness;

namespace ArxisStudio.Tests;

/// <summary>
/// Окно настроек при двойной шкале кеглей: каждая страница читается целиком.
/// </summary>
/// <remarks>
/// Та же проверка, что у экрана Welcome (запись 165). Строки настроек стояли на доке: контрол
/// прибит вправо, подпись берёт остаток, — и при крупном кегле остатка подписи могло не хватить.
/// <para>
/// Очередь общая с остальными: подписи берутся из словарей, а <c>Localizer</c> один на процесс.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class SettingsTypeScaleTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose()
    {
        Localizer.Instance.SetLanguage(Localizer.FallbackLanguage);
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Каждая страница настроек цела при двойном кегле — на обоих языках студии.</summary>
    [AvaloniaTheory]
    [InlineData("ru", "studio.appearance")]
    [InlineData("en", "studio.appearance")]
    [InlineData("ru", "extension:arxis.terminal")]
    [InlineData("ru", "studio.plugins")]
    public async Task A_settings_page_reads_whole_at_twice_the_type_scale(string language, string page)
    {
        Localizer.Instance.SetLanguage(language);

        var (owner, settings, shown) = _harness.Open();

        ((SettingsViewModel)settings.DataContext!).Select(page);
        Dispatcher.UIThread.RunJobs();

        TypeScale.Enlarge(settings, 2);

        var cut = TypeScale.Labels(settings)
            .Select(label => TypeScale.Whole(label, settings, out var why) ? null : $"«{label.Text}»: {why}")
            .OfType<string>()
            .ToList();

        Assert.True(cut.Count == 0, $"{page} ({language}) срезано при двойном кегле:\n" + string.Join("\n", cut));

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Страница плагинов цела при двойном кегле и тогда, когда в ней есть что показать: группы со
    /// счётчиком, строки и подробности выбранного — с зависимостью и путём папки.
    /// </summary>
    /// <remarks>
    /// В харнессе без плагинов страница показывает одну группу встроенных; здесь в ней стоят и
    /// внешние, а выбран плагин с зависимостью — в подробностях читается всё, что у них бывает.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_plugins_page_reads_whole_at_twice_the_type_scale_with_a_plugin_chosen()
    {
        Localizer.Instance.SetLanguage("ru");

        _harness.Install("arxis.one", "Первый");
        _harness.Install("arxis.two", "Второй", dependsOn: "arxis.one");

        var (owner, settings, shown) = _harness.Open(page: "studio.plugins");
        var page = PluginsOf(settings);

        page.Selected = page.Cards.Single(card => card.Plugin.Id == "arxis.two");
        Dispatcher.UIThread.RunJobs();

        TypeScale.Enlarge(settings, 2);

        var cut = TypeScale.Labels(settings)
            .Select(label => TypeScale.Whole(label, settings, out var why) ? null : $"«{label.Text}»: {why}")
            .OfType<string>()
            .ToList();

        Assert.True(cut.Count == 0, "страница плагинов срезана при двойном кегле:\n" + string.Join("\n", cut));

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>
    /// Подпись строки настроек не упирается в свой контрол — ни в обычном кегле, ни в двойном.
    /// </summary>
    /// <remarks>
    /// Док зазора не знает: контрол прибит вправо, подпись берёт остаток, и при двойном кегле
    /// «Плотность» вставала к сегментам вплотную. Поле на контроле зазор дало бы, но подпись,
    /// которой и так едва хватало места, срезало бы. Строка теперь держит зазор сама, а когда
    /// тесно — ставит контрол под подпись.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task A_settings_row_keeps_its_label_apart_from_its_control(double scale)
    {
        Localizer.Instance.SetLanguage("ru");

        var (owner, settings, shown) = _harness.Open();

        if (scale > 1)
            TypeScale.Enlarge(settings, scale);

        Assert.True(Application.Current!.TryFindResource("AxGapFormRow", out var found), "в теме нет AxGapFormRow");

        var gap = (double)found!;
        var rows = settings.GetVisualDescendants()
            .OfType<Panel>()
            .Where(panel => panel.Classes.Contains("settings-row") && panel.IsEffectivelyVisible)
            .ToList();

        Assert.NotEmpty(rows);

        foreach (var row in rows)
        {
            var parts = row.Children.Where(child => child.IsVisible).ToList();

            Assert.Equal(2, parts.Count);

            var label = Placed(parts[0], row);
            var control = Placed(parts[1], row);
            var apart = control.Top >= label.Bottom || control.Left - label.Right >= gap - 0.5;

            Assert.True(apart, $"при кегле ×{scale} подпись {label} и контрол {control} ближе {gap}");
        }

        await CloseAsync(settings, shown);
        owner.Close();
    }

    /// <summary>Где часть стоит в своей строке.</summary>
    private static Rect Placed(Visual part, Visual row) =>
        new Rect(part.Bounds.Size).TransformToAABB(part.TransformToVisual(row)!.Value);
}
