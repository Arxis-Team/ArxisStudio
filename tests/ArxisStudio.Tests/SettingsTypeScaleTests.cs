using ArxisStudio.Settings;
using ArxisStudio.Shell.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

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
    public void A_settings_page_reads_whole_at_twice_the_type_scale(string language, string page)
    {
        Localizer.Instance.SetLanguage(language);

        var (owner, settings, _) = _harness.Open();

        ((SettingsViewModel)settings.DataContext!).Select(page);
        Dispatcher.UIThread.RunJobs();

        TypeScale.Enlarge(settings, 2);

        var cut = TypeScale.Labels(settings)
            .Select(label => TypeScale.Whole(label, settings, out var why) ? null : $"«{label.Text}»: {why}")
            .OfType<string>()
            .ToList();

        Assert.True(cut.Count == 0, $"{page} ({language}) срезано при двойном кегле:\n" + string.Join("\n", cut));

        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
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
    public void A_settings_row_keeps_its_label_apart_from_its_control(double scale)
    {
        Localizer.Instance.SetLanguage("ru");

        var (owner, settings, _) = _harness.Open();

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

        settings.Cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        owner.Close();
    }

    /// <summary>Где часть стоит в своей строке.</summary>
    private static Rect Placed(Visual part, Visual row) =>
        new Rect(part.Bounds.Size).TransformToAABB(part.TransformToVisual(row)!.Value);
}
