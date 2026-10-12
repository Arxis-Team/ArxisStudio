using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Modules.Xaml.Session;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;

namespace ArxisStudio.Modules.Xaml.Documents;

// Данные элемента: что читают его привязки и какими классами проекта их можно задать.
// Часть DesignView; общее описание типа — в DesignView.cs.
internal sealed partial class DesignView
{
    /// <summary>Член данных — так его пишет документ.</summary>
    private const string DataContextMember = "DataContext";

    /// <summary>Данные для дизайна, написанные элементом свойства или атрибутом корня.</summary>
    private const string DesignDataContextMember = "Design.DataContext";

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Тип данных (<c>x:DataType</c>) разрешает среда поколения — асинхронно, — и вопрос ждёт её, держа только текст и
    /// сессию; объекты показа читаются уже после, одним синхронным отрезком (<see cref="Describe"/>). Замена поколения
    /// посреди ожидания кончает вопрос отменой (<see cref="LetGo"/>), а не держит поколение через себя.
    /// </para>
    /// <para>
    /// Компиляция привязок — ближайший <c>x:CompileBindings</c> элемента и его предков, а без него — свойство проекта.
    /// Загрузчик на это не отвечает: живой показ грузит привязки отражением, прощая, а сборка дизайна идёт настройками
    /// проекта — и не соберёт <c>{Binding}</c> без типа данных.
    /// </para>
    /// </remarks>
    public async ValueTask<XamlDataScope?> DescribeDataAsync(XamlElementPath element, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (Shown() is not { } session || element.Resolve(session.Document) is not { } syntax)
            return null;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _generation.Token);
        XamlDataContextInfo info;

