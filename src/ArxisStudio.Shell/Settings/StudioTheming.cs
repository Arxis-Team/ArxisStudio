using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace ArxisStudio.Shell.Settings;

/// <summary>
/// Применение выбранного оформления ко всей студии: темы и плотности.
/// </summary>
/// <remarks>
/// Вариант ставится и приложению, и каждому открытому окну: приложение задаёт
/// значение по умолчанию для окон, которые ещё появятся, а уже показанное окно
/// свой вариант само не перечитывает — без второго шага смена темы видна только
/// после перезапуска.
/// <para>
/// Про системную рамку здесь не знают: окно студии красит её само, когда его
/// вариант темы меняется. Обходить окна ради неё вторым списком значило бы
/// заводить второе место, где помнят, что окно вообще есть.
/// </para>
/// </remarks>
public static class StudioTheming
{
    /// <summary>Где тема держит словари ступеней плотности.</summary>
    private const string DensityFolder = "avares://ArxisStudio.Themes.Arxis/Density/";

    /// <summary>Применяет тему к приложению и всем открытым окнам.</summary>
    /// <param name="theme">Выбранная тема.</param>
    public static void Apply(StudioTheme theme)
    {
        var variant = theme == StudioTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;

        if (Application.Current is not { } app)
            return;

        app.RequestedThemeVariant = variant;

        if (app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
                window.RequestedThemeVariant = variant;
        }
    }

    /// <summary>Подмешивает словарь ступени плотности поверх темы.</summary>
    /// <param name="density">Выбранная ступень.</param>
    /// <remarks>
    /// Словарь ложится в ресурсы приложения, а не в стили: ресурсы приложения
    /// спрашиваются раньше стилей, где лежит тема, и переопределение выигрывает,
    /// не трогая саму тему. Окна здесь не обходятся, в отличие от варианта
    /// темы: смена ресурсов приложения доходит до каждой динамической ссылки
    /// сама.
    /// <para>
    /// Прежняя ступень снимается по адресу, а не по памяти. Поле с последним
    /// подмешанным словарём было бы вторым местом, где помнят, что подмешано, и
    /// разошлось бы с первым у всякого, кто подмешал словарь мимо этого метода.
    /// </para>
    /// <para>
    /// Обычная ступень подмешивается так же, как остальные, хотя её словарь
    /// пуст. Особого случая «обычная — ничего не делать» здесь нет, и ради этого
    /// пустой файл в теме и заведён: такое условие первым бы и сломалось.
    /// </para>
    /// </remarks>
    public static void Apply(StudioDensity density)
    {
        if (Application.Current is not { } app)
            return;

        var merged = app.Resources.MergedDictionaries;

        foreach (var stale in merged.OfType<ResourceInclude>().Where(IsDensity).ToList())
            merged.Remove(stale);

        var source = new Uri(DensityFolder + density + ".axaml");

        merged.Add(new ResourceInclude(source) { Source = source });
    }

    private static bool IsDensity(ResourceInclude include) =>
        include.Source?.OriginalString.StartsWith(DensityFolder, StringComparison.Ordinal) == true;
}
