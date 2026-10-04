using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Превью файлов: поставщики по расширениям и подписчики на перемены — один реестр на студию.
/// </summary>
/// <remarks>
/// <para>
/// Плагину он выдаётся обёрткой (<see cref="PluginFilePreviews"/>), которая знает, чей это вызов: у
/// поставщика и у подписки есть хозяин, и с выгрузкой плагина студия снимает всё, что на него записано
/// (<see cref="RemoveOwnedBy"/>). Оставшись в реестре, поставщик и обработчик держали бы контекст
/// загрузки выгруженного плагина.
/// </para>
/// <para>
/// Чужой код — поставщика и подписчика — студия зовёт только через шов сбоев (<see cref="PluginGuard"/>):
/// упавший поставщик отвечает пустотой, упавший подписчик не мешает остальным, и падение засчитывается
/// виновнику. Отмена — не падение: плитка ушла из вида, и поставщик тут ни при чём.
/// </para>
/// <para>
/// Записи реестра ставятся при подъёме плагина, а читаются плитками, поэтому реестр закрыт замком; чужой
/// код зовётся вне замка.
/// </para>
/// </remarks>
/// <param name="log">Журнал студии: занятое расширение и негодное объявление.</param>
/// <param name="guard">Шов сбоев.</param>
public sealed class StudioFilePreviews(IStudioLog log, PluginGuard guard)
{
    private const string LogSource = "Previews";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Provided> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Subscriber> _subscribers = [];

    /// <summary>Есть ли поставщик для расширения файла.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    public bool CanPreview(string filePath)
    {
        if (ExtensionOf(filePath) is not { } extension)
            return false;

        lock (_gate)
            return _providers.ContainsKey(extension);
    }

    /// <summary>Превью файла у поставщика его расширения.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="pixels">Длинная сторона, которую покажут.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Превью; null — поставщика нет, ему нечего показать или он упал.</returns>
    /// <exception cref="OperationCanceledException">Отменили, пока поставщик отвечал.</exception>
    public async Task<FilePreview?> GetAsync(string filePath, int pixels, CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => GetAsync(filePath, pixels, cancellationToken));

        if (pixels <= 0 || ExtensionOf(filePath) is not { } extension)
            return null;

        Provided provided;

        lock (_gate)
        {
            if (!_providers.TryGetValue(extension, out provided))
                return null;
        }

        FilePreview? answer = null;

        var ran = await guard.RunAsync(provided.Owner, $"превью файла {Path.GetFileName(filePath)}", async () =>
        {
            try
            {
                answer = await provided.Provider.GetPreviewAsync(filePath, pixels, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Плитка ушла из вида, пока поставщик отвечал: это не его сбой.
            }
        });

        cancellationToken.ThrowIfCancellationRequested();

        return ran && answer is { Image.IsEmpty: false } ? answer : null;
    }

    /// <summary>Ставит поставщика его хозяина.</summary>
    /// <param name="owner">Идентификатор плагина.</param>
    /// <param name="provider">Поставщик.</param>
    /// <returns>Запись, снимающая поставщика.</returns>
    public IDisposable Register(string owner, IFilePreviewProvider provider)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentNullException.ThrowIfNull(provider);

        // Список расширений — чужой код: читается через шов, один раз.
        var declared = guard.Get(owner, "расширения поставщика превью", () => provider.Extensions) ?? [];
        var registration = new Registration(this);

        lock (_gate)
        {
            foreach (var written in declared)
            {
                if (Normalized(written) is not { } extension)
                {
                    log.Write(StudioLogLevel.Warning, LogSource, $"{owner}: «{written}» — не расширение файла, превью для него не ставится");
                    continue;
                }

                if (_providers.TryGetValue(extension, out var other) && !string.Equals(other.Owner, owner, StringComparison.Ordinal))
                {
                    log.Write(StudioLogLevel.Warning, LogSource, $"{owner}: превью {extension} уже даёт {other.Owner}");
                    continue;
                }

                _providers[extension] = new Provided(owner, provider, registration);
            }
        }

