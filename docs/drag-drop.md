# Перетаскивание между панелями

Панель несёт мышью своё в чужую: файлы из окна проекта — на доску дизайнера, цвет из палитры — на
холст соседа. Обе стороны говорят на SDK и друг о друге не знают: источник кладёт данные и
разрешает эффекты, цель отвечает, что сделает отпускание, а всё между ними — подсказку у курсора,
курсор, поиск цели под мышью, окна поверх окон, Esc — ведёт студия.

Всё, что здесь написано, закреплено тестами: примеры компилируются
([DragDropGuideTests](../tests/ArxisStudio.Tests/DragDropGuideTests.cs)), договор цели и источника
проверяет [StudioDragDropTests](../tests/ArxisStudio.Tests/StudioDragDropTests.cs). Подробности
каждого члена — в XML-комментариях
[StudioDragDrop.cs](../src/ArxisStudio.Sdk/Extensibility/StudioDragDrop.cs); здесь — порядок
действий и причины.

Появилось в SDK 7.11 — его и просите в манифесте:

```json
{ "sdk": { "min": "7.11" } }
```

## Цель: объявить себя и ответить

Целью элемент объявляет себя сам — `StudioDragDrop.AllowDrop` — и отвечает четырьмя событиями:

| Событие | Когда | Что делает цель |
|---|---|---|
| `DragEnter` | курсор с несомым пришёл на цель | готовится: разбирает данные один раз |
| `DragOver` | курсор движется, сменились клавиши; сразу за `DragEnter` | **отвечает**: `Effect` и `Hint` |
| `Drop` | отпустили над целью | делает; итог `Effect` — ответ источнику |
| `DragLeave` | ушли, бросили или отпустили | убирает отметку и заготовку |

Договор короткий, и каждое его слово кем-то оплачено:

- **Ответ — каждый раз заново.** `DragOver` начинается с отказа (`None`): цель, забывшая ответить,
  отказывает, а не принимает всё подряд. `Drop` начинается с последнего ответа `DragOver` — решив
  там, в `Drop` можно не решать.
- **Эффект — ровно один из разрешённых** источником (`AllowedEffects`) или `None`. Неразрешённый
  или составной студия считает отказом и пишет об этом в журнал — одной строкой за тягу.
- **`DragLeave` приходит всегда, и после `Drop` тоже.** Отметку убирают в одном месте.
- **Цель — ближайший к курсору элемент с `AllowDrop`.** Свойство не наследуется: переход курсора
  между детьми цели её не дёргает. События всплывают, как у `DragDrop` Avalonia; ответив,
  поставьте `Handled` — внешняя цель ответ не перепишет.
- **`Hint` — фраза для человека:** что сделает отпускание («Вернуть «Main.axaml» на доску») или
  почему нельзя («Только картинки .png»). Курсор говорит «можно» или «нельзя», подсказка — что
  именно и почему.
- **Падение обработчика студию не роняет:** цель выпадает из этой тяги, плагин получает сбой, как
  за всякое необработанное исключение.

Объявить цель можно и разметкой — `StudioDragDrop.AllowDrop="True"`, тип живёт под адресом студии.
Целью лучше делать контейнер панели, а не её список: над надписью «пусто», лежащей поверх списка,
курсор нашёл бы надпись, а не цель.

```csharp
using System;
using System.Linq;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace Guide.Target;

/// <summary>Панель, на которую из окна проекта несут картинки: она их прикалывает к себе.</summary>
[ToolWindow("pinboard.pins")]
public sealed class PinsPanel : ToolWindow
{
    private readonly AxListBox _pins = new();

    protected override Control Build()
    {
        StudioDragDrop.SetAllowDrop(_pins, true);
        StudioDragDrop.AddDragOverHandler(_pins, OnDragOver);
        StudioDragDrop.AddDropHandler(_pins, OnDrop);

        return _pins;
    }

    public override void Release()
    {
        StudioDragDrop.RemoveDragOverHandler(_pins, OnDragOver);
        StudioDragDrop.RemoveDropHandler(_pins, OnDrop);
    }

    private void OnDragOver(object? sender, StudioDragEventArgs e)
    {
        e.Handled = true;

        if (Pictures(e).Length == 0)
        {
            // Несут не файлы — сказать нечего; файлы, но не картинки, — сказать почему.
            e.Hint = e.Data.Files.Count == 0 ? null : Context.Strings["pins.drop.none"];
            return;
        }

        // Ссылка, а не копия: панель показывает файл, а не забирает его.
        if ((e.AllowedEffects & DragDropEffects.Link) == 0)
            return;

        e.Effect = DragDropEffects.Link;
        e.Hint = Context.Strings["pins.drop.pin"];
    }

    private void OnDrop(object? sender, StudioDragEventArgs e)
    {
        e.Handled = true;

        foreach (var picture in Pictures(e))
            _pins.Items.Add(picture);
    }

    private static string[] Pictures(StudioDragEventArgs e) =>
        [.. e.Data.Files.Where(file => file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))];
}
```

