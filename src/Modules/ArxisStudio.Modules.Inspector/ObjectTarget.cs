using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Inspector;

/// <summary>
/// Цель-объект (<see cref="IStudioInspector.Inspect"/>): свойства CLR объекта — значения текстом, перемены — по
/// <see cref="INotifyPropertyChanged"/>, подписи, разделы и границы — атрибутами .NET.
/// </summary>
/// <remarks>
/// <para>
/// <b>Объект чужой, и весь его код — через шов хозяина.</b> Чтение, запись и подписка зовутся ручкой объекта: упавшее
/// свойство даёт пустое значение, а сбой засчитан плагину. С выгрузкой хозяина цель закрывается и отпускает и объект,
/// и сведения о его свойствах — сборка плагина инспектором не держится.
/// </para>
/// <para>
/// <b>Только отражение CLR.</b> Реестр свойств Avalonia о типе плагина не спрашивается, а конвертеры
/// <see cref="TypeDescriptor"/> — только у типов, которые не выгружаются: оба помнят тип навсегда, и выгрузить
/// сборку плагина было бы нельзя. Перечисление плагина читается и пишется именами, а значение своего типа плагина
/// видно текстом <see cref="object.ToString"/> и только читается.
/// </para>
/// <para>
/// <b>Заданное — отличное от значения по умолчанию</b> (<see cref="DefaultValueAttribute"/>): его строка помечена, и
/// сброс возвращает значение по умолчанию. Без объявленного значения по умолчанию сбрасывать не к чему. Истории у
/// объекта нет: Ctrl+Z ему не шагает.
/// </para>
/// </remarks>
internal sealed class ObjectTarget : InspectorTarget
{
    private readonly IStudioGuarded<object> _instance;
    private readonly IStudioStrings? _strings;
    private readonly InspectorHeader _header;
    private readonly InspectorTypeInfo _type;
    private Dictionary<string, Member> _members = new(StringComparer.Ordinal);
    private IReadOnlyList<InspectorProperty> _properties = [];
    private INotifyPropertyChanged? _notifier;

    /// <summary>Заводит цель.</summary>
    /// <param name="instance">Ручка к объекту — на счёт его хозяина.</param>
    /// <param name="title">Имя в шапке.</param>
    /// <param name="strings">Словарь модуля: почему текст не читается; пусто — без пояснения.</param>
    public ObjectTarget(IStudioGuarded<object> instance, string title, IStudioStrings? strings)
    {
        _instance = instance;
        _strings = strings;

        var type = instance.TryGet("gives its type", static it => it.GetType(), out var known) && known is not null
            ? known
            : typeof(object);

        _type = InspectorTypeInfo.Of(type);
        _header = new InspectorHeader(title) { Subtitle = type.Name, Detail = _type.FullName };
        Describe(type);

        if (instance.TryGet("tells whether it announces changes", static it => it as INotifyPropertyChanged, out var notifier) &&
            notifier is not null &&
            instance.Run("subscribes to its changes", _ => notifier.PropertyChanged += OnPropertyChanged))
        {
            _notifier = notifier;
        }

        instance.Released += OnReleased;
    }

    /// <inheritdoc/>
    public override InspectorHeader Header => _header;

    /// <inheritdoc/>
    public override InspectorTypeInfo Type => _type;

    /// <inheritdoc/>
    public override IReadOnlyList<InspectorProperty> Properties => _properties;

