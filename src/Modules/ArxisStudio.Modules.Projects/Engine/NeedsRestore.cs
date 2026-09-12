using ArxisStudio.ProjectSystem;
using ArxisStudio.ProjectSystem.MSBuild;

namespace ArxisStudio.Modules.Projects.Engine;

/// <summary>
/// Ждёт ли открытое восстановления.
/// </summary>
/// <remarks>
/// Спрашивается у самой загрузки: провайдер MSBuild говорит <c>APS2005</c> тогда и только тогда,
/// когда проект объявил пакеты, а итог их восстановления читать неоткуда — файла assets нет или он
/// не прочёлся. Считать самим, по свойствам проектов, значило бы держать вторую копию этого правила
/// и разойтись с ним на первом же проекте без пакетов.
/// </remarks>
internal static class NeedsRestore
{
    /// <summary>Говорит ли итог загрузки, что пакеты не восстановлены.</summary>
    /// <param name="result">Итог загрузки.</param>
    public static bool From(WorkspaceLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var diagnostic in result.Diagnostics)
        {
            if (string.Equals(diagnostic.Code, MSBuildDiagnosticCodes.RestoreAssetsMissing, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
