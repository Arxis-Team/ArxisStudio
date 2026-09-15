using ArxisStudio.Controls;

namespace ArxisStudio.Shell;

/// <summary>
/// Кнопка полосы: переключатель студии, чью включённость решает полоса, а не нажатие.
/// </summary>
/// <remarks>
/// Включённость инструмента знает приложение, а не контрол: нажатие зовёт команду, а
/// горит ли кнопка после этого — скажет реестр полосы, когда команда отработает. Обычный
/// переключатель перевернул бы себя сам ещё до команды, и кнопка команды, которая ничего
/// не включает, осталась бы гореть после первого же нажатия.
/// <para>
/// Тему кнопка берёт у <see cref="AxToggleButton"/>: наследник без этой оговорки
/// искал бы тему по своему типу и остался бы без неё.
/// </para>
/// </remarks>
public sealed class ToolBarButton : AxToggleButton
{
    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxToggleButton);

    /// <summary>Нажатие включённость не переворачивает: её ставит полоса.</summary>
    protected override void Toggle()
    {
    }
}
