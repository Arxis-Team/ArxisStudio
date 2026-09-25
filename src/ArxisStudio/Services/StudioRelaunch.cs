using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace ArxisStudio.Services;

/// <summary>
/// Перезапуск студии на уровне процессов: как поднять новую копию, как им договориться и как
/// новой дождаться, пока уйдёт прежняя.
/// </summary>
/// <remarks>
/// Порядок у перезапуска один, и держится он двумя сторонами. Прежняя копия записывает сессию
/// файлом, поднимает новую с <c>--restore=&lt;файл&gt;</c> и <c>--after=&lt;номер&gt;</c> и ждёт
/// ответа. Ответ — сам файл: новая забирает сессию раньше Avalonia и стирает прочитанное, и его
/// исчезновение прежняя и ждёт. Именованное событие было бы короче, но в .NET оно есть только на
/// Windows, а файл одинаков везде.
/// <para>
/// Дальше новая ждёт, пока прежняя отпустит папку данных и уйдёт: только тогда свободны порт
/// инструментов разработчика, файлы раскладки и теневые копии плагинов. Не ответила новая —
/// прежняя её снимает, стирает файл и работает дальше: человек не остаётся без студии ни на одной
/// развилке.
/// </para>
/// <para>
/// Ключи пропускает разбор проекта в командной строке (<see cref="StudioArguments"/>): всё, что
/// начинается с дефиса, ему не адресовано.
/// </para>
/// </remarks>
internal static class StudioRelaunch
{
    /// <summary>Ключ с файлом сессии.</summary>
    public const string RestoreKey = "--restore=";

    /// <summary>Ключ с номером прежнего процесса.</summary>
    public const string AfterKey = "--after=";

    /// <summary>Сколько прежняя копия ждёт, пока новая примет сессию.</summary>
    /// <remarks>
    /// Новая отвечает до Avalonia, за доли секунды, но свежий исполняемый файл проверяет
    /// антивирус, и это бывают секунды. Дольше — значит, новая не поднимется, и держать человека
    /// дальше незачем.
    /// </remarks>
    public static readonly TimeSpan Handshake = TimeSpan.FromSeconds(10);

