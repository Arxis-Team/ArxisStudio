using ArxisStudio.Sdk;

namespace Arxis.CodeViewer;

/// <summary>
/// Точка входа пробного просмотрщика.
/// </summary>
/// <remarks>
/// Заявлять редактор документов не нужно: студия находит наследников
/// <see cref="DocumentEditor"/> в сборке плагина сама, как находит команды по
/// атрибуту. До первого открытого файла объявленного типа плагин спит —
/// будит его <c>onFileType:</c> из манифеста.
/// </remarks>
public sealed class CodeViewerPlugin : StudioPlugin
{
    private IStudioContext? _context;

    /// <inheritdoc/>
    public override void Activate(IStudioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        context.Log.Write(StudioLogLevel.Info, "CodeViewer", "Просмотрщик поднят");
    }

    /// <inheritdoc/>
    public override void Deactivate()
    {
        _context?.Log.Write(StudioLogLevel.Info, "CodeViewer", "Просмотрщик выключен");
        _context = null;
    }
}
