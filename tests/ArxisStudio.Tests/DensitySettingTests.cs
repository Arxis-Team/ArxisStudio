using ArxisStudio.Controls;
using ArxisStudio.Settings;
using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Плотность интерфейса как настройка студии.
/// </summary>
/// <remarks>
/// Сами ступени проверяет тема: что в словаре, в каком порядке и что направленный
/// зазор берёт величину своей ступени. Здесь — то, чего теме не видно: что
/// настройка переживает файл, что страница оформления её показывает, сохраняет и
/// откатывает, и что подмешанная ступень доезжает до уже открытого окна.
/// <para>
/// Очередь общая: подмешивание трогает ресурсы приложения, а приложение одно на
/// процесс. Живая проверка при этом целиком синхронна и снимает свою ступень в
/// <c>finally</c> — между её шагами никто другой на поток интерфейса не попадёт,
/// а после неё приложение остаётся таким, каким было.
/// </para>
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class DensitySettingTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"arxis-density-{Guid.NewGuid():N}");

    public DensitySettingTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Файл, записанный до плотности, читается как обычная ступень.
    /// </summary>
    /// <remarks>
    /// Такой файл есть у каждого, кто запускал студию раньше. Первая по списку
    /// ступень — плотная, и прочитайся отсутствующее поле как «первая», студия
    /// после обновления молча сжалась бы у всех.
    /// </remarks>
    [Fact]
    public void A_file_written_before_density_reads_as_normal()
    {
        var path = Path.Combine(_home, "settings.json");

        File.WriteAllText(path, """{ "theme": "light", "language": "en" }""");

        Assert.Equal(StudioDensity.Normal, new JsonSettingsStore(path).Current.Density);
    }

    /// <summary>
    /// Мёртвое поле прежних версий не оживает.
    /// </summary>
    /// <remarks>
    /// Плотность лежала в файле с первого дня и была снята с модели, потому что
    /// не значила ничего. У тех, кто запускал студию тогда, в файле так и лежит
    /// <c>"density"</c> — и вернувшаяся настройка, прочитав его, оживила бы выбор,
    /// сделанный, когда он ничего не менял. Найдено живьём: студия на машине
    /// разработчика поднялась плотной из файла от 8 сентября. Тест на файл без
    /// поля этого не видел — поле там было, только мёртвое.
    /// </remarks>
    [Fact]
    public void The_dead_density_field_of_earlier_versions_stays_dead()
    {
        var path = Path.Combine(_home, "settings.json");

        File.WriteAllText(path, """
            {
              "theme": "dark",
              "accentColor": "#3574F0",
              "density": "compact",
              "language": "ru"
            }
            """);

        var store = new JsonSettingsStore(path);

        Assert.Equal(StudioDensity.Normal, store.Current.Density);

        store.Save();

        var written = File.ReadAllText(path);

        Assert.DoesNotContain("\"density\"", written);
        Assert.Contains("\"interfaceDensity\": \"normal\"", written);
    }

    /// <summary>Ступень переживает запись и лежит в файле словом.</summary>
    [Fact]
    public void The_density_survives_the_file_as_a_word()
    {
        var path = Path.Combine(_home, "settings.json");
        var store = new JsonSettingsStore(path);

        store.Current.Density = StudioDensity.Compact;
        store.Save();

        Assert.Contains("\"interfaceDensity\": \"compact\"", File.ReadAllText(path));
        Assert.Equal(StudioDensity.Compact, new JsonSettingsStore(path).Current.Density);
    }

    /// <summary>
    /// Страница оформления копит ступень, пишет её по «Сохранить» и забывает по «Отмене».
    /// </summary>
    [Fact]
    public async Task The_appearance_page_stages_saves_and_reverts_the_density()
    {
        var path = Path.Combine(_home, "settings.json");
        var page = new AppearancePage(new JsonSettingsStore(path));

        Assert.Equal(1, page.DensityIndex);

        page.DensityIndex = 2;

        Assert.True(page.HasChanges);
        Assert.False(File.Exists(path), "до «Сохранить» файл не трогается");

        page.Revert();

        Assert.Equal(1, page.DensityIndex);
        Assert.False(page.HasChanges);

        page.DensityIndex = 0;
        await page.CommitAsync([]);

        Assert.False(page.HasChanges, "записанное несохранённым не считается");
        Assert.Equal(StudioDensity.Compact, new JsonSettingsStore(path).Current.Density);
    }

    /// <summary>Номер вне ряда ступеней страница не принимает.</summary>
    /// <remarks>
    /// Сегментный переключатель такого не отдаёт, но свойство открыто привязке, и
    /// ступень «3» ушла бы в файл, а оттуда — в адрес словаря, которого в теме нет.
    /// </remarks>
    [Fact]
    public void A_number_outside_the_tiers_is_refused()
    {
        var page = new AppearancePage(new JsonSettingsStore(Path.Combine(_home, "settings.json")));

        page.DensityIndex = 3;
        page.DensityIndex = -1;

        Assert.Equal(1, page.DensityIndex);
        Assert.False(page.HasChanges);
    }

    /// <summary>
    /// Подмешанная ступень доезжает до уже открытого окна, и смена её не копит.
    /// </summary>
    /// <remarks>
    /// Три вопроса одной проверкой. Выигрывают ли ресурсы приложения у стилей, где
    /// лежит тема, — иначе ступень подмешалась бы и не значила ничего. Доходит ли
    /// смена до окна, которое уже на экране, — вариант темы, например, окно само не
    /// перечитывает. И заменяет ли новая ступень прежнюю, а не ложится поверх:
    /// вторая подмешанная поверх первой работала бы, пока в словарях одни и те же
    /// ключи, и разошлась бы на первом же различии.
    /// <para>
    /// Заодно это единственное место, где студия проверяет адрес словарей в теме:
    /// переименуй тема папку или файл — упадёт здесь, а не у человека, выбравшего
    /// плотность.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void An_applied_tier_reaches_an_open_window_and_replaces_the_previous_one()
    {
        var button = new AxButton { Content = "Готово" };
        var window = new Window { Content = new StackPanel { Children = { button } } };

        window.Show();

        try
        {
            window.UpdateLayout();
            Assert.Equal(28d, button.Bounds.Height);

            StudioTheming.Apply(StudioDensity.Compact);
            window.UpdateLayout();
            Assert.Equal(24d, button.Bounds.Height);

            StudioTheming.Apply(StudioDensity.Comfortable);
            window.UpdateLayout();
            Assert.Equal(32d, button.Bounds.Height);

            StudioTheming.Apply(StudioDensity.Normal);
            window.UpdateLayout();
            Assert.Equal(28d, button.Bounds.Height);

            Assert.Single(Tiers());
        }
        finally
        {
            foreach (var tier in Tiers())
                Application.Current!.Resources.MergedDictionaries.Remove(tier);

            window.Close();
        }
    }

    /// <summary>
    /// Интерфейс, собранный кодом, идёт за плотностью так же, как разметка.
    /// </summary>
    /// <remarks>
    /// Так пишет пример плагина и так советует docs/plugin-markup.md: значение
    /// темы в коде берут привязкой к ресурсу, а не числом. Обещание «идёт за
    /// темой и плотностью» дано словами, и здесь оно проверено — иначе совет
    /// оставался бы советом, а ARX0010 требовал бы переписать число на то, что
    /// не работает.
    /// </remarks>
    [AvaloniaFact]
    public void A_panel_built_in_code_follows_the_density_through_a_resource_binding()
    {
        var panel = new StackPanel { Children = { new Border(), new Border() } };
        var window = new Window { Content = panel };

        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("AxGapFormRow"));
        window.Show();

        try
        {
            Assert.Equal(12d, panel.Spacing);

            StudioTheming.Apply(StudioDensity.Compact);
            Assert.Equal(8d, panel.Spacing);

            StudioTheming.Apply(StudioDensity.Comfortable);
            Assert.Equal(16d, panel.Spacing);
        }
        finally
        {
            foreach (var tier in Tiers())
                Application.Current!.Resources.MergedDictionaries.Remove(tier);

            window.Close();
        }
    }

    private static List<ResourceInclude> Tiers() =>
        [.. Application.Current!.Resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Where(include => include.Source?.OriginalString.Contains("/Density/", StringComparison.Ordinal) == true)];
}
