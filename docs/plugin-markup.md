# Разметка в плагинах — что можно и чем платится

Замер от 2026-09-08: студия на `4691e4e`, Avalonia 12.1.1, .NET 10. Дополнен 2026-09-15, запись 186
[плана](plan.md): держатель найден и снят, `{Text}` работает и без `x:Class`. Лежит отдельно от плана
намеренно: это не сделанная работа, а измеренное свойство движка, к которому придётся возвращаться
при каждом разговоре о плагинах.

Вопрос был один: можно ли **полноценно** писать интерфейс плагина разметкой. Запись 83
разметку в расширениях открыла, но два обещания тогда дать отказалась — свои словари
ресурсов и `avares://` — со словами «сперва замер, потом обещание». Замер сделан, и он
дал больше, чем спрашивали.

## Короткий ответ

`.axaml` в плагине работает целиком: собирается, упаковывается, показывается, выглядит как надо
и — с записи 186 — **перезагружается на ходу**, с `x:Class` и без.

До записи 186 плагин, объявивший свой тип-контрола, после показа панели не выгружался, а `x:Class` —
это всегда свой тип-контрола. Держала Avalonia: свойство кэширует свои метаданные для каждого типа,
у которого их спросили, словарём с сильным ключом-типом, а спрашивает оформление. Средство забыть
типы у Avalonia открытое — `AvaloniaPropertyRegistry.UnregisterByModule`, — и студия зовёт его,
выгружая контекст плагина. [Подробности ниже](#кто-держал--найдено-и-снято).

| Дорога | Что даёт разметка | Перезагрузка на ходу |
|---|---|---|
| `.axaml` с `x:Class` | всё: code-behind, `{Text}`, `Click=`, `x:DataType` | есть — с записи 186 |
| `.axaml` без `x:Class`, грузится по URI | стили, словари ресурсов, дерево панели, `{Text}` | есть |
| панель кодом без своих типов-контролов (как `HelloPanel`) | ничего | есть |

Встроенных модулей всё это не касается: они живут в основном контексте и не выгружаются
по определению. `Modules.Terminal` и `Modules.Sample` написаны разметкой и правы.

## Что работает — проверено сборкой и живой студией

Плагин собран из [шаблона](../templates/Arxis.Plugin) вне репозитория и поставлен в
студию; ему добавлено всё, чего разметке может захотеться.

- **Компиляция даётся бесплатно.** `buildTransitive/Avalonia.props` доезжает до плагина
  через `ProjectReference → SDK → PackageReference Avalonia`, `AvaloniaXaml
  Include="**\*.axaml"` — item по умолчанию, `InitializeComponent` пишет генератор
  Avalonia. Своей строки в csproj не нужно.
- **`avares://` работает** — вопреки оговорке записи 83. Свой файл стилей
  (`StyleInclude`), свой словарь ресурсов (`ResourceInclude` в `MergedDictionaries`),
  своя картинка (`Image Source`), `AssetLoader.Open` / `Exists` / `GetAssets`.
  Загрузчик ресурсов Avalonia 12.1.1 ищет сборку по простому имени **среди всего, что
  загружено в процессе**, а не через `Assembly.Load` в основном контексте: в одном и том
  же замере `Assembly.Load("PluginLib")` падает с `FileNotFoundException`, а
  `avares://PluginLib/...` в тот же миг отдаёт содержимое. Именно этого запись 83 и
  боялась — опасение оказалось неверным.
- **Привязки.** `x:DataType` + `{a:Binding}` компилируются. Без `x:DataType` —
  `AVLN2100: Cannot parse a compiled binding without an explicit x:DataType directive`,
  и текст говорит сам за себя.
- **`ARX0006` видит разметку плагина.** Посаженные `<a:Button/>` и `<a:TextBox/>` дали
  два замечания с названными заменами. Файлы анализатору отдаёт сама Avalonia
  (`_InjectAvaloniaAdditionalFiles`), своей строки не нужно и здесь.
- **Упаковка не требует ничего.** Разметка и `AvaloniaResource` уезжают внутри сборки —
  скомпилированный XAML плюс ресурс `!AvaloniaResources`; в `package/` по-прежнему
  только `plugin.json`, `bin/`, `lang/`, `assets/`, `README.md`.
- **Живьём:** панель встала в объявленную манифестом зону, подпись красная и жирная — из
  своего файла стилей по `avares://`, полоса зелёная — из своей кисти оттуда же,
  картинка своя. `get_problems` со `scan: true` — 469 элементов, 1044 привязки, ни одной
  сломанной.

## Цена, которая была: перезагрузка

Замер 2026-09-08. Живьём, дважды из двух нажатий «Плагины → Перезагрузить»:

```
WARN Plugins  Xaml Probe: прежняя копия осталась в памяти — надёжнее перезапустить студию
```

Соседний `Hello` в том же сеансе перезагружался чисто — и до, и после. Утечка не
расползалась: она принадлежала тому плагину, который её завёл.

Бисекция на живой студии, по одному изменению за перезапуск:

| Панель плагина | Перезагрузка |
|---|---|
| разметка + `avares://` (стиль, словарь, картинка) | **утечка** |
| разметка, `avares://` нет вовсе | **утечка** |
| разметки нет, панель кодом, но свой класс `PluginPanelView : AxUserControl` | **утечка** |
| панель кодом без своего типа-контрола (как `HelloPanel`) | выгружается чисто |

То же вне студии, на голом Avalonia — ни одной строки студии в процессе, только
коллекционируемый `AssemblyLoadContext`, теневая копия и `WeakReference` после
`Unload` и двенадцати циклов сборки:

```
=== создан, но не показан ===
nothing touched at all              leaked: False
a plain object of the plugin        leaked: False
a code-built Avalonia control       leaked: False
an x:Class control from .axaml      leaked: False
StyleInclude+ResourceInclude        leaked: False
an avares:// asset opened           leaked: True
avares:// then InvalidateAssembly.. leaked: False
Image Source=avares://              leaked: True
the same, then InvalidateAssembly.. leaked: False

=== показан в окне, со стилями и раскладкой ===
no plugin control type (Factory)    leaked: False
a plugin control type (CodeBuilt)   leaked: True
a plugin control type from .axaml   leaked: True
```

Вывод тогда был верный, но неполный: держит **Avalonia**, а не студия, и держит тип-контрол,
побывавший на экране; созданный и не показанный — выгружается. Какое именно поле, названо не было:
обход статических полей Avalonia на глубину 6 ключей-типов из сборки плагина не нашёл.

## Кто держал — найдено и снято

Замер 2026-09-15, новый стенд того же устройства (Fluent-тема, сброс кэша загрузчика ресурсов перед
выгрузкой, как в студии). Обход графа объектов от статических полей всех сборок основного контекста
— с заходом в массивы структур и в зависимые дескрипторы — нашёл дорогу к типу плагина:

```
Avalonia.Visual::IsVisibleProperty : StyledProperty<bool>
  _metadataCache : Dictionary<Type, AvaloniaPropertyMetadata>
  _entries : Dictionary<Type, AvaloniaPropertyMetadata>.Entry[]
  [6].key : Type PluginLib.CodeBuilt
```

`AvaloniaProperty.GetMetadata(Type)` запоминает ответ для каждого типа, у которого его спросили, и
ключ держит сильно. Спрашивает оформление, поэтому созданный и не показанный контрол не держится, а
показанный — держится. Проба, снявшая ключи-типы плагина со всех таких словарей, назвала свойства,
которые держали тип в Fluent-теме: `Border.Background`, `Visual.ClipToBounds`, `Visual.IsVisible`,
`TemplatedControl.Template`. После снятия выгрузились и контрол, собранный кодом, и контрол из
разметки с `x:Class` — три прогона из трёх. Почему обход 2026-09-08 этот словарь не нашёл, установить
нечем: его кода не осталось. Нынешний заходит в массив записей словаря — туда, где ключ и лежит.

**Средство у Avalonia открытое:** `AvaloniaPropertyRegistry.Instance.UnregisterByModule(типы)`
забывает по списку типов их регистрации, переопределения метаданных и кэши. С ним выгружаются все
дороги, два прогона из двух, без единого обращения к внутренностям:

```
RESULT codebuilt [unregister] leaked: False
RESULT xamlclass [unregister] leaked: False
RESULT noclass   [unregister] leaked: False
RESULT plain     [unregister] leaked: False
```

Студия зовёт его в `PluginLoadContext.Release()` сразу после сброса кэша загрузчика ресурсов — на
общем шве всех трёх дорог выгрузки, по типам всех сборок контекста: приватная зависимость плагина
тоже может завести свой контрол. Держит правку
`PluginTeardownTests.A_plugin_whose_control_was_on_screen_still_unloads`: без вызова он падает.

**Живьём.** Плагин из шаблона — панель с `x:Class`, как её пишет шаблон, и вторая, без него. Студия
без вызова: после показа панели и «Перезагрузить · Xaml Probe» — прежнее
`прежняя копия осталась в памяти`. С вызовом: две перезагрузки подряд, панель показана перед каждой,
предупреждений ноль, соседний `Hello` перезагружается как прежде.

**Ловушка стенда.** В безголовом режиме таймер отрисовки стоит, и пачка композитора с закрытым окном
держит его содержимое до первого тика: обход находил дорогу
`Compositor._pendingBatch → ServerCompositionTarget → PresentationSource → Window → CodeBuilt`, и опыт
то утекал, то нет. Живая студия отрисовывает сама; на стенде и в тесте отрисовку прокручивают
`AvaloniaHeadlessPlatform.ForceRenderTimerTick()`.

## Дорога без `x:Class`

Ради перезагрузки она больше не нужна, но работает и нужна другому: словарю ресурсов, стилям,
разметке, которую плагин грузит по адресу сам. Корень такого дерева принадлежит Avalonia:

```
x:Class control from .axaml            leaked: True   (до записи 186)
.axaml with no x:Class, loaded by URI  leaked: False
       root is Avalonia.Controls.StackPanel, from Avalonia.Controls
```

Чем эта дорога платит:

- Нет `InitializeComponent` и нет code-behind — панель разбирает полученное дерево по
  именам.
- Нет `Click="OnHelloClick"`: обработчик без `x:Class` привязать некуда, цеплять кодом.

**`{Text ключ}` здесь работает с записи 186.** До неё не работал: расширение узнавало плагин по сборке
корня разметки ([`TextExtension.ProvideValue`](../src/ArxisStudio.Sdk/Extensibility/TextExtension.cs)),
а корень здесь — Avalonia. Живьём, в панели без класса пробного плагина: `!panel.hint!`. Щуп на стенде
показал, что знает о себе разметка на трёх дорогах:

```
x:Class:     root = PluginLib.XamlClass [PluginLib],        provider = XamlIlContext+Context<XamlClass> [PluginLib]
без класса:  root = StackPanel [Avalonia.Controls],          provider = XamlIlContext+Context<StackPanel> [PluginLib]
шаблон в словаре ресурсов: root = ResourceDictionary [Avalonia.Base], provider = ... [PluginLib]
```

Контекст, который скомпилированная разметка передаёт расширению, объявлен в её собственной сборке.
Теперь расширение берёт сборку корня, а если корень не из расширения — сборку разметки; простое имя
из адреса не нужно, и двум плагинам с одинаковым именем сборки перепутать словари нечем. Живьём та же
панель показала «Отсюда начинается ваш плагин». Держит
`TextExtensionTests.Markup_without_a_class_takes_captions_from_its_own_assembly` — на разметке,
скомпилированной сборкой тестов и загруженной по адресу; без правки он падает.

## Отдельно: кэш загрузчика ресурсов — поставлено

Открытый `avares://`-ассет (картинка, любой файл из `!AvaloniaResources`) прибивает
сборку плагина в кэше загрузчика ресурсов Avalonia — по простому имени. Это **вторая,
ненужная** причина той же беды, и, в отличие от первой, она наша:

```
an avares:// asset opened           leaked: True
avares:// then InvalidateAssembly.. leaked: False
```

Стоит с записи 115: `AssetLoader.InvalidateAssemblyCache(имя)` в
[`PluginLoadContext.Release()`](../src/ArxisStudio.Extensibility/PluginHost.cs) — до
`Unload()`, по именам сборок контекста. Место общее для всех трёх дорог выгрузки —
прощание поднятого плагина, сбой загрузки сборки, сбой активации, — а не у одной из
них: плагин, упавший на подъёме, тоже успевает тронуть ресурсы. `StyleInclude` и
`ResourceInclude` так не делают: они идут не через ассеты, а через скомпилированный
XAML.

**Ловушка шире, чем здесь было написано.** Замер записи 115: попасть в кэш хватает
одного **вопроса** про `avares://`-адрес с именем сборки — своих ресурсов у неё может
не быть вовсе. `Exists`, ответивший «такого ресурса нет», сборку в кэше уже оставил:

```
тронули avares (Exists = False), кэш не сбрасывали  -> УТЕЧКА
тронули avares (Exists = False), кэш сбросили       -> выгрузился чисто
avares не трогали вовсе                             -> выгрузился чисто
```

Оттуда же вторая половина: без сброса кэша `GetAssembly` после перезагрузки отдаёт
**прежнюю** копию сборки — новый плагин получил бы ресурсы старого.

```
1. кэш не сбрасывали                          -> ПРЕЖНЯЯ копия
2. сбросили, но прежняя ещё в процессе        -> ПРЕЖНЯЯ копия
3. сбросили, и прежнюю уже собрали             -> НОВАЯ копия
```

Беда была самоподдерживающейся: кэш держит сборку живой, живая находится по простому
имени первой, и второй случай воспроизводился бы на каждой следующей перезагрузке.
Порядок «сперва забыть, потом выгрузить» её разрывает. Живьём она не выстреливала
только потому, что [`ReleasedAll`](../src/ArxisStudio.Extensibility/PluginHost.cs)
крутит сборку мусора до подъёма новой копии, — то есть порядок в студии был уже
правильный, и не хватало ровно сброса.

Держит правку `PluginTeardownTests.A_plugin_seen_by_the_asset_loader_still_unloads`:
без сброса он падает.

Плагину с `x:Class` одна эта правка помочь не могла: его держали ещё и свойства Avalonia, и снял их
только вызов записи 186. Замер 2026-09-15 это и разделил: без сброса кэша утекала и разметка без
класса, со сбросом, но без вызова — только свой тип-контрол.

## Мелочи, которые стоит сказать автору плагина

- **Расширения разметки Avalonia в плагине пишутся с префиксом:** `{a:DynamicResource}`,
  `{a:Binding}`. Адрес по умолчанию у панели плагина наш, и `DynamicResource` под ним не
  объявлен — `AVLN2000: Unable to resolve type DynamicResource from namespace
  https://github.com/Arxis-Team/ArxisStudio`. В README шаблона правило про префикс
  сказано **про контролы**, а из расширений показан только свой `{Text}`, который
  работает без префикса; то есть первое, что автор напишет по совету того же README
  («привязывайтесь к модели»), сорвёт сборку. Свои модули эту дорогу уже прошли:
  `{a:Binding Project}` в `SamplePanelView.axaml`, `{a:DynamicResource}` в терминале.
- **Хозяин `avares://` — простое имя сборки, одно на процесс.** Два плагина со сборками
  одного простого имени разъезжаются молча: в замере два контекста с `PluginLib`,
  `GetAssembly` отдаёт первый загруженный. Уникальность имени сборки стоит требовать
  словами.
- `Assets/` (под avares) и `assets/` (папка пакета) на Windows совпадают, и файл уезжает
  дважды — и внутрь сборки, и в пакет; на Linux и macOS не совпадут. Ресурсы разметки
  лучше держать не в `assets/`.
- **Объявления `xmlns` — только в корне разметки.** Объявленный на вложенном элементе
  `xmlns:x` ломает сборку: `AVLN2000: xmlns declarations are only allowed on the root element`.

## Значения темы — договор, а не совет

С SDK 5.3 это правило, и проверяет его сборка плагина. Раздел стоит особняком от замера
выше: там измеренное свойство движка, здесь — обещание, которое студия даёт расширению и
которого ждёт от него.

**Размер и цвет называют ресурсом темы, а не числом.** Число не переключается вместе с
темой и не сжимается вместе с плотностью: панель плагина остаётся тёмной в светлой студии
и просторной в плотной, а рядом стоят панели самой студии, которые переключились.

**Цвет темы — роль, и у роли два ключа.**

| Ключ | Пример | Кто называет |
|---|---|---|
| Цвет | `AxAccentColor`, `AxTextPrimaryColor` | там, где свойство ждёт `Color`: `SolidColorBrush.Color`, рисование в коде |
| Кисть | `AxAccentBrush`, `AxTextPrimaryBrush` | разметка: `Foreground`, `Background`, `BorderBrush`, `Fill`, `Stroke` |

**С SDK 6.0 палитра называет роль, а не место в ряду.** Значения не сдвинулись, сдвинулись
имена, и ключ 5.x в студии 6.0 не разрешается — кисть молча остаётся пустой. Ступеней шкал
(`AxGray1`, `AxBlue6`) в словаре больше нет вовсе. Прежнее имя при сборке называет `ARX0009`
вместе с новым; порядок переименования такой:

| До 6.0 | С 6.0 |
|---|---|
| `AxBg1`, `AxInp` · `AxBg2` · `AxBgSunken` | `AxSurfaceBase` · `AxSurfacePanel`, во всплывающем `AxSurfaceOverlay` · `AxSurfaceSunken` |
| `AxBg3` · `AxBg4` · `AxInpDisabled` | `AxHover`, у приподнятой плашки `AxSurfaceRaised` · `AxPressed`, у дорожки `AxTrack` · `AxFillDisabled` |
| `AxBrd` · `AxBrd2` | `AxStrokeSubtle` · `AxStrokeControl` |
| `AxFg` · `AxFg2` · `AxFg3` · `AxFgDisabled` · `AxOnAcc` | `AxTextPrimary` · `AxTextSecondary` · `AxTextTertiary` · `AxTextDisabled` · `AxTextOnAccent` |
| `AxAcc`, `AxAccHover` · `AxAccStrong*`, `AxAccPressed` | `AxAccent`, `AxAccentHover` · `AxAccentFill*`, `AxAccentFillPressed` |
| `AxSel` · `AxSelInactive` · `AxOutlineFocused` | `AxSelectionActive` · `AxSelectionInactive` · `AxFocusRing` |
| `AxRed`, `AxYel`, `AxGrn` и их `*Text` | `AxError`, `AxWarning`, `AxSuccess` и их `*Text` |
| `AxInfoBackground`, `AxInfoBorder` и соседи · `AxOutlineError`, `AxOutlineWarning` | `AxInfoFill`, `AxInfoStroke` и соседи · `AxErrorOutline`, `AxWarningOutline` |
| `AxOrg`, `AxPur` · `AxLinkOn` · `AxTooltip*` · `AxCodeFg`, `AxCodeAttr` | `AxTintOrange`, `AxTintPurple` · `AxLinkOnPlate` · `AxToolTipFill`, `AxToolTipStroke` · `AxCodeText`, `AxCodeAttribute` |
| `AxPopupShadow`, `AxModalShadow` | `AxShadowPopup`, `AxShadowModal` |

Расстояния — шкала `AxSpaceHair 2 · AxSpaceTight 4 · AxSpaceSnug 6 · AxSpace 8 ·
AxSpaceWide 12 · AxSpaceLoose 16 · AxSpaceSection 24 · AxSpaceScreen 40`, у каждой ступени
вторая форма `…Thickness` для `Margin` и `Padding`, направленные зазоры
(`AxSpaceSnugTrailingThickness` — `0,0,6,0`) и смысловые имена (`AxGapIconText`,
`AxGapControls`, `AxGapFormRow`, `AxGapGroup`). Кегли — `AxFontSize` и его соседи.

**Три правила, и каждое называет ключ, а не просто запрещает.**

- `ARX0008` — число в разметке: `Spacing="8"` → «8 — это AxSpace»; `Spacing="10"` → «10 — не
  ступень шкалы; ближайшие — AxSpace (8) и AxSpaceWide (12)»; `Foreground="#5A8FF3"` → «это цвет
  AxAccentBrush или AxFocusRingBrush». Цвет, которого в теме нет, правило не трогает: своя палитра графика законна.
- `ARX0009` — ресурс назван не тем именем: цвет там, где свойству нужна кисть, или имя темы до
  SDK 6.0. `{a:DynamicResource AxAccentColor}` в `Foreground` не разрешится в кисть и не нарисует
  ничего, и узнать об этом без правила можно только глазами; `{a:DynamicResource AxFg3Brush}` →
  «AxFg3Brush — имя темы до SDK 6.0; теперь это AxTextTertiaryBrush». Прежнее имя правило
  находит и в коде, в любой строке: `GetResourceObservable("AxFg3Brush")` ломается так же молча.
- `ARX0010` — то же в коде: `new Thickness(…)` из чисел, число в `Spacing` и `FontSize`
  контролов Avalonia, `Color.Parse("#…")` и `Brush.Parse("#…")`.

**Чего правила не спрашивают.** Ширины и высоты — канва плагина в 137 пикселей его дело.
Отступ меньше двух — поправка на пиксель, а не зазор. Цвет, собранный из байтов
(`Color.FromRgb`), — так строятся палитры со своей жизнью: схема терминала, запасные цвета
на случай темы без ключа.

**В коде значение темы берут привязкой.** Это то же, что `{DynamicResource}` в разметке, и
так же идёт за темой и плотностью:

```csharp
intro.Bind(TextBlock.FontSizeProperty, intro.GetResourceObservable("AxFontSizeSmall"));
body.Bind(StackPanel.SpacingProperty, body.GetResourceObservable("AxGapFormRow"));
```

Так написан пример `Arxis.HelloPlugin`, и так пишет шаблон. Плагин, назвавший ключ шкалы,
объявляет в манифесте `sdk.min` не ниже 5.3: в студии без шкалы ресурс не разрешится, и
отступ молча станет нулевым. Плагин, назвавший роль палитры, — не ниже 6.0: в студии 5.x
у роли было другое имя. Высоты хрома (`AxTabHeight`, `AxTitleBarHeight`,
`AxStatusBarHeight`, `AxMenuRowHeight`, `AxToolbarButtonSize`, `AxWindowButtonWidth`), шаг
лестницы дерева (`AxTreeIndent`) и доля высоты строки (`AxLineHeightRatio`) появились в
6.1 — панель, назвавшая их, требует не ниже.

## Открытые концы

Три прежних закрыты записью 186:

1. ~~Какое именно поле Avalonia держит тип-контрол.~~ `AvaloniaProperty._metadataCache`; снимается
   открытым `AvaloniaPropertyRegistry.UnregisterByModule`, у себя, без разговора с Avalonia.
2. ~~Отваливается ли `{Text}` на дороге без `x:Class`.~~ Отваливался — живьём `!panel.hint!`; теперь
   берёт сборку разметки.
3. ~~Решение не принято.~~ Принимать нечего: обещания «плагин перезагружается на ходу» и «панель
   написана разметкой» больше не исключают друг друга.

Нового открытого одно, и студию оно не держит: кэш метаданных Avalonia с сильными ключами-типами —
кандидат в замечание самой Avalonia. Пока `UnregisterByModule` на месте, студии этого хватает. Пропади
он при обновлении Avalonia — не соберётся студия; перестань он снимать кэш — упадёт тест выгрузки.

Закрыто раньше: **сброс кэша загрузчика ресурсов** — поставлен записью 115 и держится тестом.

## Как перепроверить

**Живьём.** Собрать плагин из шаблона вне репозитория
(`dotnet build -p:ArxisStudioPath=...`), выложить его
`-p:AxPluginDeploy=true`, поднять студию **собранным exe** с перехваченным stdout
(журнал `StudioLog` пишется в `Console.Out`), дойти щелчком до каркаса, открыть панель плагина,
нажать «Меню команд → Плагины → Перезагрузить · …» и смотреть журнал на строку «прежняя копия
осталась в памяти». Порядок работы с MCP — в [devtools.md](devtools.md). Выложенный плагин за собой
убирают: он лежит в данных пользователя.

**На стенде.** Две сборки: библиотека-«плагин» со ссылкой только на Avalonia (контрол кодом,
`.axaml` с `x:Class` и без, словарь ресурсов с шаблоном) и консольный хозяин на `Avalonia` +
`Avalonia.Headless` + `Avalonia.Themes.Fluent`, поднимающий
`AppBuilder.Configure<App>().UseHeadless(...).SetupWithoutStarting()`. Пакеты берутся из локального
кэша: `nuget.config` рядом со стендом с единственным источником `%USERPROFILE%\.nuget\packages`, и
стенд не скачивает ничего. Дальше на каждый опыт: свой коллекционируемый `AssemblyLoadContext`,
теневая копия файла, `WeakReference(alc, trackResurrection: true)`, одно действие, три
`ForceRenderTimerTick()`, сброс кэша загрузчика ресурсов, `Unload()` и до двенадцати циклов
`GC.Collect()` + `WaitForPendingFinalizers()`. Каждый опыт — в своём методе с
`[MethodImpl(MethodImplOptions.NoInlining)]`, иначе ссылка, оставшаяся в кадре
вызывающего, покажет утечку, которую сама же и создала (тот же приём, что у
`PluginHost.Retire`). Сборка хозяина — в Release: в Debug локальные переменные живут до
конца метода. Держателя ищут обходом от статических полей: массивы структур и `DependentHandle`
обходятся тоже — в записях словаря ключ и лежит.
