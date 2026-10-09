# Встроенные модули

Встроенный модуль — папка с `module.json` и `bin/`, устроенная как внешний плагин
([src/Plugins/CLAUDE.md](../Plugins/CLAUDE.md)), только приезжает она со студией, живёт в основном
контексте загрузки и отдельно не выгружается. **Код между режимами переносится без правок**: образец
модуля — [ArxisStudio.Modules.Sample](ArxisStudio.Modules.Sample), образец плагина —
`src/Plugins/Arxis.HelloPlugin`.

Рабочие модули, а не образцы: [Terminal](ArxisStudio.Modules.Terminal),
[Console](ArxisStudio.Modules.Console), служба проектов [Projects](ArxisStudio.Modules.Projects), окно
проекта [Project](ArxisStudio.Modules.Project), служба XAML [Xaml](ArxisStudio.Modules.Xaml) и дизайнер
[UiDesigner](ArxisStudio.Modules.UiDesigner). У служб, окна проекта и дизайнера свой `CLAUDE.md` в папке.

## Новый модуль

Отличий от плагина ровно два: список модулей объявлен в самой студии
([`StudioModules.Assemblies`](../ArxisStudio/Services/StudioModules.cs) — сюда добавляют новый модуль, а
в [ArxisStudio.csproj](../ArxisStudio/ArxisStudio.csproj) — ссылку на него с пометками
`StudioModule="true" Private="false" ExcludeAssets="runtime"`), и выгрузить модуль отдельно нельзя.

У модуля **своя папка в `modules/` выхода, формой как у установленного плагина**: манифест в корне,
сборки в `bin/`.

```
modules/arxis.terminal/module.json
modules/arxis.terminal/bin/{ArxisStudio.Modules.Terminal,Porta.Pty,XTerm.NET}.dll
```

Имя папки — идентификатор из манифеста; читает его сам модуль, а не студия. Состав модуль отдаёт сам
(цель `StudioModuleFiles` в [Directory.Build.targets](Directory.Build.targets)): манифест, `lang/*.json`,
`templates/**` и сборки. Папка встроенного модуля — та, что над его `bin/`, и контракт он объявляет от
неё же (`bin/…`), слово в слово как плагин. Как студия раскладывает выход целиком — в
[src/ArxisStudio/CLAUDE.md](../ArxisStudio/CLAUDE.md).

**Один файл — один модуль.** Модули живут в одном основном контексте загрузки, где имя грузится ровно
один раз, и две папки с одноимённой сборкой дали бы две копии на диске и одну работающую. `AXL1002` валит
сборку, называя файл и всех его владельцев; общей сборке место в `lib/` — её проект называют прямо в
`ArxisStudio.csproj`, — а чужой контракт подключают `Private="false" ExcludeAssets="runtime"`: возит его
владелец. Идентификатор, который не прочитался из манифеста, называет `AXL1003`.

Словари у модуля свои, в его `lang/`, и дорога к ним та же, что у плагина: чужих ключей — ни студии, ни
соседа — в словаре расширения не видно. Ключи студии внутренние, и переименование одного не должно менять
текст в чужой панели; модуль от этого правила не освобождён, и тем он и переносится во внешний плагин
перекладыванием папки — вместе со строками, а не отдельно от них. Роль `AxStrings`, по которой `ARX0012`
проверяет словари, метит общий [Directory.Build.targets](Directory.Build.targets), так что новый модуль
приходит с проверкой.

Шаблоны пунктов «Добавить ▸» — данные: `templates/**` стоит в `DefaultItemExcludes` и едет в
`modules/<id>/templates`; пропажу называет `AXL1004`. Переводы строк шаблона — тоже его содержимое: служба
отдаёт их как были, и git их не трогает — `.gitattributes` снимает с `templates/**` модулей и плагинов
`text`, а `.editorconfig` велит писать их с LF. Без этого свежий клон с `core.autocrlf` выписывал шаблону
CRLF, и заметка образца у человека зависела от того, кто собрал студию (запись 348).

Строку подключения анализатора csproj нового модуля пишет сам — через `ProjectReference` он не
передаётся ([CODING_STANDARDS.md](../../CODING_STANDARDS.md)).
