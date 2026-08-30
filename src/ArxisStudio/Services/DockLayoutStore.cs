using ArxisStudio.Docking;
using ArxisStudio.Shell;

namespace ArxisStudio.Services;

/// <summary>
/// Файл раскладки: прочитать при запуске, записать при изменении.
/// </summary>
/// <remarks>
/// Перевод в текст и обратно делает движок; здесь только файл и решение, писать
/// в него или нет. Испорченный файл не мешает запуску: студия начинает со
/// стандартной раскладки, а первая же запись его исправит.
/// </remarks>
public sealed class DockLayoutStore
{
    private readonly string _path;

    /// <summary>Заводит хранилище над файлом.</summary>
    /// <param name="path">Путь к файлу; по умолчанию — <see cref="StudioPaths.LayoutFile"/>.</param>
    public DockLayoutStore(string? path = null)
    {
        _path = path ?? StudioPaths.LayoutFile;
    }

    /// <summary>
    /// Запрещена ли запись.
    /// </summary>
    /// <remarks>
    /// Так бывает ровно в одном случае: файл написан студией новее этой.
    /// Переписать его своими словами значило бы отнять у человека раскладку,
    /// которую он собрал в новой версии, — всего лишь за то, что он заглянул в
    /// проект старой.
    /// </remarks>
    public bool ReadOnly { get; private set; }

    /// <summary>Читает раскладку.</summary>
    /// <param name="complaint">Что сказать человеку, если не прочитали; иначе null.</param>
    /// <returns>Раскладка либо null.</returns>
    public DockLayout? Load(out string? complaint)
    {
        complaint = null;

        string text;

        try
        {
            if (!File.Exists(_path))
                return null;

            text = File.ReadAllText(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            complaint = $"Раскладку не прочитать: {e.Message}";
            return null;
        }

        var layout = DockLayoutSerializer.Read(text, out var problem);

        switch (problem)
        {
            case DockLayoutProblem.Newer:
                ReadOnly = true;
                complaint = "Файл раскладки написан студией новее этой — он оставлен как есть, "
                            + "а окно собрано по стандартной раскладке";
                break;

            case DockLayoutProblem.Unreadable:
                complaint = "Файл раскладки не разобран — окно собрано по стандартной раскладке";
                break;
        }

        return layout;
    }

    /// <summary>Записывает раскладку; молча ничего не делает, если запись запрещена.</summary>
    /// <param name="layout">Что записываем.</param>
    /// <returns>Что сказать человеку, если не записали; иначе null.</returns>
    public string? Save(DockLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (ReadOnly)
            return null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, DockLayoutSerializer.Write(layout));

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Раскладку не записать: {e.Message}";
        }
    }
}
