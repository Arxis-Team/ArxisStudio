using ArxisStudio.Controls;
using ArxisStudio.Docking;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Узкая панель при двойном кегле: подписи кнопок и вкладок сокращаются многоточием, а текст
/// панели переносится — ничто не обрывается краем.
/// </summary>
/// <remarks>
/// Замер записи 165 нашёл в каркасе при двойном кегле оборванные подписи — «Сосчитать файлы
/// проекта» на кнопке панели примера и «Пример модуля» на вкладке, — а безголовый снимок той же
/// панели показал ещё и вводную строку примера. Проверяются настоящие вещи, а не копии: группа
/// докинга со своим шаблоном и панель примера плагина со своими словарями. Шрифт здесь настоящий,
/// поэтому и ширины настоящие.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class NarrowLabelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"arxis-narrow-{Guid.NewGuid():N}");

    public NarrowLabelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Вкладка узкой группы сокращает имя и оставляет крестик на месте.</summary>
    /// <remarks>
    /// Полоса вкладок прокручивается, и прежде вкладка стояла своей полной ширины: при двойном
    /// кегле имя уходило за край, крестик пропадал, а под шапкой вылезала полоса прокрутки.
    /// Ширина группы — та, что у левой панели каркаса на снимке живой студии.
    /// </remarks>
    [AvaloniaFact]
    public void A_tab_in_a_narrow_group_shortens_its_name_and_keeps_the_cross()
    {
        var items = new DockItems();

        items.Add("sample", new DockItem("sample", new Border()) { Title = "Пример модуля", CanClose = true });

        var view = new DockView
        {
            Items = items,
            Root = new DockGroup { Id = "left", Items = ["sample"], Selected = "sample" },
        };
        var window = new Window { Content = view, Width = 214, Height = 400 };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tab = view.GetVisualDescendants().OfType<AxTabItem>().Single();

        Assert.False(Trimmed(Label(tab, "Пример модуля")), "при обычном кегле имя вкладки сокращено, хотя помещается");

        TypeScale.Enlarge(window, 2);

        var viewer = tab.FindAncestorOfType<ScrollViewer>()!;
        var cross = tab.GetVisualDescendants().OfType<Border>().Single(part => part.Name == "PART_Close");
        var right = cross.TranslatePoint(new Point(cross.Bounds.Width, 0), viewer)!.Value.X;

        Assert.True(Trimmed(Label(tab, "Пример модуля")), "имя вкладки не сокращено, а вкладка шире группы");
        Assert.True(right <= viewer.Viewport.Width + 0.5, $"крестик кончается на {right:0.#}, а полоса — на {viewer.Viewport.Width:0.#}");
        Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 0.5, "полоса прокручивается ради одной вкладки");

        window.Close();
    }

    /// <summary>Панель примера плагина при двойном кегле: ни одна подпись не оборвана краем.</summary>
    /// <remarks>
    /// Подпись кнопки, которой не хватает ширины, сокращается многоточием; вводная строка
    /// переносится. При обычном кегле не сокращено ничего — многоточие не должно появляться там,
    /// где места хватает. Пример ставится из своего архива и поднимается хостом, а панель строится
    /// той же дорогой, что в студии: по атрибуту из сборки, с выданным плагину контекстом. Ширина —
    /// та, что у правой панели каркаса на снимке живой студии.
    /// </remarks>
    [AvaloniaFact]
    public void The_sample_plugin_panel_at_double_type_cuts_no_label()
    {
        var catalog = new PluginCatalog(Path.Combine(_root, "plugins"));

        Assert.Null(catalog.InstallFromArchive(HelloArchive.Path).Error);

        var store = new PluginSettingsStore(null, Path.Combine(_root, "plugin-settings.json"));

        using var host = new PluginHost(new StudioContextFactory(new StudioLog(), new StudioCommands(), null, settings: store));

        var loaded = Assert.Single(host.LoadStartup(catalog.Scan()), plugin => plugin.IsLoaded);
        var type = loaded.Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Single(candidate => candidate.GetCustomAttributes(typeof(ToolWindowAttribute), false)
                .OfType<ToolWindowAttribute>()
                .Any(attribute => attribute.Id == "hello.panel"));
        var panel = Assert.IsAssignableFrom<ToolWindow>(Activator.CreateInstance(type));

        panel.Attach(loaded.Studio!);

        var window = new Window { Content = panel.Content, Width = 260, Height = 600 };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var count = panel.Content.GetVisualDescendants().OfType<AxButton>().ElementAt(1);

        Assert.All(TypeScale.Labels(window), label => Assert.False(Trimmed(label), $"при обычном кегле «{label.Text}» сокращена"));

        TypeScale.Enlarge(window, 2);

        var cut = TypeScale.Labels(window)
            .Select(label => TypeScale.Whole(label, window, out var why) ? null : $"«{label.Text}»: {why}")
            .OfType<string>()
            .ToList();

        Assert.True(cut.Count == 0, string.Join("; ", cut));
        Assert.True(Trimmed(Label(count, null)), "подписи кнопки не хватает ширины, а она не сокращена");

        window.Close();
        panel.Release();
    }

    private static TextBlock Label(Control control, string? text) =>
        control.GetVisualDescendants().OfType<TextBlock>().First(label => text is null || label.Text == text);

    private static bool Trimmed(TextBlock label) =>
        label.TextLayout.TextLines.Any(line => line.HasCollapsed);
}
