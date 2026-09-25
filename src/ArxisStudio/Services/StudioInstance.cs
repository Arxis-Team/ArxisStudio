using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArxisStudio.Services;

/// <summary>
/// Один экземпляр студии на одну папку данных пользователя.
/// </summary>
/// <remarks>
/// Две студии над одной папкой спорят за её файлы: раскладку пишет та, что закрылась
/// последней, недавние проекты и выключенные плагины — та, что записала позже, и правка
/// первой пропадает молча. Поэтому вторая студия не поднимается, а отдаёт первой свои
/// аргументы — «открой вот это» — и уходит. Так ведут себя Rider и Visual Studio Code:
/// двойной щелчок по решению в Проводнике открывает его в уже запущенной студии.
/// <para>
/// Занятость держит именованный мьютекс, а не файл-замок и не семафор. Мьютекс
/// упавшего процесса система отпускает сама — следующий захват получает его брошенным и
/// честно становится первым; файл-замок и семафор пережили бы падение и не пустили бы
/// студию вовсе. Аргументы едут по именованному каналу, открытому только своему
/// пользователю.
/// </para>
/// <para>
/// Мьютекс привязан к потоку, который его взял. Берёт его <see cref="Claim"/> в точке
/// входа, а отпускает <see cref="Dispose"/> там же, на выходе из цикла приложения: перезапущенная
/// копия ждёт папку и получает её раньше, чем прежний процесс уйдёт совсем. Не дошедший до
/// этой строки процесс отпускает система, когда умирает.
/// </para>
/// </remarks>
internal sealed class StudioInstance : IDisposable
{
    /// <summary>Сколько вторая студия ждёт ответа первой.</summary>
    /// <remarks>
    /// Первая могла взять мьютекс и ещё не открыть канал — это миллисекунды. Больше трёх
    /// секунд значит, что первая не отвечает, и ждать дольше — оставить человека без окна.
    /// </remarks>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly List<string[]> _pending = [];
    private Action<string[]>? _listener;

    private StudioInstance(Mutex mutex, string name, bool first)
    {
        _mutex = mutex;
        _name = name;
        IsFirst = first;
    }

    /// <summary>Первая ли это студия над своей папкой данных.</summary>
    public bool IsFirst { get; }

    /// <summary>Занятость этого процесса, если он первый; иначе <c>null</c>.</summary>
    public static StudioInstance? Current { get; set; }

    /// <summary>
    /// Папку держит другая студия, но на просьбу не ответила, и эта поднялась сама.
    /// </summary>
    public static bool Unanswered { get; set; }

