using System.ComponentModel;
using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Sdk;

namespace ArxisStudio.Modules.Console.Log;

/// <summary>
/// Строка журнала на экране: запись и то, во что она превращена для показа.
/// </summary>
/// <remarks>
/// Всё, что видно в строке, считается один раз — при её создании. Список
/// виртуализирован, и вычисление в свойстве, к которому привязка обращается
/// при каждом появлении строки на экране, стоило бы прокрутки.
/// <para>
/// Наблюдаемое здесь ровно одно — счётчик повторов: при включённой свёртке
/// новая одинаковая запись поднимает бейдж у строки, которая уже построена и
/// уже на экране. Всё остальное после создания строки не меняется, и обещать
/// обратное не за чем.
/// </para>
/// </remarks>
public sealed class LogRow : INotifyPropertyChanged
{
    private readonly string? _format;

    private int _repeats = 1;

    /// <summary>Заводит строку по записи журнала.</summary>
    /// <param name="record">Запись.</param>
    /// <param name="stamp">Показывать ли столбец времени.</param>
    /// <remarks>
    /// Показ времени — свойство строки, а не панели, и это не случайность:
    /// строку рисует шаблон списка, а до шаблона снаружи дотягиваются только
    /// селектором стиля. Селектор, лезущий внутрь чужого шаблона ради одного
    /// столбца, — дорога, на которой правка темы молча ломает панель.
    /// </remarks>
    /// <param name="repeats">
    /// Как назвать счётчик повторов: строка формата с <c>{0}</c> из словаря студии. Пусто — знак
    /// покажет одно число.
    /// </param>
    public LogRow(StudioLogRecord record, bool stamp = true, string? repeats = null)
    {
        ArgumentNullException.ThrowIfNull(record);

        Record = record;
        Text = FirstLine(record.Message);
        HasStamp = stamp;
        _format = repeats;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Сама запись — её отдаёт панель подробностей и копирование.</summary>
    public StudioLogRecord Record { get; }

    /// <summary>Время записи, как его показывает журнал.</summary>
    public string Stamp => Record.Stamp;

    /// <summary>Показывать ли столбец времени.</summary>
    public bool HasStamp { get; }

    /// <summary>
    /// Уровень словом: <c>ERROR</c>, <c>WARN</c>, <c>INFO</c>, <c>DEBUG</c>.
    /// </summary>
    /// <remarks>
    /// С экрана это слово ушло — там уровень показан значком, — но осталось там, где нужно именно
    /// слово: в буфере обмена (<see cref="LogText"/>) и в <see cref="ToString"/>, которым строку
    /// называет программа чтения с экрана.
    /// </remarks>
    public string Level => Record.LevelName;

    /// <summary>Запись об ошибке.</summary>
    public bool IsError => Record.Level == StudioLogLevel.Error;

    /// <summary>Запись-предупреждение.</summary>
    public bool IsWarning => Record.Level == StudioLogLevel.Warning;

    /// <summary>Обычное сообщение.</summary>
    public bool IsInfo => Record.Level == StudioLogLevel.Info;

    /// <summary>Подробность для отладки.</summary>
    public bool IsDebug => Record.Level == StudioLogLevel.Debug;

    /// <summary>Кто написал.</summary>
    public string Source => Record.Source;

    /// <summary>
    /// Первая строка сообщения.
    /// </summary>
    /// <remarks>
    /// Многострочное сообщение — обычное дело: так в журнал попадает
    /// исключение со стеком. В списке от него нужна первая строка, остальное
    /// показывает панель подробностей; строка в двадцать четыре пикселя,
    /// растянутая стеком, сломала бы и вид, и оценку длины полосы прокрутки.
    /// </remarks>
    public string Text { get; }

    /// <summary>
    /// Тон сообщения: ошибка и предупреждение — своим цветом, отладка — приглушённо.
    /// </summary>
    /// <remarks>
    /// Цвет здесь второй признак, а не единственный: уровень назван значком, и различаются значки
    /// рисунком, а не краской. Человеку, не различающему цвета, строка читается по значку.
    /// </remarks>
    public AxTextTone LevelTone => Record.Level switch
    {
        StudioLogLevel.Error => AxTextTone.Error,
        StudioLogLevel.Warning => AxTextTone.Warning,
        StudioLogLevel.Debug => AxTextTone.Secondary,
        _ => AxTextTone.Primary,
    };

    /// <summary>Сколько одинаковых записей схлопнуто в эту строку.</summary>
    public int Repeats
    {
        get => _repeats;
        private set
        {
            if (_repeats == value)
                return;

            _repeats = value;

            Raise(nameof(Repeats));
            Raise(nameof(RepeatsText));
            Raise(nameof(RepeatsTip));
            Raise(nameof(IsRepeated));
        }
    }

    /// <summary>Счётчик повторов текстом — его показывает знак повторов.</summary>
    public string RepeatsText => Repeats.ToString(CultureInfo.CurrentCulture);

    /// <summary>
    /// Счётчик повторов словами — его читают под курсором и средства доступности.
    /// </summary>
    /// <remarks>
    /// Знак показывает число, потому что места у него на число: строка журнала высотой в
    /// двадцать точек. Что это за число, говорит подсказка — без неё «3» рядом с записью значит
    /// что угодно.
    /// </remarks>
    public string RepeatsTip => _format is { Length: > 0 } format
        ? string.Format(CultureInfo.CurrentCulture, format, Repeats)
        : RepeatsText;

    /// <summary>Есть ли что показывать знаком повторов.</summary>
    public bool IsRepeated => Repeats > 1;

    /// <summary>Ещё одна такая же запись.</summary>
    public void Repeat() => Repeats++;

    /// <summary>
    /// Одинаковы ли записи с точки зрения свёртки.
    /// </summary>
    /// <param name="other">Запись, пришедшая следом.</param>
    /// <remarks>
    /// Время в счёт не идёт — иначе не схлопнулось бы ничего, и свёртка была
    /// бы переключателем без действия.
    /// </remarks>
    public bool SameAs(StudioLogRecord other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Record.Level == other.Level
            && string.Equals(Record.Source, other.Source, StringComparison.Ordinal)
            && string.Equals(Record.Message, other.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Строка целиком — тем же видом, каким её показывает список.
    /// </summary>
    /// <remarks>
    /// Не для отладки: этим текстом строку называет программа чтения с экрана.
    /// Без него она читает имя класса — <c>LogRow</c>, — и список превращается
    /// в десяток одинаковых объявлений. Проверено сканом живой студии.
    /// </remarks>
    public override string ToString() =>
        $"{Stamp} {Level} {Source} {Text}";

    private static string FirstLine(string message)
    {
        if (message is not { Length: > 0 })
            return string.Empty;

        var end = message.IndexOfAny(['\r', '\n']);

        return end < 0 ? message : message[..end];
    }

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