    /// <summary>Сколько новая копия ждёт, пока прежняя закроется, прежде чем снять её.</summary>
    /// <remarks>
    /// Закрытие — это прощание панелей и выгрузка плагинов, и чужой код волен в нём задержаться.
    /// Раскладка к этому времени записана, а сессия у новой копии на руках: снять зависшую
    /// прежнюю ничего не теряет.
    /// </remarks>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    /// <summary>Сколько ждать выхода процесса, которому велели умереть.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>Как часто прежняя копия смотрит, забрали ли сессию.</summary>
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);

    /// <summary>Сессия, с которой эту копию подняли; null — подняли обычно.</summary>
    public static StudioSession? Session { get; private set; }

    /// <summary>Почему сессию, названную в командной строке, не удалось взять; null — нечего сказать.</summary>
    public static string? Complaint { get; private set; }

    /// <summary>Номер прежнего процесса; null — эту копию подняли не перезапуском.</summary>
    public static int? Predecessor { get; private set; }

    /// <summary>
    /// Как найти прежний процесс по номеру; null — его нет или он не наш.
    /// </summary>
    /// <remarks>Шов ради тестов: убивать настоящие процессы тест не должен.</remarks>
    internal static Func<int, IPredecessor?> Find { get; set; } = Running.Find;

    /// <summary>Разбирает ключи перезапуска.</summary>
    /// <param name="arguments">Аргументы командной строки; null — их нет.</param>
    /// <returns>Номер прежнего процесса и файл сессии; null — ключа нет или он не разобрался.</returns>
    public static (int? After, string? Restore) Parse(IEnumerable<string>? arguments)
    {
        int? after = null;
        string? restore = null;

        foreach (var argument in arguments ?? [])
        {
            if (argument.StartsWith(AfterKey, StringComparison.Ordinal)
                && int.TryParse(argument.AsSpan(AfterKey.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                && pid > 0)
            {
                after = pid;
            }
            else if (argument.StartsWith(RestoreKey, StringComparison.Ordinal) && argument.Length > RestoreKey.Length)
            {
                restore = argument[RestoreKey.Length..];
            }
        }

        return (after, restore);
    }

    /// <summary>
    /// Команда, которой поднимается новая копия.
    /// </summary>
    /// <param name="session">Файл сессии.</param>
    /// <param name="pid">Номер этого, прежнего процесса.</param>
    /// <param name="processPath">Исполняемый файл процесса.</param>
    /// <param name="entry">Сборка студии — нужна, если процесс поднят хозяином <c>dotnet</c>.</param>
    /// <remarks>
    /// Аргументы идут списком, а не строкой: путь к папке данных бывает с пробелами, и склеенная
    /// руками строка разошлась бы с тем, как её разберёт новая копия. Окружение наследуется: под
    /// ним живут переменные студии — папка истории, второй экземпляр, порт инструментов, — и
    /// перезапуск не вправе их терять. Под <c>dotnet ArxisStudio.dll</c> первым аргументом идёт
    /// сама студия, иначе хозяин поднялся бы без приложения.
    /// </remarks>
    public static ProcessStartInfo Command(string session, int pid, string? processPath, string? entry)
    {
        ArgumentException.ThrowIfNullOrEmpty(session);
        ArgumentException.ThrowIfNullOrEmpty(processPath);

        var command = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrEmpty(entry);
            command.ArgumentList.Add(entry);
        }

        command.ArgumentList.Add(RestoreKey + session);
        command.ArgumentList.Add(AfterKey + pid.ToString(CultureInfo.InvariantCulture));

        return command;
    }

    /// <summary>Поднимает новую копию студии с сессией.</summary>
    /// <param name="session">Файл сессии.</param>
    /// <returns>Процесс новой копии; null — система его не подняла.</returns>
    public static Process? Launch(string session) =>
        Process.Start(Command(
            session,
            Environment.ProcessId,
            Environment.ProcessPath,
            Assembly.GetEntryAssembly()?.Location));

    /// <summary>
    /// Ждёт, пока новая копия заберёт сессию.
    /// </summary>
    /// <param name="session">Файл сессии.</param>
    /// <param name="exited">Вышла ли уже новая копия.</param>
    /// <param name="patience">Сколько ждать.</param>
    /// <returns><c>true</c> — забрала; <c>false</c> — не забрала или умерла раньше.</returns>
    /// <remarks>
    /// Ждётся паузами, а не блокировкой: поток интерфейса, замёрзший на десять секунд, человек
    /// принял бы за зависание. Умершая новая копия ответом не считается — ждать её дальше незачем.
    /// </remarks>
    public static async Task<bool> AwaitTakenAsync(string session, Func<bool> exited, TimeSpan patience)
    {
        ArgumentException.ThrowIfNullOrEmpty(session);
        ArgumentNullException.ThrowIfNull(exited);

        var deadline = DateTime.UtcNow + patience;

        while (File.Exists(session))
        {
            if (exited() || DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(Poll);
        }

        return true;
    }

    /// <summary>Снимает новую копию, не принявшую сессию.</summary>
    /// <param name="successor">Её процесс; null — снимать некого.</param>
    public static void Abort(Process? successor)
    {
        if (successor is null)
            return;

        using (successor)
        {
            try
            {
                if (!successor.HasExited)
                {
                    successor.Kill(entireProcessTree: true);
                    successor.WaitForExit(Grace);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Умерла сама, пока её снимали: снимать больше нечего.
            }
        }
    }

    /// <summary>
    /// Новая копия: забирает сессию, названную в командной строке, — тем и отвечает прежней.
    /// </summary>
    /// <param name="arguments">Аргументы командной строки.</param>
    /// <remarks>
    /// Не прочиталась сессия — файл остаётся, и прежняя ответа не дождётся: снимет эту копию,
    /// сотрёт файл и останется работать со всем, что было открыто. Так человек теряет меньше, чем с
    /// новой студией на пустом месте.
    /// <para>
    /// Преемницей прежней копии становится только забравшая сессию. Не ответившая считать так не
    /// вправе: она ждала бы папку, которую прежняя и не думает отпускать, а не дождавшись — сняла бы
    /// её, и с ней студию, в которой человек остался работать. Такая копия ведёт себя как обычный
    /// второй запуск: отдаёт свои аргументы открытой студии и уходит.
    /// </para>
    /// </remarks>
    public static void Accept(IEnumerable<string>? arguments) =>
        (Session, Complaint, Predecessor) = Read(arguments);

    /// <summary>
    /// Забирает сессию из командной строки и решает, преемница ли эта копия.
    /// </summary>
    /// <param name="arguments">Аргументы командной строки.</param>
    /// <returns>
    /// Сессия, жалоба на неё и номер прежнего процесса — только если сессия забрана; всё пусто, если
    /// перезапуском эту копию не поднимали.
    /// </returns>
    /// <remarks>
    /// Отдельно от <see cref="Accept"/>, который кладёт ответ в поля процесса: проверять решение
    /// надо без них — поля живут, пока жив процесс тестов.
    /// </remarks>
    internal static (StudioSession? Session, string? Complaint, int? Predecessor) Read(IEnumerable<string>? arguments)
    {
        var (after, restore) = Parse(arguments);

        if (after is not { } pid || restore is null)
            return (null, null, null);

        var session = StudioSession.Take(restore, out var complaint);

        return (session, complaint, session is null ? null : pid);
    }

    /// <summary>
    /// Занимает папку данных после прежней копии: ждёт, пока та её отпустит, а зависшую снимает.
    /// </summary>
    /// <param name="name">Имя занятости из <see cref="StudioInstance.NameFor"/>.</param>
    /// <param name="pid">Номер прежнего процесса.</param>
    /// <remarks>
    /// Мьютекс ждётся, а не проверяется после выхода процесса: в щель между «прежняя ушла» и «новая
    /// спросила» вошёл бы третий запуск и стал бы первым. Снятая прежняя оставляет мьютекс
    /// брошенным, и следующий захват достаётся этой копии.
    /// </remarks>
    public static StudioInstance Claim(string name, int pid)
    {
        var instance = StudioInstance.Claim(name, Patience);

        if (instance.IsFirst)
            return instance;

        instance.Dispose();
        Stop(pid);

        return StudioInstance.Claim(name, Grace);
    }

    /// <summary>
    /// Ждёт, пока прежняя копия уйдёт совсем; не ушла за отведённое — снимает её.
    /// </summary>
    /// <param name="pid">Номер прежнего процесса.</param>
    /// <param name="patience">Сколько ждать до того, как снять.</param>
    /// <returns><c>true</c> — прежней больше нет.</returns>
    /// <remarks>
    /// Папку данных прежняя отпускает раньше, чем уходит: следом идут выход из <c>Main</c> и
    /// финализаторы, а до них заняты порт инструментов разработчика и файлы. Процесс сверяется —
    /// тот же исполняемый файл и поднят раньше этого: номер умершего система отдаёт следующему, и
    /// снять чужую программу по совпавшему номеру было бы непростительно.
    /// </remarks>
    public static bool AwaitExit(int pid, TimeSpan patience)
    {
        using var predecessor = Find(pid);

        if (predecessor is null || predecessor.WaitForExit(patience))
            return true;

        predecessor.Kill();

        return predecessor.WaitForExit(Grace);
    }

    /// <summary>Снимает прежнюю копию без ожидания — она не отпустила папку данных.</summary>
    private static void Stop(int pid)
    {
        using var predecessor = Find(pid);

        if (predecessor is null)
            return;

        predecessor.Kill();
        predecessor.WaitForExit(Grace);
    }

    /// <summary>Прежний процесс студии глазами новой копии.</summary>
    internal interface IPredecessor : IDisposable
    {
        /// <summary>Ждёт выхода.</summary>
        /// <param name="timeout">Сколько ждать.</param>
        /// <returns><c>true</c> — процесс вышел.</returns>
        bool WaitForExit(TimeSpan timeout);

        /// <summary>Снимает процесс.</summary>
        void Kill();
    }

    /// <summary>Настоящий процесс, сверенный с этим.</summary>
    private sealed class Running(Process process) : IPredecessor
    {
        /// <summary>Находит прежний процесс студии; null — его нет или под номером чужой.</summary>
        public static IPredecessor? Find(int pid)
        {
            Process found;

            try
            {
                found = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                return null;
            }

            try
            {
                using var self = Process.GetCurrentProcess();

                if (!found.HasExited
                    && found.StartTime < self.StartTime
                    && string.Equals(found.MainModule?.FileName, self.MainModule?.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    return new Running(found);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Вышел, пока его сверяли, или сверить нечем: ни ждать, ни снимать такого нельзя.
            }

            found.Dispose();

            return null;
        }

        /// <inheritdoc/>
        public bool WaitForExit(TimeSpan timeout)
        {
            try
            {
                return process.WaitForExit(timeout);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                return true;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Только сам процесс, без дерева: новая копия и есть его потомок, и снятие дерева унесло бы
        /// её вместе с прежней.
        /// </remarks>
        public void Kill()
        {
            try
            {
                process.Kill(entireProcessTree: false);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Вышел сам, пока его снимали.
            }
        }

        /// <inheritdoc/>
        public void Dispose() => process.Dispose();
    }
}
