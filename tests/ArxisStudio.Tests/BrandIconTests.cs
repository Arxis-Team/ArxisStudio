using ArxisStudio.Brand;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Значок приложения: лежащий в репозитории <c>.ico</c> — это знак, как он рисуется сейчас.
/// </summary>
/// <remarks>
/// Руками значок не рисуют: его рендерит <see cref="BrandIcon"/> из того же
/// знака, что стоит на заставке. Правка знака, не пересобравшая значок, падает
/// здесь, а не в панели задач у человека, — и тест сам говорит, как значок
/// пересобрать.
/// <para>
/// Пересборка — одна команда из терминала разработчика:
/// <c>ARXIS_WRITE_ICON=1 dotnet test --filter The_icon_is_the_mark_as_it_is_drawn_now</c>.
/// Тест перепишет файл и упадёт с просьбой закоммитить: молча переписанный
/// значок был бы правкой, которой никто не видел.
/// </para>
/// </remarks>
public class BrandIconTests
{
    private static readonly Uri Asset = new("avares://ArxisStudio.Shell/Assets/arxis.ico");

    /// <summary>В значке все размеры, которые спрашивает Windows, и каждый — PNG.</summary>
    [AvaloniaFact]
    public void The_icon_carries_every_size_windows_asks_for()
    {
        var entries = Entries(Committed());

        Assert.Equal(BrandIcon.Sizes, entries.Select(entry => entry.Size));
        Assert.All(entries, entry => Assert.True(
            entry.Image.AsSpan().StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]),
            $"картинка {entry.Size} — не PNG"));
    }

    /// <summary>Каждый размер значка — ровно то, что рисует знак сейчас.</summary>
    [AvaloniaFact]
    public void The_icon_is_the_mark_as_it_is_drawn_now()
    {
        if (Environment.GetEnvironmentVariable("ARXIS_WRITE_ICON") == "1")
        {
            var path = Repository.Path("src", "ArxisStudio.Shell", "Assets", "arxis.ico");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, BrandIcon.Ico());

            Assert.Fail($"значок перезаписан: {path}. Пересоберите студию и закоммитьте файл.");
        }

        foreach (var entry in Entries(Committed()))
        {
            Assert.True(
                entry.Image.AsSpan().SequenceEqual(BrandIcon.Png(entry.Size)),
                $"картинка {entry.Size} в значке разошлась со знаком. Пересоберите значок: " +
                "ARXIS_WRITE_ICON=1 dotnet test --filter The_icon_is_the_mark_as_it_is_drawn_now");
        }
    }

    /// <summary>
    /// Окно студии несёт значок сам, без упоминания в своей разметке.
    /// </summary>
    /// <remarks>
    /// Значок назначен общим стилем окна, а не каждому окну порознь: окон у студии
    /// много — заставка, приветствие, главное, настройки, оторванные панели,
    /// диалоги плагинов, — и забытое окно показало бы в панели задач родовой
    /// значок .NET.
    /// </remarks>
    [AvaloniaFact]
    public void A_studio_window_carries_the_icon_by_itself()
    {
        var window = new Window();

        window.Show();

        try
        {
            Assert.NotNull(window.Icon);
        }
        finally
        {
            window.Close();
        }
    }

    private static byte[] Committed()
    {
        using var stream = AssetLoader.Open(Asset);
        using var copy = new MemoryStream();

        stream.CopyTo(copy);

        return copy.ToArray();
    }

    /// <summary>Разбирает каталог <c>.ico</c>: размер записи и её картинку.</summary>
    private static List<(int Size, byte[] Image)> Entries(byte[] ico)
    {
        var count = BitConverter.ToUInt16(ico, 4);
        var entries = new List<(int, byte[])>();

        for (var index = 0; index < count; index++)
        {
            var record = 6 + index * 16;
            var side = ico[record] == 0 ? 256 : ico[record];
            var length = BitConverter.ToInt32(ico, record + 8);
            var offset = BitConverter.ToInt32(ico, record + 12);

            entries.Add((side, ico[offset..(offset + length)]));
        }

        return entries;
    }
}
