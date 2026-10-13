using ArxisStudio.Markup.Xaml;
using ArxisStudio.ProjectSystem;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Xaml;

/// <summary>
/// Показ документа: корень, построенный из текста в живом поколении типов проекта, и дорога между
/// объектами и элементами текста.
/// </summary>
/// <remarks>
/// <para>
/// <b>Объекты поколения не держат.</b> Корень, приложение формы и всё, что отдают
/// <see cref="ObjectAt"/> и <see cref="GetDeclaredObjects"/>, построено из типов проекта, и пока на это
/// есть ссылка, поколение не может уйти, а замена типов кончается перезапуском. Поэтому держатель
/// отпускает своё участником (<see cref="IStudioXamlDesign.Register"/>), а показ следом отдаёт корень
/// (<see cref="RootChanged"/> с пустым корнем). Между вызовами помнят путь элемента
/// (<see cref="XamlElementPath"/>), а не объект: путь переживает и пересборку части, и замену поколения,
/// и перезапуск.
/// </para>
/// <para>
/// <b>Показ один на документ</b> и переживает замены поколений: корень меняется, показ остаётся.
/// Отпускает его тот, кто взял, — <c>Dispose</c>; документ при этом остаётся открытым.
/// </para>
/// <para>
/// <b>Потоки.</b> Всё — в потоке интерфейса: объекты принадлежат ему.
/// </para>
/// </remarks>
public interface IXamlDesignView : IDisposable
{
    /// <summary>Документ, который показывают.</summary>
    IXamlDocumentHandle Document { get; }

    /// <summary>
    /// Корень, построенный из текста; null — поколения нет, его заменяют, документ не показался или
    /// его корень не контрол.
    /// </summary>
    /// <remarks>Корень-окно никогда не показывается окном: его содержимое показывает тот, кто одалживает.</remarks>
    Control? Root { get; }

    /// <summary>
    /// Корень сменился: документ построен заново, поколение отпущено на замену (корень пуст) или
    /// пришло новое.
    /// </summary>
    event EventHandler? RootChanged;

    /// <summary>
    /// Приложение формы — стили, ресурсы и шаблоны данных, которые объявляет <c>App.axaml</c> её проекта
    /// или проекта, который на него ссылается, — построенное как написано, без конструктора класса
    /// приложения; null — приложения нет или поколения нет.
    /// </summary>
    /// <remarks>
    /// Своё у каждого показа: стиль и словарь Avalonia отдаёт одному владельцу, и рамка формы берёт их
    /// себе. Объект поколения, как и корень.
    /// </remarks>
    Application? Application { get; }

    /// <summary>Файл приложения формы; пусто — приложения нет.</summary>
    CanonicalPath ApplicationFile { get; }

    /// <summary>Приложение формы сменилось: его файл сохранили, поколение заменили или отпустили.</summary>
    event EventHandler? ApplicationChanged;

    /// <summary>Путь элемента, построившего объект.</summary>
    /// <param name="live">Объект показа.</param>
    /// <returns>Путь; null — объект построен не элементом документа: шаблоном, рамкой, никем.</returns>
    XamlElementPath? PathOf(object live);

    /// <summary>Объект, построенный элементом.</summary>
    /// <param name="path">Путь элемента в показанном тексте.</param>
    /// <returns>Объект; null — элемента нет или он ничего не построил. Дольше вызова не держат.</returns>
    object? ObjectAt(XamlElementPath path);

    /// <summary>
    /// Объекты, которые документ объявил сам, — у каждого свой элемент, и элемент ведёт к нему же, —
    /// кроме корня, в порядке документа.
    /// </summary>
    /// <returns>Чтение сейчас; пусто — показывать нечего. Дольше вызова не держат.</returns>
    /// <remarks>
    /// Не всё, что построено: метка внутри кнопки построена её шаблоном и элемента не имеет, а
    /// предложить её к выбору значило бы предложить править то, чего в тексте нет.
    /// </remarks>
    IReadOnlyList<object> GetDeclaredObjects();

    /// <summary>Что построил элемент: тип объекта, его базы и тип родителя — именами.</summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <returns>Сведения; null — элемента нет, он ничего не построил или показ отдал объекты.</returns>
    /// <remarks>Появилось в контракте 1.1.</remarks>
    XamlElementInfo? DescribeElement(XamlElementPath element);

    /// <summary>
    /// Члены элемента — свои и присоединённые, которые читает родитель или уже написал документ, — с
    /// нынешними значениями.
    /// </summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <returns>Члены по имени; пусто — элемента нет или показ отдал объекты.</returns>
    /// <remarks>
    /// Событий и списков среди них нет: обработчик — не значение, а детей пишут элементами. Члены, которые
    /// документ написать не может, — только для чтения: <see cref="XamlMemberInfo.CanWrite"/> у них ложь.
    /// Присоединённый член стороннего владельца — <c>ToolTip.Tip</c> — сюда не попадает, пока документ его
    /// не написал: его спрашивают по имени (<see cref="GetMember"/>). Появилось в контракте 1.1.
    /// </remarks>
    IReadOnlyList<XamlMemberInfo> GetMembers(XamlElementPath element);

    /// <summary>Член элемента по имени, как его пишет документ: <c>Width</c>, <c>ToolTip.Tip</c>.</summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="member">Имя члена.</param>
    /// <returns>Член; null — имя ничего не значит на этом объекте, это событие или список.</returns>
    /// <remarks>Появилось в контракте 1.1.</remarks>
    XamlMemberInfo? GetMember(XamlElementPath element, string member);

