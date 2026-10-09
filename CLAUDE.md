# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Что это

Оболочка, из которой всё остальное приходит плагинами: в самой студии нет ни одной панели и ни
одного формата документа — она умеет зоны, вкладки, меню, статус-бар и один контракт
расширяемости. Окно проекта, дизайнер форм, терминал и консоль — встроенные модули на том же
контракте, что и внешние плагины.

Три документа, и они не взаимозаменяемы. [README.md](README.md) — что работает сегодня.
[docs/plan.md](docs/plan.md) — **журнал сделанного, а не опись текущего состояния**: принятые
решения, формат манифеста (приложение A). [docs/design-system.md](docs/design-system.md) —
дизайн-система: роли цвета и пороги контраста, сетка и плотность, типографика, иконки, поведение
контролов и цели редизайна по вехам; числа берутся оттуда, а не придумываются. Тесты темы
проверяют правила — пороги, кратность, наличие состояний, — а не переписанные значения.

## Где что написано

Здесь — то, что нужно до первого чтения кода. Правила для любой правки кода —
[CODING_STANDARDS.md](CODING_STANDARDS.md), он подключён в конце этого файла. Устройство подсистемы — в
`CLAUDE.md` её папки, и он подхватывается сам, когда читаешь её файлы:
[приложение](src/ArxisStudio/CLAUDE.md), [хост плагинов](src/ArxisStudio.Extensibility/CLAUDE.md),
[докинг](src/ArxisStudio.Docking/CLAUDE.md), [локальная история](src/ArxisStudio.LocalHistory/CLAUDE.md),
[SDK](src/ArxisStudio.Sdk/CLAUDE.md), [модули](src/Modules/CLAUDE.md) — и у служб проектов и XAML,
окна проекта и дизайнера свои, — [плагины](src/Plugins/CLAUDE.md), [тесты](tests/ArxisStudio.Tests/CLAUDE.md).