## Источник: отдать тягу студии

Почти любому источнику хватает одного вызова — `IStudioDragDrop.DragAsync`. Жест — дело источника:
тягой он становится, когда мышь с нажатой кнопкой ушла от точки нажатия дальше
`StudioDragDrop.Threshold` (6 точек, порог вкладок докинга и окна проекта). Дальше студия
захватывает указатель, ведёт подсказку и курсор и отдаёт ответ цели задачей, когда кнопку
отпустили; Esc и потеря захвата — отказ.

Ответ — что сделала цель. `Copy` и `Link` источнику делать нечего, `Move` значит «забрали»:
источник убирает своё сам. Не готовы отдать — не разрешайте `Move`.

```csharp
using System;
using System.Threading.Tasks;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Guide.Source;

/// <summary>Цвета палитры — свой формат: значение общего типа, имя с приставкой плагина.</summary>
public static class Swatches
{
    public static StudioDataFormat<Color> Color { get; } = new("palette.color");
}

/// <summary>Плитка палитры, которую несут мышью на чужой холст.</summary>
public sealed class SwatchCarry
{
    private readonly IStudioDragDrop _drags;
    private readonly Control _tile;
    private readonly Color _color;
    private Point? _pressed;

    public SwatchCarry(IStudioDragDrop drags, Control tile, Color color)
    {
        _drags = drags;
        _tile = tile;
        _color = color;

        tile.PointerPressed += (_, e) => _pressed = e.GetPosition(tile);
        tile.PointerReleased += (_, _) => _pressed = null;
        tile.PointerMoved += OnMoved;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } start || !e.GetCurrentPoint(_tile).Properties.IsLeftButtonPressed)
            return;

        var at = e.GetPosition(_tile);

        if (Math.Abs(at.X - start.X) < StudioDragDrop.Threshold && Math.Abs(at.Y - start.Y) < StudioDragDrop.Threshold)
            return;

        _pressed = null;
        _ = CarryAsync(e);
    }

    private async Task CarryAsync(PointerEventArgs trigger)
    {
        var data = new StudioDragData().With(Swatches.Color, _color);
        var visual = new StudioDragVisual(_color.ToString()) { IconBrush = new SolidColorBrush(_color) };

        // Копия: палитра цвет не отдаёт, а делится им.
        await _drags.DragAsync(_tile, trigger, data, DragDropEffects.Copy, visual);
    }
}
```

Службу берут у контекста — `context.GetService<IStudioDragDrop>()`. В студии она есть всегда, но
контракт честен: служба может не прийти, и источник без неё просто не несёт.

## Источник со своими целями: сеанс

Окно проекта носит файлы и внутри себя — между своими каталогами, переносом и копией, — и за свой
край, к чужим целям. Свои цели оно отвечает само, а чужие спрашивает сеансом —
`IStudioDragDrop.Begin`:

- **`ElementAt`** — что под курсором: верхний элемент в верхнем окне студии. Своё ли под курсором,
  решают по нему, а не попаданием в своё окно: то не видит оторванного окна поверх.
- **`Over`** — над чужим: сеанс находит цель и спрашивает её.
- **`OverOwn`** — над своим: отвечает источник, прежняя чужая цель слышит `DragLeave`, а подсказку
  и курсор по-прежнему ведёт студия. Эффект здесь — решение источника, а не разрешение чужим: окно
  проекта переносит внутри себя, хотя чужим перенос не разрешает.
- **`Drop`** — отпустили над чужим: ответ цели, сеанс закрыт. **`Dispose`** — бросили или отпустили
  над своим.

Указатель, захват и Esc в этой дороге — дело источника.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Guide.Session;

