using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.UiDesigner.Snapshots;

/// <summary>
/// Снимки форм: картинка формы, какой её последний раз показала живая вкладка, и то, с чего она снята.
/// </summary>
/// <remarks>
/// <para>
/// <b>Снимок — кэш, а не документ.</b> Он пересоздаётся при каждом показе формы и привязан к машине —
/// шрифты, масштаб, — поэтому лежит в машинной папке студии, как локальная история, а не рядом с
/// решением: в репозиторий человека ему незачем. Переменная среды <see cref="EnvironmentVariable"/>
/// переносит папку, а <c>0</c> выключает снимки — так живут тесты.
/// </para>
/// <para>
/// <b>Свежесть сверяется с текстом, а не со временем записи.</b> Рядом с картинкой лежит, с чего она
/// снята: отпечаток текста формы и её приложения. Форма, переписанная снаружи, — Rider, слияние ветки, —
/// получает отметку «устарел», а пересохранённая без перемен — нет. Правка кода контролов проекта снимок
/// не старит: её видно только живой форме, и следующий показ снимет заново.
/// </para>
/// <para>
/// Картинка и сведения пишутся каждая через временный файл и подмену, сведения — последними: читающий,
/// не нашедший сведений, считает, что снимка нет, а не берёт полуписаную картинку.
/// </para>
/// </remarks>
/// <param name="folder">Папка снимков.</param>
internal sealed class FormSnapshots(string folder)
{
    /// <summary>Переменная среды: папка снимков или <c>0</c> — форм не снимать.</summary>
    public const string EnvironmentVariable = "ARXIS_PREVIEWS";

    /// <summary>
    /// Длинная сторона снимка в точках: крупнейшая плитка окна проекта, 128 точек, при масштабе экрана
    /// 300 %. Это разрешение картинки, а не размер интерфейса: показывающий её уменьшает.
    /// </summary>
    public const int Pixels = 384;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Папка снимков.</summary>
    public string Folder => folder;

    /// <summary>Хранилище по шву модуля, переменной среды или в машинной папке студии; null — снимки выключены.</summary>
    /// <param name="context">Контекст модуля.</param>
    public static FormSnapshots? For(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return For(context.GetService<UiDesignerOptions>());
    }

    /// <summary>Хранилище по шву модуля, переменной среды или в машинной папке студии; null — снимки выключены.</summary>
    /// <param name="options">Шов модуля; null — продуктовые умолчания.</param>
    public static FormSnapshots? For(UiDesignerOptions? options)
    {
        var folder = options?.SnapshotsFolder
            ?? Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                "ArxisStudio",
                "Previews");

        return string.IsNullOrWhiteSpace(folder) || folder == "0" ? null : new FormSnapshots(folder);
    }

    /// <summary>Отпечаток текста: SHA-256 его UTF-8.</summary>
    /// <param name="text">Текст.</param>
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Пишет снимок формы — в фоне.</summary>
    /// <param name="snapshot">С чего снято.</param>
    /// <param name="picture">Картинка, PNG.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <remarks>Отпечаток приложения считается здесь же, с диска: в потоке интерфейса файл не читают.</remarks>
    public Task WriteAsync(FormSnapshot snapshot, byte[] picture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(picture);

        return Task.Run(() =>
        {
            var recorded = snapshot.Application is { } application && File.Exists(application)
                ? snapshot with { ApplicationText = Hash(File.ReadAllText(application)) }
                : snapshot;
            var key = KeyOf(snapshot.Form);

            Directory.CreateDirectory(folder);
            Replace(Path.Combine(folder, key + ".png"), picture);
            Replace(Path.Combine(folder, key + ".json"), JsonSerializer.SerializeToUtf8Bytes(recorded, Json));
        }, cancellationToken);
    }

    /// <summary>Снимок формы и отвечает ли он ей нынешней — в фоне.</summary>
    /// <param name="formPath">Путь к форме.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Превью; null — снимка нет или он не читается.</returns>
    public Task<FilePreview?> ReadAsync(string formPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(formPath);

        return Task.Run(() => Read(formPath), cancellationToken);
    }

    private FilePreview? Read(string formPath)
    {
        try
        {
            var key = KeyOf(formPath);
            var about = Path.Combine(folder, key + ".json");
            var image = Path.Combine(folder, key + ".png");

            if (!File.Exists(about) || !File.Exists(image)
                || JsonSerializer.Deserialize<FormSnapshot>(File.ReadAllBytes(about), Json) is not { } snapshot
                || !string.Equals(snapshot.Form, formPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new FilePreview(File.ReadAllBytes(image)) { IsStale = IsStale(snapshot) };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Менялись ли форма или её приложение после снимка.</summary>
    private static bool IsStale(FormSnapshot snapshot)
    {
        if (!File.Exists(snapshot.Form) || Hash(File.ReadAllText(snapshot.Form)) != snapshot.Text)
            return true;

        if (snapshot.Application is not { } application)
            return false;

        return !File.Exists(application) || Hash(File.ReadAllText(application)) != snapshot.ApplicationText;
    }

    /// <summary>Имя снимка: отпечаток пути без учёта регистра — путь на Windows его не различает.</summary>
    private static string KeyOf(string formPath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(formPath.ToUpperInvariant())))[..32];

    /// <summary>Пишет файл через временный и подмену: читающий видит прежний или новый, но не половину.</summary>
    private static void Replace(string path, byte[] content)
    {
        var temporary = path + ".tmp";

        File.WriteAllBytes(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>С чего снят снимок формы.</summary>
/// <param name="Form">Путь к форме.</param>
/// <param name="Text">Отпечаток её текста (<see cref="FormSnapshots.Hash"/>).</param>
/// <param name="Application">Путь к её приложению; null — приложения нет.</param>
/// <param name="ApplicationText">Отпечаток текста приложения; null — не снят.</param>
/// <param name="Theme">Вариант темы, в котором снято: <c>Light</c>, <c>Dark</c>, <c>Default</c>.</param>
/// <param name="Width">Ширина картинки в точках.</param>
/// <param name="Height">Высота картинки в точках.</param>
internal sealed record FormSnapshot(
    string Form,
    string Text,
    string? Application,
    string? ApplicationText,
    string Theme,
    int Width,
    int Height);
