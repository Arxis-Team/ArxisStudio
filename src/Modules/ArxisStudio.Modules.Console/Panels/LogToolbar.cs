using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Modules.Console.Log;
using Avalonia.Controls;
using Avalonia.Media;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Полоса отбора: счётчики уровней и их вид.
/// </summary>
/// <remarks>
/// Отдельным типом, потому что у счётчиков своя, ни на что не похожая связь с темой и с отбором:
/// число берётся из всего журнала, включённость — из отбора, а цвет значка — из темы, и берётся он
/// кистью, а не стилем. Панели остаётся сказать «покажи столько-то» и «включено вот это».
/// <para>
/// Кистью, а не селектором: селектор, лезущий в содержимое чужой кнопки, до значка не достаёт —
/// проверено на живой студии, значок оставался цвета кнопки. Кисти при этом по-прежнему
/// принадлежат теме: здесь только просьба выдать их по имени.
/// </para>
/// </remarks>
/// <param name="view">Разметка панели, чьи части полоса показывает.</param>
internal sealed class LogToolbar(LogPanelView view)
{
    /// <summary>Приглушение выключенного уровня.</summary>
    /// <remarks>
    /// Выключенный счётчик гаснет целиком: включённый лежит на заливке, но число выключенного
    /// уровня рядом с ним читалось бы тем же весом.
    /// </remarks>
    private const double Muted = 0.45;

    /// <summary>Берёт у темы цвета уровней — при построении и на каждую смену темы.</summary>
    public void ReadTheme()
    {
        view.ErrorIcon.Foreground = Brush("AxErrorBrush");
        view.WarningIcon.Foreground = Brush("AxWarningBrush");
    }

    /// <summary>Показывает, какие уровни включены.</summary>
    /// <param name="filter">Отбор, из которого берётся включённость.</param>
    public void ShowLevels(LogFilter filter)
    {
        Show(view.Errors, filter.Error);
        Show(view.Warnings, filter.Warning);
        Show(view.Infos, filter.Info);
        Show(view.Debugs, filter.Debug);

        static void Show(AxToggleButton toggle, bool on)
        {
            toggle.IsChecked = on;
            toggle.Opacity = on ? 1 : Muted;
        }
    }

    /// <summary>Показывает, сколько записей каждого уровня в журнале.</summary>
    /// <param name="counts">Счётчики по всему журналу.</param>
    public void ShowCounts(LogCounts counts)
    {
        view.ErrorCount.Text = Number(counts.Error);
        view.WarningCount.Text = Number(counts.Warning);
        view.InfoCount.Text = Number(counts.Info);
        view.DebugCount.Text = Number(counts.Debug);
    }

    private static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);

    private IBrush? Brush(string key) =>
        view.TryFindResource(key, view.ActualThemeVariant, out var value) ? value as IBrush : null;
}
