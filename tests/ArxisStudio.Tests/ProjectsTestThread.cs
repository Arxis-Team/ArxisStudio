using System.Collections.Concurrent;
using ArxisStudio.Modules.Projects.Delivery;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Поток интерфейса службы проектов, которым тест управляет сам.
/// </summary>
/// <remarks>
/// Настоящий поток со своей очередью и своим контекстом синхронизации: продолжение <c>await</c>,
/// начатого в нём, возвращается в него же — как у диспетчера студии, — и задача студии, начатая
/// здесь, здесь же и снимается. Простой ждётся маркером в очереди, а не паузой: маркер приходит
/// ровно тогда, когда до него дошла очередь.
/// </remarks>
internal sealed class ProjectsTestThread : IProjectsThread, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public ProjectsTestThread()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "projects-ui" };
        _thread.Start();
    }

    /// <summary>Что бросили дела потока; в студии это ушло бы необработанным.</summary>
    public ConcurrentQueue<Exception> Crashes { get; } = new();

    /// <inheritdoc/>
    public bool CheckAccess() => Thread.CurrentThread == _thread;

    /// <inheritdoc/>
    public void Post(Action action)
    {
        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
            // Тест кончился и поток закрыт: служба дописывает то, чего уже никто не прочтёт.
        }
    }

    /// <inheritdoc/>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        var started = new TaskCompletionSource<Task<T>>(TaskCreationOptions.RunContinuationsAsynchronously);

        Post(() =>
        {
            try
            {
                started.SetResult(work());
            }
            catch (Exception e)
            {
                started.SetException(e);
            }
        });

        return started.Task.Unwrap();
    }

    /// <summary>Ждёт, пока поток разберёт отданное — и то, что отдали по ходу разбора.</summary>
    public async Task IdleAsync()
    {
        while (true)
        {
            var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Post(() => idle.SetResult(_queue.Count == 0));

            if (await idle.Task.WaitAsync(TimeSpan.FromSeconds(30)))
                return;
        }
    }

    /// <summary>Разбирает очередь изнутри текущего дела — так прокачивает поток модальное окно.</summary>
    public void PumpNested()
    {
        Assert.True(CheckAccess(), "вложенная прокачка — только из самого потока");

        while (_queue.TryTake(out var action))
            Run(action);
    }

    /// <inheritdoc/>
    public void Dispose() => _queue.CompleteAdding();

    private void Pump()
    {
        SynchronizationContext.SetSynchronizationContext(new Context(this));

        foreach (var action in _queue.GetConsumingEnumerable())
            Run(action);
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Crashes.Enqueue(e);
        }
    }

    private sealed class Context(ProjectsTestThread owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => owner.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("поток службы проектов синхронно не зовут");

        public override SynchronizationContext CreateCopy() => this;
    }
}
