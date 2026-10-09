# Как плагин работает с инспектором

Инспектор студии — одна панель свойств на всех, как Inspector у Unity: модуль `arxis.inspector`. Что в нём
показано, решают источники: дизайнер ставит туда выбранное на холсте, а плагин — свои объекты. Как свойства
выглядят, решают вклады: редакторы полей, разделы, таблицы метаданных и пункты меню — их пишут и модули
студии, и плагины, одним контрактом. Инспектор сам ничего не меняет: правка уходит источнику одной записью и
становится одним шагом его истории.

Всё, что здесь написано, закреплено тестами: примеры компилируются
([InspectorGuideTests](../tests/ArxisStudio.Tests/InspectorGuideTests.cs)), а имена и версии сверяются с
кодом. Подробности каждого типа — в XML-комментариях контракта
([ArxisStudio.Inspector.Contracts](../src/Modules/ArxisStudio.Inspector.Contracts)); живой образец всего, что
ниже, — встроенный модуль [ArxisStudio.Modules.Sample](../src/Modules/ArxisStudio.Modules.Sample).

## Что можно сделать

| Задача | Чем | У Unity |
|---|---|---|
| показать свой объект | `context.Inspector()?.Inspect(объект, имя)` | `Selection.activeObject` |
| показать свою модель со своей историей | наследник `InspectorTarget` и `Show` | `SerializedObject` и `Editor` |
| описать свойства чужого типа | `InspectorMetadata` | `[Range]`, `[Header]`, `[Tooltip]` |
| своё поле для типа значения | `PropertyEditor` с `[PropertyEditor]` | `PropertyDrawer` |
| свой интерфейс для типа цели | `InspectorSection` с `[InspectorSection]` | `CustomEditor` |
| пункты меню строки и ⋮ | `InspectorMenu` | `contextualPropertyMenu` |
| описать свойства своих контролов в проекте | атрибуты .NET у свойства | атрибуты у поля |

## Объявите модуль зависимостью

```json
{
  "id": "arxis.export",
  "sdk": { "min": "7.22" },
  "dependencies": [
    { "id": "arxis.inspector", "min": "1.1", "optional": true }
  ],
  "activation": [ "onContribution:arxis.inspector" ]
}
```

Границы две: **1.0** — цели, вклады и служба показа; **1.1** — показ объекта (`Inspect`). Плагину, который
только дополняет инспектор редакторами и разделами, служба не нужна вовсе: вклады студия найдёт в его сборке
сама — открытые классы с открытым конструктором, — построит через шов сбоев с контекстом плагина и снимет с
его выгрузкой. Спящий плагин проснётся событием `onContribution:arxis.inspector`, когда инспектор впервые
спросит вклады. Зависимость необязательная: без инспектора плагин поднимется, а `context.Inspector()` вернёт
`null`.

Ссылка при сборке — на контракт:

```xml
<ProjectReference Include="$(ArxisStudioPath)/src/Modules/ArxisStudio.Inspector.Contracts/ArxisStudio.Inspector.Contracts.csproj"/>
```

Контракт ссылается только на SDK, набор контролов и Avalonia; сборку контракта студия грузит в основной
контекст один раз, и копия из каталога плагина её не подменит.

## Покажите свой объект

Обычный объект CLR — настройки, модель панели. Свойства — открытые свойства экземпляра, которые читаются;
пишется то, у чего есть открытая запись и чей текст читается: числа, строки, флажки, перечисления, цвет и
отступ Avalonia. Значение своего типа — структуры или класса плагина — видно текстом и только читается:
конвертер запомнил бы тип навсегда, и плагин было бы не выгрузить; нужна правка — разложите его на свойства или
пишите свою цель. Подпись, раздел, пояснение, границы и значение по умолчанию — атрибутами .NET; значение,
равное значению по умолчанию, — незаданное, и «Сбросить» возвращает его. О переменах объект говорит сам —
`INotifyPropertyChanged`.

