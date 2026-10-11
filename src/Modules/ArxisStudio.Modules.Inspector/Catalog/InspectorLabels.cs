using System.Text;
using ArxisStudio.Inspector;

namespace ArxisStudio.Modules.Inspector.Catalog;

/// <summary>
/// Подпись строки словами, как <c>NicifyVariableName</c> у Unity: «HorizontalAlignment» — «Horizontal Alignment»,
/// «ZIndex» — «Z Index».
/// </summary>
/// <remarks>
/// <para>
/// Слова расходятся на границе регистра — строчная перед прописной, — и на конце аббревиатуры: «IsUIElement» — «Is UI
/// Element». Цифры отходят от букв, а прописная за цифрой остаётся с ней: «Grid2D» — «Grid 2D». Первая буква —
/// прописная.
/// </para>
/// <para>
/// У присоединённого члена владельца нет: «Name», а не «AutomationProperties.Name», — раздел и так говорит, к чему он.
/// Директивы и члены для дизайна (<c>x:Name</c>, <c>d:DesignWidth</c>) — как написаны: это имена разметки, а не
/// свойства. Подпись, которую дали источник или метаданные, остаётся их подписью; точное имя XAML остаётся
/// идентификатором — его показывает подсказка подписи и находит поиск.
/// </para>
/// </remarks>
internal static class InspectorLabels
{
    /// <summary>Подпись свойства: данная источником или метаданными, а без неё — его имя словами.</summary>
    /// <param name="property">Свойство.</param>
    public static string Of(InspectorProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (property.DisplayName is { Length: > 0 } given)
            return given;

        var id = property.Id;

        if (id.Contains(':', StringComparison.Ordinal))
            return id;

        var dot = id.LastIndexOf('.');

        return Words(dot > 0 && dot < id.Length - 1 ? id[(dot + 1)..] : id);
    }

    /// <summary>Имя члена словами: границы регистра и аббревиатур, цифры отдельно, первая — прописная.</summary>
    /// <param name="name">Имя члена.</param>
    public static string Words(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var text = name.TrimStart('_');

        if (text.StartsWith("m_", StringComparison.Ordinal))
            text = text[2..];

        if (text.Length == 0)
            return name;

        var words = new StringBuilder(text.Length + 8);

        for (var at = 0; at < text.Length; at++)
        {
            if (at > 0 && Boundary(text, at))
                words.Append(' ');

            words.Append(at == 0 ? char.ToUpperInvariant(text[at]) : text[at]);
        }

        return words.ToString();
    }

    /// <summary>Начинается ли здесь новое слово.</summary>
    private static bool Boundary(string text, int at)
    {
        var previous = text[at - 1];
        var current = text[at];

        if (char.IsUpper(current))
        {
            // Строчная перед прописной — граница. После аббревиатуры и цифры граница — только перед словом с прописной:
            // «UIElement» — «UI Element», «Vector3Int» — «Vector 3 Int», а «Grid2D» — «Grid 2D».
            var word = at + 1 < text.Length && char.IsLower(text[at + 1]);

            return char.IsLower(previous) || ((char.IsUpper(previous) || char.IsDigit(previous)) && word);
        }

        return char.IsDigit(current) && char.IsLetter(previous);
    }
}
