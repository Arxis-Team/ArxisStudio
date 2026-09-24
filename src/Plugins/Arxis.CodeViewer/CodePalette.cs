using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;

namespace Arxis.CodeViewer;

/// <summary>
/// Красит подсветку синтаксиса ключами темы студии.
/// </summary>
/// <remarks>
/// Набор подсветки приезжает с AvaloniaEdit со своими цветами — зелёный
/// комментарий, синее ключевое слово, — и подобраны они под белый лист чужого
/// редактора. В тёмной студии это чужая палитра посреди своей, а правило у
/// расширений одно: цвет называют ключом темы, а не значением.
/// <para>
/// Цветов у темы на код пять — текст, слово языка, имя, строка, примечание, — и
/// раскладывается по ним весь набор: цвет, которому роли не нашлось, остаётся
/// пустым и берёт цвет самого редактора, то есть ту же роль текста. Чужого
/// значения после этого в наборе не остаётся ни одного — ни цвета, ни кегля, ни
/// гарнитуры, — и это проверяется тестом.
/// </para>
/// <para>
/// Роль ищется по имени цвета из набора, и имена эти — не наши: их дал автор
/// подсветки. Незнакомое имя — не беда: оно получает цвет текста, а не чужой.
/// </para>
/// </remarks>
internal sealed class CodePalette : IDisposable
{
    /// <summary>Роль темы: слова языка — ключевые слова, теги, заголовки.</summary>
    private const string Keywords = "AxCodeTagColor";

    /// <summary>Роль темы: имена и числа — вызовы, атрибуты, ссылки, литералы чисел.</summary>
    private const string Names = "AxCodeAttributeColor";

    /// <summary>Роль темы: строки и символы.</summary>
    private const string Strings = "AxCodeStringColor";

    /// <summary>Роль темы: примечания — комментарии и директивы.</summary>
    private const string Comments = "AxCodeCommentColor";

    /// <summary>Роли, за которыми палитра следит в теме.</summary>
    private static readonly string[] Roles = [Keywords, Names, Strings, Comments];

    private readonly Dictionary<string, Color> _theme = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _watched = [];
    private readonly IReadOnlyList<HighlightingColor> _colours;
    private readonly TextEditor _editor;

    /// <summary>Заводит палитру над набором подсветки и следит за темой.</summary>
    /// <param name="definition">Набор подсветки, который красим.</param>
    /// <param name="editor">Редактор: его просят перерисоваться, когда тема сменилась.</param>
    /// <param name="host">Контрол, у которого спрашивают ресурсы темы.</param>
    /// <remarks>
    /// Первое значение берётся у приложения, а не у наблюдателя: вкладка в этот
    /// момент ещё не в дереве, ресурсов у неё нет, и наблюдатель заговорит
    /// только когда она в него встанет, — а покрасить надо до первого показа.
    /// </remarks>
    public CodePalette(IHighlightingDefinition definition, TextEditor editor, Control host)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(host);

        _colours = Colours(definition);
        _editor = editor;

        foreach (var role in Roles)
        {
            if (Application.Current is { } application &&
                application.TryGetResource(role, application.ActualThemeVariant, out var value) &&
                value is Color colour)
            {
                _theme[role] = colour;
            }

            _watched.Add(host.GetResourceObservable(role)
                .Subscribe(new AnonymousObserver<object?>(value => Accept(role, value))));
        }

        Paint();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var watched in _watched)
            watched.Dispose();

        _watched.Clear();
    }

    /// <summary>
    /// Все цвета набора: именованные и те, что живут внутри правил.
    /// </summary>
    /// <remarks>
    /// Одних именованных мало: цвет бывает объявлен прямо в правиле и в списке
    /// набора не числится — так покрашены пометки <c>TODO</c> в комментарии и
    /// текст XML-комментария у C#. Обход идёт по ссылкам, а не по равенству:
    /// два разных цвета с одинаковыми полями — это два цвета, и покрасить надо
    /// оба.
    /// </remarks>
    private static IReadOnlyList<HighlightingColor> Colours(IHighlightingDefinition definition)
    {
        var found = new List<HighlightingColor>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<HighlightingRuleSet>();

        void Take(HighlightingColor? colour)
        {
            if (colour is not null && seen.Add(colour))
                found.Add(colour);
        }

        void Reach(HighlightingRuleSet? set)
        {
            if (set is not null && seen.Add(set))
                queue.Enqueue(set);
        }

        foreach (var colour in definition.NamedHighlightingColors)
            Take(colour);

        Reach(definition.MainRuleSet);

        while (queue.Count > 0)
        {
            var set = queue.Dequeue();

            foreach (var rule in set.Rules)
                Take(rule.Color);

            foreach (var span in set.Spans)
            {
                Take(span.StartColor);
                Take(span.SpanColor);
                Take(span.EndColor);

                Reach(span.RuleSet);
            }
        }

        return found;
    }

    /// <summary>Роль темы для цвета набора; пусто — цвет самого редактора.</summary>
    /// <remarks>
    /// Имена приходят из наборов AvaloniaEdit: C#, XML, JSON и MarkDown. Слова
    /// языка узнаются и правилом — у C# их два десятка, и писать каждое незачем;
    /// у JavaScript имя написано вторым словом с большой буквы, поэтому сравнение
    /// без учёта регистра.
    /// </remarks>
    private static string? Role(string? name) => name switch
    {
        null => null,

        "Comment" or "DocComment" or "Preprocessor" or "BlockQuote" => Comments,

        "String" or "Char" or "Character" or "Regex" or "XmlString" or "AttributeValue" or "CData" => Strings,

        "NumberLiteral" or "Number" or "Digits" or "MethodCall" or "AttributeName" or "FieldName"
            or "Entity" or "Link" or "Image" => Names,

        "XmlTag" or "XmlDeclaration" or "DocType" or "KnownDocTags" or "Bool" or "Null" or "TrueFalse"
            or "Modifiers" or "ParameterModifiers" or "Visibility" or "GetSetAddRemove" or "ThisOrBaseReference"
            or "JavaScriptIntrinsics" or "JavaScriptLiterals" or "JavaScriptGlobalFunctions" => Keywords,

        _ when name.StartsWith("Heading", StringComparison.Ordinal) => Keywords,
        _ when name.Contains("Keyword", StringComparison.OrdinalIgnoreCase) => Keywords,

        _ => null,
    };

    /// <summary>Новое значение роли из темы.</summary>
    private void Accept(string role, object? value)
    {
        if (value is not Color colour || (_theme.TryGetValue(role, out var known) && known == colour))
            return;

        _theme[role] = colour;

        Paint();
    }

    /// <summary>
    /// Раскладывает цвета набора по ролям темы.
    /// </summary>
    /// <remarks>
    /// Снимается и то, что цветом не является: кегль и гарнитура. Заголовок
    /// MarkDown приезжает кеглем 30, а блок кода — гарнитурой Footlight MT Light;
    /// размеры и шрифт у студии свои, и берёт их редактор ключами темы.
    /// </remarks>
    private void Paint()
    {
        foreach (var colour in _colours)
        {
            colour.Background = null;
            colour.FontFamily = null;
            colour.FontSize = null;

            colour.Foreground = Role(colour.Name) is { } role && _theme.TryGetValue(role, out var value)
                ? new SimpleHighlightingBrush(value)
                : null;
        }

        _editor.TextArea.TextView.Redraw();
    }
}
