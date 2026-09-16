using ArxisStudio.Controls;
using ArxisStudio.Modules.Console.Log;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArxisStudio.Modules.Console.Panels;

/// <summary>
/// Контекстное меню строки журнала: что можно сделать с записью, на которой стоят.
/// </summary>
/// <remarks>
/// Меню — не украшение, а второй вход к тем же действиям: кнопка полосы одна на панель, а действие
/// человек задумывает, глядя на строку. Так устроены списки Rider и Visual Studio.
/// <para>
/// Отбор по источнику входит сюда трижды, и каждый раз по делу. «Только этот» и «Скрыть этот» —
/// ответы на ту запись, на которую смотрят: источник у неё уже есть, называть его в списке ещё
/// раз человеку незачем. Подменю «Источники» — тот же список, что у кнопки полосы, для отбора из
/// нескольких: до кнопки от строки далеко, а решение о показе принимают, читая записи.
/// </para>
/// <para>
/// Меню собирается на каждый показ: пункты зависят от того, что выделено, и держать их между
/// показами значило бы обновлять их по каждому щелчку в списке.
/// </para>
/// </remarks>
/// <param name="strings">Словарь студии: подписи пунктов.</param>
/// <param name="copy">Скопировать выделенные записи целиком.</param>
/// <param name="copyMessage">Скопировать сообщение записи, на которой стоят.</param>
/// <param name="clear">Очистить журнал.</param>
/// <param name="sources">Отбор по источнику: что спросить и куда ответить.</param>
internal sealed class LogRowMenu(
    IStudioStrings strings,
    Action copy,
    Action copyMessage,
    Action clear,
    LogSourcePicker sources)
{
    /// <summary>Показывает меню там, где его попросили.</summary>
    /// <param name="anchor">
    /// К чему привязать меню: у мыши это список — меню встаёт под указателем, — у клавиатуры
    /// строка, на которой стоят.
    /// </param>
    /// <param name="row">Строка, на которой стоят; <c>null</c> — щёлкнули мимо строк.</param>
    /// <param name="atPointer">Просили мышью: меню встаёт под указателем, а не у края списка.</param>
    /// <exception cref="ArgumentNullException"><paramref name="anchor"/> равен <c>null</c>.</exception>
    /// <remarks>
    /// Без строки меню показывает список источников и «Очистить»: копировать нечего, а «только
    /// этот» и «скрыть этот» некого. Пустого меню при этом не бывает — оно всегда отвечает хоть
    /// что-то.
    /// <para>
    /// Место меню — не мелочь: привязанное к списку, оно встаёт у его угла, и при списке во всю
    /// ширину панели это метр от того места, куда человек щёлкнул. Мышью меню просят там, где
    /// смотрят.
    /// </para>
    /// </remarks>
    public void ShowAt(Control anchor, LogRow? row, bool atPointer)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        var flyout = new AxMenuFlyout();

        foreach (var item in Items(row))
            flyout.Items.Add(item);

        flyout.ShowAt(anchor, atPointer);
    }

    /// <summary>
    /// Собирает пункты меню.
    /// </summary>
    /// <param name="row">Строка, на которой стоят; <c>null</c> — щёлкнули мимо строк.</param>
    /// <returns>Пункты в порядке показа.</returns>
    /// <remarks>
    /// Отдельно от показа: пункты собираются в коде, а показываются в попапе — в отдельном окне,
    /// которого у безголового прогона нет, — и проверять их честнее там, где они рождаются.
    /// </remarks>
    public IReadOnlyList<AxMenuItem> Items(LogRow? row)
    {
        var items = new List<AxMenuItem>();

        if (row is not null)
        {
            items.Add(Item(strings["console.copy"], new KeyGesture(Key.C, KeyModifiers.Control), copy));
            items.Add(Item(strings["console.copy.message"], null, copyMessage));
            items.Add(Item(strings["console.source.only"], null, () =>
                sources.Pick(LogSources.Only(row.Source, sources.Known()))));
            items.Add(Item(strings["console.source.hide"], null, () =>
                sources.Pick(sources.Chosen().Hide(row.Source))));
        }

        items.Add(Sources());

        // Очистка помечена необратимой: тема красит такой пункт цветом ошибки. Разделителя перед
        // ним нет — Separator это виджет Avalonia, а расширению их заводить нельзя (ARX0001);
        // цвет отделяет пункт надёжнее линии.
        items.Add(Item(strings["console.clear"], null, clear, destructive: true));

        return items;

        static AxMenuItem Item(string header, KeyGesture? gesture, Action act, bool destructive = false)
        {
            var item = new AxMenuItem { Header = header, InputGesture = gesture, IsDestructive = destructive };

            item.Click += (_, _) => act();

            return item;
        }
    }

    /// <summary>Подменю со списком источников — тем же, что у кнопки полосы.</summary>
    private AxMenuItem Sources()
    {
        var item = new AxMenuItem { Header = strings["console.sources"] };

        foreach (var source in LogSourceMenu.Items(sources, strings))
            item.Items.Add(source);

        return item;
    }
}
