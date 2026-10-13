# Как плагин работает с разметкой

Документы разметки решения студия держит одна — встроенный модуль `arxis.xaml`. Он открывает
`.axaml` живыми: у документа своя история правок, свой текст и объекты, построенные из этого текста в
**поколении** — типах проекта, собранных дизайнером и загруженных в выгружаемый контекст. Сохраняет
документ служба файлов, а запись на диск мимо студии приходит в документ шагом его истории. Плагину не
достаются ни загрузчик Markup, ни поколение — ему достаются документ, путь элемента и две службы.

Всё, что здесь написано, закреплено тестами: примеры компилируются
([XamlGuideTests](../tests/ArxisStudio.Tests/XamlGuideTests.cs)), а имена и версии сверяются с кодом.
Подробности каждого метода — в XML-комментариях контракта
([ArxisStudio.Xaml.Contracts](../src/Modules/ArxisStudio.Xaml.Contracts)); здесь — порядок действий и
причины.

## Две службы, одна на своё дело

| Служба | Берётся | О чём она |
|---|---|---|
| `IStudioXamlDocuments` | `context.XamlDocuments()` | открыть документ, править, отменить, сохранить, показать; что за файл по его корню |
| `IStudioXamlDesign` | `context.XamlDesign()` | поколение типов: состояние, пересборка, участники замены, отсрочки |

Тому, кто показывает текст, замена типов не нужна, а тому, кто держит построенное из них, — нужна вся.
Обе живут в одном модуле, берутся экспортом и могут ответить `null`: модуля нет, он не поднялся или его
версия ниже той, что объявил ваш манифест.

## Объявите модуль зависимостью

```json
{
  "id": "arxis.forms",
  "sdk": { "min": "7.15" },
  "dependencies": [
    { "id": "arxis.xaml", "min": "1.4" }
  ]
}
```

Границы пять: **1.0** — документы, показ, поколение; **1.1** — члены элемента, их значения и откуда они,
проверка и предпросмотр значения; **1.2** — данные элемента: откуда они, их тип и компиляция привязок, модели
проекта и классы файла кода; **1.3** — выражения элемента: какие они и работают ли; **1.4** — черновик привязки: его
проверка, что путь может назвать дальше, и режим привязки члена по умолчанию. Плагину, которому члены и данные не
нужны, хватит 1.0. SDK нужен 7.15 — с него
синтаксис разметки общий; обёртка нескольких соседей одной правкой (`WrapElements`) — с 7.16.
Ссылка при сборке — на контракт:

```xml
<ProjectReference Include="$(ArxisStudioPath)/src/Modules/ArxisStudio.Xaml.Contracts/ArxisStudio.Xaml.Contracts.csproj"/>
```

С ним приедут модель проектов и синтаксис Markup — `ArxisStudio.Markup` и `ArxisStudio.Markup.Xaml`: на
их языке написан контракт. В пакет плагина они не поедут — студия объявила их общими, и упаковка их
отсеивает. Загрузчика Markup (`ArxisStudio.Markup.Xaml.Loader`) в ссылках нет и не будет: он строит
объекты поколения, и тип из него в руках плагина держал бы поколение.

## Возьмите службы

```csharp
using System;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Taking;

/// <summary>Плагину нужны документы разметки и знать, где поколение типов проекта.</summary>
public sealed class FormsPlugin : StudioPlugin
{
    private IStudioContext? _context;
    private IStudioXamlDesign? _design;

    public IStudioXamlDocuments? Documents { get; private set; }

    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        Documents = context.XamlDocuments();
        _design = context.XamlDesign();

        if (Documents is null || _design is null)
        {
            // Службы может не быть, и это не беда: студия собирается и без модуля XAML.
            context.Log.Write(StudioLogLevel.Warning, "Forms", "Службы XAML нет");

            return;
        }

        _design.StateChanged += OnDesign;
    }

    public override void Deactivate()
    {
        if (_design is not null)
            _design.StateChanged -= OnDesign;

        _design = null;
        Documents = null;
        _context = null;
    }

    private void OnDesign(object? sender, EventArgs e) => _context?.Log.Write(
        StudioLogLevel.Info,
        "Forms",
        $"Поколение: {_design?.State} {_design?.StateReason}");
}
```

