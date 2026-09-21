; Правила, ещё не вошедшие в выпуск SDK.
; Формат файла задан Roslyn: https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ARX0001 | ArxisStudio | Warning | Виджет Avalonia в интерфейсе плагина: строить нужно на контролах ArxisStudio.Controls — и создавая, и наследуя
ARX0002 | ArxisStudio | Warning | Ключ манифеста не найден в словаре расширения lang/en.json
ARX0003 | ArxisStudio | Warning | Кнопка полосы зовёт команду, которой расширение не объявляло
ARX0004 | ArxisStudio | Warning | Свой контрол полосы объявлен, а класса с [ToolBarItem] нет
ARX0005 | ArxisStudio | Warning | Класс помечен [ToolBarItem], а в манифесте его нет
ARX0006 | ArxisStudio | Warning | Виджет Avalonia в разметке плагина: строить нужно на контролах ArxisStudio.Controls
ARX0007 | ArxisStudio | Warning | Тег манифеста написан не так, как студия его прочтёт
ARX0008 | ArxisStudio | Warning | Число вместо значения темы в разметке расширения: отступ, кегль или цвет темы называют ресурсом
ARX0009 | ArxisStudio | Warning | Ресурс темы назван не тем именем: цвет там, где нужна кисть, имя темы до SDK 6.0 или ключ Ax*, которого в теме нет, — в разметке и в коде
ARX0010 | ArxisStudio | Warning | Число вместо значения темы в коде расширения: new Thickness, Spacing, FontSize, Color.Parse
ARX0011 | ArxisStudio | Warning | Запись значка в манифесте студия не разберёт: имени нет в наборе, забыта приставка arxis: или указан файл картинки
ARX0012 | ArxisStudio | Warning | Словарь расширения студия не прочтёт: возьмёт пустым, и подписи покажутся ключами
ARX0013 | ArxisStudio | Warning | Кнопка со значком без имени: экранный диктор прочтёт «кнопка» и замолчит — в разметке и в коде
ARX0014 | ArxisStudio | Warning | Расширение правит стили или ресурсы всего приложения: перекрасит студию и переживёт свою выгрузку
ARX0015 | ArxisStudio | Warning | Запись пункта создания студия прочтёт не так, как задумано: незнакомый вид или правило имени, лишняя переменная, путь наружу, файлы не у того вида, пункт с кодом без onNewItem: у спящего расширения
ARX0016 | ArxisStudio | Warning | Пункт создания с кодом объявлен, а класса с [NewItem] нет
ARX0017 | ArxisStudio | Warning | Класс помечен [NewItem], а пункта с кодом в манифесте нет
