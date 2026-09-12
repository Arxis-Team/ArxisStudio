# Как плагин читает проект

Открытое решение студия держит одна — встроенный модуль `arxis.projects`. Он читает `.sln`,
`.slnx` и `*proj` настоящим MSBuild, следит за диском, перечитывает модель, когда она устарела, и
отдаёт соседям **снимок**: неизменяемое описание решения и каждого его проекта. Плагину движок не
нужен и не достанется — ему достаётся снимок и три службы.

Всё, что здесь написано, закреплено тестами: примеры компилируются
([ProjectsGuideTests](../tests/ArxisStudio.Tests/ProjectsGuideTests.cs)), а числа и коды сверяются с
кодом. Подробности каждого метода — в XML-комментариях контракта
([ArxisStudio.Projects.Contracts](../src/Modules/ArxisStudio.Projects.Contracts)); здесь — то, чего
в них нет: порядок действий и причины.

## Три службы, одна на своё дело

| Служба | Берётся | О чём она |
|---|---|---|
| `IStudioProjects` | `context.Projects()` | что открыто, снимок, перемены, открыть/перечитать/закрыть |
| `IStudioBuild` | `context.Build()` | восстановить, собрать, пересобрать, очистить |
| `IStudioPackages` | `context.Packages()` | поставить и убрать пакет NuGet |

Разведены они по тому, чем занят берущий: подписчику снимков события сборки не нужны, а тому, кто
рисует окно сборки, не нужен снимок. Все три живут в одном модуле и берутся одной дорогой —
экспортом, и каждая может ответить `null`: модуля нет, он не поднялся или его версия ниже той, что
объявил ваш манифест.

## Объявите модуль зависимостью

```json
{
  "id": "arxis.outline",
  "sdk": { "min": "5.0" },
  "dependencies": [
    { "id": "arxis.projects", "min": "1.2" }
  ]
}
```

Зависимость делает две вещи: держит порядок подъёма — модуль поднимется раньше вас — и выдерживает
нижнюю границу версии. Границы такие, и они не выдуманы задним числом:

- **1.0** — модель: `IStudioProjects`, снимки, перемены;
- **1.1** — сборка: `IStudioBuild`;
- **1.2** — пакеты: `IStudioPackages`.

Просите ту, которой вам хватает: плагин, читающий снимки, с границей `1.0` поднимется и в студии,
где сборки ещё не было.

Ссылка при сборке — одна, и ставится она тем же способом, каким шаблон плагина
([templates/Arxis.Plugin](../templates/Arxis.Plugin)) ссылается на SDK:

```xml
<ProjectReference Include="$(ArxisStudioPath)/src/Modules/ArxisStudio.Projects.Contracts/ArxisStudio.Projects.Contracts.csproj"/>
```

Ядро модели приедет вместе с ней: контракт написан на языке `ArxisStudio.ProjectSystem` и ссылается
на него сам. В собранном плагине эта сборка не окажется — студия объявила её общей и упаковка её
отсеивает; `ArxisStudio.Projects.Contracts.dll` уедет копией, и вреда в этом нет: контракт студия
грузит в основной контекст один раз, и копия из папки плагина его не подменит.

## Возьмите службу и подпишитесь

```csharp
using System;
using ArxisStudio.Projects;
using ArxisStudio.Sdk;

namespace Guide.Taking;

/// <summary>Плагину нужно знать, что открыто, и услышать, когда это переменится.</summary>
public sealed class OutlinePlugin : StudioPlugin
{
    private IStudioContext? _context;
    private IStudioProjects? _projects;

    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _projects = context.Projects();

        if (_projects is null)
        {
            // Службы может не быть, и это не беда: студия собирается и без модуля проектов.
            context.Log.Write(StudioLogLevel.Warning, "Outline", "Службы проектов нет");

            return;
        }

        // Сначала подписка, потом чтение: перемена, случившаяся между ними, иначе прошла бы мимо.
        _projects.Changed += OnProjects;

        Show(_projects.Status);
    }

    public override void Deactivate()
    {
        if (_projects is not null)
            _projects.Changed -= OnProjects;

        _projects = null;
        _context = null;
    }

    private void OnProjects(object? sender, ProjectsChangedEventArgs e) => Show(e.Current);

    private void Show(ProjectsStatus status) => _context?.Log.Write(
        StudioLogLevel.Info,
        "Outline",
        $"{status.State}: проектов {status.Snapshot?.Projects.Length ?? 0}");
}
```

