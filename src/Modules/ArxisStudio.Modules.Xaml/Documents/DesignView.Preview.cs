using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Data;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Xaml.Documents;

// Предпросмотр значения: объект показа выглядит так, будто документ написал другое, а документ не тронут.
// Часть DesignView; общее описание типа — в DesignView.cs.
internal sealed partial class DesignView
{
    private readonly List<ValuePreview> _previews = [];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Значение ставится на приоритете анимации, а не локальным: локальное значение — то, что написал
    /// документ, и затёрло бы его вместе с привязкой. Анимация лежит поверх всего, и снятие её значения
    /// (<see cref="IDisposable"/>, который возвращает <see cref="AvaloniaObject.SetValue{T}(StyledProperty{T}, T, BindingPriority)"/>)
    /// возвращает и написанное, и стиль, и привязку в точности. Так же холст показывает тягу на живом контроле
    /// и пишет текст один раз, при отпускании.
    /// </para>
    /// <para>
    /// У прямого свойства и свойства CLR приоритета нет — поставленное значение не снять, не потеряв прежнего,
    /// — и предпросмотра у них нет.
    /// </para>
    /// </remarks>
    public IXamlValuePreview? Preview(XamlElementPath element, string member)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        Dispatcher.UIThread.VerifyAccess();

        if (Live(element) is not { } shown
            || shown.Object is not AvaloniaObject
            || Resolve(shown, member) is not { AvaloniaProperty: { IsDirect: false, IsReadOnly: false } })
        {
            return null;
        }

        var preview = new ValuePreview(this, element, member, _entry.Owner.Defer(_entry.Owner.Words.Preview));

        _previews.Add(preview);

        return preview;
    }

    /// <summary>
    /// Предпросмотры кончаются: текст документа сменился — записанное значение пришло, — поколение уходит или
    /// показ отпущен.
    /// </summary>
    internal void EndPreviews()
    {
        foreach (var preview in _previews.ToList())
            preview.Dispose();
    }

    /// <summary>Предпросмотр значения одного члена одного элемента.</summary>
    /// <remarks>
    /// Держит путь и имя, а не объект: объект берётся заново на каждом показе. Держит и снятие показанного
    /// значения — оно ссылается на объект, — и замену поколения: пока жив предпросмотр, поколение не уходит.
    /// </remarks>
    private sealed class ValuePreview(DesignView view, XamlElementPath element, string member, IDisposable deferral)
        : IXamlValuePreview
    {
        private IDisposable? _shown;
        private bool _ended;

        /// <inheritdoc/>
        public XamlValueCheck Show(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            Dispatcher.UIThread.VerifyAccess();

            if (_ended)
                return default;

            // Пустое — «снять атрибут», выражение — не значение: показать их объекту нечем, и он остаётся с
            // написанным документом.
            if (text.Length == 0 || IsExpression(text))
            {
                Withdraw();
                return default;
            }

            if (view.Live(element) is not { Object: AvaloniaObject target } shown
                || Resolve(shown, member) is not { AvaloniaProperty: { } property } descriptor)
            {
                return default;
            }

            // Картинка — имя ресурса: его читает загрузка, а не предпросмотр, и объект ждёт записи.
            if (descriptor.ReadsResource)
            {
                Withdraw();
                return default;
            }

            var converted = descriptor.ConvertFromText(text);

            if (!converted.Succeeded)
                return view.Check(descriptor, text);

            Withdraw();
            _shown = target.SetValue(property, converted.Value, BindingPriority.Animation);

            return default;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_ended)
                return;

            _ended = true;
            Withdraw();
            deferral.Dispose();
            view._previews.Remove(this);
        }

        private void Withdraw()
        {
            _shown?.Dispose();
            _shown = null;
        }
    }
}
