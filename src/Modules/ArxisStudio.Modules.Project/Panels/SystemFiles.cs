using ArxisStudio.Modules.Project.Model;
using ArxisStudio.ProjectSystem;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace ArxisStudio.Modules.Project.Panels;

/// <summary>
/// Файлы в буфере обмена системы: положить свои, узнать, наши ли они ещё, прочитать чужие.
/// </summary>
internal interface ISystemFiles
{
    /// <summary>Кладёт буфер правки в буфер системы: файлами — для проводника, отметкой — для себя.</summary>
    /// <param name="clip">Буфер правки.</param>
    Task PutAsync(FileClip clip);

    /// <summary>
    /// Что вставлять: свой буфер, если он ещё в системе, иначе чужие файлы — копией; пусто — нечего.
    /// </summary>
    /// <param name="own">Свой буфер правки — на случай, когда буфера системы нет вовсе.</param>
    Task<FileClip?> TakeAsync(FileClip? own);

    /// <summary>Убирает свой буфер из системы, если он ещё там: чужое не трогает.</summary>
    /// <param name="clip">Свой буфер.</param>
    Task ForgetAsync(FileClip clip);
}

/// <summary>
/// Буфер обмена системы для файлов окна проекта.
/// </summary>
/// <remarks>
/// <para>
/// Скопированное и вырезанное в окне кладётся в буфер системы файлами — их вставит проводник, — и
/// рядом ставится отметка процесса с буфером правки. По отметке вставка узнаёт своё: вырезанное
/// переносится, скопированное копируется. Нет отметки, а файлы есть — их положил проводник или
/// другая программа, и вставка их только копирует: переносить чужое из-под чужой программы окно не
/// берётся. Нет ни того ни другого — буфер занят чем-то ещё, и прежнее вырезанное кончилось, как в
/// проводнике.
/// </para>
/// <para>
/// Проводник, вставляя вырезанное в окне, копирует, а не переносит: отметки «перенести» для него
/// окно не ставит — унести файлы решения из-под студии чужой программой значило бы разойтись со
/// снимком. Буфера у платформы может не быть — тогда правка живёт своим состоянием.
/// </para>
/// </remarks>
/// <param name="top">Окно верхнего уровня, в котором живёт панель.</param>
internal sealed class SystemFiles(Func<TopLevel?> top) : ISystemFiles
{
    private static readonly DataFormat<FileClip> Marker = DataFormat.CreateInProcessFormat<FileClip>("arxis.project.clip");

    /// <summary>Подмена для тестов: у безголового прогона буфера системы может не быть.</summary>
    internal static Func<ISystemFiles>? Override { get; set; }

    /// <summary>Буфер системы окна — или подмена теста.</summary>
    /// <param name="top">Окно верхнего уровня.</param>
    public static ISystemFiles For(Func<TopLevel?> top) => Override?.Invoke() ?? new SystemFiles(top);

    /// <inheritdoc/>
    public async Task PutAsync(FileClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (top() is not { Clipboard: { } clipboard } window)
            return;

        var transfer = new DataTransfer();

        foreach (var path in clip.Paths)
        {
            IStorageItem? item = Directory.Exists(path.Value)
                ? await window.StorageProvider.TryGetFolderFromPathAsync(path.Value).ConfigureAwait(true)
                : await window.StorageProvider.TryGetFileFromPathAsync(path.Value).ConfigureAwait(true);

            if (item is not null)
                transfer.Add(DataTransferItem.CreateFile(item));
        }

        transfer.Add(DataTransferItem.Create(Marker, clip));

        await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
    }

    /// <inheritdoc/>
    public async Task<FileClip?> TakeAsync(FileClip? own)
    {
        if (top()?.Clipboard is not { } clipboard)
            return own;

        if (await Ours(clipboard).ConfigureAwait(true) is { } ours)
            return ours;

        if (await clipboard.TryGetFilesAsync().ConfigureAwait(true) is not { Length: > 0 } items)
            return null;

        var foreign = new List<ClipItem>();

        foreach (var item in items)
        {
            if (item.TryGetLocalPath() is { } local && CanonicalPath.TryCreate(local, out var path))
                foreign.Add(new ClipItem(path, Directory.Exists(local), []));
        }

        return foreign.Count > 0 ? new FileClip(ClipMode.Copy, foreign) : null;
    }

    /// <inheritdoc/>
    public async Task ForgetAsync(FileClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (top()?.Clipboard is { } clipboard && ReferenceEquals(await Ours(clipboard).ConfigureAwait(true), clip))
            await clipboard.ClearAsync().ConfigureAwait(true);
    }

    private static async Task<FileClip?> Ours(IClipboard clipboard) =>
        await clipboard.TryGetInProcessDataAsync().ConfigureAwait(true) is { } data
            ? await data.TryGetValueAsync(Marker).ConfigureAwait(true)
            : null;
}
