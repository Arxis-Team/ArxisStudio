namespace ArxisStudio.Extensibility;

/// <summary>
/// Папка теневых копий во временной папке системы: сборки грузятся из копии, а не из файла автора.
/// </summary>
/// <remarks>
/// Общий контекст держит загруженный файл открытым до конца процесса, выгружаемый — до сборки мусора.
/// Без копии автор не смог бы пересобрать плагин, не закрыв студию, а тест — прибрать за собой
/// временную папку.
/// <para>
/// Копии прежних запусков выметаются при первом обращении за процесс: файл, только что отпущенный
/// контекстом, ещё занят, и убрать его получается лишь в следующий раз. Занятое другой студией
/// остаётся — её копия, её право.
/// </para>
/// <para>
/// Папок две — у плагинов и у контрактов, — и выметались они двумя копиями одного свойства. Копии
/// разошлись: у контрактов флаг «выметено» ставился последним, у плагинов — первым, и сорвавшееся
/// создание папки запоминалось у плагинов как удавшееся на весь процесс.
/// </para>
/// </remarks>
/// <param name="name">Имя папки во временной папке системы.</param>
internal sealed class ShadowFolder(string name)
{
    private bool _swept;

    /// <summary>Путь к папке; при первом обращении за процесс она создаётся и выметается.</summary>
    public string Root
    {
        get
        {
            var root = Path.Combine(Path.GetTempPath(), name);

            if (_swept)
                return root;

            Directory.CreateDirectory(root);

            // Флаг ставится последним: выставь мы его раньше, второй вызов получил бы дорогу к
            // папке, которую ещё не создали, а сорвавшееся создание запомнилось бы как успешное.
            _swept = true;

            foreach (var stale in Directory.EnumerateFileSystemEntries(root))
            {
                try
                {
                    if (Directory.Exists(stale))
                        Directory.Delete(stale, recursive: true);
                    else
                        File.Delete(stale);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }

            return root;
        }
    }
}