Три правила в одиннадцати строках.

**Сначала подписка, потом чтение.** Наоборот — значит проспать перемену, случившуюся между двумя
вызовами. Событие после чтения может принести то же самое; `ProjectsStatus.Sequence` скажет, новое
ли это: номер не больше прочитанного — нового ничего.

**Отписка в `Deactivate`.** Забытая подписка снимется сама, когда контекст плагина станут выгружать,
но это страховка от беды, а не способ работать.

**`null` — рабочий ответ.** Плагин, падающий оттого, что модуля не оказалось, падает на чужой
машине, а не на вашей.

## Снимок — это не диск

`Status.Snapshot` (он же `Current`) — неизменяемая запись: решение, его проекты, их ссылки, пакеты,
целевые среды и выходные файлы на момент последнего чтения. Её можно читать из любого потока без
замков, складывать в поле и сравнивать с прежней — она никогда не изменится под руками.

Но она и не обновляется сама. Снимок говорит, что́ проект **сказал о себе**, когда его прочитали; о
том, что лежит на диске сейчас, он не знает. Файл, дописанный секунду назад, окажется в снимке после
перезагрузки — её служба затеет сама, увидев перемену.

```csharp
using System.Collections.Generic;
using System.Linq;
using ArxisStudio.ProjectSystem;

namespace Guide.Reading;

/// <summary>Строки дерева решения.</summary>
public static class Outline
{
    /// <summary>Проект, его среда и сколько у него ссылок на пакеты.</summary>
    /// <param name="snapshot">Снимок; null — решение не открыто.</param>
    public static IReadOnlyList<string> Lines(SolutionSnapshot? snapshot)
    {
        if (snapshot is null)
            return [];

        return
        [
            .. snapshot.Projects.Select(project =>
            {
                var framework = project.ActiveTargetFramework ?? string.Join(';', project.TargetFrameworks);

                return $"{project.Name} [{framework}] — пакетов {project.PackageReferences.Length}";
            }),
        ];
    }
}
```

Проект, который не прочитался, из снимка не пропадает: он остаётся со своим именем, путём и
диагностикой в `ProjectSnapshot.Diagnostics`, а `HasErrors` у него поднят. Решение, потерявшее
сломанный проект, было бы меньше, чем оно есть.

## Событие говорит, что переменилось

```csharp
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace Guide.Reacting;

/// <summary>Часть плагина, которая держит своё дерево в согласии с моделью.</summary>
public sealed class Tree
{
    /// <summary>Обработчик <c>IStudioProjects.Changed</c>.</summary>
    public void OnProjects(object? sender, ProjectsChangedEventArgs e)
    {
        // Перемены, которых я не касаюсь: идёт загрузка, сменилась конфигурация.
        if ((e.Changes & (ProjectsChanges.Snapshot | ProjectsChanges.Session)) == 0)
            return;

        // Сессия сменилась — открыли другое решение, и всё прежнее больше не моё.
        if (e.Changes.HasFlag(ProjectsChanges.Session))
            Forget();

        foreach (ProjectSnapshot project in e.Removed)
            Remove(project);

        foreach (ProjectSnapshot project in e.Added)
            Add(project);

        foreach (ProjectSnapshot project in e.Modified)
            Refresh(project);
    }

    private void Forget()
    {
    }

    private void Add(ProjectSnapshot project)
    {
    }

    private void Remove(ProjectSnapshot project)
    {
    }

    private void Refresh(ProjectSnapshot project)
    {
    }
}
```

Событие приходит **только в поток интерфейса** и по порядку. Промежуточные состояния склеиваются:
пока поток занят, служба копит перемены и отдаёт их одним событием, а разность в нём считается от
прошлого доставленного состояния до нового. Значит, `Added` и `Modified` — это не «что случилось
последним шагом», а «чем новое отличается от того, что вы видели».

Тяжёлого в обработчике не делают: он держит поток интерфейса, а долгой работе место в
`context.Tasks` — там у неё будут имя, полоса и отмена.

Упавший обработчик соседям не мешает: остальные получат событие, а исключение студия припишет тому,
чей код бросил.

## Что переживает переоткрытие, а что нет

`ProjectIdentity` принадлежит сессии. Закрыли решение и открыли снова — `ProjectsStatus.Session`
сменился, и идентичности у тех же самых проектов стали другие. То, что должно пережить
переоткрытие, храните по `ProjectFilePath`.

