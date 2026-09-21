using System.Globalization;
using System.Text;
using Avalonia.Media;

namespace ArxisStudio.Sdk;

/// <summary>
/// Создание по объявлениям расширений: что стоит в меню «Добавить ▸» и что оно кладёт на диск.
/// </summary>
/// <remarks>
/// Берётся через <see cref="IStudioContext.GetService{T}"/>. Меню показывает и создание зовёт тот, кто
/// знает место, — окно проекта студии или панель плагина; служба знает, что объявили расширения
/// (<c>contributions.newItems</c>), и умеет собрать из объявленного файлы. На диск она не пишет
/// ничего: собранное кладёт зовущий — окно проекта через службу файлов, которая записывает создание в
/// локальную историю и перечитывает модель.
/// <para>
/// Так же разделены File Templates и действие «New» у IntelliJ и <c>CreateAssetMenu</c> и
/// <c>ProjectWindowUtil</c> у Unity: объявление — у расширения, место и имя — у окна, запись — у
/// того, кто отвечает за файлы проекта.
/// </para>
/// </remarks>
public interface IStudioNewItems
{
    /// <summary>
    /// Пункты включённых расширений в порядке показа: модули первыми, затем по идентификатору, а
    /// внутри расширения — в порядке манифеста.
    /// </summary>
    /// <remarks>
    /// Собирается на каждый вопрос, а не держится списком: состав расширений меняется от подъёма к
    /// подъёму, и спрашивать надо тогда, когда меню открывают, — так студия строит и своё главное меню.
    /// Подписи уже переведены на язык интерфейса. Пункт спящего плагина здесь есть: сборка для
    /// показа не нужна.
    /// </remarks>
    IReadOnlyList<StudioNewItem> Items { get; }

    /// <summary>
    /// Что положит пункт под этим именем — пути от целевого каталога.
    /// </summary>
    /// <param name="item">Пункт из <see cref="Items"/>.</param>
    /// <param name="name">Имя, как его набрали, без каталогов.</param>
    /// <param name="variant">Идентификатор варианта; null — первый.</param>
    /// <returns>
    /// Пути в порядке манифеста; пусто у пункта с кодом — что он положит, знает только он, — и у
    /// пункта, чьё расширение уже выключено.
    /// </returns>
    /// <remarks>
    /// Нужно диалогу имени: занято ли то, что появится, видно раньше, чем нажата кнопка, — и у файла
    /// <c>Person.axaml</c>, и у <c>Person.axaml.cs</c> рядом с ним.
    /// </remarks>
    IReadOnlyList<string> Paths(StudioNewItem item, string name, string? variant = null);

    /// <summary>
    /// Имя, которое предложить: <c>$n$</c> заменён первым числом, при котором всё создаваемое в
    /// каталоге свободно.
    /// </summary>
    /// <param name="item">Пункт из <see cref="Items"/>.</param>
    /// <param name="directory">Целевой каталог — полный путь.</param>
    /// <param name="variant">Идентификатор варианта; null — первый.</param>
    /// <returns>Имя без каталогов; пустое, если пункт имени не предлагает.</returns>
    /// <remarks>
    /// Свободным считается то, чего нет на диске. У пункта с кодом проверяется само имя: что ещё он
    /// положит рядом, заранее не узнать.
    /// </remarks>
    string Suggest(StudioNewItem item, string directory, string? variant = null);

