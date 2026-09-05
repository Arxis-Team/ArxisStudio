using System.Runtime.CompilerServices;
using Avalonia.Metadata;

// Тот же словарь разметки, что и у библиотек студии:
// https://github.com/Arxis-Team/ArxisStudio. Отсюда в разметку расширения
// приходит {Text ключ} — и приходит по тому же адресу, что и контролы, чтобы
// автору панели хватало одного xmlns.
//
// Объявлено одно пространство имён из двух: модель манифеста
// (ArxisStudio.Sdk.Plugins) — чистые данные, из разметки её не пишут.
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio", "ArxisStudio.Sdk")]

// Префикс, который предложит инструмент, когда адрес объявляют псевдонимом.
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio", "ax")]

// Шов «сборка расширения → его словарь строк» держит хозяин, поднимающий
// расширения. В контракте плагина этого шва быть не должно: автору достаётся
// одно {Text ключ}, а кто и когда положил связь — не его забота.
[assembly: InternalsVisibleTo("ArxisStudio.Extensibility")]
[assembly: InternalsVisibleTo("ArxisStudio.Tests")]
