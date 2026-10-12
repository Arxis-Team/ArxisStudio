using System.Globalization;
using ArxisStudio.Controls;

namespace ArxisStudio.Modules.Inspector.Editors;

/// <summary>Преобразование полями: сдвиг в точках, поворот в градусах, масштаб, наклон в градусах.</summary>
/// <param name="X">Сдвиг по X.</param>
/// <param name="Y">Сдвиг по Y.</param>
/// <param name="Angle">Поворот.</param>
/// <param name="ScaleX">Масштаб по X.</param>
/// <param name="ScaleY">Масштаб по Y.</param>
/// <param name="SkewX">Наклон по X.</param>
/// <param name="SkewY">Наклон по Y.</param>
internal readonly record struct TransformParts(double X, double Y, double Angle, double ScaleX, double ScaleY, double SkewX, double SkewY)
{
    /// <summary>Тождество: ничего не сдвинуто, не повёрнуто, не растянуто и не наклонено.</summary>
    public static TransformParts Identity => new(0, 0, 0, 1, 1, 0, 0);

    /// <summary>Тождество ли это.</summary>
    public bool IsIdentity => this == Identity;
}

/// <summary>
/// Запись преобразования разметкой Avalonia — <c>translate(10px, 20px) rotate(45deg) scale(1.5) skew(10deg, 0deg)</c> —
/// и обратно.
/// </summary>
/// <remarks>
/// <para>
/// Полями читается только запись, которую поля и напишут: каждое действие не больше раза и по порядку — сдвиг, поворот,
/// масштаб, наклон, как TRS у Unity. <c>matrix</c>, повтор и другой порядок значат другое преобразование — оно
/// остаётся текстом. Углы читаются в градусах, радианах, градах и оборотах, а пишутся градусами; ноль единиц не
/// требует, как и у Avalonia.
/// </para>
/// <para>Тождество пишется сбросом: атрибут, который ничего не делает, разметке ни к чему.</para>
/// </remarks>
internal static class TransformText
{
    /// <summary>Читает запись полями.</summary>
    /// <param name="text">Запись; пусто и <c>none</c> — тождество.</param>
    /// <param name="parts">Поля; не прочлось — тождество.</param>
    /// <returns>Прочлась ли запись полями.</returns>
    public static bool TryRead(string? text, out TransformParts parts)
    {
        parts = TransformParts.Identity;

        if (string.IsNullOrWhiteSpace(text) || string.Equals(text.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            return true;

        var read = TransformParts.Identity;
        var last = -1;
        var at = 0;

        while (true)
        {
            at = Skip(text, at);

            if (at >= text.Length)
                break;

            var name = Name(text, ref at);
            var open = Skip(text, at);

            if (name.Length == 0 || open >= text.Length || text[open] != '(')
                return false;

            var close = text.IndexOf(')', open);

            if (close < 0)
                return false;

            var arguments = text[(open + 1)..close].Split(',').Select(static argument => argument.Trim()).ToArray();
            var kind = KindOf(name);

            // Каждое действие — не больше раза и по порядку: иначе это другое преобразование, и поля его не передадут.
            if (kind <= last || !TryApply(name, arguments, ref read))
                return false;

            last = kind;
            at = close + 1;
        }

        parts = read;
        return true;
    }

    /// <summary>Пишет поля записью; тождество — пусто: атрибут снимают.</summary>
    /// <param name="parts">Поля.</param>
    public static string? Write(TransformParts parts)
    {
        if (parts.IsIdentity)
            return null;

        var functions = new List<string>(4);

        if (parts.X != 0 || parts.Y != 0)
            functions.Add($"translate({Number(parts.X)}px, {Number(parts.Y)}px)");

        if (parts.Angle != 0)
            functions.Add($"rotate({Number(parts.Angle)}deg)");

        if (parts.ScaleX != 1 || parts.ScaleY != 1)
        {
            functions.Add(parts.ScaleX.Equals(parts.ScaleY)
                ? $"scale({Number(parts.ScaleX)})"
                : $"scale({Number(parts.ScaleX)}, {Number(parts.ScaleY)})");
        }

        if (parts.SkewX != 0 || parts.SkewY != 0)
            functions.Add($"skew({Number(parts.SkewX)}deg, {Number(parts.SkewY)}deg)");

        return string.Join(' ', functions);
    }

    /// <summary>Порядок действия в записи: сдвиг, поворот, масштаб, наклон; −1 — такого действия поля не знают.</summary>
    private static int KindOf(string name) => name switch
    {
        "translate" or "translatex" or "translatey" => 0,
        "rotate" => 1,
        "scale" or "scalex" or "scaley" => 2,
        "skew" or "skewx" or "skewy" => 3,
        _ => int.MinValue,
    };

    /// <summary>Ставит действие в поля; аргументы не прочлись — нет.</summary>
    private static bool TryApply(string name, string[] arguments, ref TransformParts parts)
    {
        switch (name)
        {
            case "translate" when arguments.Length is 1 or 2 && TryLength(arguments[0], out var x) &&
                                  TryLength(arguments.Length == 2 ? arguments[1] : "0", out var y):
                parts = parts with { X = x, Y = y };
                return true;

            case "translatex" when arguments.Length == 1 && TryLength(arguments[0], out var x):
                parts = parts with { X = x };
                return true;

            case "translatey" when arguments.Length == 1 && TryLength(arguments[0], out var y):
                parts = parts with { Y = y };
                return true;

            case "rotate" when arguments.Length == 1 && TryAngle(arguments[0], out var angle):
                parts = parts with { Angle = angle };
                return true;

            case "scale" when arguments.Length is 1 or 2 && TryNumber(arguments[0], out var x) &&
                              TryNumber(arguments.Length == 2 ? arguments[1] : arguments[0], out var y):
                parts = parts with { ScaleX = x, ScaleY = y };
                return true;

            case "scalex" when arguments.Length == 1 && TryNumber(arguments[0], out var x):
                parts = parts with { ScaleX = x };
                return true;

            case "scaley" when arguments.Length == 1 && TryNumber(arguments[0], out var y):
                parts = parts with { ScaleY = y };
                return true;

            case "skew" when arguments.Length is 1 or 2 && TryAngle(arguments[0], out var x) &&
                             TryAngle(arguments.Length == 2 ? arguments[1] : "0", out var y):
                parts = parts with { SkewX = x, SkewY = y };
                return true;

            case "skewx" when arguments.Length == 1 && TryAngle(arguments[0], out var x):
                parts = parts with { SkewX = x };
                return true;

            case "skewy" when arguments.Length == 1 && TryAngle(arguments[0], out var y):
                parts = parts with { SkewY = y };
                return true;

            default:
                return false;
        }
    }

    /// <summary>Длина в точках: <c>10px</c>, а ноль — и без единицы.</summary>
    private static bool TryLength(string text, out double value)
    {
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            return TryNumber(text[..^2], out value);

        return TryNumber(text, out value) && value == 0;
    }

    /// <summary>Угол в градусах: <c>deg</c>, <c>grad</c>, <c>rad</c>, <c>turn</c>, а ноль — и без единицы.</summary>
    private static bool TryAngle(string text, out double degrees)
    {
        // Грады — раньше радиан: «grad» кончается на «rad».
        foreach (var (unit, scale) in new[] { ("deg", 1d), ("grad", 0.9), ("rad", 180 / Math.PI), ("turn", 360d) })
        {
            if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                var read = TryNumber(text[..^unit.Length], out var value);

                degrees = value * scale;
                return read;
            }
        }

        return TryNumber(text, out degrees) && degrees == 0;
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static string Number(double value) => AxNumberField.Format(value, 6);

    /// <summary>Имя действия строчными: буквы подряд.</summary>
    private static string Name(string text, ref int at)
    {
        var start = at;

        while (at < text.Length && char.IsAsciiLetter(text[at]))
            at++;

        return text[start..at].ToLowerInvariant();
    }

    private static int Skip(string text, int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;

        return at;
    }
}
