using System.Globalization;
using System.Text;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell;
using ArxisStudio.Shell.Localization;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Служба создания: пункты «Добавить ▸» всех расширений и сборка файлов по ним.
/// </summary>
/// <remarks>
/// Живёт в студии, а не у службы проектов: манифесты читает студия, будит спящих она же, и чужой код
/// зовёт через свой шов. Служба проектов только кладёт собранное на диск, а склеивает одно с другим
/// окно проекта — так же, как склеил бы плагин.
/// <para>
/// Списка служба не держит: пункты собираются из манифестов на каждый вопрос, как главное меню
/// студии. Поэтому включение, выключение и перезагрузка расширения доходят до «Добавить ▸» без единой
/// подписки, а пункт выключенного не переживает его ни на одно открытие меню.
/// </para>
/// </remarks>
public sealed class StudioNewItems : IStudioNewItems
{
    private readonly IStudioLog _log;
    private readonly PluginGuard _guard;
    private readonly PluginContributionRegistry _contributions;

    /// <summary>Заводит службу над реестрами студии.</summary>
    /// <param name="log">Журнал: о пунктах, которые не собрались, автор узнаёт из него.</param>
    /// <param name="guard">Шов, через который зовётся код пунктов.</param>
    /// <param name="contributions">Реестр вкладов: в нём живёт код пунктов поднятых расширений.</param>
    public StudioNewItems(IStudioLog log, PluginGuard guard, PluginContributionRegistry contributions)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(contributions);

