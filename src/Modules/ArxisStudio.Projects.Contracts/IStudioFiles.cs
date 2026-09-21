using System.Collections.Immutable;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Projects;

/// <summary>Путь и то, куда он переезжает или копируется, — оба полными путями.</summary>
/// <param name="From">Откуда.</param>
/// <param name="To">Куда: полный путь, включая имя; переименование — это переезд в той же папке.</param>
public sealed record FileMove(CanonicalPath From, CanonicalPath To)
{
    /// <summary>
    /// Файл на месте назначения заменить, а не отказать: прежнее содержимое уходит в локальную
    /// историю, и вернуть его можно оттуда.
    /// </summary>
    /// <remarks>
    /// Так отвечают на вопрос «Заменить?» при вставке. Заменяется только файл файлом: папку на месте
    /// назначения служба не сливает и не стирает — занятая папка остаётся отказом
    /// <see cref="ProjectsDiagnosticCodes.TargetExists"/>. Появилось в версии 1.4.
    /// </remarks>
    public bool Replace { get; init; }
}

/// <summary>Что создать: файл с содержимым или каталог — полным путём.</summary>
/// <param name="Path">Где: полный путь, включая имя.</param>
/// <remarks>
/// Недостающие каталоги на пути создаются сами: <c>Views/Dialogs/About.axaml</c> в проекте без
/// <c>Views</c> заводит оба каталога, и отмена уберёт их вместе с файлом. Появилось в версии 1.6.
/// </remarks>
public sealed record FileCreation(CanonicalPath Path)
{
    /// <summary>Содержимое файла — байты как есть, с отметкой порядка байт, если она нужна; у каталога пусто.</summary>
    public ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>Создать каталог, а не файл.</summary>
    public bool IsDirectory { get; init; }
}

/// <summary>Что сделала правка файлов.</summary>
/// <remarks>
/// Приходит тем, кто держит файлы открытыми: редактор, у которого документ переехал, должен
/// следовать за ним, а у которого удалили — закрыться, и узнать об этом ему больше неоткуда.
/// </remarks>
public sealed class FilesChangedEventArgs : EventArgs
{
    /// <summary>Заводит перемену.</summary>
    /// <param name="moved">Что куда переехало.</param>
    /// <param name="copied">Что откуда скопировано.</param>
    /// <param name="deleted">Что удалено.</param>
    public FilesChangedEventArgs(
        ImmutableArray<FileMove> moved,
        ImmutableArray<FileMove> copied,
        ImmutableArray<CanonicalPath> deleted)
        : this(moved, copied, deleted, ImmutableArray<CanonicalPath>.Empty)
    {
    }

    /// <summary>Заводит перемену вместе с созданным.</summary>
    /// <param name="moved">Что куда переехало.</param>
    /// <param name="copied">Что откуда скопировано.</param>
    /// <param name="deleted">Что удалено.</param>
    /// <param name="created">Что создано.</param>
    /// <remarks>Появилось в версии 1.6.</remarks>
    public FilesChangedEventArgs(
        ImmutableArray<FileMove> moved,
        ImmutableArray<FileMove> copied,
        ImmutableArray<CanonicalPath> deleted,
        ImmutableArray<CanonicalPath> created)
    {
        Moved = moved.IsDefault ? [] : moved;
        Copied = copied.IsDefault ? [] : copied;
        Deleted = deleted.IsDefault ? [] : deleted;
        Created = created.IsDefault ? [] : created;
    }

    /// <summary>Что куда переехало — файлы и папки, как их просили.</summary>
    public ImmutableArray<FileMove> Moved { get; }

    /// <summary>Что откуда скопировано.</summary>
    public ImmutableArray<FileMove> Copied { get; }

    /// <summary>Что удалено — файлы и папки, как их просили.</summary>
    public ImmutableArray<CanonicalPath> Deleted { get; }

    /// <summary>Что создано — файлы и каталоги, как их просили; каталогов на пути здесь нет.</summary>
    /// <remarks>Появилось в версии 1.6.</remarks>
    public ImmutableArray<CanonicalPath> Created { get; }
}