        return registration;
    }

    /// <summary>Говорит подписчикам, что превью файла сменилось.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <remarks>Звать можно из любого потока: подписчики слышат в потоке интерфейса.</remarks>
    public void Invalidate(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Invalidate(filePath));
            return;
        }

        Subscriber[] subscribers;

        lock (_gate)
            subscribers = [.. _subscribers];

        var args = new FilePreviewChangedEventArgs(filePath);

        foreach (var subscriber in subscribers)
            guard.Run(subscriber.Owner, "обработчик перемены превью", () => subscriber.Handler(this, args));
    }

    /// <summary>Подписывает обработчик хозяина на перемены превью.</summary>
    /// <param name="owner">Идентификатор плагина.</param>
    /// <param name="handler">Обработчик; null — ничего.</param>
    public void Subscribe(string owner, EventHandler<FilePreviewChangedEventArgs>? handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);

        if (handler is null)
            return;

        lock (_gate)
            _subscribers.Add(new Subscriber(owner, handler));
    }

    /// <summary>Отписывает обработчик хозяина — последнюю такую подписку, как у событий .NET.</summary>
    /// <param name="owner">Идентификатор плагина.</param>
    /// <param name="handler">Обработчик; null — ничего.</param>
    public void Unsubscribe(string owner, EventHandler<FilePreviewChangedEventArgs>? handler)
    {
        if (handler is null)
            return;

        lock (_gate)
        {
            var index = _subscribers.FindLastIndex(subscriber =>
                string.Equals(subscriber.Owner, owner, StringComparison.Ordinal) && subscriber.Handler == handler);

            if (index >= 0)
                _subscribers.RemoveAt(index);
        }
    }

    /// <summary>Снимает поставщиков и подписки ушедшего плагина.</summary>
    /// <param name="owner">Идентификатор плагина.</param>
    public void RemoveOwnedBy(string owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);

        lock (_gate)
        {
            foreach (var extension in _providers
                         .Where(pair => string.Equals(pair.Value.Owner, owner, StringComparison.Ordinal))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _providers.Remove(extension);
            }

            _subscribers.RemoveAll(subscriber => string.Equals(subscriber.Owner, owner, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Снимает расширения, которые запись ещё держит: вытесненная новой записью того же плагина их уже
    /// не держит — даже если поставщик в обеих один и тот же.
    /// </summary>
    private void Withdraw(Registration registration)
    {
        lock (_gate)
        {
            foreach (var extension in _providers
                         .Where(pair => ReferenceEquals(pair.Value.Registration, registration))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                _providers.Remove(extension);
            }
        }
    }

    /// <summary>Расширение файла с точкой; null — его нет.</summary>
    private static string? ExtensionOf(string? filePath) =>
        string.IsNullOrEmpty(filePath) ? null : Path.GetExtension(filePath) is { Length: > 1 } extension ? extension : null;

    /// <summary>Объявленное расширение, каким его сравнивают; null — это не расширение.</summary>
    private static string? Normalized(string? written) =>
        written is { Length: > 1 } && written[0] == '.' && written.IndexOfAny(['/', '\\', '*', '?']) < 0 ? written : null;

    /// <summary>Поставщик, его хозяин и запись, которой он поставлен.</summary>
    private readonly record struct Provided(string Owner, IFilePreviewProvider Provider, Registration Registration);

    /// <summary>Обработчик и его хозяин.</summary>
    private readonly record struct Subscriber(string Owner, EventHandler<FilePreviewChangedEventArgs> Handler);

    /// <summary>Запись поставщика: снимает его расширения, если они ещё за ней.</summary>
    private sealed class Registration(StudioFilePreviews registry) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            registry.Withdraw(this);
        }
    }
}

/// <summary>
/// Превью файлов глазами одного плагина.
/// </summary>
/// <remarks>
/// Реестр один на студию, а хозяин у поставщика и у подписки свой: контракт SDK о хозяине не говорит, и
/// подставить его может только тот, кто выдаёт плагину контекст, — как у команд и полосы.
/// </remarks>
/// <param name="registry">Общий реестр.</param>
/// <param name="pluginId">Чьи поставщики и подписки ставит эта обёртка.</param>
public sealed class PluginFilePreviews(StudioFilePreviews registry, string pluginId) : IStudioFilePreviews
{
    /// <inheritdoc/>
    public event EventHandler<FilePreviewChangedEventArgs>? Changed
    {
        add => registry.Subscribe(pluginId, value);
        remove => registry.Unsubscribe(pluginId, value);
    }

    /// <inheritdoc/>
    public bool CanPreview(string filePath) => registry.CanPreview(filePath);

    /// <inheritdoc/>
    public Task<FilePreview?> GetAsync(string filePath, int pixels, CancellationToken cancellationToken = default) =>
        registry.GetAsync(filePath, pixels, cancellationToken);

    /// <inheritdoc/>
    public IDisposable Register(IFilePreviewProvider provider) => registry.Register(pluginId, provider);

    /// <inheritdoc/>
    public void Invalidate(string filePath) => registry.Invalidate(filePath);
}
