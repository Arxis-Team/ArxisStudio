# ArxisStudio — план разработки (v1)

> Статус: согласован 2026-08-24. Разработка начинается по команде «приступай».

ArxisStudio — кроссплатформенный «движок разработки ПО»: Unity, но для прикладных
приложений. Референс UI — мокап `ArxisStudio.dc.html` (Claude Design, проект
`9b997186-4b89-4743-b752-1d385beb684a`): три экрана — Welcome, Editor, Run/Debug,
тёмная и светлая темы, JetBrains-подобная стилистика. Полная выжимка дизайна —
токены обеих палитр, шрифты, метрики, иконография, анимации, инвентарь экранов —
в [design-spec.md](design-spec.md); это входные данные M0.

## Стек

- .NET **net10.0**, **Avalonia 12.1.x** (мокап упоминает «Avalonia 11.3 / net9.0» — это
  декоративный текст, не требование).
- Текстовый редактор XAML — **AvaloniaEdit**.
- Отладка интерфейса самой студии — **AvaDevTools** (Debug-only, с конечной точкой MCP
  на петле): [devtools.md](devtools.md).
- Существующие и новые базовые библиотеки подключаются как **git-субмодули** с ProjectReference:
  - `ArxisStudio.Markup` — lossless XAML DOM, загрузка живых объектов, round-trip;
  - `ArxisStudio.ProjectSystem` — модель solution, MSBuild-операции, NuGet, watcher,
    адаптер `ProjectSystem.Markup.Xaml` (даёт `XamlLoadEnvironment`);
  - `ArxisStudio.DesignEditor` — канва: viewport, выделение, snapping, направляющие
    (сейчас net8.0 — при интеграции поднять до net10.0);
  - `ArxisStudio.Controls` — **новый репо**: библиотека контролов студии (см. ниже);
  - `ArxisStudio.Themes.Arxis` — **новый репо**: тема студии (dark/light по токенам мокапа).

## Согласованные решения

| Вопрос | Решение |
|---|---|
| Editor-код в проекте пользователя (Unity `Editor/`) | **Не в v1.** Только внешние плагины; SDK проектируется так, чтобы project editor-код добавлялся позже без слома |
| Изоляция расширений | **In-proc**, каждый внешний плагин в collectible `AssemblyLoadContext` |
| Модульность студии | **Всё через общий SDK-контракт, два способа загрузки**: встроенные модули (панели студии) — прямые ProjectReference в default ALC, ноль оверхеда; внешние плагины — collectible ALC с выгрузкой. Код модуля переносим между режимами без изменений |
| Стиль SDK | **Атрибуты, как Unity**: `[MenuItem]`, `[CustomInspector]`, `[PropertyDrawer]`, класс-наследник `ToolWindow` |
| Докинг | **Своя лёгкая раскладка**: фиксированные зоны (left/right/bottom/center) + сплиттеры и сворачивание; полный drag&drop-докинг — позже |
| Скоуп v1 | **Welcome + Editor**. Экран Run/Debug (runtime-агент, watch, hot reload) — этап 2. В v1 — сборка через ProjectSystem и простой запуск процесса с логом stdout в Console |
| Новые библиотеки | Проекты **внутри главного репо** (`src/`); выделение в отдельные репо — позже, когда API устоится |
| Шаблоны проектов | **Не пишем свои**: обнаруживаем установленные `dotnet new` шаблоны (template engine), создаём проект через них; Avalonia-шаблоны — через `dotnet new install Avalonia.Templates` |
| Плагины v1 | Менеджер с **локальной установкой** (папка/zip с манифестом), вкл/выкл, список установленных. Онлайн-каталог — этап 2 |
| Типы проектов | **Avalonia desktop**. Любой .sln открывается через ProjectSystem, но дизайнер и запуск гарантируются только для Avalonia |
| Язык UI | Как в мокапе (англ. термины панелей + русский текст) поверх **i18n-каркаса** с первого дня |
| XAML-вкладка | **Да**: Design / XAML / Split на AvaloniaEdit с двусторонним round-trip через Markup; подсветка без IntelliSense |
| Learn | Статические ссылки на документацию; курс с прогрессом — позже |
| Контролы и темы | Пара **ArxisStudio.Controls + ArxisStudio.Themes.Arxis** (отдельные репо-субмодули), по образцу Avalonia.Controls / Avalonia.Themes.Fluent. Плагины строят UI **только на Ax\*-контролах** — как в Unity |
| Устройство Controls | **Наследники Avalonia-контролов** (`AxButton : Button`, …) со своими шаблонами + чисто IDE-шные контролы с нуля (сегменты, toolbar, поле свойства, tool-window chrome, дерево) |
| Запрет голых Avalonia-виджетов | Layout-панели Avalonia разрешены; виджеты — только Ax\*. Контроль: **Roslyn-анализатор в SDK** (ошибка/предупреждение при сборке плагина) + предупреждение хоста при загрузке |
| API субрепозиториев | **Можно менять.** Библиотеки из субмодулей (Markup, ProjectSystem, DesignEditor, Controls, Themes.Arxis) — не замороженные зависимости: если интеграции нужен новый или изменённый публичный API, правим его прямо в субмодуле (соблюдая правила репозитория: тесты публичной поверхности, ADR, границы ответственности) и коммитим в его репо |