/// <summary>
/// Файлы открытого решения: создать, переместить, скопировать, удалить.
/// </summary>
/// <remarks>
/// <para>
/// Служба одна на студию, живёт в модуле <c>arxis.projects</c> и берётся
/// <see cref="StudioProjectsAccess.Files"/>; появилась в версии 1.3, создание — в 1.6. Правит файлы
/// та же служба, что держит модель, — иначе окно, правящее диск в обход неё, расходилось бы с её
/// снимком, а файл проекта, называющий переименованный файл по имени, остался бы со старым именем.
/// </para>
/// <para>
/// <b>Что делает правка.</b> Проверяет всё до первого байта; делает на диске и откатывает сделанное,
/// если диск отказал посередине; переписывает в файлах проектов ссылки, которые называют путь
/// буквально (<c>Include</c>, <c>Update</c>, <c>Remove</c>, <c>DependentUpon</c>, маска папки вроде
/// <c>Assets\**</c>) — только в собственном файле проекта, импорты не трогает; записывает действие в
/// локальную историю — с содержимым удалённого, так что вернуть его можно оттуда; перечитывает
/// модель причиной <see cref="ProjectsLoadReason.Files"/> и только потом возвращается и сообщает
/// <see cref="Changed"/>.
/// </para>
/// <para>
/// <b>Удаление — насовсем</b>, как в Rider: корзины нет, страхует локальная история. Спрашивать
/// человека — дело того, кто зовёт.
/// </para>
/// <para>
/// <b>Правится только то, что внутри папок проектов.</b> Файл проекта, решение, сама папка проекта
/// и выход сборки — отказ <see cref="ProjectsDiagnosticCodes.OutsideProjects"/>: проект
/// переименовывают вместе с решением, а не как файл. Копия читает источник откуда угодно — так файлы
/// из проводника вставляются в проект, — а внутри папок проектов должно быть только назначение
/// (с версии 1.4).
/// </para>
/// <para>
/// <b>Вложенные файлы служба не угадывает.</b> <c>MainWindow.axaml.cs</c> переезжает вместе с
/// <c>MainWindow.axaml</c>, только если его назвали в той же пачке: правило вложенности — дело
/// того, кто показывает дерево, и у разных окон оно может быть разным.
/// </para>
/// <para>
/// <b>Провал — результат, а не исключение</b>, как у пакетов. Исключения — пустым и неверным
/// аргументам, отмене до начала и остановленной службе. Начатая правка не отменяется: оборванная
/// посередине, она оставила бы диск ни в том, ни в другом виде.
/// </para>
/// </remarks>
public interface IStudioFiles
{
    /// <summary>Создаёт файлы и каталоги — пачкой, одним действием истории.</summary>
    /// <param name="items">Что создать; недостающие каталоги на пути создаются сами.</param>
    /// <param name="label">Метка действия для человека: «Создание NewFile1.txt».</param>
    /// <param name="cancellationToken">Отмена — пока правка не началась.</param>
    /// <returns>Итог; провал приходит с диагностиками.</returns>
    /// <remarks>
    /// Созданное не затирает ничего: занятое место — отказ
    /// <see cref="ProjectsDiagnosticCodes.TargetExists"/>, и повтор в пачке тоже. Ссылок в файле
    /// проекта создание не пишет: SDK-проект берёт новый файл своими масками сам, а проект без масок
    /// его не увидит, пока его туда не добавят. Отмена действия стирает созданное, если его не
    /// меняли, и каталоги, которые остались пустыми. Появилось в версии 1.6.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Пачка пуста, в ней пустой путь, у каталога есть содержимое или метка пуста.
    /// </exception>
    /// <exception cref="OperationCanceledException">Отменено до начала.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> CreateAsync(
        IReadOnlyList<FileCreation> items,
        string label,
        CancellationToken cancellationToken = default);

    /// <summary>Перемещает или переименовывает файлы и папки — пачкой, одним действием истории.</summary>
    /// <param name="moves">Что куда.</param>
    /// <param name="label">Метка действия для человека: «Переименование MainWindow.axaml».</param>
    /// <param name="cancellationToken">Отмена — пока правка не началась.</param>
    /// <returns>Итог; провал приходит с диагностиками.</returns>
    /// <exception cref="ArgumentException">Пачка пуста, в ней пустой путь или метка пуста.</exception>
    /// <exception cref="OperationCanceledException">Отменено до начала.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> MoveAsync(
        IReadOnlyList<FileMove> moves,
        string label,
        CancellationToken cancellationToken = default);

    /// <summary>Копирует файлы и папки — пачкой, одним действием истории.</summary>
    /// <param name="copies">Что куда; источник может лежать и вне решения.</param>
    /// <param name="label">Метка действия для человека.</param>
    /// <param name="cancellationToken">Отмена — пока правка не началась.</param>
    /// <returns>Итог; провал приходит с диагностиками.</returns>
    /// <exception cref="ArgumentException">Пачка пуста, в ней пустой путь или метка пуста.</exception>
    /// <exception cref="OperationCanceledException">Отменено до начала.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> CopyAsync(
        IReadOnlyList<FileMove> copies,
        string label,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет файлы и папки насовсем — пачкой, одним действием истории.</summary>
    /// <param name="paths">Что удалить.</param>
    /// <param name="label">Метка действия для человека.</param>
    /// <param name="cancellationToken">Отмена — пока правка не началась.</param>
    /// <returns>Итог; провал приходит с диагностиками.</returns>
    /// <exception cref="ArgumentException">Пачка пуста, в ней пустой путь или метка пуста.</exception>
    /// <exception cref="OperationCanceledException">Отменено до начала.</exception>
    /// <exception cref="ObjectDisposedException">Служба остановлена.</exception>
    Task<ProjectOperationResult> DeleteAsync(
        IReadOnlyList<CanonicalPath> paths,
        string label,
        CancellationToken cancellationToken = default);

    /// <summary>Правка прошла: что создано, что куда уехало, что скопировано, что удалено.</summary>
    /// <remarks>Приходит в поток интерфейса после того, как модель перечитана.</remarks>
    event EventHandler<FilesChangedEventArgs>? Changed;
}