    /// <inheritdoc/>
    public override InspectorValue ValueOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!_members.TryGetValue(id, out var member) ||
            !_instance.TryGet("reads " + id, it => member.Property.GetValue(it), out var value))
        {
            return InspectorValue.Empty;
        }

        var text = TextOf(value);
        var written = member.Default is { } fallback && !Equals(value, fallback.Value);

        return new InspectorValue(text)
        {
            Source = written ? InspectorValueSource.Local : InspectorValueSource.Default,
            Shared = value is not null && !value.GetType().Assembly.IsCollectible ? value : null,
        };
    }

    /// <inheritdoc/>
    public override InspectorCheck Check(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(text);

        if (!_members.TryGetValue(id, out var member) || TryRead(text, member.Property.PropertyType, out _))
            return InspectorCheck.Valid;

        return InspectorCheck.Invalid(
            _strings is { } strings
                ? string.Format(CultureInfo.CurrentCulture, strings["inspector.object.unreadable"], text, member.Property.PropertyType.Name)
                : text);
    }

    /// <inheritdoc/>
    /// <remarks>Пустой текст — значение по умолчанию; свойство без него сбросить нельзя, и запись не идёт.</remarks>
    public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(writes);

        var written = true;
        var touched = new List<string>();

        foreach (var write in writes)
        {
            if (!_members.TryGetValue(write.PropertyId, out var member) || !member.Writable)
            {
                written = false;
                continue;
            }

            object? value;

            if (write.Text is null)
            {
                if (member.Default is not { } fallback)
                {
                    written = false;
                    continue;
                }

                value = fallback.Value;
            }
            else if (!TryRead(write.Text, member.Property.PropertyType, out value))
            {
                written = false;
                continue;
            }

            if (_instance.Run("writes " + write.PropertyId, it => member.Property.SetValue(it, value)))
                touched.Add(write.PropertyId);
            else
                written = false;
        }

        // Записанное объявляет сама цель: объект может молчать о себе вовсе или говорить не обо всех свойствах.
        if (touched.Count > 0)
            Raise(InspectorChanges.Values, touched);

        return Task.FromResult(written && touched.Count > 0);
    }

    /// <summary>Цель больше не нужна: ручку отпустили или хозяина выгружают.</summary>
    public void Shut()
    {
        _instance.Released -= OnReleased;

        if (_notifier is { } notifier)
            _instance.Farewell("unsubscribes from its changes", _ => notifier.PropertyChanged -= OnPropertyChanged);

        _notifier = null;
        _members = new Dictionary<string, Member>(StringComparer.Ordinal);
        _properties = [];
        Close();
    }

    /// <summary>Свойства объекта — открытые свойства экземпляра, которые читаются, с атрибутами .NET.</summary>
    private void Describe(Type type)
    {
        var properties = new List<InspectorProperty>();

        foreach (var (property, at) in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(static (property, at) => (property, at)))
        {
            if (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length > 0 || _members.ContainsKey(property.Name))
                continue;

            var range = property.GetCustomAttribute<RangeAttribute>();
            var shown = property.GetCustomAttribute<EditorBrowsableAttribute>()?.State;
            var fallback = property.GetCustomAttribute<DefaultValueAttribute>();
            var writable = property.SetMethod is { IsPublic: true } &&
                           property.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly != true &&
                           Writes(property.PropertyType);

            _members[property.Name] = new Member(property, writable, fallback);
            properties.Add(new InspectorProperty(property.Name, InspectorTypeInfo.Of(property.PropertyType))
            {
                DisplayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName,
                Category = property.GetCustomAttribute<CategoryAttribute>()?.Category,
                Description = property.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Order = at,
                Kind = InspectorPropertyKind.Clr,
                OwnerTypeName = InspectorTypeInfo.NameOf(property.DeclaringType ?? type),
                IsReadOnly = !writable,
                IsHidden = property.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false || shown == EditorBrowsableState.Never,
                IsAdvanced = shown == EditorBrowsableState.Advanced,
                AcceptsText = writable,
                DefaultText = fallback is null ? null : TextOf(fallback.Value),
                Constraints = range is null
                    ? InspectorConstraints.None
                    : new InspectorConstraints
                    {
                        Minimum = Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture),
                        Maximum = Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture),
                    },
            });
        }

        _properties = properties;
    }

    /// <summary>Пишется ли значение такого типа текстом — без конвертера, который запомнил бы тип плагина.</summary>
    private static bool Writes(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;

        return value == typeof(string) || value.IsEnum || value.IsPrimitive || value == typeof(decimal) || ReaderOf(value) is not null;
    }

    /// <summary>
    /// Как прочесть текст значением типа, который не выгружается: его конвертером или его <c>Parse</c> — так читает
    /// цвет и отступ Avalonia. Тип плагина так не читается: конвертер запомнил бы его навсегда.
    /// </summary>
    private static Func<string, object?>? ReaderOf(Type type)
    {
        if (type.Assembly.IsCollectible)
            return null;

        var converter = TypeDescriptor.GetConverter(type);

        if (converter.CanConvertFrom(typeof(string)))
            return converter.ConvertFromInvariantString;

        var parse = type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);

        return parse is not null && parse.ReturnType == type ? text => parse.Invoke(null, [text]) : null;
    }

    /// <summary>Значение текстом, как его пишут: числа — инвариантно, остальное — конвертером или строкой.</summary>
    private static string? TextOf(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "True" : "False",
        Enum choice => choice.ToString(),
        IFormattable formattable when value.GetType().IsPrimitive || value is decimal =>
            formattable.ToString(null, CultureInfo.InvariantCulture),
        _ when !value.GetType().Assembly.IsCollectible => TypeDescriptor.GetConverter(value.GetType()).ConvertToInvariantString(value),
        _ => value.ToString(),
    };

    /// <summary>Читает текст значением типа свойства.</summary>
    private static bool TryRead(string text, Type type, out object? value)
    {
        var target = Nullable.GetUnderlyingType(type);

        value = null;

        if (target is not null && text.Length == 0)
            return true;

        target ??= type;

        try
        {
            if (target == typeof(string))
                value = text;
            else if (target.IsEnum)
                return Enum.TryParse(target, text.Trim(), ignoreCase: true, out value);
            else if (target.IsPrimitive || target == typeof(decimal))
                value = Convert.ChangeType(text.Trim(), target, CultureInfo.InvariantCulture);
            else if (ReaderOf(target) is { } read)
                value = read(text);
            else
                return false;

            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or
                                              NotSupportedException or ArgumentException or TargetInvocationException)
        {
            value = null;
            return false;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var ids = string.IsNullOrEmpty(e.PropertyName) ? null : new[] { e.PropertyName };

        // Объект говорит о себе из своего потока; строки живут в потоке интерфейса. Закрывшаяся тем временем цель уже
        // ничего не объявляет — это держит Raise.
        if (Dispatcher.UIThread.CheckAccess())
            Raise(InspectorChanges.Values, ids);
        else
            Dispatcher.UIThread.Post(() => Raise(InspectorChanges.Values, ids));
    }

    private void OnReleased(object? sender, EventArgs e) => Shut();

    /// <summary>Свойство объекта: что о нём узнало отражение.</summary>
    private sealed record Member(PropertyInfo Property, bool Writable, DefaultValueAttribute? Default);
}