## Состав решения

```
ArxisStudio/                        ← главный репо (Avalonia-приложение + новые библиотеки)
  external/                         ← git-субмодули
    ArxisStudio.Markup/
    ArxisStudio.ProjectSystem/
    ArxisStudio.DesignEditor/
    ArxisStudio.Controls/           ← новый репо: контролы студии (Ax*)
    ArxisStudio.Themes.Arxis/       ← новый репо: тема студии (dark/light)
  src/
    ArxisStudio.Sdk/                ← контракт расширяемости (аналог UnityEditor API)
    ArxisStudio.Extensibility/      ← хост плагинов
    ArxisStudio.Inspector/          ← property grid / drawers
    ArxisStudio.Shell/              ← каркас окна: зоны, темы, меню, настройки, i18n
    Modules/                        ← встроенные модули (Hierarchy, Inspector-панель, Console…)
    ArxisStudio/                    ← приложение: Welcome, Editor, composition root
  tests/
```

### ArxisStudio.Controls (отдельный репо)

Библиотека контролов студии — то, из чего строится весь UI ArxisStudio и плагинов,
аналог `Avalonia.Controls` в экосистеме Avalonia. Плагины **не используют** голые
Avalonia-виджеты — как в Unity, где редакторный UI строится только из контролов Unity.

- Основной приём — наследники Avalonia-контролов со своим API и своими theme-ресурсами:
  `AxButton`, `AxTextBox`, `AxCheckBox`, `AxComboBox`, `AxToggleSwitch`, `AxSlider`,
  `AxTabControl`, `AxListBox`, `AxTreeView`, `AxScrollViewer`, `AxMenu`…
- Чисто IDE-шные контролы с нуля: `AxSegmentedControl` (Design/XAML/Split, Dark/Light),
  `AxToolBar`, `AxToolWindowChrome`, `AxPropertyField`, `AxSearchField`, `AxBadge`,
  `AxToolbarToggle`, `AxStatusBar`, `AxColorSwatch` и т.п.
- Layout-примитивы Avalonia (Grid, StackPanel, DockPanel, Border…) разрешены как есть.
- Контролы lookless: шаблоны и цвета живут в теме, не в контроле.
- Набор растёт по потребностям экранов студии; в репо — gallery/demo-приложение.
- Зависимости: только Avalonia.

Контроль запрета: Roslyn-анализатор (поставляется с SDK) — диагностика при использовании
Avalonia-виджетов в коде/разметке плагина; хост при загрузке плагина может предупреждать.

