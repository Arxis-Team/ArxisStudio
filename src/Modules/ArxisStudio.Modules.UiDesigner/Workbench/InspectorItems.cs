using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArxisStudio.Xaml;
using Avalonia.Media;

namespace ArxisStudio.Modules.UiDesigner.Workbench;

/// <summary>Строка инспектора: заголовок раздела или член.</summary>
internal abstract class InspectorItem : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Строка ли это члена: её выбирают и правят; заголовок — нет.</summary>
    public virtual bool IsRow => true;

    /// <summary>Что строку называет диктору: член или раздел.</summary>
    public abstract string Label { get; }

    /// <summary>Сообщает о смене свойства.</summary>
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Заголовок раздела инспектора: «Раскладка», «Вид», «В Grid».</summary>
/// <param name="title">Заголовок.</param>
internal sealed class InspectorGroup(string title) : InspectorItem
{
    /// <summary>Заголовок.</summary>
    public string Title { get; } = title;

    /// <inheritdoc/>
    public override bool IsRow => false;

    /// <inheritdoc/>
    public override string Label => Title;
}

/// <summary>
/// Член выбранного — строкой инспектора: имя, редактор по типу значения, написанное документом и то, к
/// чему пришёл объект.
/// </summary>
/// <remarks>
/// <para>
/// <b>Правят текст.</b> Строка показывает, что написано в документе (<see cref="Written"/>), а не то, к
/// чему пришёл объект: унаследованный кегль, вписанный в поле, перестал бы наследоваться. Нынешнее
/// значение объекта — подсказка в пустом поле (<see cref="Hint"/>) вместе с тем, откуда оно.
/// </para>
/// <para>
/// <b>Фиксация — Enter или уход фокуса</b>, Esc возвращает написанное. Текст, который не прочтётся,
/// проверяется до правки (<see cref="Error"/>) и не пишется. Пустое поле — снятый атрибут.
/// </para>
/// <para>
/// <b>Несколько выбранных</b> — одна строка на член, который есть у всех; расходящееся значение
/// показано «—» (<see cref="IsMixed"/>), а правка пишет его в каждый одной записью истории.
/// </para>
/// </remarks>
internal sealed class InspectorRow : InspectorItem
{
    private readonly Func<string, string?> _check;
    private readonly Func<string, Task> _commit;
    private string _draft;
    private string? _error;

    /// <summary>Строит строку.</summary>
    /// <param name="member">Член, как его пишет документ.</param>
    /// <param name="editor">Чем править.</param>
    /// <param name="choices">Значения перечисления.</param>
    /// <param name="written">Написанное документом — у всех выбранных одно; пусто — не написано.</param>
    /// <param name="isMixed">У выбранных написано разное.</param>
    /// <param name="hint">Подсказка пустого поля: нынешнее значение и откуда оно.</param>
    /// <param name="check">Почему текст не прочтётся; null — прочтётся.</param>
    /// <param name="commit">Пишет текст в документ; пустой — снимает.</param>
    public InspectorRow(
        string member,
        XamlValueEditor editor,
        IReadOnlyList<string> choices,
        string written,
        bool isMixed,
        string? hint,
        Func<string, string?> check,
        Func<string, Task> commit)
    {
        Member = member;
        Editor = IsExpressionText(written) ? XamlValueEditor.None : editor;
        Choices = choices;
        Written = written;
        IsMixed = isMixed;
        Hint = isMixed ? Mixed : hint;
        _check = check;
        _commit = commit;
        _draft = written;
    }

    /// <summary>Как показано расходящееся значение нескольких выбранных.</summary>
    public const string Mixed = "—";

    /// <summary>Член, как его пишет документ: <c>Width</c>, <c>Canvas.Left</c>.</summary>
    public string Member { get; }

    /// <inheritdoc/>
    public override string Label => Member;

    /// <summary>Чем править.</summary>
    public XamlValueEditor Editor { get; }

    /// <summary>Значения перечисления.</summary>
    public IReadOnlyList<string> Choices { get; }

    /// <summary>Написанное документом; пусто — не написано или у выбранных разное.</summary>
    public string Written { get; }

    /// <summary>У выбранных написано разное.</summary>
    public bool IsMixed { get; }

    /// <summary>Подсказка пустого поля: «—» у разного, иначе нынешнее значение объекта и откуда оно.</summary>
    public string? Hint { get; }

    /// <summary>Написано ли что-нибудь: тогда есть что сбросить.</summary>
    public bool IsWritten => Written.Length > 0 || IsMixed;

    /// <summary>Правится текстом: строка, число, кисть.</summary>
    public bool IsText => Editor is XamlValueEditor.Text or XamlValueEditor.Number or XamlValueEditor.Brush;

