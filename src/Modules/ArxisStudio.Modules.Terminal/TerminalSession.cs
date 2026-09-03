using System.Text;
using System.Threading.Channels;
using ArxisStudio.Modules.Terminal.Pty;
using ArxisStudio.Modules.Terminal.Shells;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using XTerm.Options;
using XTerminal = XTerm.Terminal;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Один сеанс: оболочка в псевдотерминале, эмулятор экрана и дорога байтов
/// между ними.
/// </summary>
/// <remarks>
/// Байты от оболочки читаются на фоновом потоке и копятся; эмулятору они
/// отдаются на потоке интерфейса порцией — один разбор на кадр, и всё, что
/// смотрит на экран, смотрит с одного потока. Ответы эмулятора оболочке (кто
/// ты, где курсор) и всё набранное человеком уходят через очередь на своём
/// потоке: запись в трубу может встать, если оболочка занята, и поток
/// интерфейса на этом стоять не должен.
/// <para>
/// Сеанс ничего не знает о рисовании: он держит <see cref="Terminal"/> и
/// сообщает об изменениях; кто и как его показывает — его не касается. Как
/// сеанс переносит работу на поток интерфейса, задаётся снаружи: студии нужен
/// диспетчер Avalonia, тесту — прямой вызов.
/// </para>
/// </remarks>
public sealed class TerminalSession : IDisposable
{
    /// <summary>
    /// Сколько ждать хвост вывода после выхода оболочки.
    /// </summary>
    /// <remarks>
    /// Событие о выходе приходит от процесса оболочки, а её последние байты в
    /// этот миг могут ещё идти через консоль. Закрыть псевдотерминал сразу
    /// значило бы потерять «Процесс завершён» или последнюю строку ошибки.
    /// </remarks>
    public static readonly TimeSpan TailGrace = TimeSpan.FromMilliseconds(250);

    private readonly IPseudoTerminal _pty;
    private readonly Action<Action> _post;
    private readonly Lock _gate = new();
    private readonly MemoryStream _pending = new();
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Thread _reader;
    private bool _posted;
    private bool _disposed;

    /// <summary>
    /// Связывает оболочку с эмулятором.
    /// </summary>
    /// <param name="profile">Какая это оболочка.</param>
    /// <param name="pty">Псевдотерминал с уже запущенной оболочкой.</param>
    /// <param name="options">Настройки эмулятора: размер, история, курсор.</param>
    /// <param name="post">
    /// Как перенести работу на поток интерфейса; null — диспетчер Avalonia.
    /// </param>
    public TerminalSession(ShellProfile profile, IPseudoTerminal pty, TerminalOptions options, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(pty);
        ArgumentNullException.ThrowIfNull(options);

        Profile = profile;
        Title = profile.Title;
        _pty = pty;
        _post = post ?? (action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background));
        Terminal = new XTerminal(options);