### ArxisStudio.Themes.Arxis (отдельный репо)

Тема студии, аналог `Avalonia.Themes.Fluent`: ControlTheme-шаблоны для всех
Ax*-контролов + палитры **dark/light по токенам мокапа** (`--bg1…bg4`, `--brd`,
`--fg…`, `--acc`, `--sel`, семантические цвета) как Avalonia-ресурсы с переключением
варианта темы. Зависит от ArxisStudio.Controls. Позже возможны альтернативные темы.

### ArxisStudio.Sdk

Только контракт, без реализации. Плагины ссылаются на него, на ArxisStudio.Controls
и на Avalonia (layout). Публичная поверхность SDK в сигнатурах использует Ax*-контролы.

- Атрибуты: `[MenuItem("Tools/…")]`, `[CustomInspector(typeof(T))]`,
  `[PropertyDrawer(typeof(T))]`, `[ToolWindow(...)]`.
- Базовые классы: `ToolWindow`, `InspectorEditor`, `PropertyDrawer`.
- Roslyn-анализатор «только Ax*-виджеты» — поставляется вместе с SDK.
- Сервисы (получаются через `IStudioContext`): `ISelectionService`, `IWorkspaceService`
  (обёртка над ProjectSystem-снапшотом), `IDocumentService`, `ICommandService`,
  `IUndoService`, `ILogService`, `ISettingsService`.
- **Точки расширения без UI** (невизуальные плагины/модули):
  - `StudioService` — сервис плагина с жизненным циклом Start/Stop, регистрируется
    атрибутом (аналог `[InitializeOnLoad]` в Unity) и contribution `services`;
  - события-хуки студии: открытие/закрытие проекта, открытие/сохранение документа,
    до/после сборки, изменение выделения, изменения файлов (watcher ProjectSystem);
  - `IBackgroundTaskService` — фоновые задачи с отменой и прогрессом; прогресс и
    уведомления идут в стандартные слоты студии (статус-бар, тосты, Console) — плагин
    без собственного UI остаётся видимым пользователю;
  - события активации для этого: `onProjectOpened`, `onFileSaved:<ext>`, `onBuild`, …
- **Визуальное и невизуальное свободно совмещаются**: «тип» плагина не существует —
  плагин есть набор contributions, и один манифест может объявлять и фоновый сервис,
  и панели, и команды (пример: git-плагин = watcher-сервис + окно лога + команды).
  Части одной сборки разделяют состояние напрямую; ленивая активация — по объединению
  событий активации. Правила общие: UI-поток не блокировать (фон — через
  `IBackgroundTaskService`), правки документов — транзакциями `IUndoService`.
  Плагин-библиотека, публикующий API для других плагинов, требует зависимостей
  плагин-от-плагина — этап 2.
- Модель манифеста плагина.

Соответствие Unity-практикам:
- **Tools Engineering** → `[MenuItem]` + `ToolWindow` + сервисы;
- **Editor Extensibility** → contributions плагина: меню, tool windows, типы документов;
- **Custom Inspecting / Property Drawing** → `[CustomInspector]` (замена инспектора для
  типа контрола) и `[PropertyDrawer]` (редактор значения для типа свойства);
- **Plugin Development** → манифест + локальная установка + вкладка Plugins.

### ArxisStudio.Extensibility

- `plugin.json`: id, name, version, publisher, требуемая версия SDK, entry-сборка,
  **декларативные contributions** (commands, menus, toolWindows, fileTypes, settings) и
  **события активации** (`onCommand:…`, `onFileType:…`, `onToolWindow:…`, `onStartup`).
  Формат каталога и манифеста — в приложении A.
- Два режима загрузки за одним контрактом:
  - **встроенные модули** — default ALC, прямые ProjectReference, без изоляции;
  - **внешние плагины** — collectible ALC с выгрузкой (отключение/обновление без
    рестарта студии).
