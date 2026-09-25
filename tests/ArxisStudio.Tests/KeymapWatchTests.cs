using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Слежение за <c>keymap.json</c>: правка файла при открытой студии слышна, и слышна один раз.
/// </summary>
/// <remarks>
/// Файловая система здесь настоящая, и время тоже: наблюдатель ждёт, пока файл затихнет, по часам, а не
/// по диспетчеру интерфейса. Тишина, после которой проверяется, что не услышано ничего, взята с
/// запасом против паузы наблюдателя.
/// </remarks>
public class KeymapWatchTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Silence = KeymapWatch.Quiet * 4;

    private readonly string _root = TempFolder.Create("keymap-watch");

    private string File => Path.Combine(_root, "keymap.json");

    public void Dispose()
    {
        TempFolder.Erase(_root);

        GC.SuppressFinalize(this);
    }

    /// <summary>Заведённый файл слышен: его могло не быть при запуске.</summary>
    [Fact]
    public void A_file_made_while_watching_is_heard()
    {
        using var heard = new SemaphoreSlim(0);
        using var watch = new KeymapWatch(File, () => heard.Release());

        Assert.True(watch.Watching, "за папкой не следят");

        System.IO.File.WriteAllText(File, "{}");

        Assert.True(heard.Wait(Patience, TestContext.Current.CancellationToken), "заведённый файл не услышан");
    }

    /// <summary>Правка сохранённого файла слышна один раз, сколькими бы записями редактор её ни сделал.</summary>
    /// <remarks>
    /// Редактор обнуляет файл и пишет его частями. Отзовись слежение на каждую запись, студия раздавала
    /// бы сочетания по полупустому файлу.
    /// </remarks>
    [Fact]
    public void A_burst_of_writes_is_heard_once()
    {
        System.IO.File.WriteAllText(File, "{}");

        var count = 0;

        using var heard = new SemaphoreSlim(0);
        using var watch = new KeymapWatch(File, () =>
        {
            Interlocked.Increment(ref count);
            heard.Release();
        });

        System.IO.File.WriteAllText(File, string.Empty);
        System.IO.File.WriteAllText(File, "{ \"studio.palette\": ");
        System.IO.File.WriteAllText(File, "{ \"studio.palette\": \"Ctrl+Alt+P\" }");

        Assert.True(heard.Wait(Patience, TestContext.Current.CancellationToken), "правка не услышана");
        Assert.False(heard.Wait(Silence, TestContext.Current.CancellationToken), "одна правка услышана дважды");
        Assert.Equal(1, count);
    }

    /// <summary>Убранный файл слышен: без него сочетания возвращаются к тем, что по умолчанию.</summary>
    [Fact]
    public void A_removed_file_is_heard()
    {
        System.IO.File.WriteAllText(File, "{}");

        using var heard = new SemaphoreSlim(0);
        using var watch = new KeymapWatch(File, () => heard.Release());

        System.IO.File.Delete(File);

        Assert.True(heard.Wait(Patience, TestContext.Current.CancellationToken), "убранный файл не услышан");
    }

    /// <summary>Файл, сохранённый переименованием временного, слышен так же, как записанный на месте.</summary>
    [Fact]
    public void A_file_saved_by_renaming_a_temporary_one_is_heard()
    {
        var temporary = Path.Combine(_root, "keymap.json.tmp");

        using var heard = new SemaphoreSlim(0);
        using var watch = new KeymapWatch(File, () => heard.Release());

        System.IO.File.WriteAllText(temporary, "{}");
        System.IO.File.Move(temporary, File);

        Assert.True(heard.Wait(Patience, TestContext.Current.CancellationToken), "файл, пришедший переименованием, не услышан");
    }

    /// <summary>Сосед по папке не слышен: настройки и раскладка пишутся туда же и часто.</summary>
    [Fact]
    public void A_neighbour_file_is_not_heard()
    {
        using var heard = new SemaphoreSlim(0);
        using var watch = new KeymapWatch(File, () => heard.Release());

        System.IO.File.WriteAllText(Path.Combine(_root, "layout.json"), "{}");

        Assert.False(heard.Wait(Silence, TestContext.Current.CancellationToken), "запись соседнего файла услышана как правка сочетаний");
    }

    /// <summary>После прощания не слышно ничего: студия закрыта, и раздавать сочетания некому.</summary>
    [Fact]
    public void Nothing_is_heard_after_the_watch_is_let_go()
    {
        using var heard = new SemaphoreSlim(0);

        var watch = new KeymapWatch(File, () => heard.Release());

        watch.Dispose();

        System.IO.File.WriteAllText(File, "{}");

        Assert.False(heard.Wait(Silence, TestContext.Current.CancellationToken), "отпущенное слежение услышало правку");
    }
}
