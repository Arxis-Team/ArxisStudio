using ArxisStudio.Modules.Project.Model;

namespace ArxisStudio.Modules.Project.Dialogs;

/// <summary>
/// Что сказать о негодном имени — одними словами у переименования и у создания.
/// </summary>
/// <remarks>
/// Два диалога держали один и тот же перечень копией, и новая беда имени, добавленная в один, молчала бы в
/// другом. Беды имени типа — ключевое слово и не-идентификатор — у переименования не случаются: оно
/// проверяет имя только правилами файла.
/// </remarks>
internal static class NameWords
{
    /// <summary>Что сказать о негодном имени; null — говорить нечего.</summary>
    /// <param name="check">Итог проверки имени.</param>
    /// <param name="format">Строка словаря со вставками.</param>
    public static string? Say(NameCheck check, Func<string, object[], string> format) => check.Problem switch
    {
        NameProblem.Empty => format("project.rename.empty", []),
        NameProblem.Invalid => format("project.rename.invalid", [check.Subject ?? string.Empty]),
        NameProblem.Trailing => format("project.rename.trailing", []),
        NameProblem.Reserved => format("project.rename.reserved", [check.Subject ?? string.Empty]),
        NameProblem.Taken => format("project.rename.taken", [check.Subject ?? string.Empty]),
        NameProblem.NotIdentifier => format("project.add.identifier", []),
        NameProblem.Keyword => format("project.add.keyword", [check.Subject ?? string.Empty]),
        _ => null,
    };
}
