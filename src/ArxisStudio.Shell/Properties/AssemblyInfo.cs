using Avalonia.Metadata;

// Тот же словарь разметки, что и у библиотек студии:
// https://github.com/Arxis-Team/ArxisStudio.
//
// Два пространства имён: оболочка окна и локализация. Второе — ради
// расширения разметки {Loc ключ}: подпись, поставленная им, переживает смену
// языка, и в разметке студии оно встречается чаще любого контрола.
//
// Настройки сюда не попали намеренно: из разметки их не пишут.
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio", "ArxisStudio.Shell")]
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio", "ArxisStudio.Shell.Localization")]

// Префикс, который предложит инструмент, когда адрес объявляют псевдонимом.
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio", "ax")]
