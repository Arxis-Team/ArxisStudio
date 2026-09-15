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
ARX0007 | ArxisStudio | Warning | Тег манифеста написан не так, как студия его прочтёт
ARX0008 | ArxisStudio | Warning | Число вместо значения темы в разметке расширения: отступ, кегль или цвет темы называют ресурсом
ARX0009 | ArxisStudio | Warning | Ресурс темы не того семейства: ступень шкалы палитры или цвет там, где нужна кисть
ARX0010 | ArxisStudio | Warning | Число вместо значения темы в коде расширения: new Thickness, Spacing, FontSize, Color.Parse
ARX0011 | ArxisStudio | Warning | Запись значка в манифесте студия не разберёт: имени нет в наборе, забыта приставка arxis: или указан файл картинки
