using System.Collections;

namespace ArxisStudio.Modules.Terminal.Pty;

/// <summary>
/// Окружение, которое получает оболочка: процесс студии таким, каким его застал
/// подъём терминала.
/// </summary>
/// <remarks>
/// Студия меняет своё окружение на ходу, и не ради оболочек. Первое открытие
/// решения регистрирует MSBuild: локатор ставит на весь процесс
/// <c>MSBUILD_EXE_PATH</c>, <c>MSBuildExtensionsPath</c> и <c>MSBuildSDKsPath</c>,
/// а его поиск SDK — ещё и <c>DOTNET_HOST_PATH</c>. Оболочка, открытая после
/// этого, раздала бы их всему, что в ней запустят: <c>dotnet build</c> взял бы
/// SDK студии вместо того, что выбирает <c>global.json</c> его папки, а
/// инструменты на том же локаторе — её MSBuild. Поэтому окружение снимается при
/// подъёме модуля — модули поднимаются раньше, чем что-то открыли, — и оболочка
/// получает снимок, а не нынешнее окружение процесса.
/// <para>
/// Отдаётся снимок разностью. Окружение оболочки библиотека псевдотерминала
/// собирает сама, из процесса в миг запуска, и кладёт словарь терминала поверх,
/// а пустое значение в нём убирает переменную. Поэтому появившееся после снимка
/// убирается, а изменённое и пропавшее возвращается. Пустую переменную так не
/// вернуть: изменённая после снимка, до оболочки она не дойдёт вовсе. В Windows
/// пустых переменных не бывает.
/// </para>
/// </remarks>
public sealed class ShellEnvironment
{
    private static ShellEnvironment? _studio;

    private readonly Dictionary<string, string> _variables;

    private ShellEnvironment(Dictionary<string, string> variables) => _variables = variables;

    /// <summary>
    /// Окружение студии, каким его застал подъём терминала.
    /// </summary>
    /// <remarks>
    /// Кто спросил раньше подъёма — так бывает только в тестах, — получает снимок
    /// на миг вопроса, и запомненным остаётся он.
    /// </remarks>
    public static ShellEnvironment Studio => Volatile.Read(ref _studio) ?? Remember();

    /// <summary>
    /// Запоминает окружение студии, если оно ещё не запомнено.
    /// </summary>
    /// <returns>Запомненный снимок — первый, а не нынешний.</returns>
    /// <remarks>
    /// Снимок один на процесс. Модуль, выключенный и поднятый снова, его не
    /// обновляет: к тому времени студия уже могла зарегистрировать MSBuild.
    /// </remarks>
    public static ShellEnvironment Remember()
    {
        if (Volatile.Read(ref _studio) is { } remembered)
            return remembered;

        var captured = Capture();

        return Interlocked.CompareExchange(ref _studio, captured, null) ?? captured;
    }

    /// <summary>Забывает запомненный снимок — для тестов, которые делят один процесс.</summary>
    public static void Reset() => Volatile.Write(ref _studio, null);

    /// <summary>Снимок нынешнего окружения процесса.</summary>
    public static ShellEnvironment Capture() => From(Current());

    /// <summary>Снимок из готового набора переменных.</summary>
    /// <param name="variables">Имя и значение каждой.</param>
    public static ShellEnvironment From(IEnumerable<KeyValuePair<string, string>> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var copy = new Dictionary<string, string>(NameComparer);

        foreach (var (name, value) in variables)
            copy[name] = value;

        return new ShellEnvironment(copy);
    }

    /// <summary>
    /// Что положить поверх нынешнего окружения процесса, чтобы оболочка получила снимок.
    /// </summary>
    /// <returns>Переменные со значением из снимка; с пустым — те, что надо убрать.</returns>
    public Dictionary<string, string> Overlay() => Overlay(Current());

    /// <summary>
    /// Что положить поверх названного окружения, чтобы получился снимок.
    /// </summary>
    /// <param name="current">Окружение, поверх которого ляжет результат.</param>
    /// <returns>Переменные со значением из снимка; с пустым — те, что надо убрать.</returns>
    /// <remarks>
    /// Нетронутое с момента снимка в результат не попадает: обычный сеанс, при
    /// котором студия своего окружения не меняла, не получает поправок вовсе.
    /// </remarks>
    public Dictionary<string, string> Overlay(IEnumerable<KeyValuePair<string, string>> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var overlay = new Dictionary<string, string>(NameComparer);
        var present = new HashSet<string>(NameComparer);

        foreach (var (name, value) in current)
        {
            present.Add(name);

            if (!_variables.TryGetValue(name, out var was))
                overlay[name] = string.Empty;
            else if (!string.Equals(was, value, StringComparison.Ordinal))
                overlay[name] = was;
        }

        foreach (var (name, was) in _variables)
        {
            if (!present.Contains(name))
                overlay[name] = was;
        }

        return overlay;
    }

    /// <summary>Имена сравниваются так же, как их сравнивает система: в Windows — без регистра.</summary>
    private static StringComparer NameComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Окружение процесса сейчас — тем же чтением, что у библиотеки псевдотерминала.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Current() =>
        Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Select(entry => new KeyValuePair<string, string>(
                entry.Key.ToString() ?? string.Empty, entry.Value?.ToString() ?? string.Empty))
            .Where(pair => pair.Key.Length > 0);
}