```csharp
using System.Collections.Generic;
using ArxisStudio.ProjectSystem;

namespace Guide.Remembering;

/// <summary>Закладки плагина: что он помнит о проектах между открытиями.</summary>
public sealed class Bookmarks
{
    // Ключ — путь к файлу проекта, а не ProjectIdentity: идентичность принадлежит сессии, путь —
    // человеку. Переоткрытое решение находит свои закладки на месте.
    private readonly Dictionary<CanonicalPath, int> _lines = new();

    /// <summary>Запоминает строку.</summary>
    public void Remember(ProjectSnapshot project, int line) => _lines[project.ProjectFilePath] = line;

    /// <summary>Что запомнено; null — ничего.</summary>
    public int? Line(ProjectSnapshot project) =>
        _lines.TryGetValue(project.ProjectFilePath, out var line) ? line : null;
}
```

Перезагрузка — другое дело: тот же путь сессию не меняет, и идентичности остаются прежними.

## Чей это файл

```csharp
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;

namespace Guide.Owning;

/// <summary>Кому принадлежит открытый документ.</summary>
public static class Owner
{
    /// <summary>Проект, которому принадлежит файл; null — ничей или решение не открыто.</summary>
    /// <param name="projects">Служба проектов.</param>
    /// <param name="filePath">Полный путь к файлу.</param>
    public static ProjectSnapshot? Of(IStudioProjects projects, string filePath)
    {
        if (projects.Current is not { } snapshot)
            return null;

        return snapshot.TryGetProjectForFile(CanonicalPath.Create(filePath), out var project)
            ? project
            : null;
    }
}
```

Ищется по папкам проектов, а не по списку файлов: файл, которого в проекте ещё нет, принадлежит тому
же проекту, что и папка, в которой его создали.

## Собрать

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace Guide.Building;

/// <summary>Сборка из плагина.</summary>
public static class Builder
{
    /// <summary>Собирает открытое решение и пишет в журнал всё, что сказал MSBuild.</summary>
    /// <returns><c>true</c>, если собралось.</returns>
    public static async Task<bool> BuildAsync(IStudioContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Build() is not { } build)
            return false;

        var progress = new Progress<ProjectOperationProgress>(step =>
            context.Log.Write(StudioLogLevel.Debug, "Outline", step.Message));

        // Задачу в статус-баре, её отмену и уход с потока интерфейса служба берёт на себя.
        var result = await build.RunAsync(
            ProjectOperationKind.Build,
            progress: progress,
            cancellationToken: cancellationToken);

        foreach (var diagnostic in result.Diagnostics)
        {
            context.Log.Write(
                diagnostic.IsError ? StudioLogLevel.Error : StudioLogLevel.Warning,
                "Outline",
                $"{diagnostic.Code}: {diagnostic.Message}");
        }

        return !result.HasErrors;
    }
}
```

Провал сборки — это `ProjectOperationResult` с диагностиками, а не исключение. Исключения остаются
неверному виду операции, отмене и остановленной службе.

Операция идёт одна: MSBuild держит на процесс общие кэши и единственную регистрацию, поэтому
загрузки и операции стоят в одной очереди. Сборка дождётся идущей загрузки, загрузка — сборки, а
`IStudioBuild.Running` скажет, что идёт сейчас. Отмену MSBuild слышит между проектами, а не посреди
проекта, — поэтому отменённая операция итога не приносит вовсе: сказать, что успело собраться, он
уже не может.

## Поставить пакет

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using ArxisStudio.Projects;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace Guide.Packaging;

/// <summary>Правка ссылок на пакеты из плагина.</summary>
public static class Packages
{
    /// <summary>Ставит пакет или меняет версию уже объявленного.</summary>
    /// <returns>Что сказать человеку.</returns>
    public static async Task<string> InstallAsync(
        IStudioContext context,
        ProjectSnapshot project,
        string packageId,
        string version)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(project);

        if (context.Packages() is not { } packages)
            return "службы пакетов нет";

        var result = await packages.InstallAsync(project.Identity, packageId, version);

        if (!result.HasErrors)
            return $"{packageId} {version}: поставлен";

        // Ссылка, пришедшая из Directory.Build.props или из SDK, в файле проекта не объявлена —
        // и редактор, правящий один файл, её там не найдёт. Отказ у этого случая свой.
        return result.Diagnostics.Any(
            diagnostic => diagnostic.Code == ProjectsDiagnosticCodes.ReferenceNotInProjectFile)
            ? $"{packageId} объявлен импортом, а не файлом проекта — правьте импорт"
            : string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message));
    }
}
```

