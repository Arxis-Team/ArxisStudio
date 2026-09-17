using System.Text.RegularExpressions;

namespace ArxisStudio.Extensibility;

/// <summary>
/// Дороги, которые называет чужой манифест: что из них студия готова открыть.
/// </summary>
/// <remarks>
/// Манифест — файл из чужих рук, и всё, что в нём похоже на путь, путём и становится: entry-сборка,
/// контракт, словарь, значок, а идентификатор — именем папки установленного плагина. Проверка у
/// всех этих мест одна, и стоит она здесь. Пока она жила у контрактов и у распаковки архива,
/// остальные дороги — идентификатор, словари, значок — обходились без неё вовсе, и
/// <c>"id": ".."</c> превращал установку в рекурсивное удаление папки данных.
/// </remarks>
public static partial class PluginPaths
{
    /// <summary>
    /// Полный путь объявленного файла, если он остаётся внутри папки.
    /// </summary>
    /// <param name="directory">Папка, за пределы которой выходить нельзя.</param>
    /// <param name="declared">Путь, как он записан в манифесте или в архиве.</param>
    /// <returns>Полный путь; <c>null</c> — путь пуст, не разобрался или уводит наружу.</returns>
    /// <remarks>
    /// <see cref="Path.Combine(string, string)"/> здесь не защита: корневой второй аргумент он
    /// возвращает как есть, отбросив папку, а <c>..</c> уводит куда угодно. Поэтому путь сперва
    /// приводится к полному и только потом сверяется с папкой — вместе с разделителем на конце,
    /// иначе <c>plugins\abc</c> сошла бы за внутренность <c>plugins\ab</c>.
    /// <para>
    /// Сравнение порядковое: обе стороны получены из одной и той же папки одним и тем же
    /// <see cref="Path.GetFullPath(string)"/>, и регистр у них расходиться не может.
    /// </para>
    /// </remarks>
    public static string? Inside(string directory, string? declared)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (string.IsNullOrWhiteSpace(declared))
            return null;

        string root, full;

        try
        {
            // Разделитель на конце у папки бывает свой — он всегда есть у
            // AppContext.BaseDirectory, папки модуля без файла, — и второй,
            // приклеенный сверху, отказал бы модулю, объявившему контракт.
            var folder = Path.GetFullPath(directory);

            root = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
            full = Path.GetFullPath(Path.Combine(folder, declared));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
    }

    /// <summary>
    /// Годится ли идентификатор именем папки.
    /// </summary>
    /// <param name="id">Идентификатор плагина или имя сборки.</param>
    /// <remarks>
    /// Алфавит тот же, что у сборки: упаковка плагина (<c>AXP1003</c>) и раскладка модулей
    /// (<c>AXL1003</c>) принимают <c>[A-Za-z0-9._-]+</c>, и плагин, собранный SDK, под это правило
    /// подходит всегда. Сверх алфавита отсеивается точка на конце. Этим сняты <c>.</c> и <c>..</c> —
    /// в алфавит они умещаются, а именами папок не являются, — и заодно имя вида <c>abc.</c>:
    /// Windows срезает такую точку молча, и плагин встал бы в папку <c>abc</c>, то есть в чужую.
    /// </remarks>
    public static bool IsFolderName(string? id) =>
        id is { Length: > 0 } && FolderName().IsMatch(id) && !id.EndsWith('.');

    [GeneratedRegex(@"^[A-Za-z0-9._-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex FolderName();
}
