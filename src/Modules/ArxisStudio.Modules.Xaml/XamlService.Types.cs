using ArxisStudio.Markup.Xaml.Loader;
using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.Markup.Xaml;
using ArxisStudio.Xaml;
using Avalonia.Threading;

namespace ArxisStudio.Modules.Xaml;

// Типы, которые ставят в форму: контролы проекта у хоста поколения и вопрос резолверу документа.
// Часть XamlService; общее описание типа — в XamlService.cs.
internal sealed partial class XamlService
{
    /// <inheritdoc/>
    /// <remarks>
    /// Хост перечисляет контролы проекта формы и его ссылок по именам (<see cref="ProjectControlInfo"/>):
    /// собранные живым поколением и формы, класса которых ещё нет. Без сессии — формы ещё никто не
    /// открывал — ответ пуст: поднимать поколение ради палитры служба не станет.
    /// </remarks>
    public async Task<IReadOnlyList<XamlPlaceable>> GetPlaceableAsync(
        CanonicalPath document,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();

        if (Placing(document) is not { } placing)
            return [];

        var (session, project) = placing;

        try
        {
            var controls = await session.Host.GetPlaceableControlsAsync(project, cancellationToken);

            return [.. controls.Select(Placeable)];
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            // Сессия кончилась или ещё не поднялась, пока спрашивали: перечислять нечего.
            return [];
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Контрол ищется у хоста заново по классу и разметке: запись контракта — имена, а хосту нужен его
    /// собственный ответ, со всеми сведениями о типе.
    /// </remarks>
    public async Task<bool> EnsureBuiltAsync(XamlPlaceable control, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(control);
        Dispatcher.UIThread.VerifyAccess();

        if (_session is not { IsStarted: true, IsRetiring: false } session)
            return false;

        try
        {
            var listed = await session.Host.GetPlaceableControlsAsync(control.Project, cancellationToken);

            if (listed.FirstOrDefault(info => info.ClassName == control.ClassName && info.Document == control.Document) is not { } info)
                return false;

            return await session.Host.EnsureBuiltAsync(info, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            // Решение закрыли, пока собирали.
            return false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Спрашивается резолвер самой формы — тот, которым её прочтёт загрузка, — в пространствах её
    /// корня, поэтому ответ не разойдётся с исходом. Форма, которую служба не держит открытой, ответа не
    /// получает: её проект может быть и не в поколении.
    /// </remarks>
    public async Task<bool> ResolvesAsync(
        CanonicalPath document,
        XamlSnippet type,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        Dispatcher.UIThread.VerifyAccess();

        var entry = _session?.Documents.FirstOrDefault(open => open.Path == document);

        if (entry?.Live.Session is not { } live || live.Document.Root is not { } root)
            return true;

        try
        {
            var resolution = await live.Environment.TypeResolver.ResolveAsync(
                new XamlTypeName(type.XmlNamespace, type.Name),
                root.NamespaceContext,
                cancellationToken);

            return resolution.Success;
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or ObjectDisposedException)
        {
            // Резолвер, который не может ответить, — не отказ: правка пройдёт, и что не так, скажет загрузка.
            return true;
        }
    }

    /// <summary>Сессия, поднятая для решения, и проект формы в нём; null — спросить не у кого.</summary>
    private (Session.XamlDesignSession Session, ProjectIdentity Project)? Placing(CanonicalPath document)
    {
        // Проект — из снимка профиля дизайна: хост работает по нему, и у его проектов свои идентичности.
        if (document.IsEmpty
            || _session is not { IsStarted: true, IsRetiring: false } session
            || session.DesignSnapshot is not { } snapshot
            || !snapshot.TryGetProjectForFile(document, out var project))
        {
            return null;
        }

        return (session, project.Identity);
    }

    private static XamlPlaceable Placeable(ProjectControlInfo info) =>
        new(info.Name, info.ClassName, info.XmlNamespace, info.SuggestedPrefix, info.Project, info.Document, info.IsBuilt);
}
