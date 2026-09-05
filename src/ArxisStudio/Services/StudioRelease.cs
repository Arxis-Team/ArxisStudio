using System.Reflection;
using System.Runtime.InteropServices;

namespace ArxisStudio.Services;

/// <summary>
/// Чем эта студия себя называет: релиз, сборка, среда, на которой работает.
/// </summary>
/// <remarks>
/// Всё читается из атрибутов сборки, а не пишется строкой в разметке: заставка,
/// «О программе» и журнал говорят о версии одно и то же только тогда, когда
/// берут её из одного места, а место это заполняет сборка.
/// <para>
/// Считается один раз: отражение стоит миллисекунды, а спрашивают отсюда на
/// самом узком месте — пока человек смотрит на заставку и больше ни на что.
/// </para>
/// </remarks>
public static class StudioRelease
{
    private static readonly Assembly Studio = typeof(StudioRelease).Assembly;

    /// <summary>Сборка — то, чем версию называет загрузчик: <c>0.1.1</c>.</summary>
    public static string Build { get; } = Version3(Studio);

    /// <summary>
    /// Релиз — то, чем версию называет человек: <c>2026.1</c>.
    /// </summary>
    /// <remarks>
    /// Отдельно от <see cref="Build"/>, потому что это разные числа с разной
    /// жизнью: релиз меняется раз в квартал и стоит на коробке, сборка меняется
    /// каждой правкой и нужна в отчёте о сбое.
    /// <para>
    /// Объявлено после сборки, а не до: запасной ответ здесь — она сама, а
    /// статические свойства считаются в порядке объявления, и спрошенная раньше
    /// времени сборка ответила бы пустотой.
    /// </para>
    /// </remarks>
    public static string Version { get; } = Metadata("Release") ?? Build;

    /// <summary>Кому принадлежат права: <c>© 2026 Arxis</c>.</summary>
    public static string Copyright { get; } =
        Studio.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>
    /// На чём построен интерфейс: <c>Avalonia 12.1.1</c>.
    /// </summary>
    /// <remarks>
    /// Спрашивается у самой Avalonia, а не у файла версий пакетов: в отчёте о
    /// сбое важно, какая версия работает, а не какая была заказана.
    /// </remarks>
    public static string Toolkit { get; } = $"Avalonia {Version3(typeof(Avalonia.Application).Assembly)}";

    /// <summary>Где работает: <c>.NET 10 · x64</c>.</summary>
    public static string Runtime { get; } =
        $"{Framework()} · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

    /// <summary>Собственное значение из метаданных сборки.</summary>
    /// <param name="key">Ключ, под которым его положила сборка.</param>
    private static string? Metadata(string key) => Studio
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(found => string.Equals(found.Key, key, StringComparison.Ordinal))
        ?.Value is { Length: > 0 } value ? value : null;

    /// <summary>Версия сборки в трёх числах — четвёртое человеку не говорит ничего.</summary>
    private static string Version3(Assembly assembly) =>
        assembly.GetName().Version is { } version ? version.ToString(3) : "?";

    /// <summary>
    /// Имя среды: <c>.NET 10</c>.
    /// </summary>
    /// <remarks>
    /// Платформа отдаёт полное описание вида <c>.NET 10.0.3</c>. Заплатка в
    /// подвале заставки — это про поколение среды, а не про её патч: место там
    /// на одну строку, и точность до третьего числа его не стоит.
    /// </remarks>
    private static string Framework()
    {
        var described = RuntimeInformation.FrameworkDescription;
        var dot = described.IndexOf('.', described.IndexOf(' ') + 1);

        return dot > 0 ? described[..dot] : described;
    }
}