Установка правит файл проекта — или файл централизованных версий, если проект ими управляется, — и
восстанавливает пакеты. Провалившееся восстановление отменяет правку: проект возвращается байт в
байт. Удачная правка сама перечитывает модель причиной `ProjectsLoadReason.Packages`, так что
подписчику снимков делать ничего не нужно.

## Открыть и закрыть

`OpenAsync`, `ReloadAsync`, `SetConfigurationAsync` и `CloseAsync` плагину нужны редко: решение
открывает человек — из командной строки, из недавних или меню «Проект», — а перечитывает служба
сама, когда видит перемену на диске. Зовите их, когда решение открывает ваш плагин: мастер нового
проекта, клиент репозитория, свой список избранного.

Отмена и провал у них — тоже результат. Нет файла, не тот вид файла, нет SDK, проект не разобрался —
всё это `WorkspaceLoadResult` с диагностиками, и то же самое остаётся лежать в
`ProjectsStatus.LastLoad`. Провалившаяся перезагрузка прежний снимок не трогает: `Current` остаётся
прежним, а провал виден в `LastLoad`.

Вызов `ReloadAsync`, пришедший, когда загрузка уже стоит в очереди, к ней присоединяется: все ждущие
получат один итог.

## О найденном — в журнал

Панели «Проблемы» у студии нет: находки, ошибки и всё, что плагин хочет сказать человеку, идут в
журнал — `context.Log`. Уровень выбирает пишущий, источник — имя вашего плагина, и по нему человек
отбирает записи в консоли.

```csharp
using System.Linq;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;

namespace Guide.Telling;

/// <summary>О том, что нашлось в снимке, плагин говорит журналом.</summary>
public static class Findings
{
    /// <summary>Пишет в журнал проекты, которые не прочитались.</summary>
    public static void Report(IStudioLog log, SolutionSnapshot snapshot)
    {
        foreach (var project in snapshot.Projects.Where(project => project.HasErrors))
        {
            var first = project.Diagnostics.First(diagnostic => diagnostic.IsError);

            log.Write(StudioLogLevel.Error, "Outline", $"{project.Name}: {first.Code} {first.Message}");
        }
    }
}
```

Служба проектов пишет туда же и тем же: итог загрузки строкой, следом — сами находки, каждая своей
строкой и на своём уровне. Смотреть на них человек будет в одном месте с вашими.

## Путь проекта и настройки, которые едут за ним

`IStudioContext.ProjectPath` — путь к открытому решению или проекту, и он живой: свойство отвечает
на открытие и закрытие, а не остаётся тем, чем было при подъёме плагина. Плагину, которому нужен
только путь, службы не нужно вовсе.

Настройки области «проект» (`"scope": "project"` в манифесте) лежат в самом проекте, в
`.arxis/settings.json`, и переезжают вместе с ним: открыли другое решение — значения переменились, и
студия скажет об этом тем же уведомлением `IStudioSettings.Changed`, каким говорит о правке из окна
настроек. Перечитывать их самому не нужно — нужно слушать.

## Чего не делать

**Не звать MSBuild самому.** `ArxisStudio.ProjectSystem.MSBuild` и `.NuGet` общими сборками не
объявлены и плагину не достанутся: движок на процесс один, и держит его служба проектов. Второй
экземпляр — это вторая регистрация MSBuild, второй набор кэшей и беда, которую не видно до чужой
машины.

**Не считать снимок истиной о диске.** Он говорит, что проект сказал о себе при последнем чтении.
Нужен файл — читайте файл.

**Не хранить `ProjectIdentity` дольше сессии.** Переоткрытое решение раздаст проектам новые
идентичности, а ваши старые начнут молча не находиться.

**Не делать тяжёлого в обработчике.** Он держит поток интерфейса.

**Не считать службу обязательной.** `null` — обычный ответ, а не исключительный случай.

## Что читать дальше

- [ArxisStudio.Projects.Contracts](../src/Modules/ArxisStudio.Projects.Contracts) — контракт с
  XML-комментариями: у каждого метода написано, что он обещает и чем отвечает на провал.
- [external/ArxisStudio.ProjectSystem](../external/ArxisStudio.ProjectSystem) — сама модель:
  снимки, идентичности, диагностики, коды `APS*`.
- [docs/plan.md](plan.md), записи 119–129 — как это строилось и почему устроено так.