Подъём модуля дешёвый: ни оценки, ни сборки при нём нет. Поколение поднимает **первый открытый
документ** решения — оценка профилем дизайна, сборка того, что устарело, загрузка, — и первое открытие
поэтому долгое, а следующие нет. Последний отпущенный документ поколение не роняет сразу: оно живёт ещё
пару минут, чтобы закрытая и снова открытая вкладка не поднимала всё заново.

## Открыть, править, сохранить

```csharp
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Editing;

/// <summary>Ставит форме ширину и сохраняет её — одним шагом истории.</summary>
public static class Widths
{
    public static async Task SetWidthAsync(
        IStudioXamlDocuments documents,
        CanonicalPath form,
        double width,
        CancellationToken cancellationToken)
    {
        // Аренда, а не документ: отпустить её обязан тот, кто взял.
        await using var document = await documents.OpenAsync(form, cancellationToken);

        var outcome = await document.EditAsync(
            "Ширина формы",
            editor => editor.SetAttribute(
                editor.Document.Root!,
                XamlQualifiedName.Unprefixed("Width"),
                width.ToString(CultureInfo.InvariantCulture)),
            cancellationToken);

        if (outcome.TextChanged)
            await document.SaveAsync(cancellationToken);
    }
}
```

**Документ один на файл.** `OpenAsync` отдаёт аренду, и документ живёт, пока не отпущена последняя:
вкладка формы, иерархия и ваш плагин, открывшие один файл, правят один текст с одной историей и одной
отметкой «не сохранено». Отпущенный последним документ закрывается, и несохранённое уходит с ним —
поэтому пример сохраняет до того, как отпустит.

**Правят текст, а не объекты.** Правка записывается редактором синтаксиса — `XamlDocumentEditor` — и
ложится в текст и в историю раньше, чем её покажут; объекты, которые её не смогли показать, остаются
при последнем показанном тексте (`XamlDocumentState.Behind`). Написанное прямо в объект не попадёт ни
в файл, ни в историю. Элементы берут у `editor.Document`: элемент прежнего разбора редактор отвергнет.

Строение правят тем же редактором. Разметка из другого файла или из буфера обмена — `XamlFragment`:
`XamlFragment.From` поднимает элемент с объявлениями, которыми он написан, `ToXamlText` кладёт его
текстом, `XamlFragment.Parse` читает назад, а `InsertFragment` ставит в документ, объявив на корне
недостающее пространство. Копия рядом — `DuplicateElement`, обёртка — `WrapElement` и `WrapElements`
для нескольких соседей, снятие — `UnwrapElement`. Так их делает и дизайнер форм.

**Сохраняет служба файлов.** Запись уходит в локальную историю действием сохранения — общая отмена
окна проекта её не возьмёт, у документа своя история, — и сверяется с тем, что документ считает
содержимым файла. Переписанный мимо документа файл не затирается: `SaveAsync` бросает `IOException`, и
документ остаётся несохранённым.

## Запись снаружи

Рядом открыт Rider. Сохранённый им файл приходит в **чистый** документ молча — шагом истории, который
можно отменить. Поверх **несохранённого** — вопросом:

```csharp
using System;
using System.Threading.Tasks;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Conflicts;

/// <summary>Держит документ и помнит вопрос о файле, переписанном поверх правок.</summary>
public sealed class ConflictWatch : IAsyncDisposable
{
    private readonly IXamlDocumentHandle _document;

    public ConflictWatch(IXamlDocumentHandle document)
    {
        _document = document;
        _document.ExternalConflict += OnConflict;
    }

    /// <summary>Что в файле теперь; null — спрашивать не о чем.</summary>
    public string? DiskText { get; private set; }

    /// <summary>Взять файл: его текст — шаг истории, отмена вернёт правки.</summary>
    public Task TakeTheirsAsync() => _document.ResolveConflictAsync(XamlConflictChoice.TakeTheirs);

    /// <summary>Оставить правки: сохранение запишет их поверх файла.</summary>
    public Task KeepMineAsync() => _document.ResolveConflictAsync(XamlConflictChoice.KeepMine);

    public ValueTask DisposeAsync()
    {
        _document.ExternalConflict -= OnConflict;

        return _document.DisposeAsync();
    }

    private void OnConflict(object? sender, XamlExternalConflictEventArgs e) => DiskText = e.DiskText;
}
```

