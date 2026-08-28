using ArxisStudio.Extensibility;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Services;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Значок плагина: единственное место, где студия открывает картинку с диска.
/// </summary>
/// <remarks>
/// Картинку приносит посторонний, поэтому правил здесь больше, чем у прочих
/// файлов плагина: предел на размер, декодирование сразу в нужную величину и
/// общий значок вместо всего, что не прочиталось.
/// </remarks>
public class PluginIconTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        foreach (var folder in _folders.Where(Directory.Exists))
            Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Значок объявлен и лежит на месте — путь есть.</summary>
    [Fact]
    public void A_declared_icon_that_exists_is_found()
    {
        var plugin = Plugin("assets/icon.png", written: true);

        Assert.EndsWith("icon.png", plugin.IconPath!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Объявленного, но потерянного значка всё равно что нет.
    /// </summary>
    /// <remarks>
    /// Карточка покажет общий значок — плагин без картинки должен выглядеть
    /// плагином, а не дырой в списке.
    /// </remarks>
    [Fact]
    public void A_declared_icon_without_the_file_is_as_good_as_none()
    {
        var plugin = Plugin("assets/icon.png", written: false);

        Assert.Null(plugin.IconPath);
    }

    /// <summary>Не объявлен — и не ищется.</summary>
    [Fact]
    public void A_plugin_without_an_icon_declares_none()
    {
        var plugin = Plugin(icon: null, written: false);

        Assert.Null(plugin.IconPath);
    }

    /// <summary>Пустого пути хватает, чтобы не ходить на диск.</summary>
    [Fact]
    public void Nothing_is_loaded_without_a_path()
    {
        Assert.Null(new PluginIcons().Of(null));
        Assert.Null(new PluginIcons().Of(string.Empty));
    }

    /// <summary>
    /// Годная картинка сверх предела всё равно не читается.
    /// </summary>
    /// <remarks>
    /// Проверяется именно предел, а не «мусор не картинка»: файл тот же
    /// самый, и различает их только довесок. Предел стоит до декодера
    /// намеренно — «значок» в сто мегабайт незачем даже открывать, а
    /// декодирование такого файла это уже потраченная память.
    /// </remarks>
    [AvaloniaFact]
    public void A_readable_image_over_the_limit_is_refused_anyway()
    {
        var folder = Folder();
        var small = Path.Combine(folder, "small.png");
        var big = Path.Combine(folder, "big.png");
        var icon = File.ReadAllBytes(Path.Combine(Sample(), "assets", "icon.png"));

        File.WriteAllBytes(small, icon);
        File.WriteAllBytes(big, [.. icon, .. new byte[PluginIcons.MaxBytes]]);

        var icons = new PluginIcons();

        Assert.NotNull(icons.Of(small));
        Assert.Null(icons.Of(big));
    }

    /// <summary>Значок примера плагина читается по-настоящему.</summary>
    /// <remarks>
    /// Проверяется тот самый файл, который едет в пакете: испорченный или
    /// пересохранённый не в тот формат, он показался бы человеку общим
    /// значком, и заметили бы это не мы.
    /// </remarks>
    [AvaloniaFact]
    public void The_icon_of_the_sample_plugin_is_readable()
    {
        var icon = new PluginIcons().Of(Path.Combine(Sample(), "assets", "icon.png"));

        Assert.NotNull(icon);
        Assert.Equal(PluginIcons.Width, icon.PixelSize.Width);
    }

    /// <summary>Файл не картинка — значка нет, и студия жива.</summary>
    [AvaloniaFact]
    public void A_file_that_is_not_an_image_gives_no_icon()
    {
        var path = Path.Combine(Folder(), "icon.png");

        File.WriteAllText(path, "это не картинка");

        Assert.Null(new PluginIcons().Of(path));
    }

    /// <summary>
    /// Прочитанное помнится, а исправленное перечитывается.
    /// </summary>
    /// <remarks>
    /// Список плагинов пересобирается при каждом заходе в менеджер: читай мы
    /// файл заново каждый раз, растры копились бы горстями. Но правку значка
    /// автор должен увидеть, не перезапуская студию.
    /// </remarks>
    [AvaloniaFact]
    public void What_was_read_is_remembered_until_the_file_changes()
    {
        var icons = new PluginIcons();
        var path = Path.Combine(Folder(), "icon.png");

        File.Copy(Path.Combine(Sample(), "assets", "icon.png"), path);

        var first = icons.Of(path);

        Assert.Same(first, icons.Of(path));

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.NotSame(first, icons.Of(path));
    }

    private string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"arxis-icon-{Guid.NewGuid():N}");

        _folders.Add(folder);
        Directory.CreateDirectory(folder);

        return folder;
    }

    private InstalledPlugin Plugin(string? icon, bool written)
    {
        var folder = Folder();

        if (written)
        {
            Directory.CreateDirectory(Path.Combine(folder, "assets"));
            File.WriteAllBytes(Path.Combine(folder, "assets", "icon.png"), [1, 2, 3]);
        }

        return new InstalledPlugin(
            folder,
            new PluginManifest { Id = "arxis.probe", Name = "Проба", Icon = icon },
            null,
            IsEnabled: true);
    }

    private static string Sample()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Plugins", "Arxis.HelloPlugin");

            if (File.Exists(Path.Combine(candidate, "Arxis.HelloPlugin.csproj")))
                return candidate;
        }

        throw new InvalidOperationException("Не найден пример плагина src/Plugins/Arxis.HelloPlugin");
    }
}
