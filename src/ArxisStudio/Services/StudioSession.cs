using System.Text.Json;
using System.Text.Json.Serialization;
using ArxisStudio.Docking;
using ArxisStudio.ViewModels;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Services;

/// <summary>
/// Состояние студии, которое переживает её перезапуск: что было открыто и где стояло.
/// </summary>
/// <remarks>
/// Прежняя копия снимает его до того, как что-нибудь закроется, и кладёт файлом рядом с данными
/// человека; новая забирает файл раньше, чем поднимет Avalonia, и ставит состояние на место после
/// показа окна. Снимок, а не ссылки: между процессами едут пути и имена раскладки, и ничего живого.
/// <para>
/// Чего здесь нет, того перезапуск не вернёт, и это сказано в документах: оболочки терминала и
/// вывод консоли умирают с процессом, каретка внутри документа и несохранённые правки у
/// <c>DocumentView</c> наружу не отдаются.
/// </para>
/// </remarks>
public sealed record StudioSession
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Спереди было окно студии, а не Welcome.</summary>
    public bool Studio { get; init; }

    /// <summary>Место главного окна; null — окно не показывали.</summary>
    public StudioPlacement? Window { get; init; }

    /// <summary>Открытый проект; null — студия была каркасом.</summary>
    public string? Project { get; init; }

    /// <summary>Открытые документы — пути в порядке открытия.</summary>
    public IReadOnlyList<string> Documents { get; init; } = [];

    /// <summary>Что было выбрано в группах раскладки — имена по порядку обхода.</summary>
    public IReadOnlyList<string> Onstage { get; init; } = [];

    /// <summary>Показанный документ — имя в раскладке; null — не показан ни один.</summary>
    public string? Active { get; init; }

    /// <summary>Где стояла каретка — имя в раскладке; null — нигде.</summary>
    public string? Focused { get; init; }

    /// <summary>Открытое окно настроек; null — было закрыто.</summary>
    public SettingsSession? Settings { get; init; }

    /// <summary>Экран Welcome; null — спереди было окно студии.</summary>
    public WelcomeSession? Welcome { get; init; }

    /// <summary>
    /// Ради чего перезапускались: расширение и причина для разработчика.
    /// </summary>
    /// <remarks>
    /// Журнал прежней копии умер вместе с ней, и новая пишет эти причины в свой — иначе автор
    /// плагина, согласившийся на перезапуск, потерял бы ответ на вопрос «что держало мой код».
    /// </remarks>
    public IReadOnlyDictionary<string, string> Reasons { get; init; } = new Dictionary<string, string>();

    /// <summary>Файл сессии прежней копии.</summary>
    /// <param name="folder">Папка данных студии.</param>
    /// <param name="pid">Номер процесса, который перезапускается.</param>
    /// <remarks>
    /// Номер в имени нужен второй студии над той же папкой, поднятой переменной
    /// <c>ARXIS_SINGLE_INSTANCE=0</c>: две перезапускающиеся копии не делят один файл.
    /// </remarks>
    public static string FileFor(string folder, int pid) =>
        Path.Combine(folder, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"restart-{pid}.json"));

    /// <summary>Записывает сессию файлом.</summary>
    /// <param name="session">Что записать.</param>
    /// <param name="path">Куда.</param>
    /// <remarks>
    /// Через соседний файл и перенос: новая копия читает файл, как только поднимется, и
    /// недописанный кусок прочла бы как испорченный.
    /// </remarks>
    public static void Write(StudioSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var draft = path + ".tmp";

        File.WriteAllText(draft, JsonSerializer.Serialize(session, Json));
        File.Move(draft, path, overwrite: true);
    }

    /// <summary>
    /// Забирает сессию: читает файл и стирает прочитанное.
    /// </summary>
    /// <param name="path">Файл сессии.</param>
    /// <param name="complaint">Почему прочесть не вышло; null — вышло.</param>
    /// <returns>Сессия; null — файла нет, он не читается или его не стереть.</returns>
    /// <remarks>
    /// Не бросает: забирают сессию до Avalonia, в точке, где исключение унесло бы процесс без
    /// окна, а студия без прежнего состояния всё-таки лучше студии, которая не поднялась.
    /// <para>
    /// Стёртый файл и есть ответ прежней копии: она ждёт его исчезновения, прежде чем закрыться.
    /// Поэтому стирается только прочитанное целиком. Испорченный файл остаётся — прежняя не
    /// дождётся ответа, снимет эту копию и сотрёт его сама, а человек останется в студии со всем
    /// открытым. Нестёртый — тоже отказ: сессия, которую прежняя сочла бы непринятой, у новой
    /// копии была бы второй.
    /// </para>
    /// </remarks>
    public static StudioSession? Take(string path, out string? complaint)
    {
        complaint = null;

        try
        {
            var session = JsonSerializer.Deserialize<StudioSession>(File.ReadAllText(path), Json)?.Normalized()
                ?? throw new JsonException("в файле пусто");

            File.Delete(path);

            return session;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            complaint = $"{path}: {e.Message}";

            return null;
        }
    }

    /// <summary>Стирает файл сессии, если он ещё есть; отказ файловой системы не бросает.</summary>
    /// <param name="path">Файл сессии.</param>
    public static void Forget(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    /// <summary>
    /// Сессия без пустот, которые дал бы испорченный файл: <c>null</c> вместо списка или пути.
    /// </summary>
    private StudioSession Normalized() => this with
    {
        Documents = [.. (Documents ?? []).OfType<string>().Where(path => path.Length > 0)],
        Onstage = [.. (Onstage ?? []).OfType<string>().Where(id => id.Length > 0)],
        Reasons = Reasons ?? new Dictionary<string, string>(),
        Settings = Settings?.Normalized(),
        Welcome = Welcome is { } welcome ? welcome with { Filter = welcome.Filter ?? string.Empty } : null,
    };
}