    /// <summary>Флажок.</summary>
    public bool IsFlag => Editor == XamlValueEditor.Flag;

    /// <summary>Перечисление списком: значений больше четырёх.</summary>
    public bool IsChoice => Editor == XamlValueEditor.Choice && Choices.Count > SegmentLimit;

    /// <summary>Перечисление сегментами: значений не больше четырёх — столько ложится в ширину инспектора.</summary>
    public bool IsSegmented => Editor == XamlValueEditor.Choice && Choices.Count <= SegmentLimit;

    /// <summary>Только читается: выражение — привязка, ресурс — или значение, которое атрибутом не пишут.</summary>
    public bool IsReadOnly => Editor == XamlValueEditor.None;

    /// <summary>Кисть: рядом с полем — образец.</summary>
    public bool IsBrush => Editor == XamlValueEditor.Brush;

    /// <summary>Число: стрелки шагают значение.</summary>
    public bool IsNumber => Editor == XamlValueEditor.Number;

    /// <summary>Текст в поле — то, что человек набирает до фиксации.</summary>
    public string Draft
    {
        get => _draft;
        set
        {
            if (_draft == value)
                return;

            _draft = value;
            Raise();
            Raise(nameof(Swatch));

            if (_error is not null)
                Error = null;
        }
    }

    /// <summary>Почему набранное не прочтётся; null — прочтётся.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (_error == value)
                return;

            _error = value;
            Raise();
            Raise(nameof(HasError));
        }
    }

    /// <summary>Есть ли ошибка: показать её под полем.</summary>
    public bool HasError => _error is not null;

    /// <summary>Флажок: написанное как флаг; у разного и ненаписанного — неопределённость.</summary>
    public bool? Flag
    {
        get => IsMixed || Written.Length == 0 ? null : string.Equals(Written, "True", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value is not { } flag)
                return;

            _ = Write(flag ? "True" : "False");
        }
    }

    /// <summary>Выбранное значение перечисления; null — не написано или у выбранных разное.</summary>
    public string? Choice
    {
        get => Choices.FirstOrDefault(choice => string.Equals(choice, Written, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null || string.Equals(value, Written, StringComparison.OrdinalIgnoreCase))
                return;

            _ = Write(value);
        }
    }

    /// <summary>Номер выбранного значения у сегментов; -1 — ничего.</summary>
    public int ChoiceIndex
    {
        get => Choice is { } choice ? Choices.ToList().IndexOf(choice) : -1;
        set
        {
            if (value >= 0 && value < Choices.Count)
                Choice = Choices[value];
        }
    }

    /// <summary>Образец кисти — то, что набрано; не читается кистью — ничего.</summary>
    public IBrush? Swatch
    {
        get
        {
            if (!IsBrush || _draft.Length == 0)
                return null;

            try
            {
                return Brush.Parse(_draft);
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                // Ключ ресурса или недописанный цвет — значение, которого образец показать не может.
                return null;
            }
        }
    }

    /// <summary>
    /// Фиксирует набранное: прочтётся — пишет, нет — оставляет поле с ошибкой. Ничего не изменилось — ничего
    /// не пишется.
    /// </summary>
    public Task CommitAsync()
    {
        if (IsReadOnly || (!IsMixed && _draft == Written) || (IsMixed && _draft.Length == 0))
            return Task.CompletedTask;

        return Write(_draft);
    }

    /// <summary>Возвращает в поле написанное.</summary>
    public void Revert()
    {
        Draft = Written;
        Error = null;
    }

    /// <summary>Снимает атрибут: член возвращается к стилю, наследованию и умолчанию.</summary>
    public Task ResetAsync() => IsWritten ? _commit(string.Empty) : Task.CompletedTask;

    /// <summary>Шагает число стрелкой: на единицу, с Shift — на десять.</summary>
    /// <param name="steps">Сколько и куда.</param>
    public Task StepAsync(int steps)
    {
        if (!IsNumber)
            return Task.CompletedTask;

        var text = _draft.Length > 0 ? _draft : Written;

        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            value = 0;

        Draft = (value + steps).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return CommitAsync();
    }

    private Task Write(string text)
    {
        if (text.Length > 0 && _check(text) is { } error)
        {
            Error = error;

            return Task.CompletedTask;
        }

        Error = null;

        return _commit(text);
    }

    /// <summary>Сколько значений перечисления ложится сегментами в ширину инспектора.</summary>
    private const int SegmentLimit = 4;

    private static bool IsExpressionText(string text) =>
        text.StartsWith('{') && !text.StartsWith("{}", StringComparison.Ordinal);
}