    /// <summary>
    /// Собирает файлы пункта: читает шаблоны расширения и подставляет переменные либо зовёт его код.
    /// </summary>
    /// <param name="item">Пункт из <see cref="Items"/>.</param>
    /// <param name="request">Что создать и где.</param>
    /// <param name="cancellationToken">Отмена — пока файлы собираются.</param>
    /// <returns>Собранное, отказ с причиной или ничего — человек передумал в окне расширения.</returns>
    /// <remarks>
    /// Пункт с кодом будит своё расширение, если оно спит, и зовёт его через шов сбоев: упавший код
    /// отвечает отказом, а не роняет студию, и падение засчитывается ему. Код расширения студия зовёт
    /// в потоке интерфейса — оно вправе показать своё окно, — откуда бы ни позвали службу.
    /// <para>
    /// Пути собранного проверены: всё лежит внутри целевого каталога, а шаблон читался из папки
    /// своего расширения. Путь, уводящий наружу, — отказ, а не усечение.
    /// </para>
    /// </remarks>
    Task<NewItemResult> MakeAsync(StudioNewItem item, NewItemRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Вид пункта создания.</summary>
public enum NewItemKind
{
    /// <summary>Файлы по шаблонам или один пустой файл.</summary>
    File,

    /// <summary>Каталог.</summary>
    Directory,

    /// <summary>Файлы собирает код расширения.</summary>
    Code,
}

/// <summary>Каким должно быть имя, набранное в диалоге.</summary>
public enum NewItemNameRule
{
    /// <summary>Имя файла или каталога.</summary>
    File,

    /// <summary>Ещё и имя типа C#: шаблон ставит его в код.</summary>
    Identifier,
}

/// <summary>
/// Пункт меню «Добавить ▸», как его видит тот, кто показывает меню.
/// </summary>
/// <remarks>
/// Снимок объявления: подписи переведены, значки разобраны. Создать пункт своими руками можно — так
/// окно, которое показывает меню, проверяют без студии, — а собирает файлы по нему только служба.
/// </remarks>
public sealed class StudioNewItem
{
    /// <summary>Идентификатор из манифеста.</summary>
    public required string Id { get; init; }

    /// <summary>Идентификатор расширения, объявившего пункт; по нему меню делится на группы.</summary>
    public required string Owner { get; init; }

    /// <summary>Вид пункта.</summary>
    public required NewItemKind Kind { get; init; }

    /// <summary>Строка меню.</summary>
    public required string Title { get; init; }

    /// <summary>Значок; null — без значка.</summary>
    public Geometry? Icon { get; init; }

    /// <summary>Ветка внутри «Добавить ▸» — сегменты по порядку; пусто — сам «Добавить ▸».</summary>
    public IReadOnlyList<string> Menu { get; init; } = [];

    /// <summary>Имя, которое предложить, — с <c>$n$</c>, если оно есть; пусто — поле пустое.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Каким должно быть набранное имя.</summary>
    public NewItemNameRule NameRule { get; init; }

    /// <summary>Можно ли набрать путь через <c>/</c>.</summary>
    public bool Nested { get; init; }

    /// <summary>Языки проекта, в которых пункт показывается; пусто — в любом.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Пакеты, хотя бы на один из которых проект должен ссылаться; пусто — не важно.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>Разновидности пункта в порядке манифеста; пусто — пункт один.</summary>
    public IReadOnlyList<StudioNewItemVariant> Variants { get; init; } = [];

    /// <summary>
    /// Показывается ли пункт в проекте с таким языком и такими пакетами.
    /// </summary>
    /// <param name="language">Язык проекта: <c>C#</c>; null — не известен.</param>
    /// <param name="packages">Идентификаторы пакетов, на которые проект ссылается.</param>
    /// <remarks>
    /// Правило <c>when</c> манифеста, записанное один раз: внутри списка — «любое из», между
    /// списками — «все сразу», без учёта регистра. Проект, чей язык не известен, пункту с языками не
    /// подходит.
    /// </remarks>
    public bool Fits(string? language, IEnumerable<string> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);

        if (Languages.Count > 0 &&
            (language is null || !Languages.Contains(language, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        return Packages.Count == 0 || packages.Any(package => Packages.Contains(package, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Разновидность пункта создания: строка списка в диалоге имени.</summary>
public sealed class StudioNewItemVariant
{
    /// <summary>Идентификатор из манифеста.</summary>
    public required string Id { get; init; }

    /// <summary>Строка списка.</summary>
    public required string Title { get; init; }

    /// <summary>Значок; null — значок пункта.</summary>
    public Geometry? Icon { get; init; }
}

/// <summary>Что создать и где.</summary>
/// <param name="Name">
/// Имя, как его набрали, — без каталогов: путь, набранный в диалоге, зовущий уже разложил на каталог и
/// имя.
/// </param>
/// <param name="Directory">
/// Целевой каталог — полный путь. Его может ещё не быть: имя вело через новые каталоги, и заведёт их
/// тот, кто кладёт файлы.
/// </param>
/// <remarks>
/// Всё, что знает о месте только зовущий, — проект и пространство имён, — он же и называет: служба
/// модели проектов не читает. Из этого и из имени собираются переменные шаблона.
/// </remarks>
public sealed record NewItemRequest(string Name, string Directory)
{
    /// <summary>Идентификатор варианта; null — первый.</summary>
    public string? Variant { get; init; }

    /// <summary>Файл проекта, в который ляжет созданное, — полный путь; null — не известен.</summary>
    public string? ProjectFile { get; init; }

    /// <summary>Имя проекта — <c>$project$</c>.</summary>
    public string? Project { get; init; }

    /// <summary>Пространство имён целевого каталога — <c>$namespace$</c>.</summary>
    public string? Namespace { get; init; }

    /// <summary>Корневое пространство имён проекта — <c>$rootnamespace$</c>.</summary>
    public string? RootNamespace { get; init; }
}

/// <summary>Файл или каталог, собранный пунктом создания.</summary>
/// <param name="Path">Путь от целевого каталога: <c>Person.cs</c>, <c>Views/Person.axaml</c>.</param>
public sealed record NewItemFile(string Path)
{
    /// <summary>Содержимое файла; у каталога пусто.</summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Каталог, а не файл.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>Открыть созданный файл, когда пункт отработал.</summary>
    public bool Open { get; init; }
}

/// <summary>
/// Чем кончилась сборка пункта создания: файлами, отказом или ничем.
/// </summary>
/// <remarks>
/// Три исхода, а не два: код расширения вправе показать своё окно, и человек, закрывший его, не
/// ошибся — ему нечего говорить.
/// </remarks>
public sealed class NewItemResult
{
    private NewItemResult(IReadOnlyList<NewItemFile> files, string? error)
    {
        Files = files;
        Error = error;
    }

    /// <summary>Создавать нечего: человек передумал в окне расширения. Это не ошибка.</summary>
    public static NewItemResult Declined { get; } = new([], null);

    /// <summary>Собранное — в порядке, в котором его создавать; пусто при отказе и у <see cref="Declined"/>.</summary>
    public IReadOnlyList<NewItemFile> Files { get; }

    /// <summary>Почему не вышло — словами для человека; null — вышло или создавать нечего.</summary>
    public string? Error { get; }

    /// <summary>Файлы собраны.</summary>
    /// <param name="files">Что создать — хотя бы одно.</param>
    public static NewItemResult Made(IReadOnlyList<NewItemFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0 || files.Any(file => file is null))
            throw new ArgumentException("Собранного нет: создавать нечего — это NewItemResult.Declined", nameof(files));

        return new NewItemResult([.. files], null);
    }

    /// <summary>Не вышло.</summary>
    /// <param name="error">Причина словами для человека.</param>
    public static NewItemResult Failed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new NewItemResult([], error);
    }
}

/// <summary>
/// Код пункта создания: пункт манифеста вида <c>code</c>.
/// </summary>
/// <remarks>
/// Нужен там, где шаблона мало: содержимое зависит от настройки расширения, от модели проекта или от
/// ответа человека в своём окне. Класс помечают <see cref="NewItemAttribute"/> с идентификатором из
/// манифеста; экземпляр студия создаёт сама, когда расширение поднято, и держит до его выгрузки —
/// как редактор документов, — поэтому класс открытый и с открытым конструктором без параметров.
/// Спящее расширение будит событие <c>onNewItem:</c>.
/// </remarks>
public abstract class NewItemMaker
{
    /// <summary>Что студия дала расширению; доступен после <see cref="Attach"/>.</summary>
    protected IStudioContext Context { get; private set; } = null!;

    /// <summary>Связывает код пункта со студией.</summary>
    /// <param name="context">Что студия даёт расширению.</param>
    public void Attach(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
    }

    /// <summary>Собирает файлы пункта.</summary>
    /// <param name="request">Что создать и где.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Собранное, отказ с причиной или <see cref="NewItemResult.Declined"/>.</returns>
    /// <remarks>
    /// Пути — от <see cref="NewItemRequest.Directory"/> и внутри него. Переменные шаблона подставляет
    /// <see cref="NewItemTemplate.Expand"/> — те же, что у шаблонов манифеста.
    /// </remarks>
    public abstract Task<NewItemResult> MakeAsync(NewItemRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Переменные шаблонов создания: какие есть и как подставляются.
/// </summary>
/// <remarks>
/// Запись — <c>$имя$</c>, как у шаблонов Visual Studio, а не <c>{имя}</c>: фигурные скобки есть в
/// каждом файле C#, и шаблон пришлось бы экранировать целиком. Имена строчные; переменная, которой
/// нет, остаётся в тексте как была — так же поступает и строка вида <c>$"…"</c>, которой переменной
/// быть не с чего.
/// </remarks>
public static class NewItemTemplate
{
    /// <summary>Имена переменных содержимого, без знаков доллара.</summary>
    /// <remarks>
    /// <c>name</c> — имя, как его набрали; <c>namespace</c> — пространство имён целевого каталога;
    /// <c>rootnamespace</c> — корневое пространство имён проекта; <c>project</c> — имя проекта;
    /// <c>year</c> — нынешний год. В пути файла из них есть только <c>name</c>, а в имени по умолчанию
    /// — своя <c>n</c>.
    /// </remarks>
    public static IReadOnlyList<string> Variables { get; } = ["name", "namespace", "rootnamespace", "project", "year"];

    /// <summary>Подставляет переменные запроса в текст шаблона.</summary>
    /// <param name="text">Текст шаблона.</param>
    /// <param name="request">Запрос: имя, проект, пространство имён.</param>
    /// <returns>Текст с подставленными значениями.</returns>
    /// <remarks>
    /// Значение, которого зовущий не назвал, — <c>$namespace$</c> в проекте без пространств имён, —
    /// не подставляется пустой строкой: переменная остаётся на месте, и недописанное видно в файле, а
    /// не прячется в <c>namespace ;</c>.
    /// </remarks>
    public static string Expand(string text, NewItemRequest request)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(request);

        var built = new StringBuilder(text.Length);
        var at = 0;

        while (at < text.Length)
        {
            var open = text.IndexOf('$', at);

            if (open < 0)
            {
                built.Append(text, at, text.Length - at);
                break;
            }

            var close = open + 1;

            while (close < text.Length && text[close] is >= 'a' and <= 'z')
                close++;

            if (close < text.Length && close > open + 1 && text[close] == '$' &&
                Value(text[(open + 1)..close], request) is { } value)
            {
                built.Append(text, at, open - at).Append(value);
                at = close + 1;
                continue;
            }

            // Не переменная: знак остаётся, а разбор идёт со следующего — у «$$name$» переменная
            // начинается со второго знака.
            built.Append(text, at, open + 1 - at);
            at = open + 1;
        }

        return built.ToString();
    }

    private static string? Value(string name, NewItemRequest request) => name switch
    {
        "name" => request.Name,
        "namespace" => request.Namespace,
        "rootnamespace" => request.RootNamespace,
        "project" => request.Project,
        "year" => DateTime.Now.Year.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };
}
