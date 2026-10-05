using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Markup;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;

namespace ArxisStudio.Modules.UiDesigner.Documents;

/// <summary>Сообщение над холстом форм.</summary>
/// <param name="Severity">Важность.</param>
/// <param name="Text">Текст.</param>
/// <param name="Rebuild">Предложить пересобрать дизайн.</param>
/// <param name="Span">Место ошибки в тексте формы — к нему ведёт «Показать в XAML».</param>
/// <param name="OfForm">
/// Сообщение о самой форме, а текст её не называет: на доске, где форм много, его называют именем файла.
/// </param>
internal sealed record FormNotice(AxBannerSeverity Severity, string Text, bool Rebuild = false, TextSpan? Span = null, bool OfForm = false);

/// <summary>
/// Что сказать о формах на холсте: чип состояния типов проекта и одно сообщение, самое важное, — у вкладки
/// и у доски одними словами.
/// </summary>
internal static class FormNotices
{
    /// <summary>Чип состояния типов проекта: ключ строки — или ничего — и идёт ли работа.</summary>
    /// <param name="design">Поколение службы XAML; null — службы нет.</param>
    public static (string? Key, bool Busy) Chip(IStudioXamlDesign? design) => (design?.State ?? XamlDesignState.Idle) switch
    {
        XamlDesignState.Starting => ("form.state.starting", true),
        XamlDesignState.Live => ("form.state.live", false),
        XamlDesignState.Building => ("form.state.building", true),
        XamlDesignState.SwapPending => ("form.state.swapPending", false),
        XamlDesignState.Swapping => ("form.state.swapping", true),
        XamlDesignState.RestartRequired => ("form.state.restart", false),
        XamlDesignState.Unsupported => ("form.state.unsupported", false),
        XamlDesignState.Failed => ("form.state.failed", false),
        _ => (null, false),
    };

    /// <summary>
    /// Что сказать над холстом, по важности: форма не открылась; типам нужен перезапуск, они не загрузились
    /// или проект на чужой Avalonia; документ закрыт или удалён; текст не показался; сборка дизайна упала.
    /// </summary>
    /// <param name="design">Поколение службы XAML; null — службы нет.</param>
    /// <param name="session">Форма, о которой речь; null — только о типах проекта.</param>
    /// <param name="strings">Словарь модуля.</param>
    public static FormNotice? For(IStudioXamlDesign? design, FormSession? session, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(strings);

        if (session?.Problem is { } problem)
            return new FormNotice(AxBannerSeverity.Error, problem, OfForm: true);

        var reason = design?.StateReason ?? string.Empty;

        switch (design?.State)
        {
            case XamlDesignState.RestartRequired:
                return new FormNotice(AxBannerSeverity.Warning, Format(strings, "form.restart", reason));

            case XamlDesignState.Failed:
                return new FormNotice(AxBannerSeverity.Error, Format(strings, "form.failed", reason));

            case XamlDesignState.Unsupported:
                return new FormNotice(AxBannerSeverity.Warning, Format(strings, "form.unsupported", reason));
        }

        if (session?.Document is { } document)
        {
            if (document.IsClosed)
                return new FormNotice(AxBannerSeverity.Information, strings["form.closed"]);

            if (document.IsDeleted)
                return new FormNotice(AxBannerSeverity.Warning, Format(strings, "form.deleted", session.Path.FileName));

            var error = document.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError);

            if (document.State == XamlDocumentState.Broken)
                return new FormNotice(AxBannerSeverity.Error, Format(strings, "form.broken", error?.Message), Span: error?.Span, OfForm: true);

            if (document.State == XamlDocumentState.Behind)
                return new FormNotice(AxBannerSeverity.Warning, Format(strings, "form.behind", error?.Message), Span: error?.Span, OfForm: true);
        }

        return design?.LastBuild is { Succeeded: false }
            ? new FormNotice(AxBannerSeverity.Error, strings["form.buildFailed"], Rebuild: true)
            : null;
    }

    private static string Format(IStudioStrings strings, string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, strings[key], values);
}
