using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio.Modules.Xaml.Documents;

/// <summary>Нынешнее значение члена у объекта показа: текстом, само значение и откуда оно.</summary>
/// <remarks>
/// <para>
/// Источник — то, что Unity показывает отметкой переопределения, только честнее: у свойства Avalonia
/// приоритетов семь, и человеку важно знать, написал ли значение этот элемент, пришло ли оно из стиля темы
/// или от предка. Загрузчик называет привязкой всякое значение с выражением, а тема ставит размеры и кисти
/// контролов <c>{DynamicResource}</c> — выражением стиля; поэтому выражение разбирается по приоритету
/// Avalonia (запись 337: минимальная высота флажка числилась «привязкой»).
/// </para>
/// <para>
/// Подробность стиля — его селектор — берётся у кадров хранилища значений
/// (<c>GetValueStoreDiagnostic</c>). Их описание у Avalonia помечено
/// внутренним, и чтение стоит здесь одно, со всеми отказами в запасной дороге: без селектора источник
/// остаётся стилем.
/// </para>
/// </remarks>
internal static class XamlValueOrigins
{
    /// <summary>Длиннее значение текстом не показывается: это подсказка, а не дамп объекта.</summary>
    private const int TextLimit = 120;

    /// <summary>Значение члена у объекта и откуда оно; объект — на время одного вопроса.</summary>
    public static XamlMemberValue Read(XamlLoadSession session, XamlElement element, object target, XamlMemberDescriptor member)
    {
        try
        {
            if (target is AvaloniaObject owner && member.AvaloniaProperty is { } property)
                return Avalonia(session, element, owner, member, property);

            // Свойство CLR: значение отвечает геттер, а откуда оно — только документ.
            var value = member.ClrProperty is { CanRead: true } clr && clr.GetIndexParameters().Length == 0
                ? clr.GetValue(target)
                : null;

            return new XamlMemberValue(TextOf(value), Written(element, member.Name, local: false), null, Shared(value));
        }
        catch (Exception e) when (e is InvalidOperationException or TargetInvocationException or NotSupportedException
                                      or ArgumentException)
        {
            // Геттер чужого контрола упал или объект уже не в потоке: значения нет, строка остаётся.
            return new XamlMemberValue(null, XamlValueOrigin.Default, null, null);
        }
    }

    /// <summary>Значение текстом, как его написал бы документ; объект поколения текстом не показывается.</summary>
    public static string? TextOf(object? value)
    {
        var text = value switch
        {
            null => null,
            AvaloniaObject when value is not ISolidColorBrush => null,
            double number => Number(number),
            float number => Number(number),
            bool flag => flag ? "True" : "False",
            ISolidColorBrush brush => brush.Color.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        // Тип без своего ToString отвечает именем типа — это не значение.
        if (text is null || value is not null && text == value.GetType().FullName)
            return null;

        return text.Length <= TextLimit ? text : text[..TextLimit] + "…";
    }

    /// <summary>
    /// Само значение, если его можно держать: тип из общих сборок и не меняется — число, структура,
    /// перечисление Avalonia, строка. У сплошной кисти — её цвет: кисть — объект, и её поменяют. У градиента —
    /// его неизменяемый снимок: текстом его не напишешь, а показать надо (запись 374).
    /// </summary>
    public static object? Shared(object? value) => value switch
    {
        null => null,
        string => value,
        ISolidColorBrush brush => brush.Color,
        IGradientBrush gradient => gradient.ToImmutable(),
        FontFamily => value,
        _ when value.GetType() is { IsValueType: true } type && Common(type) => value,
        _ => null,
    };

    private static XamlMemberValue Avalonia(
        XamlLoadSession session, XamlElement element, AvaloniaObject owner, XamlMemberDescriptor member, AvaloniaProperty property)
    {
        var info = session.GetValueInfo(owner, property);
        var priority = owner.GetDiagnostic(property).Priority;
        var origin = OriginOf(info.Source, priority, element, member.Name);
        var value = info.EffectiveValue;

        var detail = origin switch
        {
            XamlValueOrigin.Style or XamlValueOrigin.Template => Selector(owner, property),
            XamlValueOrigin.Inherited => Ancestor(owner, property),
            XamlValueOrigin.Expression => Attribute(element, member.Name)?.GetValueText(),
            _ => null,
        };

        return new XamlMemberValue(TextOf(value), origin, detail, Shared(value));
    }

    /// <summary>Откуда значение: выражение стиля или шаблона — их, а не привязка документа.</summary>
    /// <remarks>
    /// Загрузчик зовёт привязкой и локальное значение, если на свойстве есть хоть одно выражение — например,
    /// <c>{DynamicResource}</c> стиля темы. Победил атрибут элемента — что он такое, решает он сам: литерал поверх
    /// выражения стиля — записанное значение, а не выражение (запись 372).
    /// </remarks>
    private static XamlValueOrigin OriginOf(XamlValueSource source, BindingPriority priority, XamlElement element, string name) =>
        source switch
        {
            XamlValueSource.Local => Written(element, name, local: true),
            XamlValueSource.Binding => priority switch
            {
                BindingPriority.Style or BindingPriority.StyleTrigger => XamlValueOrigin.Style,
                BindingPriority.Template => XamlValueOrigin.Template,
                BindingPriority.Inherited => XamlValueOrigin.Inherited,
                BindingPriority.Animation => XamlValueOrigin.Animation,
                _ => Written(element, name, local: true),
            },
            XamlValueSource.Style or XamlValueSource.StyleTrigger => XamlValueOrigin.Style,
            XamlValueSource.Template => XamlValueOrigin.Template,
            XamlValueSource.Inherited => XamlValueOrigin.Inherited,
            XamlValueSource.Animation => XamlValueOrigin.Animation,
            _ => XamlValueOrigin.Default,
        };

    /// <summary>
    /// Кто написал значение: атрибут элемента — значением или выражением, <c>d:</c> — для дизайна; никто —
    /// код, если значение локальное (<paramref name="local"/>), иначе умолчание: у свойства CLR, чьё значение
    /// отвечает геттер, не узнать, ставил ли его код.
    /// </summary>
    private static XamlValueOrigin Written(XamlElement element, string name, bool local)
    {
        if (Attribute(element, name) is { } attribute)
            return attribute.GetValue() is XamlMarkupExtensionValue ? XamlValueOrigin.Expression : XamlValueOrigin.Document;

        if (element.GetDesignTimeAttribute(name) is not null)
            return XamlValueOrigin.Design;

        return local ? XamlValueOrigin.Code : XamlValueOrigin.Default;
    }

    /// <summary>Атрибут члена у элемента; присоединённый — с какой бы приставкой документ ни назвал владельца.</summary>
    /// <remarks>
    /// Поиск без приставки не видел <c>c:Shelf.Slot="{Binding Number}"</c>: значение числилось поставленным кодом,
    /// снимок делал его написанным текстом, и протяжка поля числа затёрла бы привязку (запись 391). Двух
    /// присоединённых с одним именем владельца у элемента не бывает: владельцы-тёзки из разных пространств — уже не
    /// форма, а загадка.
    /// </remarks>
    private static XamlAttribute? Attribute(XamlElement element, string name) =>
        name.Contains('.', StringComparison.Ordinal)
            ? element.Attributes.FirstOrDefault(attribute =>
                attribute is not XamlNamespaceDeclaration && !attribute.IsDirective && !attribute.IsDesignTime &&
                string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))
            : element.GetAttribute(name);

