using System.Runtime.CompilerServices;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.Xaml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio.Modules.Xaml.Documents;

// Ресурсы элемента: что он может назвать по ключу и найдётся ли названное — по документу и приложению формы.
// Часть DesignView; общее описание типа — в DesignView.cs.
internal sealed partial class DesignView
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Ключи элемента и предков — у их объектов: что включают их словари, знают только они, — а где ключ объявлен,
    /// говорит синтаксис (<see cref="XamlResourceDeclarations"/>, ADR 0035 Markup) или файл включённого словаря. Ключи
    /// приложения формы — из его файла и словарей, которые оно включает (<see cref="ResourceFiles"/>), темы — у его
    /// стилей. Объекты студии, в которых форма стоит, не спрашиваются: ключ, найденный у них, — студийный.
    /// </para>
    /// <para>
    /// Карточка дизайнера, показывая форму, берёт себе стили её приложения, а у окна-корня — ещё его словарь и стили.
    /// Стили приложения показ запоминает, когда берёт его (<see cref="_applicationStyles"/>), а окну одолженное
    /// возвращается на время чтения той же дорогой, что на время записи (<see cref="IXamlRootLender"/>).
    /// </para>
    /// <para>
    /// Значения читаются в синхронном отрезке (<see cref="Resources"/>) и уходят именами и тем, что можно держать.
    /// </para>
    /// </remarks>
    public ValueTask<IReadOnlyList<XamlResourceInfo>> GetResourcesAsync(
        XamlElementPath element, string member, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);

        IReadOnlyList<XamlResourceInfo> resources = Shown() is { } session && element.Resolve(session.Document) is { } syntax
            ? Resources(session, syntax, member)
            : [];

        return ValueTask.FromResult(resources);
    }

    /// <summary>
    /// Ресурсы элемента: свои и предков, ближайший первым, затем приложения формы и его темы — каждый ключ один раз.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<XamlResourceInfo> Resources(XamlLoadSession session, XamlElement syntax, string member)
    {
        var target = session.Objects.GetObject(syntax) is { } owner && Target(session, owner, member) is { IsResolved: true } descriptor
            ? descriptor.ValueType
            : null;
        var theme = ThemeOf(session, syntax);
        var hosted = ThemeHosted(session, syntax);
        var declared = XamlResourceDeclarations.InScope(syntax);
        var files = _entry.Session.ResourceFiles;
        var included = files.IncludedBy(session.Document, _entry.Path, _entry.Session.Snapshot);
        var found = new List<XamlResourceInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var scope = XamlResourceScope.Element;

        // Тема спрошена раньше: окно, получив одолженное назад, стоит вне карточки, и его содержимое темы показа не знает.
        using (LendRoot(session))
        {
            for (var at = syntax; at is not null; at = OwnerOf(at), scope = XamlResourceScope.Ancestor)
            {
                if (session.Objects.GetObject(at) is not StyledElement node || !((IResourceNode)node).HasResources)
                    continue;

                foreach (var key in Keys(node, at))
                {
                    if (!seen.Add(key))
                        continue;

                    var declaration = declared.FirstOrDefault(entry => ReferenceEquals(entry.Scope, at) && entry.IsTextKey && entry.Key == key);
                    var file = declaration is null ? included.FirstOrDefault(entry => entry.Key == key) : null;

                    found.Add(Info(key, scope, ((IResourceNode)node).TryGetResource, theme, hosted, target) with
                    {
                        Element = declaration is null ? null : XamlElementPath.Of(declaration.Element),
                        File = file?.File ?? default,
                        KeySpan = file?.KeySpan,
                    });
                }
            }
        }

        if (Application is null)
            return found;

        foreach (var entry in ApplicationFile.IsEmpty ? [] : files.Of(ApplicationFile, _entry.Session.Snapshot))
        {
            if (Read(TryApplication, entry.Key, theme) is null || !seen.Add(entry.Key))
                continue;

            found.Add(Info(entry.Key, XamlResourceScope.Application, TryApplication, theme, hosted, target) with
            {
                File = entry.File,
                KeySpan = entry.KeySpan,
            });
        }

        foreach (var key in ThemeKeys())
        {
            if (seen.Add(key))
                found.Add(Info(key, XamlResourceScope.Theme, TryApplication, theme, hosted, target));
        }

        return found;
    }

    /// <summary>
    /// Проверка ключа ресурса: тем же поиском, что выбор, — у элемента, его предков в документе и приложения формы, — а у
    /// статической ссылки на ключ одной темы ещё и найдёт ли она его, пока элемент строится.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private XamlExpressionCheck CheckResource(XamlLoadSession session, XamlElement syntax, string member, XamlMarkupExtensionValue extension)
    {
        var words = _entry.Owner.Words;

        if ((extension.PositionalArguments.FirstOrDefault() ?? extension.GetArgument("ResourceKey"))?.Value is not XamlLiteralValue { Text: var text } ||
            text.Trim() is not { Length: > 0 } key)
        {
            return new XamlExpressionCheck(member, XamlExpressionKind.Resource, XamlExpressionState.Unchecked, words.ResourceUnchecked, false, null, null);
        }

        var theme = ThemeOf(session, syntax);
        var root = session.RootObject is TopLevel;

        // Окно-корень, пока его показывает карточка, своего словаря не держит. Записанный в нём ключ находит синтаксис, а
        // включённый словарём, которого синтаксис не знает, — само окно, получив одолженное назад, и только когда больше
        // ключ искать негде: возврат перестраивает стили всей формы, а проверка идёт после каждого перечитывания.
        var found = OnLevels(session, syntax, key, theme) ?? (root ? DeclaredAtRoot(syntax, key) : null) ?? InApplication(key, theme);

        if (found is null && root)
        {
            using (LendRoot(session))
                found = OnLevels(session, syntax, key, theme);
        }

        if (found is not { } at)
        {
            // Приложение есть, а не построено: ключ может быть у него — сломанным его не назвать.
            return Application is null && !ApplicationFile.IsEmpty
                ? new XamlExpressionCheck(member, XamlExpressionKind.Resource, XamlExpressionState.Unchecked, words.ResourcePending, false, null, null)
                : new XamlExpressionCheck(member, XamlExpressionKind.Resource, XamlExpressionState.Broken, words.ResourceMissing(key), false, null, null);
        }

        var isStatic = extension.TypeName.LocalName.StartsWith("StaticResource", StringComparison.Ordinal);
        var type = at.Value?.GetType() is { } held ? XamlMemberMetadata.TypeOf(held) : null;

        if (at.Themed && isStatic && !ThemeHosted(session, syntax))
            return new XamlExpressionCheck(member, XamlExpressionKind.Resource, XamlExpressionState.Broken, words.ResourceThemedStatic(key), false, null, type);

        var message = words.ResourceFound(at.Scope, at.Owner, type?.Name ?? at.TypeName);

        return new XamlExpressionCheck(
            member,
            XamlExpressionKind.Resource,
            XamlExpressionState.Resolved,
            at.Themed && isStatic ? words.ResourceFrozen(message) : message,
            false,
            null,
            type);
    }

    /// <summary>Ключ у элемента и предков в документе — у их объектов, ближайший первым.</summary>
    private static Found? OnLevels(XamlLoadSession session, XamlElement syntax, string key, ThemeVariant theme)
    {
        var scope = XamlResourceScope.Element;

        for (var at = syntax; at is not null; at = OwnerOf(at), scope = XamlResourceScope.Ancestor)
        {
            if (session.Objects.GetObject(at) is StyledElement node && ((IResourceNode)node).HasResources &&
                Read(((IResourceNode)node).TryGetResource, key, theme) is { } read)
            {
                return new Found(scope, at.Name.LocalName, read.Value, null, read.Themed);
            }
        }

        return null;
    }

    /// <summary>Ключ, записанный в словаре окна-корня или его стилей, — по синтаксису: тип — как его пишет документ.</summary>
    private static Found? DeclaredAtRoot(XamlElement syntax, string key)
    {
        var root = syntax;

        while (OwnerOf(root) is { } owner)
            root = owner;

        return XamlResourceDeclarations.InScope(syntax)
            .FirstOrDefault(entry => entry.IsTextKey && entry.Key == key && ReferenceEquals(entry.Scope, root)) is { } declared
            ? new Found(
                ReferenceEquals(syntax, root) ? XamlResourceScope.Element : XamlResourceScope.Ancestor,
                root.Name.LocalName,
                null,
                declared.Element.Name.LocalName,
                declared.ThemeVariant is not null)
            : null;
    }

    /// <summary>Ключ у приложения формы: записанный в его файле или словарях — приложения, найденный иначе — его темы.</summary>
    private Found? InApplication(string key, ThemeVariant theme)
    {
        if (Application is null || Read(TryApplication, key, theme) is not { } read)
            return null;

        var written = !ApplicationFile.IsEmpty &&
                      _entry.Session.ResourceFiles.Of(ApplicationFile, _entry.Session.Snapshot).Any(entry => entry.Key == key);

        return new Found(written ? XamlResourceScope.Application : XamlResourceScope.Theme, null, read.Value, null, read.Themed);
    }

    /// <summary>
    /// Ключ у приложения формы, как его ищет Avalonia: в его словаре, затем в стилях — от последнего к первому. Стили —
    /// запомненные: пока форма стоит на карточке, у самого приложения их нет.
    /// </summary>
    private bool TryApplication(object key, ThemeVariant? theme, out object? value)
    {
        value = null;

        if (Application is IResourceNode application && application.TryGetResource(key, theme, out value))
            return true;

        for (var at = _applicationStyles.Count - 1; at >= 0; at--)
        {
            if (_applicationStyles[at].TryGetResource(key, theme, out value))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Возвращает окну-корню на время чтения то, что у него взяла карточка: пока она показывает окно, его словарь и стили у
    /// неё, и окно без них ключей не знает. Не окну отдавать нечего.
    /// </summary>
    private IDisposable? LendRoot(XamlLoadSession session) => session.RootObject is TopLevel root ? _entry.Lending.Lend(root) : null;

    /// <summary>Значение ключа: без темы, а нет — в теме показа; null — ключа нет.</summary>
    private static (object? Value, bool Themed)? Read(Lookup lookup, string key, ThemeVariant theme) =>
        lookup(key, null, out var plain) ? (plain, false) : lookup(key, theme, out var themed) ? (themed, true) : null;

    /// <summary>Ресурс по ключу у того, кто его держит, — именами и тем, что можно держать.</summary>
    private static XamlResourceInfo Info(string key, XamlResourceScope scope, Lookup lookup, ThemeVariant theme, bool hosted, Type? target)
    {
        var read = Read(lookup, key, theme);
        var value = read?.Value;
        var themed = read?.Themed == true;
        var type = value?.GetType();

        return new XamlResourceInfo(key, scope)
        {
            TypeName = type is null ? null : XamlMemberMetadata.TypeOf(type).Name,
            ValueText = XamlValueOrigins.TextOf(value),
            Sample = XamlValueOrigins.Shared(value),
            IsThemed = themed,
            Fits = Fits(target, type) || type == typeof(Color) && target is not null && typeof(IBrush).IsAssignableFrom(target),
            IsConverter = value is IValueConverter,
            StaticReachable = !themed || hosted,
        };
    }

    /// <summary>Поиск ключа у того, кто держит ресурсы: словаря, элемента, приложения формы.</summary>
    private delegate bool Lookup(object key, ThemeVariant? theme, out object? value);

    /// <summary>Где нашёлся ключ: область, её элемент, значение — если его прочли, — тип, как его пишет документ, и только ли он у тем.</summary>
    private readonly record struct Found(XamlResourceScope Scope, string? Owner, object? Value, string? TypeName, bool Themed);

    /// <summary>
    /// Ключи элемента — его словаря и стилей, которые он пишет. Словарь и стили спрашиваются, только если документ их
    /// пишет: свойство создаёт пустой словарь на первый вопрос, а объект — показа, а не наш.
    /// </summary>
    private static IEnumerable<string> Keys(StyledElement node, XamlElement syntax)
    {
        var members = syntax.MemberElements.Select(static element => element.MemberName).ToHashSet(StringComparer.Ordinal);
        var keys = Enumerable.Empty<string>();

        if (members.Contains("Resources"))
            keys = keys.Concat(Keys(node.Resources, themed: false).Select(static entry => entry.Key));

        if (members.Contains("Styles"))
            keys = keys.Concat(Keys(node.Styles).Select(static entry => entry.Key));

        return keys;
    }

    /// <summary>
    /// Ключи темы приложения формы — стилей, которые оно подключает, от последнего к первому, как их спрашивает Avalonia.
    /// Их знают только объекты стилей, а стили — запомненные (<see cref="_applicationStyles"/>).
    /// </summary>
    private IEnumerable<string> ThemeKeys() =>
        _applicationStyles.Reverse().SelectMany(static style => Keys(style)).Select(static entry => entry.Key);

    /// <summary>Ключи стилей — их словарей, стилей внутри и включённых.</summary>
    private static IEnumerable<(string Key, bool Themed)> Keys(IStyle style)
    {
        var dictionary = style switch
        {
            Styles styles when ((IResourceNode)styles).HasResources => styles.Resources,
            StyleBase single when ((IResourceNode)single).HasResources => single.Resources,
            _ => null,
        };

        if (dictionary is not null)
        {
            foreach (var entry in Keys(dictionary, themed: false))
                yield return entry;
        }

        IReadOnlyList<IStyle> children = style is StyleInclude { Loaded: { } loaded } ? [loaded] : style.Children;

        foreach (var child in children)
        {
            foreach (var entry in Keys(child))
                yield return entry;
        }
    }

    /// <summary>Строковые ключи словаря — его, слитых с ним и словарей тем.</summary>
    private static IEnumerable<(string Key, bool Themed)> Keys(IResourceDictionary dictionary, bool themed)
    {
        foreach (var key in dictionary.Keys)
        {
            if (key is string text)
                yield return (text, themed);
        }

        foreach (var provider in dictionary.MergedDictionaries)
        {
            if (Dictionary(provider) is { } merged)
            {
                foreach (var entry in Keys(merged, themed))
                    yield return entry;
            }
        }

        foreach (var provider in dictionary.ThemeDictionaries.Values)
        {
            if (Dictionary(provider) is { } variant)
            {
                foreach (var entry in Keys(variant, themed: true))
                    yield return entry;
            }
        }
    }

    private static IResourceDictionary? Dictionary(IResourceProvider provider) => provider switch
    {
        IResourceDictionary dictionary => dictionary,
        ResourceInclude { Loaded: { } loaded } => loaded,
        _ => null,
    };

    /// <summary>
    /// Тема, в которой форма показана: у элемента — его; у формы, которая ещё нигде не стоит, темы нет вовсе, и берётся
    /// тема приложения формы, а без неё светлая. Без темы и с темой «по умолчанию» словари тем не открываются, и ключ
    /// одной темы не нашёлся бы вовсе.
    /// </summary>
    private ThemeVariant ThemeOf(XamlLoadSession session, XamlElement syntax)
    {
        if (session.Objects.GetObject(syntax) is StyledElement { ActualThemeVariant: { } shown } && shown != ThemeVariant.Default)
            return shown;

        return Application?.ActualThemeVariant is { } asked && asked != ThemeVariant.Default ? asked : ThemeVariant.Light;
    }

    /// <summary>
    /// Известна ли тема, пока элемент строится: он сам или предок в документе — окно или <c>ThemeVariantScope</c>. Только
    /// там статическая ссылка находит ключ, который есть лишь у тем.
    /// </summary>
    private static bool ThemeHosted(XamlLoadSession session, XamlElement syntax)
    {
        for (var at = syntax; at is not null; at = OwnerOf(at))
        {
            if (session.Objects.GetObject(at) is TopLevel or ThemeVariantScope)
                return true;
        }

        return false;
    }
}
