; Правила, ещё не вошедшие в выпуск SDK.
; Формат файла задан Roslyn: https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ARX0001 | ArxisStudio | Warning | Виджет Avalonia в интерфейсе плагина: строить нужно на контролах ArxisStudio.Controls
ARX0002 | ArxisStudio | Warning | Ключ манифеста не найден в словаре плагина lang/strings.json
ARX0003 | ArxisStudio | Warning | Кнопка полосы зовёт команду, которой плагин не объявлял
ARX0004 | ArxisStudio | Warning | Свой контрол полосы объявлен, а класса с [ToolBarItem] нет
ARX0005 | ArxisStudio | Warning | Класс помечен [ToolBarItem], а в манифесте его нет
ARX0006 | ArxisStudio | Warning | Виджет Avalonia в разметке плагина: строить нужно на контролах ArxisStudio.Controls