- Ленивая активация: меню и списки хост строит по манифесту, не загружая сборок;
  сборка плагина грузится при первом событии активации. Ленивость распространяется и
  на встроенные модули (Hierarchy активируется при открытии проекта, а не на старте).
- Дистрибутив внешнего плагина — каталог, упакованный в zip с расширением
  **`.axplugin`**; установка = распаковка в папку плагинов, удаление = удаление папки.
  Состояние вкл/выкл — в настройках студии, каталог плагина после установки неизменяем.
- MSBuild-таргет упаковки для авторов плагинов: раскладывает выход сборки в формат
  каталога, не кладёт в `bin/` общие контракты (Sdk, Controls, Avalonia) и
  предупреждает, если их добавили руками; умеет класть результат прямо в папку
  плагинов для отладки.
- Зарезервировано в манифесте, реализация в этапе 2: зависимости плагин-от-плагина
  (`dependencies`), подпись/доверие пакетов.
- Резолвер: общие контракты (Sdk, ArxisStudio.Controls, Avalonia) всегда из
  default-контекста — одна идентичность типов, без дублей в памяти.
- Сканирование атрибутов с кэшем между запусками; активация/деактивация; изоляция
  ошибок (упавший плагин отключается, студия живёт).
- Правило производительности: SDK-события — для редких фактов (выделение изменилось,
  документ сохранён); per-frame пути (жесты, рендер) не выходят за границы контрола.

#### Обработка сбоев плагинов