```csharp
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;

namespace Guide.Inspector.Objects;

public sealed class ExportSettings : INotifyPropertyChanged
{
    private int _quality = 90;

    public event PropertyChangedEventHandler? PropertyChanged;

    [Category("image")]
    [Description("Сжатие JPEG: больше — чище и тяжелее")]
    [Range(1, 100)]
    [DefaultValue(90)]
    public int Quality
    {
        get => _quality;
        set
        {
            _quality = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quality)));
        }
    }

    [DisplayName("Папка")]
    public string Folder { get; set; } = "export";

    [Browsable(false)]
    public string Secret { get; set; } = string.Empty;
}

public sealed class ExportPanelModel(IStudioContext context) : IDisposable
{
    private IDisposable? _shown;

    public ExportSettings Settings { get; } = new();

    public void ShowInInspector()
    {
        _shown?.Dispose();
        _shown = context.Inspector()?.Inspect(Settings, context.Strings["export.settings"]);
    }

    // Ушла панель — её объект уходит из инспектора, и показан снова тот, кто был до него.
    public void Dispose() => _shown?.Dispose();
}
```

Показ — не переход: каретку инспектор не берёт. Показан последний поставленный, а его ручка, отпущенная,
возвращает того, кто был до него, — дизайнер, уступивший место вашему объекту, получит его назад. Код объекта
инспектор зовёт через шов вашего плагина: упавший геттер — пустое значение и сбой на ваш счёт, а не на счёт
студии. Истории у объекта нет: Ctrl+Z в инспекторе ему не шагает. Нужна история — пишите свою цель.

## Своя цель

Цель — как `SerializedObject` и `Editor` у Unity вместе: свойства идентификаторами, типы именами, значения
текстом, запись — пачкой, одним шагом вашей истории. Имена, а не `Type`: цель может жить в поколении типов,
которое заменят, и держать его инспектору нельзя.

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using ArxisStudio.Inspector;

namespace Guide.Inspector.Targets;

public sealed class Layer
{
    public string Name { get; set; } = "Слой";

    public double Opacity { get; set; } = 1;
}

public sealed class LayerTarget(Layer layer, Stack<(double Before, double After)> history) : InspectorTarget
{
    public override InspectorHeader Header => new(layer.Name) { Subtitle = "Слой" };

    public override InspectorTypeInfo Type { get; } = new("Guide.Layer");

    public override IReadOnlyList<InspectorProperty> Properties { get; } =
    [
        new InspectorProperty("Opacity", InspectorTypeInfo.Of(typeof(double)))
        {
            Category = "appearance",
            Constraints = new InspectorConstraints { Minimum = 0, Maximum = 1, Step = 0.01, Decimals = 2 },
            DefaultText = "1",
        },
    ];

    public override InspectorValue ValueOf(string id) => id == "Opacity"
        ? new InspectorValue(layer.Opacity.ToString(CultureInfo.InvariantCulture))
        {
            Source = layer.Opacity == 1 ? InspectorValueSource.Default : InspectorValueSource.Local,
        }
        : InspectorValue.Empty;