        _log = log;
        _guard = guard;
        _contributions = contributions;
    }

    /// <summary>
    /// Кто сейчас вправе вкладываться — спрашивается на каждый вопрос.
    /// </summary>
    /// <remarks>
    /// Способ спросить, а не список: службу раздают расширениям раньше, чем студия подняла первое
    /// из них, и состав с тех пор меняется. Ставит его окно, когда служба расширений заведена.
    /// </remarks>
    public Func<IEnumerable<InstalledPlugin>> Contributing { get; set; } = () => [];

    /// <summary>Будит ждущих, которым подошло событие, — дорогой студии.</summary>
    public Action<Func<InstalledPlugin, bool>> Activate { get; set; } = _ => { };

    /// <inheritdoc/>
    public IReadOnlyList<StudioNewItem> Items => [.. Declared().Select(found => found.Item)];

    /// <inheritdoc/>
    public IReadOnlyList<string> Paths(StudioNewItem item, string name, string? variant = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Диалог спрашивает на каждую букву, и пустое поле — не ошибка зовущего, а начало ввода.
        if (string.IsNullOrEmpty(name) || Find(item) is not { } found)
            return [];

        return Outputs(found, name, variant) ?? [];
    }

    /// <inheritdoc/>
    public string Suggest(StudioNewItem item, string directory, string? variant = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        const string number = "$n$";

        if (!item.Name.Contains(number, StringComparison.Ordinal))
            return item.Name;

        var found = Find(item);

        // Потолок — не предел имён, а страховка от каталога, где заняты все: предложить первое
        // занятое и дать человеку поправить его честнее, чем перебирать без конца.
        for (var n = 1; n < 10_000; n++)
        {
            var name = item.Name.Replace(number, n.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var outputs = found is null ? null : Outputs(found, name, variant);

            if ((outputs is { Count: > 0 } ? outputs : [name]).All(path => Free(Path.Combine(directory, path))))
                return name;
        }

        return item.Name.Replace(number, "1", StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public async Task<NewItemResult> MakeAsync(
        StudioNewItem item,
        NewItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request));

        // Путь в имени зовущий раскладывает сам — каталог к каталогу, имя к имени: иначе у двух
        // зовущих разошлось бы, в каком каталоге считать пространство имён.
        if (request.Name.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("В имени нет каталогов: путь раскладывает зовущий", nameof(request));

        if (!Path.IsPathFullyQualified(request.Directory))
            throw new ArgumentException("Целевой каталог — полный путь", nameof(request));

        cancellationToken.ThrowIfCancellationRequested();

        if (Find(item) is not { } found)
        {
            _log.Write(StudioLogLevel.Warning, "NewItems", $"{item.Owner}: пункта {item.Id} больше нет — расширение выключено или обновлено");

            return NewItemResult.Failed(Localizer.Instance["newitem.gone"]);
        }

        return found.Item.Kind switch
        {
            NewItemKind.Directory => NewItemResult.Made([new NewItemFile(request.Name) { IsDirectory = true }]),
            NewItemKind.Code => await CodeAsync(found, request, cancellationToken),
            _ => await TemplatesAsync(found, request, cancellationToken),
        };
    }

    /// <summary>
    /// Подставляет переменные в шаблон, прочитанный байтами.
    /// </summary>
    /// <param name="template">Байты шаблона.</param>
    /// <param name="request">Запрос: из него переменные.</param>
    /// <returns>Байты файла.</returns>
    /// <remarks>
    /// Кодировка — по отметке порядка байт, без неё — UTF-8, а сама отметка и переводы строк доезжают
    /// как были: файл, записанный под <c>CRLF</c> с отметкой, не должен приходить в проект другим.
    /// Шаблон, который в своей кодировке не читается, — картинка, шрифт — кладётся как есть: заменять
    /// в нём нечего, а подмена битых байт знаком вопроса его бы испортила. Шаблон без переменных
    /// тоже возвращается нетронутым, байт в байт.
    /// </remarks>
    internal static byte[] Expand(byte[] template, NewItemRequest request)
    {
        var (encoding, preamble) = Detect(template);
        string text;

        try
        {
            text = encoding.GetString(template, preamble, template.Length - preamble);
        }
        catch (DecoderFallbackException)
        {
            return template;
        }

        var expanded = NewItemTemplate.Expand(text, request);

        if (string.Equals(expanded, text, StringComparison.Ordinal))
            return template;

        var body = encoding.GetBytes(expanded);
        var file = new byte[preamble + body.Length];

        template.AsSpan(0, preamble).CopyTo(file);
        body.CopyTo(file, preamble);

        return file;
    }

    /// <summary>Кодировка шаблона по отметке порядка байт — строгая: битый байт бросает, а не подменяется.</summary>
    private static (Encoding Encoding, int Preamble) Detect(byte[] bytes) => bytes switch
    {
        [0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 4),
        [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 3),
        [0xFF, 0xFE, ..] => (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2),
        [0xFE, 0xFF, ..] => (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2),
        _ => (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 0),
    };

    /// <summary>
    /// Пункты включённых расширений вместе с объявлением и хозяином — в порядке показа.
    /// </summary>
    /// <remarks>
    /// Порядок тот же, что у полосы: модули первыми, затем по идентификатору, внутри расширения — как
    /// в манифесте. Второй пункт с тем же идентификатором у одного расширения не показывается: по
    /// идентификатору пункт находит свой код, и двум строкам с одним именем достался бы один класс.
    /// </remarks>
    private IEnumerable<Found> Declared()
    {
        var contributing = Contributing()
            .Where(candidate => candidate is { IsEnabled: true, IsValid: true })
            .OrderByDescending(candidate => candidate.IsBuiltIn)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal);

        foreach (var plugin in contributing)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var declared in plugin.Manifest!.Contributions.NewItems)
            {
                if (Describe(plugin, declared) is { } item && seen.Add(item.Id))
                    yield return new Found(plugin, declared, item);
            }
        }
    }

    /// <summary>Пункт, как его показывают; null — показывать нечего.</summary>
    /// <remarks>
    /// Незнакомый вид и пункт без строки меню не показываются, и студия говорит о них в журнал, когда
    /// читает манифест (<see cref="Complaints"/>), а не здесь: здесь спрашивают на каждом открытии меню.
    /// </remarks>
    private static StudioNewItem? Describe(InstalledPlugin plugin, PluginNewItem declared)
    {
        if (Kind(declared) is not { } kind || declared.Id is not { Length: > 0 } id)
            return null;

        var strings = plugin.Strings;
        var title = strings.Resolve(declared.Title);

        if (title.Length == 0)
            return null;

        return new StudioNewItem
        {
            Id = id,
            Owner = plugin.Id,
            Kind = kind,
            Title = title,
            Icon = ManifestIcons.Resolve(declared.Icon, out _),
            Menu = [.. StudioMenu.Segments(declared.Menu, strings).Where(segment => segment.Length > 0)],
            Name = strings.Resolve(declared.Name),
            NameRule = declared.IsIdentifier ? NewItemNameRule.Identifier : NewItemNameRule.File,
            Nested = declared.Nested,
            Languages = Words(declared.When?.Languages),
            Packages = Words(declared.When?.Packages),
            Variants = kind == NewItemKind.Directory ? [] : [.. Variants(declared).Select(variant => new StudioNewItemVariant
            {
                Id = variant.Id,
                Title = strings.Resolve(variant.Title),
                Icon = ManifestIcons.Resolve(variant.Icon, out _),
            })],
        };
    }

    /// <summary>Вид пункта; null — слово студии не знакомо.</summary>
    private static NewItemKind? Kind(PluginNewItem declared) =>
        declared.IsDirectory ? NewItemKind.Directory
        : declared.IsCode ? NewItemKind.Code
        : declared.IsFile ? NewItemKind.File
        : null;

    /// <summary>Варианты, которые можно показать: с идентификатором и строкой, без повторов.</summary>
    private static IEnumerable<PluginNewItemVariant> Variants(PluginNewItem declared) =>
        declared.Variants
            .Where(variant => variant is { Id.Length: > 0, Title.Length: > 0 })
            .DistinctBy(variant => variant.Id, StringComparer.Ordinal);

    private static string[] Words(IList<string>? words) =>
        [.. (words ?? []).Select(word => word?.Trim()).OfType<string>().Where(word => word.Length > 0)];

    /// <summary>Объявление пункта у включённого хозяина; null — пункта больше нет.</summary>
    private Found? Find(StudioNewItem item) =>
        Declared().FirstOrDefault(found =>
            string.Equals(found.Item.Owner, item.Owner, StringComparison.Ordinal) &&
            string.Equals(found.Item.Id, item.Id, StringComparison.Ordinal));

    /// <summary>
    /// Файлы варианта; null — такого варианта у пункта нет.
    /// </summary>
    /// <remarks>
    /// У пункта без вариантов файлы — его собственные; у пункта с ними собственных не читают, и
    /// вариант, не названный зовущим, — первый.
    /// </remarks>
    private static IList<PluginNewItemFile>? Files(PluginNewItem declared, string? variant)
    {
        var variants = Variants(declared).ToList();

        if (variants.Count == 0)
            return declared.Files;

        return variant is null
            ? variants[0].Files
            : variants.FirstOrDefault(each => string.Equals(each.Id, variant, StringComparison.Ordinal))?.Files;
    }

    /// <summary>Что положит пункт под этим именем; null — варианта нет.</summary>
    private static List<string>? Outputs(Found found, string name, string? variant)
    {
        switch (found.Item.Kind)
        {
            case NewItemKind.Directory:
                return [name];

            case NewItemKind.Code:
                return [];
        }

        if (Files(found.Declared, variant) is not { } files)
            return null;

        return files.Count == 0
            ? [name]
            : [.. files.Select(file => Separators(Named(file.Path, name)))];
    }

    /// <summary>
    /// Собирает файлы пункта по шаблонам его расширения.
    /// </summary>
    private async Task<NewItemResult> TemplatesAsync(Found found, NewItemRequest request, CancellationToken cancellationToken)
    {
        if (Files(found.Declared, request.Variant) is not { } files)
            return Refuse(found, $"варианта {request.Variant} нет", Localizer.Instance["newitem.variant"]);

        // Пункт без файлов — один пустой файл с набранным именем: «Добавить ▸ Файл».
        if (files.Count == 0)
            return NewItemResult.Made([new NewItemFile(request.Name)]);

        var made = new List<NewItemFile>();

        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
                return Refuse(found, "у файла не назван путь (path)", Localizer.Instance["newitem.broken"]);

            if (Inside(request.Directory, Named(file.Path, request.Name)) is not { } path)
                return Refuse(found, $"путь «{file.Path}» уводит из целевого каталога", Localizer.Instance["newitem.broken"]);

            var content = ReadOnlyMemory<byte>.Empty;

            if (file.Template is { Length: > 0 } template)
            {
                // Шаблон — путь из чужого манифеста, и читается он той же проверкой, что entry и
                // словари: путь наружу из папки расширения не открывается.
                var source = PluginPaths.Inside(found.Owner.Directory, template);

                if (source is null)
                    return Refuse(found, $"шаблон «{template}» уводит из папки расширения", Localizer.Instance["newitem.broken"]);

                if (!File.Exists(source))
                    return Refuse(found, $"шаблона «{template}» нет в папке расширения", Localizer.Instance["newitem.broken"]);

                content = Expand(await File.ReadAllBytesAsync(source, cancellationToken), request);
            }

            made.Add(new NewItemFile(path) { Content = content, Open = file.Open });
        }

        return NewItemResult.Made(made);
    }

    /// <summary>
    /// Зовёт код пункта: будит хозяина, если он спит, и спрашивает его класс через шов.
    /// </summary>
    /// <remarks>
    /// В потоке интерфейса — откуда бы ни позвали: подъём ставит панели хозяина, а код пункта вправе
    /// показать своё окно. Отмена — не сбой расширения: код, честно бросивший на отмену, упавшим не
    /// считается, и отмена доходит до зовущего как отмена.
    /// </remarks>
    private async Task<NewItemResult> CodeAsync(Found found, NewItemRequest request, CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => CodeAsync(found, request, cancellationToken));

        var owner = found.Owner;
        var id = found.Item.Id;

        Activate(waiting =>
            string.Equals(waiting.Id, owner.Id, StringComparison.Ordinal) &&
            PluginActivation.WaitsForNewItem(waiting.Manifest, id));

        if (_contributions.MakerFor(owner.Id, id) is not { } maker)
        {
            return Refuse(found,
                $"кода пункта нет — расширение не поднялось, не ждёт {PluginActivation.OnNewItem}{id} или не объявило класс [NewItem(\"{id}\")]",
                Localizer.Instance["newitem.nocode"]);
        }

        NewItemResult? result = null;
        var cancelled = false;

        var ran = await _guard.RunAsync(owner.Id, $"пункт создания {id}", async () =>
        {
            try
            {
                result = await maker.MakeAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
        });

        if (cancelled)
            throw new OperationCanceledException(cancellationToken);

        // Причину сбоя шов уже назвал в журнале — с именем расширения и стеком.
        if (!ran || result is null)
            return NewItemResult.Failed(Localizer.Instance["newitem.failed"]);

        if (result.Files.Count == 0)
            return result;

        var made = new List<NewItemFile>();

        foreach (var file in result.Files)
        {
            if (Inside(request.Directory, file.Path) is not { } path)
                return Refuse(found, $"код положил бы «{file.Path}» вне целевого каталога", Localizer.Instance["newitem.broken"]);

            made.Add(file with { Path = path });
        }

        return NewItemResult.Made(made);
    }

    /// <summary>
    /// Путь от целевого каталога, если он в нём остаётся; null — пуст или уводит наружу.
    /// </summary>
    /// <remarks>
    /// Та же проверка, что у путей манифеста (<see cref="PluginPaths.Inside"/>): <c>..</c> и корневой
    /// путь — наружу. Сам каталог — тоже не место для файла: у <c>.</c> имени нет.
    /// </remarks>
    private static string? Inside(string directory, string path) =>
        PluginPaths.Inside(directory, path) is { } full
            ? Path.GetRelativePath(Path.GetFullPath(directory), full)
            : null;

    private static string Named(string path, string name) => path.Replace("$name$", name, StringComparison.Ordinal);

    private static string Separators(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    private static bool Free(string path) => !File.Exists(path) && !Directory.Exists(path);

    /// <summary>
    /// Отказ: автору — в журнал подробно, с именем расширения, человеку — словами.
    /// </summary>
    /// <remarks>
    /// Путь наружу, пропавший шаблон, файл без пути — ошибки автора, а не человека: поправить их он не
    /// может, и строке в окне довольно сказать, что пункт неисправен и где причина.
    /// </remarks>
    private NewItemResult Refuse(Found found, string detail, string message)
    {
        _log.Write(StudioLogLevel.Warning, "NewItems", $"{found.Owner.DisplayName}: пункт {found.Item.Id} — {detail}");

        return NewItemResult.Failed(message);
    }

    /// <summary>
    /// Что в объявленных пунктах студия не прочтёт — словами для журнала.
    /// </summary>
    /// <param name="plugin">Чей манифест.</param>
    /// <returns>Замечания по пунктам; пусто — всё читается.</returns>
    /// <remarks>
    /// Говорится один раз, когда студия читает манифест, — там же, где о значках: пункты собираются на
    /// каждом открытии меню, и замечание звучало бы столько же раз. Автору то же самое раньше скажет
    /// сборка (<c>ARX0015</c>).
    /// </remarks>
    public static IEnumerable<string> Complaints(InstalledPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        foreach (var declared in plugin.Manifest?.Contributions.NewItems ?? [])
        {
            var named = declared.Id is { Length: > 0 } id ? $"пункт создания {id}" : "пункт создания без id";

            if (declared.Id is not { Length: > 0 })
                yield return $"{named} не показан: без идентификатора его нечем связать с кодом";
            else if (Kind(declared) is null)
                yield return $"{named} не показан: вид «{declared.Kind}» студии не знаком — directory, file или code";
            else if (plugin.Strings.Resolve(declared.Title).Length == 0)
                yield return $"{named} не показан: у него нет строки меню (title)";

            ManifestIcons.Resolve(declared.Icon, out var problem);

            if (problem is not null)
                yield return $"{named} — {problem}";

            foreach (var variant in declared.Variants)
            {
                ManifestIcons.Resolve(variant.Icon, out var trouble);

                if (trouble is not null)
                    yield return $"{named}, вариант {variant.Id} — {trouble}";
            }
        }
    }

    /// <summary>Пункт вместе с объявлением и расширением, которое его объявило.</summary>
    private sealed record Found(InstalledPlugin Owner, PluginNewItem Declared, StudioNewItem Item);
}
