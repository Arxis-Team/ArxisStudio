using Avalonia;
using Avalonia.Data;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Слушает перевод одного ключа — чтобы знать, что язык студии сменился.
/// </summary>
/// <remarks>
/// У словарей модуля события смены языка нет, а привязка к строке есть: она обновляется вместе
/// с языком, и проба зовёт перестройку, когда перевод сменился.
/// </remarks>
internal sealed class LanguageProbe : AvaloniaObject, IDisposable
{
    private static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LanguageProbe, string?>("Text");

    private readonly IDisposable _binding;
    private readonly Action _changed;

    /// <summary>Привязывает пробу к строке словаря.</summary>
    /// <param name="binding">Привязка к переводу ключа.</param>
    /// <param name="changed">Что делать, когда перевод сменился.</param>
    public LanguageProbe(BindingBase binding, Action changed)
    {
        _changed = changed;
        _binding = Bind(TextProperty, binding);
    }

    /// <inheritdoc/>
    public void Dispose() => _binding.Dispose();

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && change.OldValue is not null)
            _changed();
    }
}