Пока ответа нет, `HasConflict` — да, а текст документа прежний. Ответ получают все аренды документа:
вопрос один на файл, а не на того, кто спросил.

## Показать

Показ отдаёт **корень** — то, что документ построил в поколении, — и ведёт от объекта к пути элемента
и обратно:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;
using Avalonia.Controls;

namespace Guide.Xaml.Showing;

/// <summary>Показывает корень формы в рамке и помнит выбранное путём.</summary>
public sealed class Preview : IXamlDesignParticipant, IDisposable
{
    private readonly IXamlDesignView _view;
    private readonly IDisposable _registration;

    public Preview(IXamlDesignView view, IStudioXamlDesign design)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(design);

        _view = view;
        _view.RootChanged += OnRoot;
        _registration = design.Register(this);

        Show();
    }

    /// <summary>Где стоит корень.</summary>
    public Border Frame { get; } = new();

    /// <summary>Что выбрано — путём элемента: он переживает и правку, и замену поколения.</summary>
    public XamlElementPath? Selected { get; private set; }

    /// <summary>Человек щёлкнул по объекту.</summary>
    public void Select(object live) => Selected = _view.PathOf(live);

    /// <summary>Объект выбранного — на время вызова, а не в поле.</summary>
    public object? SelectedObject() => Selected is { } path ? _view.ObjectAt(path) : null;

    public ValueTask ReleaseAsync(CancellationToken cancellationToken)
    {
        // Корень ещё на месте: холст успел бы заморозить с ним кадр. Своё отпускают здесь — рамку,
        // запомненные объекты; показ отдаст корень следом.
        Frame.Child = null;

        return ValueTask.CompletedTask;
    }

    public ValueTask RestoreAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void Dispose()
    {
        _registration.Dispose();
        _view.RootChanged -= OnRoot;
        Frame.Child = null;
        _view.Dispose();
    }

    private void OnRoot(object? sender, EventArgs e) => Show();

    // Окно ничьим содержимым не бывает: его содержимое показывают, одалживая (IXamlRootLender), — так
    // это делает рамка формы дизайнера.
    private void Show() => Frame.Child = _view.Root is Window ? null : _view.Root;
}
```

Показ берут у аренды: `await document.ShowAsync(null, cancellationToken)`. Он **один на документ** —
корень один контрол и в двух деревьях стоять не может; второй `ShowAsync` откажет, пока первый не
отпущен. Окно показывают через `IXamlRootLender`: тот, кто вынимает из окна содержимое, ресурсы и
стили, возвращает их на время каждой записи сессии в корень.

`GetDeclaredObjects` отдаёт только то, что документ объявил сам: метку внутри кнопки построил её
шаблон, и править её значило бы править то, чего в тексте нет. `Application` — приложение формы:
стили, ресурсы и шаблоны данных её `App.axaml`, построенные как написано, без конструктора класса
приложения. Тема студии форме не подходит — у формы своя.

## Члены и значения

Показ отвечает и о том, что построил элемент: какого он типа (`DescribeElement`), какие у него члены и к
чему они пришли (`GetMembers`, `GetMember`), прочтётся ли значение (`CheckValue`) и каким станет объект с
другим значением (`Preview`). Ответы — имена и строки, поэтому держать их можно сколько угодно:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Members;

/// <summary>Читает члены выбранного элемента и протягивает число — одним шагом истории.</summary>
public sealed class Members(IXamlDesignView view)
{
    /// <summary>Что у элемента можно править и откуда нынешнее значение — строками для панели.</summary>
    public string[] Describe(XamlElementPath element) =>
    [
        .. view.GetMembers(element)
            .Where(member => member.CanWrite && member.AcceptsText && member.Attributes.Browsable)
            .Select(member => $"{member.Name} = {member.Value.Text} ({member.Value.Origin})"),
    ];

    /// <summary>Прочтётся ли набранное — словами для человека, если нет.</summary>
    public string? Problem(XamlElementPath element, string member, string text) =>
        view.CheckValue(element, member, text).Error;

    /// <summary>Протяжка: значение видно на объекте на каждом шаге, а документ не тронут.</summary>
    public IXamlValuePreview? Begin(XamlElementPath element, string member) => view.Preview(element, member);

    /// <summary>Отпустили — значение ложится в документ одним шагом; предпросмотр кончится сам.</summary>
    public async Task CommitAsync(XamlElementPath element, string member, string text, CancellationToken cancellationToken) =>
        await view.Document.EditAsync($"{member} = {text}", editor =>
        {
            if (element.Resolve(editor.Document) is { } target)
                editor.SetAttribute(target, XamlQualifiedName.Parse(member), text);
        }, cancellationToken);
}
```