        try
        {
            info = await session.GetDataContextAsync(syntax, linked.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException && !cancellationToken.IsCancellationRequested)
        {
            // Показ отдал объекты или сменил сессию, пока ждали среду: отвечать не о чем.
            return null;
        }

        return ReferenceEquals(Shown(), session) ? Describe(element, info) : null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Каталог берётся у хоста его очередью, именами; файлы кода читаются не в потоке интерфейса. Типов поколения ответ
    /// не держит.
    /// </remarks>
    public async ValueTask<IReadOnlyList<XamlDataTypeInfo>> GetDataTypesAsync(CancellationToken cancellationToken = default)
    {
        if (_released || _disposed
            || _entry.Session.Snapshot is not { } snapshot
            || !snapshot.TryGetProjectForFile(_entry.Path, out var project))
        {
            return [];
        }

        if (await _entry.Owner.CatalogOfAsync(_entry.Path, cancellationToken) is not { } catalog)
            return [];

        var declared = await Task.Run(() => _entry.Owner.DataTypes.Declared(snapshot, project), cancellationToken);

        return DataTypes.Merge(catalog, declared);
    }

    /// <summary>Сведения о данных — по объектам показа сейчас; типы поколения переводятся в имена и отпускаются.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private XamlDataScope? Describe(XamlElementPath element, XamlDataContextInfo info)
    {
        if (Live(element) is not { } shown)
            return null;

        var (source, sourceElement, sourceText) = SourceOf(shown);
        var context = source == XamlDataSource.None ? null : (shown.Object as StyledElement)?.DataContext;
        var byDefault = _entry.Session.CompilesBindingsByDefault(_entry.Path);

        return new XamlDataScope(
            source,
            sourceElement,
            sourceText,
            context is null ? null : XamlMemberMetadata.TypeOf(context.GetType()),
            info.WrittenDataType,
            info.DataTypeElement is { IsPropertyElementSyntax: false } typed ? XamlElementPath.Of(typed) : null,
            info.DataType is { } dataType ? XamlMemberMetadata.TypeOf(dataType) : null,
            CompilesBindings(shown.Element) ?? byDefault,
            byDefault);
    }

    /// <summary>Откуда у элемента данные: написал ли он их сам, взял у предка, дал ли их дизайн или код.</summary>
    /// <remarks>
    /// <para>
    /// Ищется от элемента вверх, мимо элементов свойств. Первый, кто пишет <c>DataContext</c> — атрибутом или длинной
    /// записью своего члена, — и есть источник. Не пишет никто — источник тот, у чьего объекта данные не унаследованы:
    /// их поставил код его конструктора, а у корня — ещё и дизайн.
    /// </para>
    /// <para>
    /// Унаследованное корнем — не данные формы: корень стоит в рамке дизайнера, и его данные пришли бы из студии.
    /// </para>
    /// </remarks>
    private static (XamlDataSource Source, XamlElementPath? Element, string? Text) SourceOf(LiveElement shown)
    {
        var objects = shown.Session.Objects;

        for (var at = shown.Element; at is not null; at = OwnerOf(at))
        {
            var live = objects.GetObject(at);

            if (Written(at, live?.GetType()) is { } written)
            {
                return (ReferenceEquals(at, shown.Element) ? written.Source : XamlDataSource.Inherited,
                    XamlElementPath.Of(at), written.Text);
            }

            if (live is not StyledElement owner
                || owner.GetDiagnostic(StyledElement.DataContextProperty).Priority is BindingPriority.Inherited or BindingPriority.Unset)
            {
                continue;
            }

            return OwnerOf(at) is null && owner is Control control && control.IsSet(Design.DataContextProperty)
                ? (XamlDataSource.Design, XamlElementPath.Root, DesignText(at, owner.GetType()))
                : (XamlDataSource.Code, null, null);
        }

        return (XamlDataSource.None, null, null);
    }

    /// <summary>Что элемент сам пишет в данные; null — не пишет.</summary>
    private static (XamlDataSource Source, string? Text)? Written(XamlElement element, Type? type)
    {
        if (element.GetAttribute(DataContextMember) is { } attribute)
        {
            var text = attribute.GetValueText();

            return attribute.GetValue() is XamlMarkupExtensionValue extension
                ? (IsSelf(extension) ? XamlDataSource.Self : XamlDataSource.Expression, text)
                : (XamlDataSource.Instance, text);
        }

        return type is not null && XamlValueOrigins.PropertyElement(element, DataContextMember, type) is { } written
            ? (XamlDataSource.Instance, XamlValueOrigins.ContentText(written))
            : null;
    }

    /// <summary>Как корень пишет данные для дизайна: <c>d:DataContext</c>, атрибутом или элементом свойства.</summary>
    private static string? DesignText(XamlElement root, Type type) =>
        root.GetDesignTimeAttribute(DataContextMember)
        ?? root.Attributes.FirstOrDefault(static attribute =>
            !attribute.IsDirective && !attribute.IsDesignTime &&
            string.Equals(attribute.Name.LocalName, DesignDataContextMember, StringComparison.Ordinal))?.GetValueText()
        ?? (XamlValueOrigins.PropertyElement(root, DesignDataContextMember, type) is { } written
            ? XamlValueOrigins.ContentText(written)
            : null);

    /// <summary>
    /// Привязка элемента к самому себе: <c>{Binding $self}</c> или <c>{Binding RelativeSource={RelativeSource Self}}</c> без
    /// пути.
    /// </summary>
    private static bool IsSelf(XamlMarkupExtensionValue extension)
    {
        if (extension.TypeName.LocalName is not ("Binding" or "CompiledBinding" or "ReflectionBinding"))
            return false;

        var path = (extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("Path"))?.Value;

        if (path is XamlLiteralValue { Text: { } text })
            return string.Equals(text.Trim(), "$self", StringComparison.Ordinal);

        return path is null
               && extension.GetArgument("RelativeSource")?.Value is XamlMarkupExtensionValue relative
               && (relative.PositionalArguments.FirstOrDefault() ?? relative.GetArgument("Mode"))?.Value is XamlLiteralValue { Text: "Self" };
    }

    /// <summary>Ближайший <c>x:CompileBindings</c> элемента и его предков, читаемый как да или нет; null — такого нет.</summary>
    private static bool? CompilesBindings(XamlElement element)
    {
        for (var at = element; at is not null; at = OwnerOf(at))
        {
            if (at.GetDirective("CompileBindings") is { } written && bool.TryParse(written.Trim(), out var compiles))
                return compiles;
        }

        return null;
    }
}