        Terminal.DataReceived += (_, e) => Send(e.Data);
        Terminal.TitleChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Title))
                return;

            Title = e.Title;
            TitleChanged?.Invoke(this, e.Title);
        };

        _pty.Exited += OnPtyExited;

        _reader = new Thread(Read)
        {
            IsBackground = true,
            Name = $"terminal:{profile.Id}",
        };

        _reader.Start();
        _ = Task.Run(WriteOutgoingAsync);
    }

    /// <summary>Какая это оболочка.</summary>
    public ShellProfile Profile { get; }

    /// <summary>Экран и всё, что на нём: эмулятор.</summary>
    public XTerminal Terminal { get; }

    /// <summary>Заголовок: имя оболочки, пока она не назвала себя сама.</summary>
    public string Title { get; private set; }

    /// <summary>Код выхода оболочки; null — она ещё идёт.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Жива ли ещё оболочка.</summary>
    public bool IsRunning => !_disposed && ExitCode is null;

    /// <summary>Экран изменился: пришла порция вывода.</summary>
    public event EventHandler? Changed;

    /// <summary>Оболочка сменила заголовок.</summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>Оболочка вышла; в аргументе — код выхода.</summary>
    public event EventHandler<int>? Exited;

    /// <summary>Запускает оболочку в новом сеансе.</summary>
    /// <param name="profile">Какую.</param>
    /// <param name="workingDirectory">Где.</param>
    /// <param name="settings">Настройки терминала.</param>
    /// <param name="columns">Ширина окна в знаках.</param>
    /// <param name="rows">Высота в строках.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    public static async Task<TerminalSession> StartAsync(
        ShellProfile profile,
        string workingDirectory,
        TerminalSettings settings,
        int columns,
        int rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);

        var pty = await PortaPseudoTerminal.StartAsync(profile, workingDirectory, columns, rows, cancellationToken);

        return new TerminalSession(profile, pty, Options(settings, columns, rows));
    }

    /// <summary>Настройки эмулятора из настроек терминала.</summary>
    /// <param name="settings">Настройки терминала.</param>
    /// <param name="columns">Ширина окна в знаках.</param>
    /// <param name="rows">Высота в строках.</param>
    public static TerminalOptions Options(TerminalSettings settings, int columns, int rows)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new TerminalOptions
        {
            Cols = Math.Max(1, columns),
            Rows = Math.Max(1, rows),
            Scrollback = settings.Scrollback,
            TermName = PortaPseudoTerminal.TerminalName,
            CursorBlink = settings.CursorBlink,
            // Перевод строки — дело оболочки, не терминала: псевдотерминал уже
            // отдаёт CR LF, и добавлять возврат каретки самим значило бы
            // удвоить пустые строки.
            ConvertEol = false,
            Theme = TerminalTheme.Campbell(
                Color.FromRgb(0x1E, 0x1F, 0x22),
                Color.FromRgb(0xCC, 0xCC, 0xCC),
                Color.FromRgb(0x2E, 0x43, 0x6E)),
        };
    }

    /// <summary>Отправляет оболочке набранный текст.</summary>
    /// <param name="text">Что набрали.</param>
    public void SendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Terminal.ScrollToBottom();
        Send(text);
    }

    /// <summary>
    /// Отправляет оболочке особую клавишу.
    /// </summary>
    /// <param name="key">Что нажали.</param>
    /// <param name="modifiers">С чем.</param>
    /// <param name="symbol">Символ клавиши в раскладке, если известен.</param>
    /// <returns>Ушло ли что-то: false — клавиша обычная, символ придёт текстом.</returns>
    public bool SendKey(Key key, KeyModifiers modifiers, string? symbol = null)
    {
        if (KeyMap.Sequence(Terminal, key, modifiers, symbol) is not { Length: > 0 } sequence)
            return false;

        // Нажатие возвращает к живому краю: человек печатает туда, а не в
        // историю, которую листал.
        Terminal.ScrollToBottom();
        Send(sequence);

        return true;
    }

    /// <summary>
    /// Вставляет текст из буфера обмена.
    /// </summary>
    /// <param name="text">Что вставить.</param>
    /// <remarks>
    /// Обёртку скобками вставки, если оболочка её просила, и переводы строк
    /// как нажатия Enter делает эмулятор: он знает режим, а сеанс — нет.
    /// </remarks>
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Terminal.ScrollToBottom();
        Terminal.Paste(text);
    }

    /// <summary>Есть ли что чистить: экран принадлежит нам, а не полноэкранной программе.</summary>
    public bool CanClearScreen => IsRunning && !Terminal.IsAlternateBufferActive;

    /// <summary>
    /// Очищает экран, оставив строку, на которой стоит курсор.
    /// </summary>
    /// <remarks>
    /// Чистит по возможности не терминал, а сама оболочка: Ctrl+L умеют все, у
    /// кого есть построчный редактор, и делают это лучше нас — приглашение
    /// перерисовано, набранное сохранено, а экраны остались одним и тем же.
    /// Последнее и есть главное: ConPTY держит свою копию экрана, и уборка
    /// только на нашей стороне с ней разошлась бы — PSReadLine рисует строку
    /// ввода по запомненным координатам и попал бы ими в пустоту, так что
    /// следующая набранная буква появилась бы не там, где курсор.
    /// <para>
    /// Своими руками — только для оболочки без редактора строки, то есть для
    /// <c>cmd</c>. Строка курсора при этом остаётся: на ней стоит приглашение,
    /// и стереть её вместе с остальным — а это ровно то, что делает
    /// <c>Clear</c> эмулятора, — значит оставить пустой экран без приглашения,
    /// которое оболочка заново не нарисует. Собрано из трёх шагов, потому что
    /// готового такого действия у эмулятора нет: строка курсора уезжает
    /// наверх, курсор идёт за ней, а история чистится следом.
    /// </para>
    /// <para>
    /// На альтернативном экране не делает ничего: там рисует полноэкранная
    /// программа по своей модели, и подъём её строк был бы ложью о том, что у
    /// неё на экране.
    /// </para>
    /// </remarks>
    public void ClearScreen()
    {
        if (!CanClearScreen)
            return;

        if (Profile.ClearsItself)
        {
            Send("\f");
            return;
        }

        var buffer = Terminal.Buffer;
        var row = buffer.Y;

        if (row > 0)
        {
            buffer.ScrollUp(row, false);
            buffer.SetCursor(buffer.X, 0);
        }

        buffer.ClearScrollback();
        Terminal.ScrollToBottom();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Сообщает оболочке и эмулятору новый размер окна.</summary>
    /// <param name="columns">Ширина в знаках.</param>
    /// <param name="rows">Высота в строках.</param>
    public void Resize(int columns, int rows)
    {
        if (_disposed)
            return;

        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);

        if (Terminal.Cols == columns && Terminal.Rows == rows)
            return;

        // Сперва отдать экрану всё уже прочитанное, и только потом менять
        // размер. Прочитанное написано оболочкой для прежнего экрана, и в нём
        // есть установки курсора абсолютными координатами: строка 18 из 24.
        // Применённые к экрану нового размера, они бьют мимо — приглашение
        // оказывается посреди старого вывода, а набранное не там, где курсор.
        // Ровно это и ломало разметку при тяге границы: байты копятся на
        // фоновом потоке, а размер меняется на потоке интерфейса.
        Drain();

        var buffer = Terminal.Buffer;
        var top = buffer.YBase;
        var cursorRow = buffer.Y;

        Terminal.Resize(columns, rows);

        // Расхождение о том, куда растёт окно. ConPTY со своим экраном растит
        // его вниз: строка курсора остаётся на своём месте, а под ней
        // появляется пустое. Эмулятор растит вверх — подтягивает историю
        // сверху и уводит курсор в самый низ. После этого установка курсора
        // абсолютными координатами, которой оболочка рисует строку ввода, бьёт
        // мимо ровно на разницу: набранное ложится посреди старого вывода.
        // Возвращаем окно туда, где его держит ConPTY.
        if (_pty.KeepsOwnScreen && buffer.YBase < top)
        {
            buffer.ScrollUp(top - buffer.YBase, false);
            buffer.SetCursor(buffer.X, cursorRow);
        }

        // Оболочке — последней: её ответ на новый размер придёт уже в экран,
        // который этот размер знает.
        _pty.Resize(columns, rows);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _pty.Exited -= OnPtyExited;
        _outgoing.Writer.TryComplete();
        _pty.Dispose();
        Terminal.Dispose();
    }

    /// <summary>Ставит текст в очередь к оболочке.</summary>
    private void Send(string text)
    {
        if (!IsRunning || string.IsNullOrEmpty(text))
            return;

        _outgoing.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Фоновое чтение: копит прочитанное и просит поток интерфейса забрать порцию.</summary>
    private void Read()
    {
        var buffer = new byte[16384];

        try
        {
            while (!_disposed)
            {
                var read = _pty.Output.Read(buffer, 0, buffer.Length);

                if (read <= 0)
                    break;

                lock (_gate)
                {
                    _pending.Write(buffer, 0, read);

                    if (_posted)
                        continue;

                    _posted = true;
                }

                _post(Drain);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Труба закрыта — сеанс кончился, это не сбой.
        }
    }

    private void Drain()
    {
        byte[] bytes;

        lock (_gate)
        {
            bytes = _pending.ToArray();
            _pending.SetLength(0);
            _posted = false;
        }

        if (_disposed || bytes.Length == 0)
            return;

        Terminal.Write(bytes);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task WriteOutgoingAsync()
    {
        await foreach (var chunk in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (_disposed)
                return;

            _pty.Write(chunk);
        }
    }

    private void OnPtyExited(object? sender, int code) => _post(() => Finish(code));

    private void Finish(int code)
    {
        if (_disposed || ExitCode is not null)
            return;

        ExitCode = code;
        _outgoing.Writer.TryComplete();
        Exited?.Invoke(this, code);
        _ = ReleaseLaterAsync();
    }

    /// <summary>Дожидается хвоста вывода и закрывает псевдотерминал — это и освобождает читателя.</summary>
    private async Task ReleaseLaterAsync()
    {
        await Task.Delay(TailGrace).ConfigureAwait(false);

        _post(() =>
        {
            if (!_disposed)
                _pty.Dispose();
        });
    }
}
