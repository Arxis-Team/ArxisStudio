using System.Threading.Channels;

namespace ArxisStudio.Modules.Projects.Engine;

/// <summary>
/// Очередь работы над движком: по одному делу, в порядке прихода.
/// </summary>
/// <remarks>
/// MSBuild держит на процесс общие кэши, окружение и единственную регистрацию, и две его работы,
/// идущие рядом, делят их без спроса. Поэтому всё, что его трогает, идёт одной полосой. Очередь не
/// знает, что в ней стоит: склейку и отмену решает тот, кто ставит дела, а она отвечает за порядок и
/// за то, чтобы упавшее дело не остановило следующие.
/// </remarks>
internal sealed class Lane
{
    private readonly Channel<Func<Task>> _queue =
        Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Action<Exception> _fault;

    /// <summary>Заводит очередь и её единственного читателя.</summary>
    /// <param name="fault">Куда сказать об упавшем деле.</param>
    public Lane(Action<Exception> fault)
    {
        _fault = fault;
        Completion = Task.Run(ReadAsync);
    }

    /// <summary>Завершится, когда очередь закрыта и последнее дело кончилось.</summary>
    public Task Completion { get; }

    /// <summary>Ставит дело в конец очереди.</summary>
    /// <param name="work">Дело.</param>
    /// <returns><c>false</c> — очередь закрыта, и дело не встанет.</returns>
    public bool Enqueue(Func<Task> work) => _queue.Writer.TryWrite(work);

    /// <summary>Закрывает очередь: стоящие дела доделаются, новые не встанут.</summary>
    public void Complete() => _queue.Writer.TryComplete();

    private async Task ReadAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await work();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _fault(e);
            }
        }
    }
}
