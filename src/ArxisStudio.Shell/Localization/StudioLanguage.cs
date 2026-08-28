namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Язык, который студия умеет показать.
/// </summary>
/// <remarks>
/// Список собирается из того, что нашлось: встроенные словари плюс файлы в
/// папках со словарями. Прибитого гвоздями перечня языков нет — иначе
/// положенный рядом словарь работал бы, но не показывался бы в настройках.
/// </remarks>
/// <param name="Code">Код языка — он же имя файла словаря.</param>
/// <param name="Name">
/// Название на нём самом: «Русский», «English». Берётся из самого словаря
/// (ключ <c>language.name</c>), а без него — из кода.
/// </param>
public sealed record StudioLanguage(string Code, string Name);
