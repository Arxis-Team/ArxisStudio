using System.Globalization;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Project;

/// <summary>
/// Слова модуля: строка словаря со вставками и слово в строке состояния — одним способом на весь модуль.
/// </summary>
/// <remarks>
/// Строку со вставками собирали семь копий в шести классах и ещё шесть мест — по месту, а слово в строке
/// состояния говорили двое. Вставки — по правилам языка человека: число в них пишется так, как он
/// привык.
/// </remarks>
internal static class ModuleWords
{
    /// <summary>Строка словаря со вставками.</summary>
    /// <param name="strings">Словари модуля.</param>
    /// <param name="key">Ключ строки.</param>
    /// <param name="values">Что вставить.</param>
    public static string Format(this IStudioStrings strings, string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, strings[key], values);

    /// <summary>Говорит человеку в строке состояния; без строки состояния — молча.</summary>
    /// <param name="context">Что студия даёт модулю.</param>
    /// <param name="message">Что сказать.</param>
    public static void Tell(this IStudioContext context, string message) => context.GetService<IStudioStatus>()?.Show(message);
}
