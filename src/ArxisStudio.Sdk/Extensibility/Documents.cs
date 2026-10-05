using Avalonia.Controls;

namespace ArxisStudio.Sdk;

/// <summary>
/// Редактор документов: модуль или плагин, который берётся открывать файлы.
/// </summary>
/// <remarks>
/// Центральная область студии сама не знает ни одного формата: что такое
/// документ и как его показывать, решает редактор, заявивший себя на файл.
/// Таких редакторов два: просмотрщик Code Viewer — плагин на общих правах — и
/// дизайнер форм, модуль студии; модули студия спрашивает раньше плагинов.
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

    /// <summary>
    /// Показывает файл там, где редактор держит его сам, — на своей панели, — а не вкладкой документа.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <returns>
    /// <c>true</c> — файл показан, и вкладку студия не открывает; <c>false</c> — открыть его вкладкой,
    /// как обычно (<see cref="OpenAsync"/>).
    /// </returns>
    /// <remarks>
    /// Студия спрашивает об этом редактор, взявшийся за файл (<see cref="CanOpen"/>), раньше, чем откроет
    /// вкладку: так дизайнер в режиме доски показывает форму на своём холсте, как сцена Unity — сцену, а не
    /// открывает её второй раз вкладкой. Уже открытую вкладку файла студия выводит вперёд, не спрашивая.
    /// Упавший показ записывается на плагин, и файл открывается вкладкой. По умолчанию — <c>false</c>.
    /// Появилось в SDK 7.18.
    /// </remarks>
    public virtual Task<bool> RevealAsync(string filePath) => Task.FromResult(false);
}

/// <summary>
/// Открытый документ: то, что стоит в центральной области, пока выбрана его
/// вкладка.
/// </summary>
/// <remarks>
/// <para>
/// <b>Несохранённое.</b> Документ, которого правят, говорит об этом сам —
/// <see cref="SetModified"/>, — и вкладка носит точку на месте крестика. Сохраняет его
/// <see cref="SaveAsync"/>: зовёт студия по Ctrl+S, перед закрытием и перед перезапуском.
/// </para>
/// <para>
/// <b>Закрытие.</b> Прежде чем закрыть — крестиком, Ctrl+W, вместе с окном или ради
/// перезапуска, — студия спрашивает документ (<see cref="CanCloseAsync"/>): здесь редактор с
/// автосохранением сохраняет сам, а занятый — отказывает. Документы, оставшиеся несохранёнными,
/// студия перечисляет человеку одним вопросом на все, как Visual Studio и Rider: «Сохранить»,
/// «Не сохранять» или «Отмена». Перед перезапуском она не спрашивает, а сохраняет: перезапуск
/// вернёт вкладки, и вернуть их надо с тем, что в них было.
/// </para>
/// <para>
/// Все эти вызовы — код плагина, и студия зовёт их через шов: упавший отвечает за себя, а не
/// роняет студию. Отказ закрыться у упавшего не в счёт — иначе сломанный документ держал бы
/// открытым окно, которое человек закрывает.
/// </para>
/// </remarks>
public abstract class DocumentView : IAsyncDisposable
{
    /// <summary>Содержимое вкладки.</summary>
    public abstract Control Content { get; }

    /// <summary>Что написано на вкладке.</summary>
    public abstract string Title { get; }

    /// <summary>В документе есть несохранённое: вкладка носит точку, закрытие спросит.</summary>
    /// <remarks>Появилось в SDK 7.14.</remarks>
    public bool IsModified { get; private set; }

    /// <summary>
    /// Куда отдать каретку, когда документ показывают; null — первому внутри, кто может её взять.
    /// </summary>
    /// <remarks>
    /// Как у <see cref="ToolWindow.FocusTarget"/>: место, с которого в документе работают, — холст,
    /// поле редактора. Спрашивается один раз, когда документ открыт; названный контрол должен лежать
    /// внутри <see cref="Content"/>. Появилось в SDK 7.14.
    /// </remarks>
    public virtual Control? FocusTarget => null;

    /// <summary><see cref="IsModified"/> сменилось.</summary>
    /// <remarks>Поднимает его <see cref="SetModified"/> — в потоке, где его позвали, то есть в потоке интерфейса.</remarks>
    public event EventHandler? ModifiedChanged;

    /// <summary>Сохраняет документ.</summary>
    /// <returns><c>true</c> — сохранено или сохранять нечего; <c>false</c> — не вышло, и закрытие останавливается.</returns>
    /// <remarks>
    /// Удачное сохранение снимает отметку само: зовите <see cref="SetModified"/> с <c>false</c>. По
    /// умолчанию сохранять нечего. Появилось в SDK 7.14.
    /// </remarks>
    public virtual Task<bool> SaveAsync() => Task.FromResult(true);

    /// <summary>Можно ли закрыть документ сейчас.</summary>
    /// <param name="reason">Почему закрывают.</param>
    /// <returns><c>false</c> — нельзя: вкладка остаётся, а с ней и окно.</returns>
    /// <remarks>
    /// Зовётся раньше вопроса о несохранённом: редактор с автосохранением сохраняет здесь и снимает
    /// отметку — тогда о нём человека не спросят. По умолчанию можно. Появилось в SDK 7.14.
    /// </remarks>
    public virtual ValueTask<bool> CanCloseAsync(DocumentCloseReason reason) => ValueTask.FromResult(true);

    /// <summary>Ставит или снимает отметку несохранённого.</summary>
    /// <param name="value">Есть ли несохранённое.</param>
    /// <remarks>Звать из потока интерфейса: вкладка перерисовывает точку тут же.</remarks>
    protected void SetModified(bool value)
    {
        if (IsModified == value)
            return;

        IsModified = value;
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

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

/// <summary>Почему документ закрывают.</summary>
/// <remarks>Появилось в SDK 7.14.</remarks>
public enum DocumentCloseReason
{
    /// <summary>Закрывают вкладку: крестиком или Ctrl+W.</summary>
    Tab,

    /// <summary>Закрывают окно студии.</summary>
    Window,

    /// <summary>Студия перезапускается и вернёт вкладку в новой копии.</summary>
    Restart,
}