/// <summary>Окно настроек в сессии перезапуска.</summary>
public sealed record SettingsSession
{
    /// <summary>Открытый раздел — имя страницы; null — первый.</summary>
    public string? Page { get; init; }

    /// <summary>Строка поиска.</summary>
    public string Search { get; init; } = string.Empty;

    /// <summary>Выбранный в менеджере плагин; null — не выбран.</summary>
    public string? Plugin { get; init; }

    /// <summary>Группы менеджера, которые человек свернул или раскрыл сам.</summary>
    public IReadOnlyDictionary<string, bool> Folded { get; init; } = new Dictionary<string, bool>();

    /// <summary>Место окна; null — посреди экрана, как при обычном открытии.</summary>
    public StudioPlacement? Window { get; init; }

    internal SettingsSession Normalized() => this with
    {
        Search = Search ?? string.Empty,
        Folded = Folded ?? new Dictionary<string, bool>(),
    };
}

/// <summary>Экран Welcome в сессии перезапуска.</summary>
/// <param name="Section">Открытый раздел.</param>
/// <param name="Filter">Что набрано в поиске недавних.</param>
/// <param name="Plugins">
/// Настройки открыты дверью «Плагины», а не «Настройки»: отметка в полосе Welcome стоит на ней.
/// </param>
public sealed record WelcomeSession(WelcomeSection Section, string Filter, bool Plugins);

/// <summary>
/// Место окна: где стояло, какого размера было в обычном виде и было ли развёрнуто.
/// </summary>
/// <param name="X">Левый край в пикселях экрана.</param>
/// <param name="Y">Верхний край в пикселях экрана.</param>
/// <param name="Width">Ширина обычного вида — в точках, не пикселях.</param>
/// <param name="Height">Высота обычного вида — в точках.</param>
/// <param name="Scaling">Масштаб экрана, на котором окно стояло: им точки переводятся в пиксели.</param>
/// <param name="Maximized">Окно было развёрнуто.</param>
/// <remarks>
/// Позиция у Avalonia в пикселях, размер — в точках, и сверять место с экранами можно только в
/// одних единицах. Развёрнутое окно помнит обычные границы: сняв разворот, человек возвращается к
/// ним, а не к размеру во весь экран.
/// </remarks>
public sealed record StudioPlacement(int X, int Y, double Width, double Height, double Scaling, bool Maximized)
{
    /// <summary>Снимает место окна.</summary>
    /// <param name="window">Окно.</param>
    /// <param name="position">Где стояло окно в обычном виде.</param>
    /// <param name="size">Размер обычного вида.</param>
    public static StudioPlacement Of(Window window, PixelPoint position, Size size)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new StudioPlacement(
            position.X,
            position.Y,
            size.Width,
            size.Height,
            window.DesktopScaling,
            window.WindowState == WindowState.Maximized);
    }

    /// <summary>
    /// Где окну встать сейчас: записанное место, если его видно хоть на одном экране.
    /// </summary>
    /// <param name="screens">Экраны, какие есть сейчас, — в пикселях.</param>
    /// <param name="fallback">Рабочая область основного экрана; null — возвращать некуда.</param>
    /// <remarks>
    /// Правило то же, что у оторванного окна, и функция та же: второй монитор отключили, и окно,
    /// вставшее за ним, человек не нашёл бы. Размер переводится в пиксели масштабом того экрана,
    /// на котором окно стояло.
    /// </remarks>
    public PixelPoint Land(IReadOnlyList<PixelRect> screens, PixelRect? fallback)
    {
        var scaling = Scaling > 0 ? Scaling : 1;

        return DockFloat.Landed(
            new PixelRect(X, Y, (int)Math.Ceiling(Width * scaling), (int)Math.Ceiling(Height * scaling)),
            screens,
            fallback);
    }

    /// <summary>
    /// Ставит окно на записанное место — до показа.
    /// </summary>
    /// <param name="window">Окно, которое ещё не показывали.</param>
    /// <remarks>
    /// Позиция раньше размера: окно студии урезает размер по рабочей области экрана, на котором
    /// стоит, и поставленный первым размер урезался бы по чужому экрану.
    /// </remarks>
    public void Put(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.WindowStartupLocation = WindowStartupLocation.Manual;

        if (window.Screens is { } screens)
            window.Position = Land([.. screens.All.Select(screen => screen.Bounds)], screens.Primary?.WorkingArea);

        window.Width = Width;
        window.Height = Height;

        if (Maximized)
            window.WindowState = WindowState.Maximized;
    }
}
