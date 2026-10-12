using System.Collections.Immutable;
using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;

namespace ArxisStudio.Xaml;

/// <summary>Откуда у элемента данные, которые читают его привязки.</summary>
/// <remarks>Появилось в контракте 1.2.</remarks>
public enum XamlDataSource
{
    /// <summary>Данных нет: ни элемент, ни предки их не задали, и код не поставил.</summary>
    None,

    /// <summary>Унаследованы от предка — его называет <see cref="XamlDataScope.SourceElement"/>.</summary>
    Inherited,

    /// <summary>Элемент создаёт их сам — экземпляр класса элементом свойства: <c>&lt;Window.DataContext&gt;&lt;vm:Main/&gt;</c>.</summary>
    Instance,

    /// <summary>Элемент берёт их выражением: <c>DataContext="{Binding Customer}"</c>.</summary>
    Expression,

    /// <summary>Элемент сам себе данные: <c>DataContext="{Binding $self}"</c>.</summary>
    Self,

    /// <summary>
    /// Их поставил код — конструктор формы или элемента, — а документ не пишет; <see cref="XamlDataScope.SourceElement"/>
    /// пуст.
    /// </summary>
    Code,

    /// <summary>
    /// Данные для дизайна — <c>Design.DataContext</c> или <c>d:DataContext</c> корня: дизайнер показывает их, а в
    /// приложении данные поставит код.
    /// </summary>
    Design,
}

/// <summary>Что читают привязки элемента — именами: откуда данные, какого они типа и проверяются ли пути.</summary>
/// <param name="Source">Откуда данные.</param>
/// <param name="SourceElement">
/// Элемент, который их задал: сам элемент или предок, у данных для дизайна — корень; null — их задал код или их нет.
/// </param>
/// <param name="SourceText">Как они написаны: <c>&lt;vm:Main /&gt;</c>, <c>{Binding Customer}</c>; null — не написаны.</param>
/// <param name="SourceType">
/// Тип данных у объекта показа сейчас — в дизайнере это данные для дизайна, если корень их пишет; null — данных нет.
/// </param>
/// <param name="WrittenDataType">Тип данных в области — как написан <c>x:DataType</c>; null — не написан.</param>
/// <param name="DataTypeElement">Элемент, написавший этот <c>x:DataType</c>; null — не написан.</param>
/// <param name="DataType">Тип, к которому <c>x:DataType</c> разрешился; null — не написан или не разрешился.</param>
/// <param name="CompilesBindings">
/// <c>{Binding}</c> у элемента компилируется: путь проверяет сборка по <c>x:DataType</c>, и без него сборка не пройдёт.
/// </param>
/// <param name="CompilesByProjectDefault">Проект компилирует привязки по умолчанию (<c>AvaloniaUseCompiledBindingsByDefault</c>).</param>
/// <remarks>
/// Имена, а не типы: тип — объект поколения, и держатель сведений держал бы его через замену. Появилось в контракте 1.2.
/// </remarks>
public sealed record XamlDataScope(
    XamlDataSource Source,
    XamlElementPath? SourceElement,
    string? SourceText,
    XamlTypeInfo? SourceType,
    string? WrittenDataType,
    XamlElementPath? DataTypeElement,
    XamlTypeInfo? DataType,
    bool CompilesBindings,
    bool CompilesByProjectDefault)
{
    /// <summary>
    /// Тип, по которому читаются пути привязок, — как их прочтёт проект: компилируемые — по <c>x:DataType</c>, а
    /// некомпилируемые — по данным, которые у элемента будут при работе формы, и по <c>x:DataType</c> только без них.
    /// </summary>
    /// <remarks>
    /// <c>x:DataType</c> — то, по чему путь проверяет компилятор. Привязка, которую он не компилирует, читает данные
    /// элемента, и у потомка элемента, отдавшего содержимому другие данные, тип данных области назвал бы рабочий путь
    /// сломанным (запись 424).
    /// </remarks>
    public XamlTypeInfo? PathType => CompilesBindings ? DataType : SourceType ?? DataType;

    /// <summary>
    /// Привязке к данным не хватит типа: привязки компилируются, а <c>x:DataType</c> в области не написан или не
    /// разрешился, — сборка такую форму не соберёт.
    /// </summary>
    public bool NeedsDataType => CompilesBindings && DataType is null;
}

/// <summary>Класс проекта, которым документ может задать данные, — именами.</summary>
/// <param name="FullName">Полное имя: <c>App.ViewModels.MainViewModel</c>.</param>
/// <param name="Name">Короткое имя.</param>
/// <param name="ClrNamespace">Пространство CLR.</param>
/// <param name="XmlNamespace">Пространство разметки, которым его пишет документ: <c>using:App.ViewModels</c>.</param>
/// <param name="SuggestedPrefix">Префикс, который предлагает сборка; null — не предлагает.</param>
/// <param name="IsBuilt">Класс есть в живом поколении: проект с ним собран.</param>
/// <param name="CanCreate">Документ может создать его: открытый конструктор без параметров, не абстрактный.</param>
/// <param name="IsData">
/// Не объект Avalonia — данные, а не контрол, стиль или приложение; у ещё не построенного класса — не известно, и
/// он числится данными.
/// </param>
/// <param name="IsControl">Контрол.</param>
/// <param name="IsFormClass">Класс формы: <c>x:Class</c> со своей разметкой.</param>
/// <param name="Notifies">Сообщает о переменах свойств (<c>INotifyPropertyChanged</c>).</param>
/// <param name="IsConverter">Конвертер привязки, а не данные.</param>
/// <param name="IsTemplate">Шаблон данных — так устроен <c>ViewLocator</c>, — а не данные.</param>
/// <param name="Files">Файлы кода, где класс объявлен; пусто — ни один файл проекта его не объявляет.</param>
/// <remarks>Появилось в контракте 1.2.</remarks>
public sealed record XamlDataTypeInfo(
    string FullName,
    string Name,
    string ClrNamespace,
    string XmlNamespace,
    string? SuggestedPrefix,
    bool IsBuilt,
    bool CanCreate,
    bool IsData,
    bool IsControl,
    bool IsFormClass,
    bool Notifies,
    bool IsConverter,
    bool IsTemplate,
    ImmutableArray<CanonicalPath> Files)
{
    /// <summary>Модель: данные, а не конвертер или шаблон, — её предлагают источником данных.</summary>
    public bool IsModel => IsData && !IsConverter && !IsTemplate;
}
