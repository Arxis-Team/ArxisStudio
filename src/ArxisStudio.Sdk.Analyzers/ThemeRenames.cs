using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ArxisStudio.Sdk.Analyzers;

/// <summary>
/// Имена темы, которых с SDK 6.0 нет, и роли, которые их заменили.
/// </summary>
/// <remarks>
/// В 6.0 палитра перестала называть цвет местом в ряду (<c>AxBg2</c>, <c>AxFg3</c>,
/// <c>AxBrd</c>) и назвала его ролью (<c>AxSurfacePanel</c>, <c>AxTextTertiary</c>,
/// <c>AxStrokeSubtle</c>); ступени шкал, из которых собирались цвета, ушли из словаря вовсе.
/// Значения при этом не сдвинулись, но ключ — обещание расширению, и разметка, назвавшая
/// прежний, получила бы в студии 6.0 не ошибку, а пустую кисть.
/// <para>
/// Таблица записана руками, и правилу <see cref="ThemeTokens"/> это не противоречит: там
/// список разошёлся бы с темой, а здесь — история, и из нынешней темы её не вывести. Что
/// каждая названная здесь роль в теме есть, проверяет тест.
/// </para>
/// <para>
/// Где прежний ключ служил нескольким ролям — <c>AxBg3</c> был и наведением, и приподнятой
/// поверхностью, — названы все, первой та, что встречалась чаще: выбрать по месту может
/// только автор.
/// </para>
/// </remarks>
internal static class ThemeRenames
{
    private static readonly Regex Step = new("^Ax(Gray|Blue|Green|Red|Yellow|Orange|Purple|Teal)[0-9]+$", RegexOptions.Compiled);

    /// <summary>Прежнее имя цвета без суффикса — роли, которые его заменили.</summary>
    private static readonly Dictionary<string, string[]> Colours = new(StringComparer.Ordinal)
    {
        ["AxBgSunken"] = ["AxSurfaceSunken"],
        ["AxBg1"] = ["AxSurfaceBase"],
        ["AxBg2"] = ["AxSurfacePanel", "AxSurfaceOverlay"],
        ["AxBg3"] = ["AxHover", "AxSurfaceRaised"],
        ["AxBg4"] = ["AxPressed", "AxTrack", "AxStrokeStrong"],
        ["AxInp"] = ["AxSurfaceBase"],
        ["AxInpDisabled"] = ["AxFillDisabled"],
        ["AxBrd"] = ["AxStrokeSubtle"],
        ["AxBrd2"] = ["AxStrokeControl"],
        ["AxFg"] = ["AxTextPrimary"],
        ["AxFg2"] = ["AxTextSecondary"],
        ["AxFg3"] = ["AxTextTertiary"],
        ["AxFgDisabled"] = ["AxTextDisabled"],
        ["AxOnAcc"] = ["AxTextOnAccent"],
        ["AxAcc"] = ["AxAccent"],
        ["AxAccHover"] = ["AxAccentHover"],
        ["AxAccPressed"] = ["AxAccentFillPressed", "AxLinkPressed"],
        ["AxAccStrong"] = ["AxAccentFill"],
        ["AxAccStrongHover"] = ["AxAccentFillHover"],
        ["AxSel"] = ["AxSelectionActive"],
        ["AxSelInactive"] = ["AxSelectionInactive"],
        ["AxOutlineFocused"] = ["AxFocusRing"],
        ["AxOutlineError"] = ["AxErrorOutline"],
        ["AxOutlineWarning"] = ["AxWarningOutline"],
        ["AxRed"] = ["AxError"],
        ["AxYel"] = ["AxWarning"],
        ["AxGrn"] = ["AxSuccess"],
        ["AxOrg"] = ["AxTintOrange"],
        ["AxPur"] = ["AxTintPurple"],
        ["AxRedText"] = ["AxErrorText"],
        ["AxYellowText"] = ["AxWarningText"],
        ["AxGreenText"] = ["AxSuccessText"],
        ["AxInfoBackground"] = ["AxInfoFill"],
        ["AxSuccessBackground"] = ["AxSuccessFill"],
        ["AxWarningBackground"] = ["AxWarningFill"],
        ["AxErrorBackground"] = ["AxErrorFill"],
        ["AxInfoBorder"] = ["AxInfoStroke"],
        ["AxSuccessBorder"] = ["AxSuccessStroke"],
        ["AxWarningBorder"] = ["AxWarningStroke"],
        ["AxErrorBorder"] = ["AxErrorStroke"],
        ["AxLinkOn"] = ["AxLinkOnPlate"],
        ["AxTooltipBackground"] = ["AxToolTipFill"],
        ["AxTooltipBorder"] = ["AxToolTipStroke"],
        ["AxCodeFg"] = ["AxCodeText"],
        ["AxCodeAttr"] = ["AxCodeAttribute"],

        // Холст и точки снятого дизайнера и две тени цветом: шаблоны их не брали.
        ["AxCanvas"] = [],
        ["AxDot"] = [],
        ["AxShadow"] = [],
        ["AxAbShadow"] = [],
    };

    /// <summary>Прежние тени: у них нет пары цвета и кисти.</summary>
    private static readonly Dictionary<string, string> Shadows = new(StringComparer.Ordinal)
    {
        ["AxPopupShadow"] = "AxShadowPopup",
        ["AxModalShadow"] = "AxShadowModal",
    };

    /// <summary>Каждое прежнее имя вместе с ролями, которые его заменили.</summary>
    internal static IEnumerable<KeyValuePair<string, string[]>> All =>
        Colours
            .SelectMany(pair => new[] { "Color", "Brush" }.Select(suffix =>
                new KeyValuePair<string, string[]>(pair.Key + suffix, pair.Value.Select(role => role + suffix).ToArray())))
            .Concat(Shadows.Select(pair => new KeyValuePair<string, string[]>(pair.Key, [pair.Value])));

    /// <summary>
    /// Что сказать о ключе, которого с 6.0 в теме нет; <c>null</c> — ключ не из прежних.
    /// </summary>
    /// <param name="key">Ключ, как его назвали.</param>
    /// <param name="brush">Месту нужна кисть: прежний цвет тогда называется новой кистью, а не цветом.</param>
    public static string? Retired(string key, bool brush)
    {
        if (Step.IsMatch(key))
            return $"{key} — ступень шкалы палитры: с SDK 6.0 шкал в теме нет, разметка называет роль";

        if (Shadows.TryGetValue(key, out var shadow))
            return $"{key} — имя темы до SDK 6.0; теперь это {shadow}";

        var suffix = key.EndsWith("Color", StringComparison.Ordinal) ? "Color"
            : key.EndsWith("Brush", StringComparison.Ordinal) ? "Brush"
            : null;

        if (suffix is null || !Colours.TryGetValue(key.Substring(0, key.Length - suffix.Length), out var roles))
            return null;

        if (roles.Length == 0)
            return $"{key} — имя темы до SDK 6.0, снято без замены";

        var named = string.Join(" или ", roles.Select(role => role + (brush ? "Brush" : suffix)));

        return roles.Length == 1
            ? $"{key} — имя темы до SDK 6.0; теперь это {named}"
            : $"{key} — имя темы до SDK 6.0, служившее нескольким ролям; теперь это {named} — смотря по месту";
    }
}
