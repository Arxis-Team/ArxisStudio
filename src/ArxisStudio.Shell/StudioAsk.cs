using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Shell.Localization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Shell;

/// <summary>
/// Вопрос, на который студия не вправе ответить за человека.
/// </summary>
/// <remarks>
/// Диалог собирается кодом, а не разметкой: вопросов у студии наперечёт, и форм у
/// них две — значок, текст и две кнопки у вопроса, поле и две кнопки у имени. Заводить
/// под каждый свой <c>.axaml</c> значило бы повторить форму столько раз, сколько
/// вопросов, а подвал с кнопками у обеих форм один.
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
    /// <param name="refuse">Надпись на кнопке отказа; null — «Отмена».</param>
    /// <param name="question">
    /// Это вопрос, а не предупреждение: значок вопроса в цвете акцента вместо знака внимания.
    /// </param>
    /// <returns><c>true</c>, если человек согласился.</returns>
    /// <remarks>
    /// Диалог модальный намеренно: решение, задевающее чужое, не то, мимо чего
    /// можно щёлкнуть. Esc и отказ оставляют всё как было.
    /// <para>
    /// Не каждый вопрос предупреждает. «Перезапустить, чтобы применить изменения?» ничего не
    /// отнимает — отказ у него не «Отмена», а «Не сейчас», и знак внимания над ним пугал бы тем,
    /// чего нет. Такой вопрос и называет себя вопросом.
    /// </para>
    /// </remarks>
    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirm,
        bool danger = false,
        string? refuse = null,
        bool question = false)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var cancel = new AxButton { Content = refuse ?? Localizer.Instance["common.cancel"] };
        var agree = new AxButton
        {
            Content = confirm,
            Appearance = danger ? AxButtonAppearance.Danger : AxButtonAppearance.Primary,
        };
        var alert = new AxIcon { Data = question ? AxIcons.Question : AxIcons.Warning };

        // Цвет — привязкой к теме, а не значением, снятым один раз: снятая кисть не переключилась
        // бы вместе с темой.
        alert.Bind(TemplatedControl.ForegroundProperty, alert.GetResourceObservable(question ? "AxAccentBrush" : "AxWarningBrush"));

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
            Buttons = Footer(cancel, agree),
        };

        cancel.Click += (_, _) => dialog.Close(false);
        agree.Click += (_, _) => dialog.Close(true);

        // Вопрос открывается с клавиатурой на ответе, а не в пустоте: без этого фокуса не
        // было ни у кого, и Enter с пробелом не отвечали ничего. На необратимом клавиатура
        // стоит на «Отмене» — случайный Enter не должен соглашаться на то, чего не вернуть, —
        // на обратимом на согласии. Кольцо видно сразу: человек должен знать, что сделает Enter.
        dialog.Opened += (_, _) => (danger ? cancel : agree).Focus(NavigationMethod.Tab);

        return await dialog.ShowDialog<bool?>(owner) == true;
    }

    /// <summary>
    /// Спрашивает имя — одно поле и две кнопки.
    /// </summary>
    /// <param name="owner">Окно, которому принадлежит вопрос.</param>
    /// <param name="title">Заголовок.</param>
    /// <param name="hint">Подсказка в пустом поле.</param>
    /// <param name="confirm">Надпись на кнопке согласия.</param>
    /// <returns>Введённое имя как есть; null — отказались.</returns>
    /// <remarks>
    /// Модальным окном, а не полем в меню: меню закрывается от первого же щелчка мимо, и начатое
    /// пропало бы вместе с недопечатанным именем. Каретка сразу в поле — другого дела у этого окна
    /// нет, — и Enter соглашается. Пустое и лишние пробелы судит тот, кто спрашивал.
    /// </remarks>
    public static async Task<string?> NameAsync(Window owner, string title, string hint, string confirm)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var box = new AxTextBox { PlaceholderText = hint, Width = 260 };
        var cancel = new AxButton { Content = Localizer.Instance["common.cancel"] };
        var save = new AxButton { Content = confirm, Appearance = AxButtonAppearance.Primary };

        var dialog = new AxDialog
        {
            Title = title,
            Content = box,
            Buttons = Footer(cancel, save),
        };

        dialog.Opened += (_, _) => box.Focus();
        cancel.Click += (_, _) => dialog.Close(null);
        save.Click += (_, _) => dialog.Close(box.Text);

        box.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter)
                dialog.Close(box.Text);
        };

        return await dialog.ShowDialog<string?>(owner);
    }

    /// <summary>Подвал диалога: отказ слева, согласие справа.</summary>
    /// <remarks>
    /// Ширина кнопок и зазор между ними — ключами темы, а не числом: число не сжалось бы вместе с
    /// плотностью, и подвалы вопросов студии разошлись бы между собой.
    /// </remarks>
    private static StackPanel Footer(AxButton refuse, AxButton agree)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { refuse, agree },
        };

        refuse.Bind(Layoutable.MinWidthProperty, refuse.GetResourceObservable("AxDialogButtonMinWidth"));
        agree.Bind(Layoutable.MinWidthProperty, agree.GetResourceObservable("AxDialogButtonMinWidth"));
        buttons.Bind(StackPanel.SpacingProperty, buttons.GetResourceObservable("AxGapControls"));

        return buttons;
    }
}
