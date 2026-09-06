using ArxisStudio.Sdk;

namespace ArxisStudio.Services;

/// <summary>
/// Находки глазами одного расширения.
/// </summary>
/// <remarks>
/// Список находок один на студию, а источников в нём много, и каждый отвечает
/// за свой участок целиком: <see cref="IStudioProblems.Report"/> заменяет всё,
/// что источник сообщал прежде. Имя источника при этом сочиняет сам источник —
/// и без хозяина впереди одно расширение могло бы назваться чужим именем и
/// пустым списком снять чужие находки. Приставку ставит тот, кто выдал
/// контекст: другого места, знающего хозяина, нет.
/// <para>
/// Читать список позволено целиком и без приставки: панель проблем — это и
/// есть чтение всего, что нашли все. Право сказать и право видеть здесь
/// разные, как у журнала.
/// </para>
/// </remarks>
/// <param name="problems">Находки студии.</param>
/// <param name="pluginId">Чьи находки пишет эта обёртка.</param>
public sealed class PluginProblems(IStudioProblems problems, string pluginId) : IStudioProblems
{
    /// <inheritdoc/>
    public IReadOnlyList<StudioProblem> All => problems.All;

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => problems.Changed += value;
        remove => problems.Changed -= value;
    }

    /// <inheritdoc/>
    public void Report(string source, IEnumerable<StudioProblem> found) =>
        problems.Report(StudioProblems.Owned(pluginId, source), found);
}
