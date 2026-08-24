using Avalonia.Controls;

namespace ArxisStudio.Sdk;

/// <summary>
/// Редактор документов: модуль или плагин, который берётся открывать файлы.
/// </summary>
/// <remarks>
/// Центральная область студии сама не знает ни одного формата: что такое
/// документ и как его показывать, решает редактор, заявивший себя на файл.
/// Дизайнер форм — первый такой редактор, и живёт он в модуле на общих правах.
/// </remarks>
public abstract class DocumentEditor
{
    /// <summary>Что студия дала модулю; доступен после <see cref="Attach"/>.</summary>
    protected IStudioContext Context { get; private set; } = null!;

    /// <summary>Связывает редактор со студией.</summary>
    /// <param name="context">Что студия даёт модулю.</param>
    public void Attach(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
    }

    /// <summary>Берётся ли редактор за этот файл.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    public abstract bool CanOpen(string filePath);

    /// <summary>Открывает документ.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>Представление документа или сообщение, почему открыть не удалось.</returns>
    public abstract Task<(DocumentView? View, string? Error)> OpenAsync(string filePath);
}

/// <summary>
/// Открытый документ: то, что стоит в центральной области, пока выбрана его
/// вкладка.
/// </summary>
public abstract class DocumentView : IAsyncDisposable
{
    /// <summary>Содержимое вкладки.</summary>
    public abstract Control Content { get; }

    /// <summary>Что написано на вкладке.</summary>
    public abstract string Title { get; }

    /// <summary>Вкладка документа стала активной.</summary>
    public virtual void OnActivated()
    {
    }

    /// <summary>Активной стала другая вкладка.</summary>
    public virtual void OnDeactivated()
    {
    }

    /// <inheritdoc/>
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
