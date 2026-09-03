namespace ArxisStudio.Modules.Terminal.Pty;

/// <summary>
/// Псевдотерминал с оболочкой внутри: байты туда, байты оттуда, размер окна.
/// </summary>
/// <remarks>
/// Интерфейс — шов между модулем и библиотекой псевдотерминала. За ним
/// прячется всё платформенное: ConPTY на Windows, <c>forkpty</c> на POSIX. Сеанс
/// и панель о платформе не знают, а тесты подставляют сюда трубу вместо
/// оболочки и проверяют дорогу байтов без единого настоящего процесса.
/// </remarks>
public interface IPseudoTerminal : IDisposable
{
    /// <summary>
    /// Что пишет оболочка.
    /// </summary>
    /// <remarks>
    /// Чтение блокирующее и не кончается вместе с процессом: у ConPTY труба
    /// остаётся открытой, пока псевдоконсоль не закроют. Освобождает читателя
    /// <see cref="IDisposable.Dispose"/> — исключением ввода-вывода, и это не сбой.
    /// </remarks>
    Stream Output { get; }

    /// <summary>Код выхода оболочки; null — она ещё идёт.</summary>
    int? ExitCode { get; }

    /// <summary>Оболочка вышла; в аргументе — код выхода. Приходит с фонового потока.</summary>
    event EventHandler<int>? Exited;

    /// <summary>Отдаёт оболочке байты: набранное, вставленное, ответы терминала.</summary>
    /// <param name="bytes">Что отдать.</param>
    void Write(ReadOnlySpan<byte> bytes);

    /// <summary>Сообщает оболочке новый размер окна в знаках.</summary>
    /// <param name="columns">Ширина.</param>
    /// <param name="rows">Высота.</param>
    void Resize(int columns, int rows);
}