Члены — свои и присоединённые, которые **читает родитель**: у ребёнка `Canvas` есть `Canvas.Left`, у
ребёнка наследника сетки — `Grid.Row`, а `DockPanel.Dock` у них нет. Владелец, от которого происходит сам
`Control`, — `InputElement`, `Visual` и ниже — база всякого родителя и о родителе ничего не говорит: его
присоединённое (`InputElement.IsHoldingEnabled`) среди членов стоит, только когда документ его написал.
Картинка и значок — `Image.Source`, `Window.Icon` —
пишутся текстом, путём к ресурсу, но читает его загрузка, а не разбор значения: через резолвер ресурсов, файлом
проекта, а не последней сборкой (ADR 0031 Markup). Поэтому `CheckValue` такой текст пропускает, а предпросмотра у него
нет: имя, которое ничего не нашло, правку не пустит, и загрузка скажет почему. Присоединённый член стороннего
владельца — `ToolTip.Tip` — спрашивают по имени (`GetMember`), пока документ его не написал. Событий и
списков среди членов нет: обработчик — не значение, а детей пишут элементами. Присоединённый член
владельца из проекта пишут с его пространством: `OwnerXmlNamespace` и `OwnerPrefix` отдают
`XamlDocumentEditor.Qualify`, и тот объявит пространство, которого документу не хватает.

**Значение — то, к чему объект пришёл**, и `Origin` говорит откуда:

| `XamlValueOrigin` | Откуда |
|---|---|
| `Default` | значение по умолчанию |
| `Document` | его написал этот элемент |
| `Design` | его написал этот элемент для дизайна: `d:Width` |
| `Expression` | этот элемент написал выражение — `OriginDetail` его называет |
| `Style` | стиль или тема — `OriginDetail` называет селектор |
| `Template` | шаблон контрола, построившего объект |
| `Inherited` | унаследовано — `OriginDetail` называет предка |
| `Animation` | анимация или переход |
| `Code` | значение поставлено объекту кодом, а в документе его нет |

Записывать это значение обратно в документ нельзя: унаследованный кегль, вписанный в атрибут, перестал
бы наследоваться. Что написано, читают у синтаксиса документа. Атрибуты .NET, которыми автор контрола
описал член, — `[Category]`, `[Description]`, `[DisplayName]`, `[Browsable]`, `[EditorBrowsable]`,
`[DefaultValue]`, `[ReadOnly]`, `[Range]` — приходят в `Attributes`: так проект описывает свои контролы,
не завися от студии.

**Предпросмотр** ставит значение объекту поверх его собственного и снимает его без следа: и написанное
документом, и стиль, и привязку. Пока он жив, поколение не заменяют. Кончается он и сам — на правке
текста документа, на замене поколения и с показом, — поэтому протяжка пишет одну правку при отпускании и
отпускает предпросмотр в `using`. У прямого свойства и свойства CLR предпросмотра нет: поставленное
значение не снять, не потеряв прежнего.

## Данные

Что читают привязки элемента, показ отвечает асинхронно (`DescribeDataAsync`): тип данных (`x:DataType`)
разрешает среда поколения. Ответ — имена, держать его можно сколько угодно, а замена поколения посреди вопроса
кончает его ответом `null`. Модели проекта — `GetDataTypesAsync`, классы файла кода, который бросили из окна
проекта, — `IStudioXamlDesign.ClassesInAsync`:

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Data;

/// <summary>Говорит, что читают привязки элемента, и предлагает классы проекта — именами.</summary>
public sealed class Data(IXamlDesignView view, IStudioXamlDesign design)
{
    /// <summary>Откуда данные элемента и по какому типу читать пути его привязок.</summary>
    public async Task<string> DescribeAsync(XamlElementPath element, CancellationToken cancellationToken)
    {
        if (await view.DescribeDataAsync(element, cancellationToken) is not { } scope)
            return "элемента нет, или показ отдал объекты";

        var type = scope.PathType?.Name ?? "не известен";

        return scope.NeedsDataType
            ? $"{scope.Source}, тип {type}: привязки компилируются, а x:DataType нет — проект не соберётся"
            : $"{scope.Source}, тип {type}";
    }

