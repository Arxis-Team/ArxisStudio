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
    { "id": "arxis.xaml", "min": "1.0" }
  ]
}
```

Граница одна, **1.0**: документы, показ, поколение. SDK нужен 7.15 — с него синтаксис разметки общий.
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
        // Корень показ уже отдал — RootChanged пришёл раньше. Своё — запомненные объекты, типы — здесь.
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

## Замена поколения

Сохранённый код дизайнер собирает после паузы (`xaml.buildDelay`, 400 мс), своей сборкой — в
`bin/ArxisStudio/` и `obj/ArxisStudio/`, рядом с выходом IDE, а не поверх него, — и заменяет
поколение. Прежнее уходит из процесса, **только если его не держит никто**:

1. показы отдают корни — `RootChanged`, корень пуст;
2. участники (`Register`) отпускают своё — `ReleaseAsync`;
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
- **Не править объекты.** Записанное в объект не попадёт ни в текст, ни в историю, ни в файл.
- **Не забывать аренды и отсрочки.** Забытая аренда держит документ, забытая отсрочка — замену.
- **Не писать файл документа мимо службы.** Запись мимо неё документ примет за чужую — и спросит
  человека о его же правке.
- **Не ждать службу из участника.** Замена держит её очередь, пока участники не вернутся.

## Что читать дальше

- [projects.md](projects.md) — модель решения, профиль дизайна и запись файлов, на которых стоит служба;
- [plugin-markup.md](plugin-markup.md) — разметка самого плагина и её перезагрузка;
- [drag-drop.md](drag-drop.md) — тяга между панелями: так в форму приносят из окна проекта.
