namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>
/// Поле, которое открывается текстом записи: «Изменить как текст» в меню строки — у поля преобразования, где поля
/// передают не всякую запись.
/// </summary>
/// <remarks>Знает о нём только модуль: поле плагина открывается текстом своим путём.</remarks>
internal interface ITextualField
{
    /// <summary>Открывает запись текстом и ставит в него каретку.</summary>
    /// <returns>Открылась ли.</returns>
    bool EditAsText();
}
