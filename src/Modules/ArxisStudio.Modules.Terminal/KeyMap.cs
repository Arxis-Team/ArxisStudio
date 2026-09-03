using Avalonia.Input;
using XKey = XTerm.Input.Key;
using XTerminal = XTerm.Terminal;
using XModifiers = XTerm.Input.KeyModifiers;

namespace ArxisStudio.Modules.Terminal;

/// <summary>
/// Что уходит оболочке, когда человек нажимает клавишу.
/// </summary>
/// <remarks>
/// Терминал не передаёт клавиши — он передаёт байты, и у каждой особой
/// клавиши свой договорённый код: стрелка вверх — <c>ESC [ A</c>, Ctrl+C —
/// один байт 0x03. Сами коды знает эмулятор: они зависят от режимов, которые
/// включает оболочка (прикладные стрелки, клавиатура kitty), и держать их
/// второй копией здесь значило бы разойтись с ним. Здесь только перевод
/// клавиш Avalonia в клавиши эмулятора и решение, что вообще считать особым.
/// <para>
/// Обычные символы сюда не приходят: они идут событием ввода текста — так
/// работают раскладки и IME. Но Ctrl и Alt текста не дают, и буква с ними
/// берётся отсюда: по символу клавиши в текущей раскладке, а без него — по
/// самой клавише, чтобы Ctrl+C прерывал и в русской раскладке.
/// </para>
/// </remarks>
public static class KeyMap
{
    /// <summary>
    /// Байты для нажатия; null — клавиша не особая, символ придёт вводом текста.
    /// </summary>
    /// <param name="terminal">Эмулятор: он знает режимы и кодирует по ним.</param>
    /// <param name="key">Что нажали.</param>
    /// <param name="modifiers">С чем нажали.</param>
    /// <param name="symbol">Символ клавиши в текущей раскладке, если Avalonia его знает.</param>
    public static string? Sequence(XTerminal terminal, Key key, KeyModifiers modifiers, string? symbol)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        var converted = Convert(modifiers);

        if (Special(key) is { } special)
            return terminal.GenerateKeyInput(special, converted);

        // Без Ctrl и Alt символ придёт текстом; Shift — часть раскладки.
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0)
            return null;

        return Character(key, symbol) is { } character
            ? terminal.GenerateCharInput(character, converted)
            : null;
    }

    /// <summary>Клавиша эмулятора для особой клавиши Avalonia; null — клавиша обычная.</summary>
    /// <param name="key">Клавиша Avalonia.</param>
    public static XKey? Special(Key key) => key switch
    {
        Key.Enter => XKey.Enter,
        Key.Tab => XKey.Tab,
        Key.Back => XKey.Backspace,
        Key.Escape => XKey.Escape,
        Key.Up => XKey.UpArrow,
        Key.Down => XKey.DownArrow,
        Key.Left => XKey.LeftArrow,
        Key.Right => XKey.RightArrow,
        Key.Home => XKey.Home,
        Key.End => XKey.End,
        Key.PageUp => XKey.PageUp,
        Key.PageDown => XKey.PageDown,
        Key.Insert => XKey.Insert,
        Key.Delete => XKey.Delete,
        Key.F1 => XKey.F1,
        Key.F2 => XKey.F2,
        Key.F3 => XKey.F3,
        Key.F4 => XKey.F4,
        Key.F5 => XKey.F5,
        Key.F6 => XKey.F6,
        Key.F7 => XKey.F7,
        Key.F8 => XKey.F8,
        Key.F9 => XKey.F9,
        Key.F10 => XKey.F10,
        Key.F11 => XKey.F11,
        Key.F12 => XKey.F12,
        _ => null,
    };

    /// <summary>
    /// Символ, который клавиша даёт с Ctrl или Alt.
    /// </summary>
    /// <param name="key">Клавиша.</param>
    /// <param name="symbol">Символ раскладки, если известен.</param>
    /// <remarks>
    /// Символ раскладки — первый выбор: на нём Ctrl+Ö в немецкой раскладке
    /// значит то, что значит. Но управляющие символы и всё, что длиннее одного
    /// знака, — не символ, а служебная запись, и тогда решает сама клавиша.
    /// </remarks>
    public static char? Character(Key key, string? symbol)
    {
        if (symbol is { Length: 1 } && !char.IsControl(symbol[0]) && symbol[0] != ' ')
            return symbol[0];

        if (key is >= Key.A and <= Key.Z)
            return (char)('a' + (key - Key.A));

        if (key is >= Key.D0 and <= Key.D9)
            return (char)('0' + (key - Key.D0));

        return key switch
        {
            Key.Space => ' ',
            Key.OemOpenBrackets => '[',
            Key.OemCloseBrackets => ']',
            Key.OemBackslash or Key.OemPipe => '\\',
            Key.OemMinus => '-',
            Key.OemPlus => '=',
            Key.OemComma => ',',
            Key.OemPeriod => '.',
            Key.OemQuestion => '/',
            Key.OemSemicolon => ';',
            Key.OemQuotes => '\'',
            Key.OemTilde => '`',
            _ => null,
        };
    }

    /// <summary>Модификаторы Avalonia в модификаторы эмулятора.</summary>
    /// <param name="modifiers">Модификаторы Avalonia.</param>
    public static XModifiers Convert(KeyModifiers modifiers)
    {
        var result = XModifiers.None;

        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= XModifiers.Shift;

        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= XModifiers.Alt;

        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= XModifiers.Control;

        return result;
    }
}