    /// <summary>Модели, которые форма может создать: собранные классы данных, а не контролы и конвертеры.</summary>
    public async Task<string[]> ModelsAsync(CancellationToken cancellationToken) =>
    [
        .. (await view.GetDataTypesAsync(cancellationToken))
            .Where(type => type is { IsModel: true, IsBuilt: true, CanCreate: true })
            .Select(type => type.FullName),
    ];

    /// <summary>Классы файла, который бросили из окна проекта.</summary>
    public async Task<string[]> DroppedAsync(CanonicalPath file, CancellationToken cancellationToken) =>
    [
        .. (await design.ClassesInAsync(file, cancellationToken))
            .Select(type => type.IsBuilt ? type.FullName : $"{type.FullName} — ещё не собран"),
    ];
}
```

**Откуда данные** — `Source`, а `SourceElement` называет элемент, который их написал:

| `XamlDataSource` | Откуда |
|---|---|
| `None` | данных нет: их не написал никто и код не поставил |
| `Inherited` | от предка, который их написал |
| `Instance` | элемент создаёт их сам — классом в длинной записи: `<Window.DataContext><vm:Main /></Window.DataContext>` |
| `Expression` | элемент берёт их выражением: `DataContext="{Binding Customer}"` |
| `Self` | элемент сам себе данные: `DataContext="{Binding $self}"` |
| `Code` | их поставил код — конструктор формы или элемента, — а документ не пишет |
| `Design` | данные для дизайна корня — `Design.DataContext` или `d:DataContext`; в приложении данные поставит код |

Что корень наследует от рамки дизайнера, — данные студии, а не формы, и такой корень отвечает `None`.

**Тип путей** — `PathType`, как пути прочтёт проект: компилируемые — по разрешённому `x:DataType`, а
некомпилируемые — по типу данных сейчас и по `x:DataType` только без них: такую привязку компилятор не проверяет,
и она читает данные элемента. В дизайнере данные сейчас — это данные для дизайна, если корень их пишет. Типы —
`XamlTypeInfo`, имена, а не `Type`: тип — объект поколения.

**Компилируются ли привязки** — `CompilesBindings`: ближайший `x:CompileBindings` элемента и его предков, а без
него — свойство проекта `AvaloniaUseCompiledBindingsByDefault` (`CompilesByProjectDefault`). Живой показ грузит
привязки отражением и прощает неизвестный путь, а сборка проекта — нет: `{Binding}`, который компилируется, без
`x:DataType` проект не соберёт, и `NeedsDataType` говорит об этом заранее.

**Классы** — `XamlDataTypeInfo`: собранные (`IsBuilt`) — со сведениями каталога поколения, а объявленные файлами
кода и ещё не собранные — с тем, что сказал текст. `IsModel` — данные, а не конвертер (`IsConverter`) и не шаблон
данных вроде `ViewLocator` (`IsTemplate`); контрол — `IsControl`, класс формы с разметкой — `IsFormClass`.
Модель без `INotifyPropertyChanged` (`Notifies`) привязка прочтёт один раз. Файл кода читается текстом, не
компилятором: классы верхнего уровня, открытые; вложенный класс разметка не назовёт.

Конструктор модели, которую форма создаёт сама, дизайнер зовёт при каждом построении формы — в студии. Модель,
которая в конструкторе ходит в сеть или в базу, для этого не годится: дизайну дают модель попроще через
`Design.DataContext`, а настоящую ставит код.

## Выражения

Вид выражения и короткую запись для жетона знает один синтаксис — `XamlExpressions.Classify` и `Summarize`, без
показа и без поколения: `{Binding Customer.Name}` — привязка `Customer.Name`, `{Binding #slider.Value}` —
`#slider.Value`, `{DynamicResource Accent}` — ресурс `Accent`. Работают ли выражения элемента, показ отвечает
асинхронно (`CheckExpressionsAsync`), по одному ответу на каждый атрибут, написанный выражением:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Expressions;

/// <summary>Говорит, какие выражения элемента не работают и почему.</summary>
public sealed class Expressions(IXamlDesignView view)
{
    /// <summary>Сломанные выражения элемента — членом и причиной.</summary>
    public async Task<List<string>> BrokenAsync(XamlElementPath element, CancellationToken cancellationToken)
    {
        var broken = new List<string>();

        foreach (var check in await view.CheckExpressionsAsync(element, cancellationToken))
        {
            if (check.State == XamlExpressionState.Broken)
                broken.Add(check.BreaksBuild ? $"{check.Member}: {check.Message} (сборка остановится)" : $"{check.Member}: {check.Message}");
        }

        return broken;
    }

    /// <summary>Вид выражения и запись для жетона — по одному тексту, без показа.</summary>
    public static string? Token(string written) =>
        XamlExpressions.Classify(written) is { } kind ? $"{kind}: {XamlExpressions.Summarize(written)}" : null;
}
```

**Привязку проверяют по тому, что документ объявляет**, а не по объектам в студии: форма в дизайнере стоит внутри
контролов студии, и её живые предки и данные бывают чужими.

| Источник привязки | Чем читается путь |
|---|---|
| данные — `{Binding Name}` | компилируемая — `x:DataType` в области, которого шаблон данных не наследует; некомпилируемая — данными, которые документ, класс формы или данные для дизайна дали объектам, а `x:DataType` — только без них; у `DataContext` — областью родителя |
| элемент — `#name`, `ElementName` | типом элемента с таким именем в области имён документа, шаблона — своей |
| сам — `$self`, `RelativeSource Self` | типом самого элемента |
| предок — `$parent[T]`, `FindAncestor` | типом предка в документе, а не выше корня |

`Resolved` — путь дошёл: `SourceType` — откуда, `ResultType` — куда. `Broken` — нет: свойства нет, имени нет,
типа нет, или привязка компилируется, а `x:DataType` в области нет; `Message` говорит словами, а `BreaksBuild` —
что сборка проекта, компилирующая привязку, на этом остановится. `Unchecked` — проверка за выражением не
следует: ресурс, статический член, мультипривязка, `Source`, данные внутри шаблона контрола, `x:DataType`, который
не разрешился, и данные, которых у показа нет, — их даст работающая форма. Замена поколения посреди вопроса
кончает его пустым ответом.

Ключи ресурсов этот ответ не проверяет, а пропавший ключ `StaticResource` форму в студии не роняет: дизайнер
показывает её без значения, и молчат оба — приложение же на нём упадёт. Без показа выражение не проверить, но
выражение, которое называет тип или член, которого нет (`{x:Static Nope.Value}`, `{local:Upper}` без такого типа),
не даёт форме загрузиться, и `Diagnostics` документа говорит об этом ошибкой с местом в начале его атрибута.

## Черновик привязки

Привязку, которой в документе ещё нет, проверяют тем же правилом, что написанную (`CheckExpressionAsync`), а что её путь
может назвать дальше, отвечает `GetBindableMembersAsync`: члены типа, к которому привёл путь, — данных, элемента по
имени, предка или самого элемента. У члена — тип, пишется ли он, команда ли, список ли, можно ли шагнуть в него дальше
(`HasMembers`) и подходит ли его значение члену, к которому привязывают (`Fits`: тексту и объекту — всё, иначе тот же
или приводимый тип, число к числу).

```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Drafts;

/// <summary>Подсказывает путь привязки и проверяет её до записи.</summary>
public sealed class Drafts(IXamlDesignView view)
{
    /// <summary>Члены, которые путь <c>Customer.</c> может назвать дальше, — подходящие тексту первыми.</summary>
    public async Task<string[]> NextAsync(XamlElementPath element, CancellationToken cancellationToken) =>
        await view.GetBindableMembersAsync(element, "Text", "{Binding Customer}", cancellationToken) is { } members
            ? [.. members.Members.OrderByDescending(static member => member.Fits).Select(static member => $"{member.Name}: {member.TypeName}")]
            : [];

    /// <summary>Запишется ли привязка: сломанное не пишут.</summary>
    public async Task<bool> WorksAsync(XamlElementPath element, string binding, CancellationToken cancellationToken) =>
        await view.CheckExpressionAsync(element, "Text", binding, cancellationToken) is not { State: XamlExpressionState.Broken };
}
```

Где проект компилирует привязки, а типа данных в области нет, члены всё равно подсказываются — по данным показа: путь
можно набрать и так, а что сборке нужен `x:DataType`, скажет проверка. Режим, которым привязка к члену идёт без
`Mode`, — `XamlMemberInfo.DefaultBindingMode`; у свойства CLR его нет: привязку ставят только на свойство Avalonia.

## Замена поколения

Сохранённый код дизайнер собирает после паузы (`xaml.buildDelay`, 400 мс), своей сборкой — в
`bin/ArxisStudio/` и `obj/ArxisStudio/`, рядом с выходом IDE, а не поверх него, — и заменяет
поколение. Прежнее уходит из процесса, **только если его не держит никто**:

1. участники (`Register`) отпускают своё — `ReleaseAsync`; корень ещё на месте, и холст успевает
   заморозить кадр с формой;
2. показы отдают корни — `RootChanged`, корень пуст;
3. поколение выгружается и доказывает, что ушло;
4. показы берут новые корни, участники — своё (`RestoreAsync`).

Не ушло — нового поколения нет: только новый процесс покажет типы как есть, и служба просит студию о
перезапуске (`XamlDesignState.RestartRequired`). Поэтому **объект поколения не держат в поле**: между
вызовами помнят путь элемента, а объект берут по нему, когда нужен.

Замену придерживают отсрочкой — пока идёт то, что она бы оборвала:

```csharp
using System;
using System.Threading.Tasks;
using ArxisStudio.Xaml;

namespace Guide.Xaml.Deferring;

/// <summary>Пока человек тянет, типы под рукой не меняются.</summary>
public static class Gestures
{
    public static async Task DragAsync(IStudioXamlDesign design, Func<Task> drag)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(drag);

        using var deferral = design.Defer("тяга в превью");

        await drag();
    }
}
```

Замена пойдёт, как только отпущена последняя отсрочка. Отсрочку, как и аренду, отпускают всегда — в
`finally` или `using`: забытая держит замену до конца сеанса. **Внутри `ReleaseAsync` и
`RestoreAsync` службу не ждут**: замена держит её очередь, пока участники не вернутся.

## Где поколение

| `XamlDesignState` | Что это значит |
|---|---|
| `Idle` | ни один документ решения не открыт, или решение закрыто |
| `Starting` | поколение поднимается: оценка, сборка устаревшего, загрузка |
| `Live` | поколение живое и свежее |
| `Building` | идёт сборка дизайна; поколение пока прежнее |
| `SwapPending` | типы собраны заново, замену держат отсрочки — их называет `StateReason` |
| `Swapping` | поколение заменяется |
| `RestartRequired` | прежнее поколение не ушло, или пакет сменил версию — нужен перезапуск |
| `Unsupported` | проект собран против другого старшего номера Avalonia; документы открываются текстом |
| `Failed` | поколение не поднялось; почему — в `StateReason` |

Последняя сборка дизайна — `LastBuild`: собралась ли, что сказала, сменились ли типы. Пересобрать
сейчас — `RebuildAsync`.

## Чего не делать

- **Не держать объект поколения дольше вызова.** Корень, контрол, тип, приложение формы — всё это
  держит поколение, и замена кончится перезапуском.
- **Не править объекты.** Записанное в объект не попадёт ни в текст, ни в историю, ни в файл. Показать
  значение, не записав его, — дело предпросмотра (`Preview`): он снимет показанное без следа.
- **Не забывать аренды и отсрочки.** Забытая аренда держит документ, забытая отсрочка — замену.
- **Не писать файл документа мимо службы.** Запись мимо неё документ примет за чужую — и спросит
  человека о его же правке.
- **Не ждать службу из участника.** Замена держит её очередь, пока участники не вернутся.
- **Не класть форму в её же данные.** `<UserControl.DataContext><v:MainView/></UserControl.DataContext>` внутри
  `MainView.axaml` компилятор Avalonia собирает, а приложение падает переполнением стека: класс строит себя без
  конца. Загрузка такой элемент не строит и называет ошибкой (`AXM3054`), и форма показывается без него. Форма
  своими данными — `DataContext="{Binding $self}"` на корне или `DataContext = this` в коде, а `x:DataType` её
  класса проверит пути привязок.

## Что читать дальше

- [projects.md](projects.md) — модель решения, профиль дизайна и запись файлов, на которых стоит служба;
- [plugin-markup.md](plugin-markup.md) — разметка самого плагина и её перезагрузка;
- [drag-drop.md](drag-drop.md) — тяга между панелями: так в форму приносят из окна проекта.