    // Пустой текст — сброс к значению по умолчанию. Вся пачка — одна правка, один шаг истории.
    public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null)
    {
        foreach (var write in writes)
        {
            if (write.PropertyId != "Opacity")
                continue;

            var after = write.Text is null ? 1 : double.Parse(write.Text, CultureInfo.InvariantCulture);

            history.Push((layer.Opacity, after));
            layer.Opacity = after;
        }

        Raise(InspectorChanges.Values);

        return Task.FromResult(true);
    }

    public override InspectorCheck Check(string id, string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 1
            ? InspectorCheck.Valid
            : InspectorCheck.Invalid("Прозрачность — от 0 до 1");

    // Ctrl+Z и Ctrl+Y в инспекторе, когда в поле ничего не набрано, — шаг вашей истории.
    public override Task<bool> StepAsync(bool back)
    {
        if (!back || history.Count == 0)
            return Task.FromResult(false);

        layer.Opacity = history.Pop().Before;
        Raise(InspectorChanges.Values);

        return Task.FromResult(true);
    }

    // Слой удалили — цели больше нет: инспектор уберёт её и покажет прежнюю.
    public void Removed() => Close();
}
```

Ставится цель службой — `context.Inspector()?.Show(new LayerTarget(...))`, — и ручку показа отпускают, когда
цель больше не нужна. Перемены цель объявляет сама (`Raise`): значения, состав свойств, шапку; инспектор
переспросит только видные строки. Протяжку числа инспектор показывает предпросмотром, если цель его умеет
(`BeginPreview`), а пишет одним значением при отпускании; недописанное в поле значение держит цель
(`Hold`) — так дизайнер не меняет поколение, пока человек набирает.

## Опишите свойства таблицей

У Avalonia своих разделов нет, у типов проекта, которых у плагина нет, — тем более. Таблица метаданных
дописывает их: раздел, порядок, подпись, пояснение, скрытость, только чтение, границы, шаг, варианты и
украшения (`Header`, `Space`, `Help`). Слова таблицы ложатся поверх атрибутов источника и таблиц модулей —
по полю: что таблица не назвала, остаётся как было.

```csharp
using ArxisStudio.Inspector;
using Avalonia.Controls;

namespace Guide.Inspector.Metadata;

public sealed class CardMetadata : InspectorMetadata
{
    public override void Describe(InspectorMetadataBuilder metadata)
    {
        var strings = Context.Strings;

        metadata.Category("card").Title(strings["category.card"]).Order(15);

        // Тип проекта, которого у плагина нет, — полным именем.
        metadata.For("MyApp.Controls.Card")
            .Category("card", "Elevation", "Corner")
            .Property("Elevation", property => property.Range(0, 24).Step(1).Help(strings["card.elevation"]))
            .Property("Corner", property => property.Choices(
                new InspectorChoice("0") { Title = strings["card.corner.none"] },
                new InspectorChoice("8") { Title = strings["card.corner.soft"] }));

        // Тип Avalonia — типом; свойство — своим AvaloniaProperty.
        metadata.For<Border>()
            .Property(Border.BoxShadowProperty, property => property.Description(strings["shadow.hint"]));
    }
}
```

Варианты решают поле: у текста они — подсказки в меню поля (набрать своё можно), названные единственно
возможными (`ChoicesOnly`) — список, у перечисления — порядок, подписи и значки сегментов. Таблицу инспектор
спрашивает один раз и помнит по именам типов до перемены набора вкладов; переменились ваши данные — позовите
`Invalidate()`.

## Своё поле для типа значения

Редактор строит поле для строки — как `PropertyDrawer`: выбирается он цепочкой от точного к общему —
подсказка свойства, точный тип, обобщённое определение, базы и интерфейсы с `ForDerived`, — а при равной
точности ваш сильнее студийного. Поле у каждой строки своё и пишет только через свой контекст.

```csharp
using System;
using System.Globalization;
using System.Threading.Tasks;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Controls;

namespace Guide.Inspector.Editors;

[PropertyEditor(typeof(TimeSpan))]
public sealed class DurationEditor : PropertyEditor
{
    public override InspectorField CreateField(InspectorFieldContext context) => new DurationField(context);

    private sealed class DurationField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly AxNumberField _milliseconds;
        private IInspectorPreview? _preview;

