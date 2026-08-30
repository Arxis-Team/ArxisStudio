using ArxisStudio.Docking;
using ArxisStudio.Services;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Файл раскладки: чтение при запуске и запись при изменении.
/// </summary>
public class DockLayoutStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"arxis-layout-{Guid.NewGuid():N}");

    public DockLayoutStoreTests() => Directory.CreateDirectory(_directory);

    private string File => Path.Combine(_directory, "layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Записанная раскладка читается обратно.</summary>
    [Fact]
    public void What_was_written_is_what_is_read()
    {
        var store = new DockLayoutStore(File);

        Assert.Null(store.Save(Sample()));

        var layout = store.Load(out var complaint);

        Assert.Null(complaint);
        Assert.NotNull(layout);
        Assert.Equal("left", ((DockGroup)layout.Current!.Root).Id);
        Assert.Equal(["hello:tree"], ((DockGroup)layout.Current.Root).Items);
    }

    /// <summary>Файла нет — и это не повод жаловаться.</summary>
    /// <remarks>
    /// Первый запуск студии выглядит ровно так. Слово в журнале о том, что файла
    /// раскладки нет, человек прочитал бы как поломку.
    /// </remarks>
    [Fact]
    public void A_missing_file_is_not_a_complaint()
    {
        var store = new DockLayoutStore(File);

        Assert.Null(store.Load(out var complaint));
        Assert.Null(complaint);
        Assert.False(store.ReadOnly);
    }

    /// <summary>Испорченный файл не мешает запуску, но о себе говорит.</summary>
    [Fact]
    public void A_broken_file_does_not_stop_the_studio()
    {
        System.IO.File.WriteAllText(File, "не json вовсе");

        var store = new DockLayoutStore(File);

        Assert.Null(store.Load(out var complaint));
        Assert.NotNull(complaint);

        // Переписать его можно: терять там нечего.
        Assert.False(store.ReadOnly);
        Assert.Null(store.Save(Sample()));
        Assert.NotNull(store.Load(out _));
    }

    /// <summary>
    /// Файл от студии новее этой остаётся нетронутым.
    /// </summary>
    /// <remarks>
    /// Иначе человек, заглянувший в проект старой студией, потерял бы раскладку,
    /// собранную новой, — и узнал бы об этом, только вернувшись обратно.
    /// </remarks>
    [Fact]
    public void A_file_from_a_newer_studio_is_left_untouched()
    {
        var written = DockLayoutSerializer.Write(Sample())
            .Replace($"\"version\": {DockLayout.CurrentVersion}", "\"version\": 99", StringComparison.Ordinal);

        System.IO.File.WriteAllText(File, written);

        var store = new DockLayoutStore(File);

        Assert.Null(store.Load(out var complaint));
        Assert.NotNull(complaint);
        Assert.True(store.ReadOnly);

        Assert.Null(store.Save(Sample()));
        Assert.Equal(written, System.IO.File.ReadAllText(File));
    }

    private static DockLayout Sample() => new()
    {
        Active = DockLayout.DefaultName,
        Layouts = new Dictionary<string, DockWorkspace>(StringComparer.Ordinal)
        {
            [DockLayout.DefaultName] = new()
            {
                DocumentHome = "documents",
                Root = new DockGroup { Id = "left", Items = ["hello:tree"], Selected = "hello:tree" },
            },
        },
    };
}