- Все вызовы хост→плагин (активация, команды, построение окон, drawer'ы) идут через
  шов Extensibility с перехватом: исключение логируется с атрибуцией к плагину,
  показывается уведомление; повторные сбои → плагин помечается неисправным и
  автоматически отключается (внешний — с выгрузкой ALC).
- Контент ToolWindow вставляется в дерево не напрямую, а через защитный контейнер
  Shell, перехватывающий исключения layout-прохода: упавшая панель заменяется
  заглушкой «панель аварийно завершилась · подробности · Reload», остальная студия
  работает (Avalonia сама контролы не изолирует).
- Глобальные обработчики: необработанные исключения UI-потока и unobserved Task —
  ловим, атрибутируем к плагину по стеку/ALC, логируем, помечаем плагин.
- Правки документов — транзакциями `IUndoService`: исключение посреди правки
  откатывает её, документ не остаётся полуизменённым.
- От фатальных сбоев процесса (StackOverflow, OOM, native-краш, зависание UI-потока)
  in-proc модель не защищает — осознанная цена, как у Unity/Rider/VS.
- Этап 2: crash reporter + safe mode («прошлый сеанс упал, подозревается плагин X —
  отключить?»), watchdog зависшего UI-потока. Цену краша уже в v1 снижает
  автосохранение.

### ArxisStudio.Inspector

- Обход `AvaloniaProperty` + CLR-свойств выбранного контрола, категории
  (Layout / Appearance / Content & Interaction, как в мокапе).
- Встроенные редакторы: число, строка, bool, enum-сегменты, `Thickness`, цвет/`Brush`,
  индикатор binding.
- Реестр `PropertyDrawer`/`CustomInspector` из SDK.
- Запись значений — через абстракцию цели редактирования; приложение адаптирует её к
  Markup (структура/свойства → XAML) и DesignEditor (геометрия), чтобы всё попадало в undo.
  Inspector сам не зависит от Markup/DesignEditor.

### ArxisStudio.Shell

- Зоны tool window (left / right / bottom / center), сплиттеры, сворачивание, вкладки зон.
- Меню и тулбары, собираемые из contributions SDK.
- Строится целиком на ArxisStudio.Controls; тему (ArxisStudio.Themes.Arxis) подключает
  приложение. Переключение dark/light — смена варианта темы.
- Хранилище настроек, каркас локализации, статус-бар.

### ArxisStudio (приложение)

- **Welcome**: Projects (recents, Open, создание из dotnet new шаблонов; Clone from Git —
  по возможности), Templates (галерея обнаруженных шаблонов), Learn (статика),
  Plugins (менеджер), Settings (тема, язык, поведение).
- **Editor**: Hierarchy, Toolbox, канва (DesignEditor + Markup live-объекты через
  `ProjectXamlEnvironment`), Inspector, Project-панель, Console/Build Output/Problems,
  вкладки Design/XAML/Split. Встроенные панели реализованы через SDK (dogfooding).
- Build/Run: сборка через ProjectSystem, запуск процесса с потоком stdout в Console.

## Этапы v1

1. **M0 — Controls + Themes** ✅ *(2026-08-24)*: репозитории ArxisStudio.Controls и
   ArxisStudio.Themes.Arxis созданы; 12 контролов (AxButton, AxTextBox, AxSearchField,
   AxCheckBox, AxToggleSwitch, AxComboBox, AxListBox, AxSegmentedControl, AxBadge,
   AxChip, AxCard, AxProgressBar), палитры dark/light из мокапа с динамическим
   переключением, галерея samples/Controls.Gallery, 17 headless-тестов темы.
   В M0 ArxisTheme подключается поверх FluentTheme (базовый слой для не-Ax
   примитивов); анимации отключены решением от 2026-08-24. Набор пополняется в
   каждом этапе.
2. **M1 — каркас** ✅ *(2026-08-24)*: пять субмодулей в external/ (все —
   github.com/Arxis-Team), solution, ArxisStudio.Shell (StudioShell: тулбар/зоны
   262·302·212 со сплиттерами/статус-бар), приложение запускается с темой и
   канвой-заглушкой.
3. **M2 — Welcome** ✅ *(2026-08-24)*: экран Welcome с пятью разделами — Projects
   (недавние с персистентностью, открытие .sln/.slnx/.csproj, индикатор пропавшей
   папки), Templates (шаблоны читаются из установленных `dotnet new`, создание
   проекта), Learn (ссылки на документацию), Plugins (каталог с манифестами,
   вкл/выкл, установка из папки), Settings (тема, язык, тумблеры — сохраняются).
   Сервисы: `JsonSettingsStore`, `RecentProjects`, `TemplateCatalog`,
   `PluginCatalog`, `Localizer` с i18n-каркасом (ru/en) и `StudioTheming`.
   Первые проекты `ArxisStudio.Sdk` (модель манифеста плагина) и
   `ArxisStudio.Extensibility` (каталог плагинов). 31 тест.
4. **M3 — ядро Editor** ✅ *(2026-08-24)*: открытие проекта → Project-панель; открытие
   .axaml → канва с живыми объектами; Hierarchy из дерева Markup; синхронизация
   выделения канва↔дерево в обе стороны. Сервисы: `StudioWorkspace` (обёртка над
   `ProjectWorkspace`), `ProjectTree` (иерархия из плоского списка элементов MSBuild,
   с отсевом служебных записей обращением к диску), `DesignDocument` (разбор разметки,
   `ProjectXamlPopulation` для документов с `x:Class`, поверхность показа и порядок
   разрушения), `HierarchyNode`. Подопытный проект `tests/fixtures/DesignFixtureApp`
   собирается вместе с тестами и открывается дизайнером как настоящий. 35 тестов.
   Известное ограничение: если открываемый проект содержит сборки, уже загруженные в
   процесс самой студии (например галерея ArxisStudio.Controls), загрузчик не находит
   их типы. Проекты пользователя это не задевает; лечится в M7 вместе с изоляцией.
5. **M4 — Inspector**: property grid по выделению, геометрия через шов DesignEditor,
   запись свойств в XAML через Markup, undo/redo.
6. **M5 — Toolbox и структура**: палитра контролов, drag на канву → вставка в XAML;
   удаление/перестановка через `DeleteRequested`/`ReorderRequested`.
7. **M6 — XAML-вкладка**: AvaloniaEdit, подсветка, round-trip Design↔XAML, Split.
8. **M7 — расширяемость**: SDK стабилизирован, пример внешнего плагина, установка
   из zip, сборка и запуск проекта с логами в Console.

## Этап 2 (вне v1)

Экран Run/Debug (runtime-агент, watch живых ViewModel, hot reload в запущенный процесс,
`ArxisStudio.Runtime`), онлайн-каталог плагинов, drag&drop-докинг, editor-код в проекте
пользователя, редактирование C#/Roslyn, курс Learn с прогрессом, зависимости
плагин-от-плагина, подпись/доверие пакетов плагинов.

## Приложение A — формат каталога модуля и плагина

Один формат манифеста для обоих режимов; отличается только расположение каталога и
способ загрузки сборки.

### Внешний плагин (установленный)

```
%APPDATA%/ArxisStudio/plugins/            ← Windows; Linux/macOS — стандартные пути платформы
  arxis.figma-import/                     ← имя папки = id плагина
    plugin.json                           ← манифест
    bin/
      Arxis.FigmaImport.dll               ← entry-сборка
      Arxis.FigmaImport.pdb
      ThirdParty.Something.dll            ← приватные зависимости плагина
    assets/
      icon.svg                            ← иконка в менеджере плагинов
      preview.png
    lang/
      strings.en.json
      strings.ru.json                     ← локализация строк contributions (%key%)
    README.md
    CHANGELOG.md
```

В `bin/` не должно быть общих контрактов (`ArxisStudio.Sdk`, `ArxisStudio.Controls`,
сборки Avalonia) — резолвер всегда берёт их из default ALC; таргет упаковки это
контролирует.

### plugin.json

```json
{
  "id": "arxis.figma-import",
  "name": "Figma Import",
  "version": "2.4.0",
  "publisher": "Arxis Labs",
  "description": "Импорт макетов Figma в дизайнер форм",
  "icon": "assets/icon.svg",
  "sdk": { "min": "1.0" },
  "entry": "bin/Arxis.FigmaImport.dll",
  "contributions": {
    "commands":    [ { "id": "figma.import", "title": "%cmd.import%" } ],
    "menus":       [ { "path": "Tools/Figma/Import…", "command": "figma.import" } ],
    "toolWindows": [ { "id": "figma.panel", "title": "Figma", "zone": "right" } ],
    "fileTypes":   [ { "ext": ".fig", "name": "Figma Document" } ],
    "settings":    [ { "key": "figma.apiToken", "type": "string", "scope": "user" } ]
  },
  "activation": [ "onCommand:figma.import", "onFileType:.fig", "onToolWindow:figma.panel" ]
}
```

Всё из `contributions` хост показывает (меню, Settings, ассоциации файлов), не загружая
сборку; сборка грузится при первом событии `activation`. Атрибуты (`[MenuItem]`,
`[CustomInspector]`, …) действуют внутри сборки после активации. Для плагина «из одного
класса» допускается `"activation": ["onStartup"]` — минимальный манифест, всё объявляют
атрибуты, но такой плагин платит временем старта.

### Встроенный модуль (в главном репо)

```
src/Modules/
  ArxisStudio.Modules.Hierarchy/
    module.json                           ← тот же формат манифеста (без bin/)
    ArxisStudio.Modules.Hierarchy.csproj  ← ProjectReference на Sdk, Controls; default ALC
    HierarchyToolWindow.cs
    Assets/…
    Lang/…
```

`module.json` встраивается как embedded resource; Extensibility читает манифесты
встроенных модулей из ресурсов, внешних — с диска, дальше конвейер один. Перенос
модуля во внешний плагин = переложить каталог + собрать `.axplugin`; код не меняется.

### Проект автора плагина (dev-time)

Обычный `csproj` со ссылкой на `ArxisStudio.Sdk` (анализатор «только Ax*-виджеты»
приходит с ним); `dotnet build` таргетом раскладывает выход в формат каталога и может
класть его в папку плагинов для отладки.
