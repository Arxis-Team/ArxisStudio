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
/// Меню собирается на каждый показ: пункты зависят от того, что выделено, и держать их между
/// показами значило бы обновлять их по каждому щелчку в списке.
/// </para>
/// </remarks>
/// <param name="strings">Словарь студии: подписи пунктов.</param>
/// <param name="copy">Скопировать выделенные записи целиком.</param>
/// <param name="copyMessage">Скопировать сообщение записи, на которой стоят.</param>
/// <param name="only">Оставить в отборе только источник этой записи.</param>
/// <param name="clear">Очистить журнал.</param>
internal sealed class LogRowMenu(
    IStudioStrings strings,
    Action copy,
    Action copyMessage,
    Action<string> only,
    Action clear)
{
    /// <summary>Показывает меню у строки.</summary>
    /// <param name="list">Список записей: у него меню и встаёт.</param>
    /// <param name="row">Строка, на которой стоят; <c>null</c> — щёлкнули мимо строк.</param>
    /// <exception cref="ArgumentNullException"><paramref name="list"/> равен <c>null</c>.</exception>
    /// <remarks>
    /// Без строки меню показывает одну «Очистить»: копировать нечего, а источник неизвестен.
    /// Пустого меню при этом не бывает — оно всегда отвечает хоть что-то.
    /// </remarks>
    public void ShowAt(Control list, LogRow? row)
    {
        ArgumentNullException.ThrowIfNull(list);

        var flyout = new AxMenuFlyout();

        if (row is not null)
        {
            flyout.Items.Add(Item(strings["console.copy"], new KeyGesture(Key.C, KeyModifiers.Control), copy));
            flyout.Items.Add(Item(strings["console.copy.message"], null, copyMessage));
            flyout.Items.Add(Item(strings["console.source.only"], null, () => only(row.Source)));
        }

        // Очистка помечена необратимой: тема красит такой пункт цветом ошибки. Разделителя перед
        // ним нет — Separator это виджет Avalonia, а расширению их заводить нельзя (ARX0001);
        // цвет отделяет пункт надёжнее линии.
        flyout.Items.Add(Item(strings["console.clear"], null, clear, destructive: true));

        flyout.ShowAt(list, showAtPointer: false);

        static AxMenuItem Item(string header, KeyGesture? gesture, Action act, bool destructive = false)
        {
            var item = new AxMenuItem { Header = header, InputGesture = gesture, IsDestructive = destructive };

            item.Click += (_, _) => act();

            return item;
        }
    }
}