        public DurationField(InspectorFieldContext context)
        {
            _context = context;
            _milliseconds = new AxNumberField
            {
                Size = AxControlSize.Compact,
                Minimum = 0,
                Step = 50,
                Decimals = 0,
                IsReadOnly = context.Property.IsReadOnly,
            };

            // Протяжка — предпросмотром на цели, запись — одна, при отпускании; Esc — отмена.
            _milliseconds.ValueChanging += (_, e) => (_preview ??= _context.BeginPreview())?.Show(Text(e.Value));
            _milliseconds.ValueCommitted += (_, e) => _ = CommitAsync(Text(e.Value));
            _milliseconds.ScrubCancelled += (_, _) => Cancel();
        }

        public override Control View => _milliseconds;

        public override Control? FocusTarget => _milliseconds;

        // Подпись строки тянет это число, как у Unity.
        public override AxNumberField? ScrubTarget => _milliseconds;

        // Набранное, но не записанное, не затирается значением снаружи.
        public override bool HasDraft => _milliseconds.HasDraft;

        public override void Show(InspectorValue value)
        {
            if (HasDraft && _milliseconds.IsKeyboardFocusWithin)
                return;

            _milliseconds.Value = TimeSpan.TryParse(value.Text, CultureInfo.InvariantCulture, out var duration)
                ? duration.TotalMilliseconds
                : 0;
            _milliseconds.IsMixed = value.IsMixed;
        }

        public override void Release() => Cancel();

        private static string Text(double milliseconds) =>
            TimeSpan.FromMilliseconds(milliseconds).ToString("c", CultureInfo.InvariantCulture);

        private async Task CommitAsync(string text)
        {
            if (_preview is { } preview)
            {
                _preview = null;

                using (preview)
                    await preview.CommitAsync(text);
            }
            else
            {
                await _context.WriteAsync(text);
            }
        }

        private void Cancel()
        {
            _preview?.Dispose();
            _preview = null;
        }
    }
}
```

Поле — на контролах `Ax*`, как всякий интерфейс плагина. Студия ставит его за барьер: упавшая раскладка поля
станет строкой о сбое, а не уронит панель. Свою подсказку редактор берёт атрибутом
`[PropertyEditor(typeof(object), Hint = "…")]`, а свойство просит её таблицей — `property.Editor("…")`.

## Свой интерфейс для типа цели

Раздел — как `CustomEditor` у Unity, только для части свойств: он встаёт своим заголовком, внутрь
описанного раздела (`Category`) или в шапку (`Placement`), берёт себе строки (`Claims`) — в списке их больше
нет — и строит их у контекста теми же, что в списке (`CreateRow`). `ReplaceAll` отдаёт ему цель целиком.

```csharp
using System.Collections.Generic;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Controls;

namespace Guide.Inspector.Sections;

[InspectorSection(typeof(Image), ForDerived = true)]
public sealed class PictureSection : InspectorSection
{
    public override string Title => Context.Strings["picture.title"];

    public override IReadOnlyCollection<string> Claims => ["Source", "Stretch"];

    public override InspectorSectionView CreateView(InspectorSectionContext context) => new PictureView(context);

    private sealed class PictureView : InspectorSectionView
    {
        private readonly InspectorSectionContext _context;
        private readonly StackPanel _panel = new();
        private readonly TextBlock _path = new() { [AxText.ToneProperty] = AxTextTone.Secondary };

        public PictureView(InspectorSectionContext context)
        {
            _context = context;
            _panel.Children.Add(_path);

            foreach (var id in (string[])["Source", "Stretch"])
            {
                if (context.CreateRow(id) is { } row)
                    _panel.Children.Add(row);
            }

            Refresh(new InspectorChangedEventArgs(InspectorChanges.Values));
        }

        public override Control View => _panel;

        // Перемена цели или показ другой цели того же типа: раздел переживает её, как строки.
        public override void Refresh(InspectorChangedEventArgs change) => _path.Text = _context.ValueOf("Source").Text;
    }
}
```

## Пункты меню

Меню строки и меню ⋮ дополняют цель и вклады `InspectorMenu`. Пункт зовётся через шов того, кто его
добавил: упавший пункт засчитан вашему плагину.

```csharp
using ArxisStudio.Inspector;

