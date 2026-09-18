using ArxisStudio.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

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
/// <para>
/// Экранному диктору кнопка — кнопка, пока полоса не взялась держать её включённость
/// (<see cref="IsToggle"/>). Прежде диктор читал любую кнопку полосы «переключателем,
/// выключено», хотя включённость есть у одной из многих, а у остальных — одна команда.
/// </para>
/// </remarks>
public sealed class ToolBarButton : AxToggleButton
{
    /// <summary>
    /// Держит ли полоса включённость кнопки: единожды сказав «включено» или «выключено», она
    /// делает кнопку переключателем насовсем.
    /// </summary>
    public bool IsToggle { get; set; }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(AxToggleButton);

    /// <summary>Нажатие включённость не переворачивает: её ставит полоса.</summary>
    protected override void Toggle()
    {
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <inheritdoc/>
    /// <remarks>
    /// Смену включённости диктор узнаёт от пира: переключатель Avalonia сообщает о ней только
    /// своему пиру, а у этой кнопки пир свой.
    /// </remarks>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCheckedProperty && IsToggle && ControlAutomationPeer.FromElement(this) is Peer peer)
            peer.RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, State(change.GetOldValue<bool?>()), State(change.GetNewValue<bool?>()));
    }

    private static ToggleState State(bool? on) => on == true ? ToggleState.On : ToggleState.Off;

    /// <summary>
    /// Кнопка для диктора: нажимается, пока она команда, и включается, когда переключатель.
    /// </summary>
    /// <remarks>
    /// Шаблон спрашивается у пира при каждом обращении, поэтому кнопка, ставшая переключателем
    /// после первого доклада полосы, читается им сразу, без нового пира.
    /// </remarks>
    private sealed class Peer(ToolBarButton owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => State(owner.IsChecked);

        /// <summary>Переключатель переключает команда: нажатие — её вызов, как щелчок.</summary>
        public void Toggle() => Invoke();

        protected override object? GetProviderCore(Type providerType)
        {
            if (providerType == typeof(IToggleProvider) && !owner.IsToggle)
                return null;

            if (providerType == typeof(IInvokeProvider) && owner.IsToggle)
                return null;

            return base.GetProviderCore(providerType);
        }
    }
}