| Задача | Что читать |
|---|---|
| новый модуль | [src/Modules/CLAUDE.md](src/Modules/CLAUDE.md) |
| новый плагин, архив, выкладка | [src/Plugins/CLAUDE.md](src/Plugins/CLAUDE.md) |
| живая проверка | [docs/devtools.md](docs/devtools.md#живая-проверка-агентом) |
| контракт глазами автора плагина | [docs/projects.md](docs/projects.md), [docs/xaml.md](docs/xaml.md), [docs/drag-drop.md](docs/drag-drop.md), [docs/file-previews.md](docs/file-previews.md), [docs/plugin-markup.md](docs/plugin-markup.md) |
| почему так решено | [docs/plan.md](docs/plan.md) — запись по номеру из комментария или коммита |

## Команды

```bash
git submodule update --init external/ArxisStudio.Controls external/ArxisStudio.Icons external/ArxisStudio.Themes.Arxis external/ArxisStudio.Fonts.Cascadia external/ArxisStudio.Markup external/ArxisStudio.ProjectSystem external/ArxisStudio.Surface
```

```bash
dotnet build ArxisStudio.slnx
```

```bash
dotnet run --project src/ArxisStudio
```

Это для запуска руками, из своего терминала. Запуск для проверки — отдельным разделом ниже: там
студия должна пережить команду, которая её подняла, и `dotnet run` для этого не годится.

```bash
dotnet test tests/ArxisStudio.Tests
```

Один тест или один класс:

```bash
dotnet test tests/ArxisStudio.Tests --filter 'FullyQualifiedName~StudioDockTests.A_panel_comes_back_exactly_where_it_stood'
```

## Данные человека

`%AppData%/ArxisStudio` — данные человека: `settings.json`, `layout.json`, `recent-projects.json`,
`keymap.json` (его пишет только человек), `lang/`, `plugins/` (и `plugins/.disabled.json`, где помнится,
кого выключили), `restart-<номер>.json` — сессия перезапуска, живёт секунды: новая копия стирает
прочитанное, а оставшийся файл значит, что она сессию не приняла. Машинное — в
`%LocalAppData%/ArxisStudio`: локальная история (`LocalHistory`, большая и привязана к машине) и снимки
форм для плиток (`Previews`, кэш машины, а не файл решения). Переносят их `ARXIS_LOCAL_HISTORY` и
`ARXIS_PREVIEWS`, выключает `0` — так живёт процесс тестов (`TestLocalHistory`, `TestPreviews`), а тест,
которому они нужны, называет свою временную папку прямо (тест снимков — швом
`UiDesignerOptions.SnapshotsFolder`). Теневые копии плагинов — в `%TEMP%/arxis-plugin-shadow` и
`arxis-contract-shadow`, чистятся при первом обращении за сеанс.

## Проверка на живой студии

**Проверяя работу, студию запускают и управляют ею через MCP — это ожидаемый способ, а не
исключение, и спрашивать разрешения на него не нужно.** Тесты закрепляют поведение, а живая
студия отвечает на то, чего в них не видно.

До первого запуска — четыре правила; готовая команда запуска, клиент JSON-RPC, порядок инструментов,
места, где легко обмануться, и перезапуск — в [docs/devtools.md](docs/devtools.md#живая-проверка-агентом).

- Запускать собранный exe, а не `dotnet run`, и закрывать его перед пересборкой: запущенная студия
  держит свои сборки, и пересборка падает с `MSB3027`.
- Историю и снимки форм уводить во временные папки — `ARXIS_LOCAL_HISTORY` и `ARXIS_PREVIEWS` той же
  командой, что запускает студию, — иначе проверка пишет в данные человека.
- Закрывать по номеру процесса (`Start-Process -PassThru` → `Stop-Process -Id`), а не по имени: рядом
  может работать студия человека.
- Вторую студию рядом с первой поднимать с `ARXIS_SINGLE_INSTANCE=0` и `ARXIS_DEVTOOLS_MCP_PORT=5199`:
  иначе она молча отдаст запуск первой.

## Слои и направление ссылок

| Проект | Роль | Ссылается на |
|---|---|---|
| [ArxisStudio.Sdk](src/ArxisStudio.Sdk) | контракт для плагинов и модель манифеста | Controls, Icons — и ничего из студии |
| [ArxisStudio.Sdk.Analyzers](src/ArxisStudio.Sdk.Analyzers) | ARX0001–ARX0017, едут вместе с SDK | Roslyn |
| [ArxisStudio.Shell](src/ArxisStudio.Shell) | каркас окна, словари, настройки, полоса | Controls, Icons |
| [ArxisStudio.Docking](src/ArxisStudio.Docking) | движок докинга | Controls, Icons |
| [ArxisStudio.Extensibility](src/ArxisStudio.Extensibility) | хост плагинов: контексты загрузки, граф, контракты, шов сбоев | Sdk, Shell |
| [ArxisStudio.LocalHistory](src/ArxisStudio.LocalHistory) | локальная история: содержимое по адресу, журнал по дням, известное состояние, срок | ничего, кроме платформы |
| [ArxisStudio.Projects.Contracts](src/Modules/ArxisStudio.Projects.Contracts) | контракт службы проектов: состояние, перемены, дорога из контекста | Sdk, ядро ProjectSystem — и ни одного движка |
| [ArxisStudio.Xaml.Contracts](src/Modules/ArxisStudio.Xaml.Contracts) | контракт службы XAML: документы, показ, поколение типов проекта | Sdk, ядро ProjectSystem, синтаксис Markup, Avalonia — и ни загрузчика, ни адаптера |
| [ArxisStudio](src/ArxisStudio) | приложение и вся склейка | всё |

Направление держит сборка, а не уговор: у движка докинга запрещённую ссылку называет `AXD1001`, у
локальной истории — `AXH1001`; почему — в `CLAUDE.md` их папок. У SDK то же правило словами: реализации
студии в нём быть не должно, а исключение одно — библиотека контролов.

## Что нельзя двигать

`AssemblyVersion` прибит на `1.0.0.0` — **навсегда**. Плагин собирается против `ArxisStudio.Sdk` и
запоминает его имя вместе с версией; сдвиг ломает связывание у каждого уже установленного плагина.
Задан он в [Directory.Build.props](Directory.Build.props). Меняются `ArxisRelease` и `Version` — то, что
версией называют люди; совместимость плагинов мерится `sdk.min` в манифесте против `StudioSdk.Version`.

`StudioSdk.Version` двигают, когда меняется контракт, и минор — когда добавляется.
`Satisfies` сравнивает мажор и минор, поэтому снятое обещание — это новый мажор, а не «оно и так
не использовалось».

Обещание — не только SDK. Набор контролов, иконки и ядро модели проектов плагин видит общими, и их
поверхность записана в `tests/ArxisStudio.Tests/Surfaces` вместе с номером, при котором её
записали. Разошлась — тест требует сдвига номера (добавленное — минор, снятое — мажор) и только
после сдвига отдаёт новую запись: перенести поверхность мимо номера нечем.

## Подмодули

Собираются все подмодули: **Controls** (контролы `Ax*`, lookless), **Icons** (контуры 16×16 и `AxIcon`),
**Themes.Arxis** (палитры, шаблоны, метрики), **Fonts.Cascadia** (шрифт ресурсом; называет его не
решение, а тема), **Markup** (lossless XAML DOM и round-trip) — все три пакета: синтаксис общий и лежит в
`lib/`, загрузчик везёт служба XAML, — **ProjectSystem** — не весь, а ядро модели, провайдер MSBuild,
правку пакетов и адаптер разметки: первые три держит служба проектов, адаптер — служба XAML, — и
**Surface** — тоже не весь, а ядро холста (`src/Surface`), инструменты (`src/Surface.Editing`) и дизайнер
интерфейса (`src/Surface.UiDesigner`): их везёт модуль дизайнера. Редактор узлов (`Surface.Nodes`) студия
не собирает. Библиотека Surface остаётся `net8.0` — это её наименьшая среда для пакета, а не долг: модуль
на `net10.0` грузит её как есть (запись 325).

Слоёв Surface в `ArxisStudio.slnx` нет: работают с ними решением Surface в корне подмодуля, и там же
образец дизайнера на трёх семействах разом — `samples/UiDesigner.Demo` (запись 323), которому нужны
ProjectSystem и Markup рядом. Так же проверяются Markup, ProjectSystem и ядро Surface: студия собирает их
исходники, а их тесты гоняют их собственные решения.

**API подмодулей можно менять** — это не замороженные зависимости. Если интеграции нужен новый
или изменённый публичный API, правьте прямо в подмодуле и коммитьте в его репозиторий, соблюдая
его правила: у Markup, ProjectSystem и Surface есть свой `CLAUDE.md`, и внутри их каталогов
он старше этого файла. Указатель подмодуля обновляется в том же коммите студии, что и правка,
которой он понадобился.

## Правила кода

@CODING_STANDARDS.md