namespace Guide.Inspector.Menus;

public sealed class ZeroMenu : InspectorMenu
{
    public override void Fill(InspectorMenuContext menu)
    {
        if (menu.Property is not { } property || !property.ValueType.Is("System.Double"))
            return;

        menu.Add(
            Context.Strings["menu.zero"],
            () => _ = menu.WriteAsync([new InspectorWrite(property.Id, "0")]),
            isEnabled: !property.IsReadOnly);
    }
}
```

## Свойства контролов проекта

Контрол из проекта человека описывает свои свойства теми же атрибутами .NET — плагин тут не нужен вовсе.
Дизайнер читает их со свойства CLR, а у присоединённого — с его геттера:

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Avalonia;
using Avalonia.Controls;

namespace Guide.Inspector.Project;

public class Gauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Value));

    public static readonly StyledProperty<string?> InternalKeyProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(InternalKey));

    [Category("layout")]
    [Description("Сколько отмерено")]
    [Range(0, 100)]
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    [Browsable(false)]
    public string? InternalKey
    {
        get => GetValue(InternalKeyProperty);
        set => SetValue(InternalKeyProperty, value);
    }
}
```

`[Browsable(false)]`, `[EditorBrowsable(Never)]`, `[Obsolete]` и `[PrivateApi]` скрывают свойство — его
видно только среди «Всех свойств»; `[EditorBrowsable(Advanced)]` и `[Unstable]` делают дополнительным — его
находит поиск. Раздел автора, совпавший с описанным без учёта регистра, — тот же раздел: `[Category("Layout")]`
встанет в макет.

## Откуда значение

Строка говорит, откуда пришло значение, тоном и подсказкой: заданное здесь — полужирная подпись и полоса
акцента, выражение — жетоном.

| Источник | Что значит |
|---|---|
| `Default` | значение по умолчанию: никто не задавал |
| `Local` | задано у самой цели |
| `Design` | задано для дизайнера (`d:`) |
| `Expression` | привязка или ресурс: значение вычисляется |
| `Style` | стиль, с селектором в уточнении |
| `Template` | шаблон хозяина |
| `Inherited` | унаследовано от предка |
| `Animation` | анимация |
| `Code` | поставлено кодом |

## Выгрузка

Плагин выгружают целиком, и инспектор отпускает всё, что от него получил, раньше, чем выгрузят сборку:
цели и объекты — ручками студии, поля и разделы — прощальной дорогой шва (`Release`). Держать после этого
нечего, но и вам — тоже: отпустите ручки показа, отпишитесь от своих событий в `Release` поля и раздела.

## Чего не делать

- **Не писать на каждом движении.** Протяжка, тяга по спектру — предпросмотр (`BeginPreview`), а запись —
  одна, при отпускании: иначе каждый пиксель — шаг истории.
- **Не писать мимо контекста.** Поле не видит цель и не должно: строка переживает смену цели того же типа, и
  запись уходит той, что показана сейчас.
- **Не держать `Type` и объекты поколения в цели.** Имена типов, пути элементов, текст значений — да; контрол
  формы, `Type` из проекта — нет: они держат поколение, и замена кончится перезапуском.
- **Не брать каретку в показе.** Показ — не переход: человек выбирает на холсте и продолжает там.
- **Не регистрировать свойства Avalonia ради инспектора и не спрашивать `TypeDescriptor` о своих типах.** Реестр
  свойств и конвертеры помнят тип навсегда — сборку плагина станет нельзя выгрузить.

## Что читать дальше

- [xaml.md](xaml.md) — служба разметки, члены элемента и предпросмотр значения, на которых стоит дизайнер;
- [plugin-markup.md](plugin-markup.md) — разметка интерфейса плагина: поля редакторов и разделов — на `Ax*`;
- [design-system.md](design-system.md) — поведение инспектора: клавиатура, шапка, варианты, меню.
