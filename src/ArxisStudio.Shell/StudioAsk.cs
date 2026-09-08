using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Shell;

/// <summary>
/// Вопрос, на который студия не вправе ответить за человека.
/// </summary>
/// <remarks>
/// Диалог собирается кодом, а не разметкой: вопросов у студии наперечёт, все
/// они одной формы — значок, текст, две кнопки, — и заводить под каждый свой
/// <c>.axaml</c> значило бы повторить эту форму столько раз, сколько вопросов.
/// <para>
/// Жил этот сбор в окне Welcome, пока спрашивало только оно. Со вторым
/// спрашивающим — окном настроек, которому нужно спросить о несохранённых
/// правках, — он переехал сюда: две копии одного диалога разошлись бы, и
/// человек увидел бы два разных вопроса об одном и том же.
/// </para>
/// </remarks>
public static class StudioAsk
{
    /// <summary>
    /// Спрашивает разрешения на действие, которое нельзя отменить.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит вопрос.</param>
    /// <param name="title">Заголовок.</param>
    /// <param name="message">Что случится.</param>
    /// <param name="confirm">Надпись на кнопке согласия.</param>
    /// <param name="danger">Действие необратимо — кнопка предупреждает цветом.</param>
    /// <returns><c>true</c>, если человек согласился.</returns>
    /// <remarks>
    /// Диалог модальный намеренно: решение, задевающее чужое, не то, мимо чего
    /// можно щёлкнуть. Esc и «Отмена» оставляют всё как было.
    /// </remarks>
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirm,
        bool danger = false)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var cancel = new AxButton { Content = Localizer.Instance["common.cancel"], MinWidth = 96 };
        var agree = new AxButton { Content = confirm, MinWidth = 96 };
        var alert = new AxIcon { Data = AxIcons.Warning, Width = 20, Height = 20 };

        // Кисть ищется с вариантом темы: без него ресурс не находится, а
        // выставленный null убил бы наследование цвета — значок стал бы
        // невидимым.
        if (owner.TryFindResource("AxYelBrush", owner.ActualThemeVariant, out var yellow) &&
            yellow is IBrush brush)
        {
            alert.Foreground = brush;
        }

        agree.Classes.Add(danger ? "danger" : "accent");

        var dialog = new AxDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
            },
            AlertIcon = alert,
            Buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { cancel, agree },
            },
        };

        cancel.Click += (_, _) => dialog.Close(false);
        agree.Click += (_, _) => dialog.Close(true);

        return await dialog.ShowDialog<bool?>(owner) == true;
    }
}
