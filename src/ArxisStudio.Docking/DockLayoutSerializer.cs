using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArxisStudio.Docking;

/// <summary>
/// Почему раскладку не прочитали.
/// </summary>
/// <remarks>
/// Причина здесь не для журнала — движок не пишет текста для человека, — а для
/// решения. Испорченный файл можно смело перезаписать первым же сохранением;
/// файл новее известного перезаписывать нельзя, иначе человек, заглянувший в
/// проект старой студией, потеряет раскладку, собранную новой.
/// </remarks>
public enum DockLayoutProblem
{
    /// <summary>Прочитали.</summary>
    None,

    /// <summary>Не разобрали: не тот текст, не та форма, дыры в дереве.</summary>
    Unreadable,

    /// <summary>Версия формата новее той, что понимает эта студия.</summary>
    Newer,
}

/// <summary>
/// Раскладка в текст и обратно.
/// </summary>
/// <remarks>
/// Только перевод: ни путей, ни файлов, ни починки дерева. Файлом занимается
/// студия, починкой — <see cref="DockTree"/>; здесь решается ровно одно — можно
/// ли доверять тому, что пришло.
/// </remarks>
public static class DockLayoutSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // Отсутствующее поле оставляет значение по умолчанию, а вот явный null
        // в поле, которое null не допускает, — это не «значения нет», это ложь
        // о форме. Без этой строки такой null молча доезжает до отрисовки и
        // роняет её вдали от причины.
        RespectNullableAnnotations = true,
    };

    /// <summary>Переводит раскладку в текст.</summary>
    /// <param name="layout">Что записываем.</param>
    /// <returns>Текст файла раскладки.</returns>
    public static string Write(DockLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return JsonSerializer.Serialize(layout, Options);
    }

    /// <summary>Читает раскладку из текста.</summary>
    /// <param name="json">Текст файла.</param>
    /// <param name="problem">Почему не прочитали; <see cref="DockLayoutProblem.None"/> — прочитали.</param>
    /// <returns>Раскладка либо null.</returns>
    public static DockLayout? Read(string? json, out DockLayoutProblem problem)
    {
        problem = DockLayoutProblem.Unreadable;

        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            // Версию смотрим отдельным лёгким проходом, до разбора целого. Файл
            // новее известного по старым правилам всё равно не разберётся, и без
            // этой проверки он выглядел бы просто испорченным — а обходятся с
            // ними по-разному.
            if (Version(json) > DockLayout.CurrentVersion)
            {
                problem = DockLayoutProblem.Newer;
                return null;
            }

            if (JsonSerializer.Deserialize<DockLayout>(json, Options) is not { } layout)
                return null;

            if (!Sound(layout))
                return null;

            problem = DockLayoutProblem.None;

            return layout;
        }
        catch (JsonException)
        {
            // Испорченный файл раскладки — не повод не запускать студию.
            return null;
        }
    }

    /// <summary>Достаёт номер версии, не разбирая остального; 0 — поля нет.</summary>
    private static int Version(string json)
    {
        using var probe = JsonDocument.Parse(json);

        return probe.RootElement.ValueKind == JsonValueKind.Object
            && probe.RootElement.TryGetProperty("version", out var version)
            && version.TryGetInt32(out var number)
                ? number
                : 0;
    }

    /// <summary>Есть ли в поддереве дыры, о которые споткнётся обход.</summary>
    /// <remarks>
    /// Аннотации ловят null в полях, но не в элементах списка: <c>[null]</c> в
    /// детях доезжает до обхода и роняет его. Деление без детей своим писателем
    /// не создаётся никогда — прибирание такое схлопывает, — значит пришло не от
    /// нас, и доверять ему нечего.
    /// </remarks>
    /// <summary>
    /// Целая ли раскладка целиком.
    /// </summary>
    /// <remarks>
    /// Проверяются не только узлы, но и сами записи. <c>null</c> посреди списка
    /// окон или вместо набора — законный JSON, а разметка о необнуляемости на
    /// элементы списков и значения словарей не распространяется: без этой
    /// проверки испорченный файл ронял бы студию на старте разыменованием, а
    /// оно — не <see cref="JsonException"/>, и разбор его не ловит.
    /// </remarks>
    private static bool Sound(DockLayout layout) =>
        layout.Layouts.Values.All(workspace =>
            workspace is not null
            && Sound(workspace.Root)
            && workspace.Floating is not null
            && workspace.Floating.All(window => window is not null && Sound(window.Root)));

    private static bool Sound(DockNode? node) => node switch
    {
        DockGroup group => !group.Items.Contains(null!),
        DockSplit split => split.Children.Count > 0 && split.Children.All(Sound),
        _ => false,
    };
}
