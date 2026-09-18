using System.Globalization;

namespace ArxisStudio.Modules.Project.Model;

/// <summary>
/// Подписи, которые дереву нужны на языке студии.
/// </summary>
/// <remarks>
/// Приходят снаружи, а не берутся из словаря здесь: дерево строится вне потока интерфейса, и
/// построитель не должен знать ни словаря, ни языка — ему передают готовые слова.
/// </remarks>
/// <param name="Dependencies">Узел «Зависимости».</param>
/// <param name="Frameworks">Группа платформ.</param>
/// <param name="Packages">Группа пакетов.</param>
/// <param name="Projects">Группа проектов.</param>
/// <param name="Assemblies">Группа сборок.</param>
/// <param name="Analyzers">Группа анализаторов.</param>
/// <param name="NotLoaded">Подпись проекта, который не загрузился.</param>
/// <param name="OneProject">Счёт у корня, когда проект один.</param>
/// <param name="ManyProjects">Счёт у корня, когда проектов несколько: шаблон с <c>{0}</c>.</param>
public sealed record Words(
    string Dependencies,
    string Frameworks,
    string Packages,
    string Projects,
    string Assemblies,
    string Analyzers,
    string NotLoaded,
    string OneProject,
    string ManyProjects)
{
    /// <summary>Английские подписи — для тестов и там, где словаря нет.</summary>
    public static Words English { get; } = new(
        "Dependencies", "Frameworks", "Packages", "Projects", "Assemblies", "Analyzers",
        "not loaded", "· 1 project", "· {0} projects");

    /// <summary>Подпись группы зависимостей.</summary>
    /// <param name="kind">Вид группы.</param>
    public string Of(DependencyKind kind) => kind switch
    {
        DependencyKind.Frameworks => Frameworks,
        DependencyKind.Packages => Packages,
        DependencyKind.Projects => Projects,
        DependencyKind.Assemblies => Assemblies,
        _ => Analyzers,
    };

    /// <summary>Счёт проектов у корня.</summary>
    /// <param name="count">Сколько проектов в решении.</param>
    /// <remarks>
    /// Без форм множественного числа: «· 1 проект» и «· проектов: 5» верны при любом числе, а форма
    /// под число — «2 проекта», «5 проектов» — потребовала бы правил языка, которых у словаря нет.
    /// </remarks>
    public string Count(int count) =>
        count == 1 ? OneProject : string.Format(CultureInfo.CurrentCulture, ManyProjects, count);
}
