using System.Collections.Concurrent;
using ArxisStudio.Sdk;
using ArxisStudio.Settings;

namespace ArxisStudio.Tests;

/// <summary>Строка состояния, которой здесь никто не смотрит.</summary>
internal sealed class SilentStatus : IStudioStatus
{
    /// <inheritdoc/>
    public void Show(string message)
    {
    }
}

/// <summary>Строка состояния, которая помнит сказанное.</summary>
/// <remarks>
/// Очередь, а не список: служба проектов говорит и из фоновых потоков, и сказанное в них тест
/// обязан застать целым.
/// </remarks>
internal sealed class StatusProbe : IStudioStatus
{
    /// <summary>Что сказали, по порядку.</summary>
    public ConcurrentQueue<string> Said { get; } = new();

    /// <summary>Сказанное последним; null — не сказано ничего.</summary>
    public string? Last => Said.LastOrDefault();

    /// <inheritdoc/>
    public void Show(string message) => Said.Enqueue(message);
}

/// <summary>Диалоги менеджера плагинов, на которые отвечает тест.</summary>
internal sealed class DialogAnswers : IPluginDialogs
{
    /// <summary>Папка, которую «выбрал» человек; null — отказался.</summary>
    public string? Folder { get; set; }

    /// <summary>Ответ на подтверждение; по умолчанию — согласие.</summary>
    public Func<bool> Answer { get; set; } = () => true;

    /// <inheritdoc/>
    public Task<string?> AskFolderAsync(string title) => Task.FromResult(Folder);

    /// <inheritdoc/>
    public Task<string?> AskArchiveAsync(string title) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string title, string message, string confirm, bool danger) =>
        Task.FromResult(Answer());

    /// <inheritdoc/>
    public void Reveal(string path)
    {
    }
}