/// <summary>Список, который переставляет строки у себя и отдаёт пути чужим.</summary>
public sealed class ListCarry(IStudioDragDrop drags, Control list) : IDisposable
{
    private IStudioDragSession? _session;

    public void Start(IReadOnlyList<string> paths, string label) =>
        _session = drags.Begin(
            list,
            StudioDragData.FromFiles(paths),
            DragDropEffects.Copy | DragDropEffects.Link,
            new StudioDragVisual(label));

    /// <summary>Курсор сдвинулся: своё отвечаем сами, чужое спрашивает студия.</summary>
    public DragDropEffects Move(Point at, KeyModifiers keys)
    {
        if (_session is not { } session)
            return DragDropEffects.None;

        if (session.ElementAt(at) is { } hit && hit.GetSelfAndVisualAncestors().Contains(list))
        {
            session.OverOwn(at, DragDropEffects.Move, null);
            return DragDropEffects.Move;
        }

        return session.Over(at, keys);
    }

    /// <summary>Отпустили: над чужим кладёт цель, над своим — мы.</summary>
    public DragDropEffects Release(Point at)
    {
        var own = _session?.ElementAt(at) is { } hit && hit.GetSelfAndVisualAncestors().Contains(list);
        var effect = own ? DragDropEffects.Move : _session?.Drop() ?? DragDropEffects.None;

        Dispose();

        return effect;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
```

## Что несут

`StudioDragData` — значения по форматам, неизменяемые: каждая цель видит то, что положил источник.
Пути файлов и каталогов — `StudioDataFormats.Files` и короткая дорога `Data.Files`; кладёт их
`StudioDragData.FromFiles`, и только абсолютными: путь от текущей папки значил бы у цели чужой
файл. Первым кладут тот, за который взялись, — как в подписи у курсора: цель, раскладывающая
несомое от точки отпускания, ставит под курсор его, и доска дизайнера так и делает. Свой формат —
`StudioDataFormat<T>` с именем под приставкой плагина, как у команд.

Тип значения видят обе стороны, а живут они в разных контекстах загрузки, поэтому он должен быть
общим: из платформы (`Color`, `string`), из SDK или из контракта, объявленного в
`provides.contracts`. Закрытый тип плагина соседу не назвать, и `TryGet` его не отдаст.

## Эффекты

| Эффект | Что значит | Кто что делает |
|---|---|---|
| `Copy` | цель берёт копию | цель — свою копию, источник — ничего |
| `Link` | цель ссылается на несомое | цель — ссылку, источник — ничего |
| `Move` | цель забирает | цель — своё, источник — убирает у себя |

Окно проекта чужим разрешает `Copy | Link`: файлы решения правит только служба файлов, и перенос
за край окна значил бы правку мимо её истории. Доска дизайнера отвечает `Link` — карточка
показывает файл, а не забирает его, — а нет ссылки, то `Copy`.

## Что видно

У курсора — подсказка студии, одетая как всплывающая подсказка темы: значок и подпись источника
(`StudioDragVisual`), а под ними `Hint` цели. Она лежит в слое оверлеев того окна студии, над которым
курсор, и переезжает за ним из окна в окно. Курсор — по ответу: перенос, копия, ссылка или «нельзя».
Отметку цели рисует сама цель — языком дизайн-системы: заливка `AxInfoFill` и кольцо `AxAccent`
толщиной `AxDropTargetThickness`, как у строки, в которую ляжет несомое.

## Чего здесь нет

- **Системной тяги.** Несут только между окнами студии, захватом указателя, как вкладки докинга:
  мышь, которой водят инструменты студии и тесты, ведёт её так же, как настоящая. Файлы из
  проводника приходят событиями `DragDrop` Avalonia, как и прежде.
- **Дороги без мыши за вас.** Перетаскивание — короткий путь, а не единственный (WCAG 2.5.7): у
  всего, что делает отпускание, должна быть команда или пункт меню. У доски дизайнера это «Вернуть
  на доску» в меню холста, у окна проекта — вырезать и вставить.

## Проверить у себя

Цель проверяется без источника: `StudioDragEventArgs` заводится открытым конструктором, а событие
поднимается на цели `RaiseEvent`. Сквозь студию — сеансом `Begin` над окном теста. В безголовом
прогоне попадание идёт по сцене отрисовки, и новому окну её кладёт только такт:
`AvaloniaHeadlessPlatform.ForceRenderTimerTick()` до первого `Over`, иначе курсор над целью
придётся на пустое место.
