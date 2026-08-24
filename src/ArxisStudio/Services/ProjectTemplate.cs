namespace ArxisStudio.Services;

/// <summary>
/// Шаблон проекта, установленный в <c>dotnet new</c>. Студия своих шаблонов не
/// держит: пользователь ставит их обычным <c>dotnet new install</c>, а студия
/// показывает то, что уже стоит в системе, — как это делают другие IDE.
/// </summary>
/// <param name="Name">Отображаемое имя шаблона.</param>
/// <param name="ShortName">Короткое имя для командной строки, например <c>avalonia.mvvm</c>.</param>
/// <param name="Languages">Языки, которые шаблон поддерживает.</param>
/// <param name="Tags">Теги шаблона: <c>Desktop/Xaml/Avalonia</c> и подобные.</param>
public sealed record ProjectTemplate(
    string Name,
    string ShortName,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Tags)
{
    /// <summary>Шаблон делает Avalonia-приложение.</summary>
    public bool IsAvalonia =>
        Tags.Any(t => t.Contains("Avalonia", StringComparison.OrdinalIgnoreCase)) ||
        ShortName.StartsWith("avalonia", StringComparison.OrdinalIgnoreCase);

    /// <summary>Верхнеуровневая категория для фильтров: первый тег.</summary>
    public string Category => Tags.Count > 0 ? Tags[0] : "Other";

    /// <summary>Теги одной строкой — то, что видно на карточке.</summary>
    public string TagLine => string.Join(" · ", Tags);
}