    /// <summary>
    /// Имя занятости для папки данных: одно на пользователя и папку.
    /// </summary>
    /// <remarks>
    /// Хэш, а не сам путь: имя канала и мьютекса ограничено и не терпит разделителей, а
    /// путь бывает любым. Регистр пути не различается там, где его не различает файловая
    /// система Windows, — иначе <c>C:\Users</c> и <c>c:\users</c> стали бы двумя студиями
    /// над одной папкой.
    /// </remarks>
    public static string NameFor(string userData)
    {
        var path = Path.GetFullPath(userData).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
            path = path.ToUpperInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + path));

        return "ArxisStudio-" + Convert.ToHexString(hash, 0, 8);
    }

    /// <summary>
    /// Занимает папку данных; вышло — эта студия первая и слушает вторые.
    /// </summary>
    /// <param name="name">Имя из <see cref="NameFor"/>.</param>
    /// <param name="wait">
    /// Сколько ждать, пока папку отпустят; null — не ждать. Ждёт перезапущенная копия: прежняя
    /// держит папку, пока не закроется.
    /// </param>
    public static StudioInstance Claim(string name, TimeSpan? wait = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var mutex = new Mutex(false, @"Local\" + name);
        bool first;

        try
        {
            first = mutex.WaitOne(wait ?? TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // Прежний хозяин упал, не отпустив. Мьютекс достался нам — это и есть
            // причина, по которой занятость держит мьютекс, а не файл.
            first = true;
        }

        var instance = new StudioInstance(mutex, name, first);

        if (first)
            _ = instance.ServeAsync();

        return instance;
    }

    /// <summary>
    /// Отдаёт аргументы первой студии.
    /// </summary>
    /// <param name="arguments">Аргументы командной строки этой, второй студии.</param>
    /// <param name="patience">Сколько ждать ответа; по умолчанию <see cref="Patience"/>.</param>
    /// <returns>Первая приняла; иначе она не отвечает.</returns>
    /// <remarks>
    /// Путь приводится к полному здесь, а не у первой: относительный путь считается от
    /// папки, из которой позвали вторую студию, а у первой папка своя, и тот же
    /// «Hello.slnx» указал бы на чужой файл.
    /// </remarks>
    public bool Send(IReadOnlyList<string> arguments, TimeSpan? patience = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var payload = JsonSerializer.Serialize(arguments.Select(Absolute).ToArray()) + "\n";
        var deadline = DateTime.UtcNow + (patience ?? Patience);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);

                client.Connect(Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));

                var bytes = Encoding.UTF8.GetBytes(payload);

                client.Write(bytes);
                client.Flush();

                return client.ReadByte() == Ack;
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                // Первая взяла мьютекс и ещё не открыла канал, или канал занят другим
                // гостем: пробуем снова, пока не выйдет время.
                Thread.Sleep(20);
            }
        }

        return false;
    }

    /// <summary>
    /// Начинает отдавать слушателю присланное — и то, что пришло раньше.
    /// </summary>
    /// <param name="listener">Кого звать; зовётся из фонового потока.</param>
    /// <remarks>
    /// Вторая студия может постучаться, пока первая ещё под заставкой. Её просьба не
    /// теряется, а ждёт, пока студии будет куда её отдать.
    /// </remarks>
    public void Listen(Action<string[]> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        List<string[]> waiting;

        lock (_gate)
        {
            _listener = listener;
            waiting = [.. _pending];
            _pending.Clear();
        }

        foreach (var arguments in waiting)
            listener(arguments);
    }

    /// <summary>
    /// Перестаёт отвечать вторым студиям, не отпуская папку данных.
    /// </summary>
    /// <remarks>
    /// Зовёт перезапуск, решившись закрыться: просьба, принятая теперь, ушла бы вместе с этим
    /// процессом. Вторая студия, не дождавшись ответа, поднимется сама — так она ведёт себя и при
    /// зависшей первой. Папка остаётся занятой до выхода: новая копия ждёт её, и третий запуск не
    /// должен проскочить в щель между ними.
    /// </remarks>
    public void Stop() => _stop.Cancel();

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Cancel();

        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Зовут не из того потока, что брал: отпустит система при выходе.
            }
        }

        _mutex.Dispose();
    }

    private const int Ack = 1;

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _name,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(_stop.Token);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);

                var line = await reader.ReadLineAsync(_stop.Token);
                var arguments = line is null ? [] : JsonSerializer.Deserialize<string[]>(line) ?? [];

                server.WriteByte(Ack);
                await server.FlushAsync(_stop.Token);

                Deliver(arguments);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (JsonException)
            {
                // Гость прислал не то. Слушать остальных это не мешает: канал
                // пересоздаётся на следующем круге.
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Канал не открылся или гость ушёл посреди разговора. Пауза здесь несущая:
                // имя канала может держать чужой процесс, и без неё круг крутился бы
                // впустую, отнимая у пула поток за потоком. Так и было найдено — зависшим
                // прогоном тестов, где второй экземпляр по ошибке считал себя первым.
                try
                {
                    await Task.Delay(250, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void Deliver(string[] arguments)
    {
        Action<string[]>? listener;

        lock (_gate)
        {
            listener = _listener;

            if (listener is null)
                _pending.Add(arguments);
        }

        listener?.Invoke(arguments);
    }

    private static string Absolute(string argument) =>
        string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-') ? argument : Path.GetFullPath(argument);
}