    /// <summary>Селектор стиля, поставившего значение, или тема контрола; null — сказать нечего.</summary>
    /// <remarks>
    /// Кадры хранилища значений Avalonia отдаёт только своим инструментам: и список кадров, и их описание —
    /// внутренние (<c>[PrivateApi]</c>). Читаются они отражением, только на чтение — тем же приёмом студия
    /// читает реестр свойств, — и одним местом: изменилось устройство — источник остаётся стилем без
    /// селектора, а не ломает строку.
    /// </remarks>
    private static string? Selector(AvaloniaObject owner, AvaloniaProperty property)
    {
        try
        {
            if (Frames.Value is not var (applied, source, active, values))
                return null;

            if (applied.GetValue(owner.GetValueStoreDiagnostic()) is not System.Collections.IEnumerable frames)
                return null;

            // Кадры идут от слабого к сильному, как их и сводит хранилище: значение даёт последний действующий, и тема
            // контрола, ставящая то же свойство, уступает стилю формы с классом.
            foreach (var frame in frames.Cast<object?>().Reverse())
            {
                if (frame is null || active.GetValue(frame) is not true || !Sets(values.GetValue(frame), property))
                    continue;

                return source.GetValue(frame) switch
                {
                    Style { Selector: { } selector } => selector.ToString(),
                    ControlTheme theme => theme.TargetType is { } target ? $"ControlTheme {target.Name}" : "ControlTheme",
                    _ => null,
                };
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or MemberAccessException
                                      or TargetException or TargetInvocationException or TypeLoadException)
        {
            // Устройство кадров у Avalonia внутреннее: изменилось — источник остаётся стилем без селектора.
        }

        return null;
    }

    /// <summary>Ставит ли кадр свойство: среди его значений есть запись этого свойства (<c>ValueEntryDiagnostic.Property</c>).</summary>
    private static bool Sets(object? values, AvaloniaProperty property)
    {
        if (values is not System.Collections.IEnumerable entries)
            return false;

        foreach (var entry in entries)
        {
            if (ReferenceEquals(entry?.GetType().GetProperty("Property")?.GetValue(entry), property))
                return true;
        }

        return false;
    }

    /// <summary>Как достать кадры: список у диагностики хранилища и свойства описания кадра; null — нечем.</summary>
    private static readonly Lazy<(PropertyInfo Applied, PropertyInfo Source, PropertyInfo Active, PropertyInfo Values)?> Frames =
        new(() =>
        {
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var applied = typeof(ValueStoreDiagnostic).GetProperty("AppliedFrames", Any);
            var frame = typeof(AvaloniaObject).Assembly.GetType("Avalonia.Diagnostics.IValueFrameDiagnostic");

            return applied is null || frame?.GetProperty("Source") is not { } source
                                   || frame.GetProperty("IsActive") is not { } active
                                   || frame.GetProperty("Values") is not { } values
                ? null
                : (applied, source, active, values);
        });

    /// <summary>Предок, от которого унаследовано значение: ближайший, у кого оно не наследуется само.</summary>
    private static string? Ancestor(AvaloniaObject owner, AvaloniaProperty property)
    {
        for (var parent = (owner as StyledElement)?.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.GetDiagnostic(property).Priority == BindingPriority.Inherited)
                continue;

            return parent.Name is { Length: > 0 } name ? $"{parent.GetType().Name} {name}" : parent.GetType().Name;
        }

        return null;
    }

    private static string Number(double number) =>
        double.IsNaN(number) ? "NaN" : number.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Тип из общих сборок: Avalonia, платформа — тех, что не выгружаются вместе с поколением.</summary>
    private static bool Common(Type type) =>
        AssemblyLoadContext.GetLoadContext(type.Assembly) is not { IsCollectible: true };
}
