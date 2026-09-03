using System.Collections.Concurrent;
using System.Text;
using ArxisStudio.Modules.Terminal.Pty;

namespace ArxisStudio.Tests;

/// <summary>
/// Псевдотерминал в памяти: труба туда, труба оттуда, выход по команде теста.
/// </summary>
/// <remarks>
/// Оболочки за ним нет, и это его смысл: сеанс и вид проверяются на дороге
/// байтов, а не на поведении cmd. Что записал сеанс, тест читает строкой;
/// чего дождаться — говорит условием.
/// </remarks>
internal sealed class FakePty : IPseudoTerminal
{
    private readonly BlockingCollection<byte[]> _output = new();
    private readonly List<byte> _written = [];
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _wrote = new(false);

    public FakePty() => Output = new QueueStream(_output);

    public Stream Output { get; }

    public int? ExitCode { get; private set; }

    public bool Disposed { get; private set; }

    public List<(int Columns, int Rows)> Sizes { get; } = [];

    public event EventHandler<int>? Exited;

    /// <summary>Всё, что сеанс отправил оболочке, одной строкой.</summary>
    public string WrittenText
    {
        get
        {
            lock (_gate)
                return Encoding.UTF8.GetString([.. _written]);
        }
    }

    /// <summary>Оболочка «пишет» текст.</summary>
    public void Emit(string text) => Emit(Encoding.UTF8.GetBytes(text));

    /// <summary>Оболочка «пишет» байты — хоть половину буквы.</summary>
    public void Emit(byte[] bytes) => _output.Add(bytes);

    /// <summary>Оболочка выходит с кодом.</summary>
    public void Exit(int code)
    {
        ExitCode = code;
        Exited?.Invoke(this, code);
    }

    /// <summary>Забывает записанное — чтобы следующее ожидание не поймало прежнее.</summary>
    public void ClearWritten()
    {
        lock (_gate)
            _written.Clear();
    }

    /// <summary>Ждёт, пока записанное не станет тем, что нужно; иначе отдаёт то, что успело прийти.</summary>
    public string WaitForWritten(Func<string, bool> ready, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var text = WrittenText;

            if (ready(text))
                return text;

            _wrote.WaitOne(50);
        }

        return WrittenText;
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
            _written.AddRange(bytes);

        _wrote.Set();
    }

    public void Resize(int columns, int rows) => Sizes.Add((columns, rows));

    public void Dispose()
    {
        Disposed = true;
        _output.CompleteAdding();
    }
}

/// <summary>Поток чтения поверх очереди: блокируется, пока нечего читать, и кончается, когда очередь закрыта.</summary>
internal sealed class QueueStream(BlockingCollection<byte[]> queue) : Stream
{
    private byte[] _current = [];
    private int _offset;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_offset >= _current.Length)
        {
            try
            {
                _current = queue.Take();
            }
            catch (InvalidOperationException)
            {
                // Очередь закрыта — оболочка кончилась.
                return 0;
            }

            _offset = 0;
        }

        var length = Math.Min(count, _current.Length - _offset);

        Array.Copy(_current, _offset, buffer, offset, length);
        _offset += length;

        return length;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