    /// <summary>Прочтётся ли текст значением члена — так, как его прочтёт загрузка.</summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="member">Имя члена.</param>
    /// <param name="text">Значение, как его написал бы документ.</param>
    /// <returns>Отказ с причиной или успех.</returns>
    /// <remarks>
    /// Пустой текст проходит — он значит «снять атрибут», — проходит и выражение: <c>{Binding …}</c> —
    /// не значение, а его разбор. Член, который проверить не у кого, — неразрешённый, без показа, — тоже
    /// проходит: отказывать в нём нечем. Появилось в контракте 1.1.
    /// </remarks>
    XamlValueCheck CheckValue(XamlElementPath element, string member, string text);

    /// <summary>Начинает предпросмотр значения члена — на объекте, не трогая документ.</summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="member">Имя члена.</param>
    /// <returns>
    /// Предпросмотр; null — показать нечем: у прямого свойства и свойства CLR нет приоритета, который снялся бы
    /// без следа, а у неразрешённого — объекта.
    /// </returns>
    /// <remarks>Появилось в контракте 1.1.</remarks>
    IXamlValuePreview? Preview(XamlElementPath element, string member);

    /// <summary>Что читают привязки элемента: откуда его данные, какого они типа и проверяются ли пути.</summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Сведения; null — элемента нет или показ отдал объекты.</returns>
    /// <remarks>
    /// Асинхронно: <c>x:DataType</c> разрешается через среду поколения. Ответ — имена, и держать его можно сколько
    /// угодно; замена поколения посреди вопроса кончает его отменой. Появилось в контракте 1.2.
    /// </remarks>
    ValueTask<XamlDataScope?> DescribeDataAsync(XamlElementPath element, CancellationToken cancellationToken = default);

    /// <summary>
    /// Классы, которыми документ может задать данные: построенные классы проекта документа и тех, на которые он
    /// ссылается, и классы, которые его файлы кода объявляют, а сборка ещё не построила.
    /// </summary>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Классы по имени; пусто — поколения нет.</returns>
    /// <remarks>
    /// Не только модели: контролы, конвертеры и шаблоны помечены, и что предлагать, решает тот, кто спрашивает.
    /// Появилось в контракте 1.2.
    /// </remarks>
    ValueTask<IReadOnlyList<XamlDataTypeInfo>> GetDataTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет выражения, которыми элемент пишет свои члены: привязке — источник и путь по тому, что документ
    /// объявляет, и примет ли её сборка проекта.
    /// </summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>
    /// Проверку каждого атрибута-выражения в порядке документа; пусто — выражений нет, элемента нет или показ отдал
    /// объекты.
    /// </returns>
    /// <remarks>
    /// Источник привязки ищется в документе, а не в дереве, где стоят объекты: форма в дизайнере стоит внутри
    /// контролов студии, и предок или данные, найденные там, были бы студийными. Компилируется ли привязка, решает
    /// ближайший <c>x:CompileBindings</c>, а без него — свойство проекта. Ключ ресурса ищется так же — у элемента, его
    /// предков в документе и приложения формы (контракт 1.5); статические члены пока не проверяются. Появилось в
    /// контракте 1.3.
    /// </remarks>
    ValueTask<IReadOnlyList<XamlExpressionCheck>> CheckExpressionsAsync(XamlElementPath element, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет выражение, которого в документе ещё нет, — тем же правилом, что написанное: черновик привязки до
    /// записи.
    /// </summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="member">Член, на котором выражение встанет: <c>Text</c>, <c>Grid.Row</c>, <c>DataContext</c>.</param>
    /// <param name="text">Выражение текстом: <c>{Binding Customer.Name, Mode=TwoWay}</c>.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Проверка; null — элемента нет, текст не выражение или показ отдал объекты.</returns>
    /// <remarks>Появилось в контракте 1.4.</remarks>
    ValueTask<XamlExpressionCheck?> CheckExpressionAsync(
        XamlElementPath element, string member, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Что привязка может назвать дальше: члены типа, к которому привёл её путь, — у данных, элемента, предка или
    /// самого элемента, по тому, что документ объявляет.
    /// </summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="member">Член, к которому привязывают: по нему решается, какие значения ему подходят.</param>
    /// <param name="binding">
    /// Привязка до шага, которого ещё нет: <c>{Binding}</c> — члены данных, <c>{Binding Customer}</c> — члены
    /// <c>Customer</c>, <c>{Binding #slider}</c> — члены элемента <c>slider</c>.
    /// </param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Тип и его члены; null — путь не привёл ни к какому типу или показ отдал объекты.</returns>
    /// <remarks>
    /// Где проект компилирует привязки, а типа данных в области нет, члены — у данных показа: путь подсказывают и
    /// так, а что сборке нужен тип данных, скажет проверка. Появилось в контракте 1.4.
    /// </remarks>
    ValueTask<XamlBindableMembers?> GetBindableMembersAsync(
        XamlElementPath element, string member, string binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ресурсы, которые элемент может назвать по ключу: свои и предков в документе — со стилями, которые те пишут, — и
    /// приложения формы с его темой.
    /// </summary>
    /// <param name="element">Путь элемента в показанном тексте.</param>
    /// <param name="member">Член, для которого спрашивают: по нему решается, подходит ли значение.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Каждый ключ один раз — тот, что найдёт поиск, ближайший первым; пусто — элемента нет или показ отдал объекты.</returns>
    /// <remarks>
    /// Ищутся по документу и приложению формы, а не по дереву, где стоят объекты: форма в дизайнере стоит внутри
    /// студии, и ключ, найденный там, — студийный. Порядок текста не важен: Avalonia находит ключ предка, написанный
    /// после места, где его назвали, и не находит ключа соседа. Появилось в контракте 1.5.
    /// </remarks>
    ValueTask<IReadOnlyList<XamlResourceInfo>> GetResourcesAsync(
        XamlElementPath element, string member, CancellationToken cancellationToken = default);
}
