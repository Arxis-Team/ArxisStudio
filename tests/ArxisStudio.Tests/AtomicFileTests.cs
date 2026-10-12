using System.Text;
using ArxisStudio.Shell;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Запись файла данных человека целиком или никак.
/// </summary>
/// <remarks>
/// Сам обрыв записи тестом не воспроизвести — для этого надо уронить процесс посреди неё; держит его
/// устройство: текст ложится в черновик и встаёт на место переносом. Здесь проверено, что видно снаружи:
/// записанное ровно то, что дали, черновиков не остаётся, а несостоявшаяся запись прежний файл не трогает
/// (запись 383).
/// </remarks>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _home = TempFolder.Create("atomic");

    public void Dispose() => TempFolder.Erase(_home, strict: true);

    /// <summary>Запись заводит папку, ложится текстом без метки порядка байт и черновиков не оставляет.</summary>
    [Fact]
    public void A_write_lands_whole_and_leaves_no_drafts()
    {
        var file = Path.Combine(_home, "Вложенная", "settings.json");

        AtomicFile.WriteAllText(file, """{ "theme": "тёмная" }""");
        AtomicFile.WriteAllText(file, """{ "theme": "светлая" }""");

        Assert.Equal(Encoding.UTF8.GetBytes("""{ "theme": "светлая" }"""), File.ReadAllBytes(file));
        Assert.Equal([file], Directory.GetFiles(Path.GetDirectoryName(file)!));
    }

    /// <summary>Несостоявшаяся запись оставляет прежний файл как был и уносит свой черновик.</summary>
    [Fact]
    public void A_failed_write_leaves_the_previous_file_and_no_draft()
    {
        var file = Path.Combine(_home, "settings.json");

        File.WriteAllText(file, "прежнее");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        try
        {
            Assert.ThrowsAny<UnauthorizedAccessException>(() => AtomicFile.WriteAllText(file, "новое"));

            Assert.Equal("прежнее", File.ReadAllText(file));
            Assert.Equal([file], Directory.GetFiles(_home));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
