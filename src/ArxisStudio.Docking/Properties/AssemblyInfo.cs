using Avalonia.Metadata;

// Тот же словарь разметки, что и у библиотек студии:
// https://github.com/Arxis-Team/ArxisStudio. Движок докинга приносит свои
// контролы — группу вкладок и её стили, — и в разметке они стоят вперемешку с
// контролами студии, отдельным псевдонимом их не разделить.
[assembly: XmlnsDefinition("https://github.com/Arxis-Team/ArxisStudio", "ArxisStudio.Docking")]

// Префикс, который предложит инструмент, когда адрес объявляют псевдонимом.
[assembly: XmlnsPrefix("https://github.com/Arxis-Team/ArxisStudio", "ax")]
