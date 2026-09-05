using System;
using Microsoft.CodeAnalysis;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Черта между контролом студии и виджетом Avalonia.
/// </summary>
/// <remarks>
/// Правило одно, а входа у него два: код (<see cref="AvaloniaWidgetAnalyzer"/>)
/// и разметка (<see cref="MarkupWidgetAnalyzer"/>). Разъехавшись, они означали
/// бы, что запрет обходится сменой места записи — <c>new TextBox()</c> нельзя,
/// а <c>&lt;TextBox/&gt;</c> можно.
/// </remarks>
internal static class StudioControls
{
    /// <summary>Полное имя базового типа, по которому проведена черта.</summary>
    public const string TemplatedControl = "Avalonia.Controls.Primitives.TemplatedControl";

    /// <summary>
    /// Сборки, чьи контролы считаются контролами студии.
    /// </summary>
    /// <remarks>
    /// Их две: виджеты и набор иконок. <c>AxIcon</c> живёт отдельной
    /// библиотекой, но остаётся контролом студии: наследника от него правило
    /// трогать не должно по той же причине, что и наследника <c>AxButton</c>.
    /// </remarks>
    private static readonly string[] StudioAssemblies = ["ArxisStudio.Controls", "ArxisStudio.Icons"];

    /// <summary>
    /// Виджет ли это, которого в интерфейсе расширения быть не должно.
    /// </summary>
    /// <param name="type">Тип, о котором спрашивают.</param>
    /// <param name="templated">Символ <c>TemplatedControl</c> этой компиляции.</param>
    /// <returns><c>true</c>, если тип — шаблонный контрол Avalonia.</returns>
    /// <remarks>
    /// Наследник контрола студии разрешён, каким бы глубоким он ни был: своя
    /// кнопка поверх <c>AxButton</c> — это по-прежнему контрол студии.
    /// </remarks>
    public static bool IsForbidden(INamedTypeSymbol type, INamedTypeSymbol templated)
    {
        var isTemplated = false;

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, templated))
            {
                isTemplated = true;
                break;
            }

            // Дошли до контрола студии раньше, чем до шаблонного контрола
            // Avalonia, — значит это контрол студии.
            if (IsStudioControl(current))
            {
                return false;
            }
        }

        return isTemplated && IsAvalonia(type);
    }

    /// <summary>
    /// Что сказать про виджет: чем его заменить или что замены пока нет.
    /// </summary>
    /// <param name="compilation">Компиляция, в которой ищут замену.</param>
    /// <param name="widget">Виджет, о котором речь.</param>
    /// <returns>Вторая половина замечания.</returns>
    /// <remarks>
    /// Имя замены составляется по правилу набора: <c>AxButton</c> для
    /// <c>Button</c>. Замечание перестаёт быть запретом и становится
    /// подсказкой, а там, где замены нет, оно само сообщает, чего в наборе
    /// студии не хватает, — и это единственный список пробелов, который не
    /// приходится вести руками.
    /// </remarks>
    public static string Advice(Compilation compilation, INamedTypeSymbol widget)
    {
        var replacement = compilation.GetTypeByMetadataName(StudioControlsNamespace + ".Ax" + widget.Name);

        return replacement is null
            ? "замены в ArxisStudio.Controls пока нет"
            : "вместо него — " + replacement.Name;
    }

    private const string StudioControlsNamespace = "ArxisStudio.Controls";

    private static bool IsStudioControl(ISymbol type) =>
        type.ContainingAssembly?.Name is { } name && Array.IndexOf(StudioAssemblies, name) >= 0;

    private static bool IsAvalonia(ISymbol type) =>
        type.ContainingAssembly?.Name is { } name &&
        (name == "Avalonia" || name.StartsWith("Avalonia.", StringComparison.Ordinal));
}
