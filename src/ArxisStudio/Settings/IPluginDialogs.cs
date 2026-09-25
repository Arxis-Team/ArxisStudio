namespace ArxisStudio.Settings;

/// <summary>
/// Чем страница плагинов просит окно: выбрать папку, выбрать архив, задать
/// вопрос, показать каталог в проводнике.
/// </summary>
/// <remarks>
/// Диалоги — дело окна, а не страницы: у страницы нет ни владельца для
/// модального вопроса, ни доступа к хранилищу файлов платформы. Интерфейсом, а
/// не четырьмя делегатами, — вместе они одно: «спроси человека», и подменяются
/// в тесте тоже вместе.
/// </remarks>
public interface IPluginDialogs
{
    /// <summary>Просит выбрать папку плагина; null — передумали.</summary>
    /// <param name="title">Заголовок окна выбора.</param>
    Task<string?> AskFolderAsync(string title);

    /// <summary>Просит выбрать архив <c>.axplugin</c>; null — передумали.</summary>
    /// <param name="title">Заголовок окна выбора.</param>
    Task<string?> AskArchiveAsync(string title);

    /// <summary>Задаёт вопрос с двумя ответами; <c>true</c> — согласились.</summary>
    /// <param name="title">Заголовок вопроса.</param>
    /// <param name="message">Сам вопрос.</param>
    /// <param name="confirm">Подпись согласия.</param>
    /// <param name="danger">Красить ли согласие как опасное.</param>
    Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger);

    /// <summary>Показывает путь средствами системы.</summary>
    /// <param name="path">Что показать.</param>
    void Reveal(string path);
}
