using System.ComponentModel;
using Avalonia;

namespace ArxisStudio.Shell.Localization;

/// <summary>
/// Тексты шаблонов темы — на языке студии.
/// </summary>
/// <remarks>
/// Шаблоны темы отдают средствам доступности имена иконочных кнопок — крестика в поле
/// поиска, в баннере, в диалоге. Тема языка студии не знает и берёт текст ресурсом, держа
/// у себя запасной; студия кладёт перевод тем же ключом в ресурсы приложения, а они
/// сильнее ресурсов темы. Прежде текст стоял в шаблоне по-русски и не переводился вовсе.
/// </remarks>
public static class ControlTexts
{
    /// <summary>Ключ ресурса темы — ключ словаря студии.</summary>
    private static readonly (string Resource, string Key)[] Texts =
    [
        ("AxTextSearchClear", "controls.search.clear"),
        ("AxTextMessageClose", "controls.message.close"),
        ("AxTextDialogClose", "controls.dialog.close"),
        ("AxTextTabOverflow", "controls.tabs.overflow"),
        ("AxTextBreadcrumbOverflow", "controls.breadcrumb.overflow"),
    ];

    /// <summary>Кладёт тексты в ресурсы приложения и переводит их вместе со студией.</summary>
    /// <param name="application">Приложение, чьи ресурсы сильнее ресурсов темы.</param>
    /// <returns>Отписка: язык живёт в синглтоне, и держать приложение подпиской незачем дольше, чем нужно.</returns>
    public static IDisposable Attach(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        void Apply()
        {
            foreach (var (resource, key) in Texts)
                application.Resources[resource] = Localizer.Instance[key];
        }

        void OnChanged(object? sender, PropertyChangedEventArgs e) => Apply();

        Apply();
        Localizer.Instance.PropertyChanged += OnChanged;

        return new Detach(() => Localizer.Instance.PropertyChanged -= OnChanged);
    }

    private sealed class Detach(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
