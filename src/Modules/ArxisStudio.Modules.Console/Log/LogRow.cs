using System.ComponentModel;
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
    public LogRow(StudioLogRecord record, bool stamp = true)
    {
        ArgumentNullException.ThrowIfNull(record);

        Record = record;
        Text = FirstLine(record.Message);
        HasStamp = stamp;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Сама запись — её отдаёт панель подробностей и копирование.</summary>
    public StudioLogRecord Record { get; }

    /// <summary>Время записи, как его показывает журнал.</summary>
    public string Stamp => Record.Stamp;

    /// <summary>Показывать ли столбец времени.</summary>
    public bool HasStamp { get; }

    /// <summary>Уровень словом.</summary>
    public string Level => Record.LevelName;

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

    /// <summary>Ошибка.</summary>
    public bool IsError => Record.Level == StudioLogLevel.Error;

    /// <summary>Предупреждение.</summary>
    public bool IsWarning => Record.Level == StudioLogLevel.Warning;

    /// <summary>Подробность для отладки.</summary>
    public bool IsDebug => Record.Level == StudioLogLevel.Debug;

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
            Raise(nameof(IsRepeated));
        }
    }

    /// <summary>Счётчик повторов текстом — его показывает бейдж.</summary>
    public string RepeatsText => Repeats.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Есть ли что показывать бейджем.</summary>
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
