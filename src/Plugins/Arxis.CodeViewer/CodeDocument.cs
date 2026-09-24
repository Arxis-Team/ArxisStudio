using ArxisStudio.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;

namespace Arxis.CodeViewer;

/// <summary>
/// Открытый файл во вкладке: текст только на просмотр, с номерами строк.
/// </summary>
/// <remarks>
/// Оформление объявлено у своего контрола, а не у приложения: стиль,
/// добавленный в <c>Application.Styles</c>, оценивался бы на каждом контроле
/// студии и пережил бы выгрузку плагина (ARX0014). Здесь он живёт ровно
/// столько, сколько вкладка.
/// <para>
/// Цвета и шрифт берутся ключами темы, а подсветка синтаксиса — своя у
/// AvaloniaEdit: её палитра из темы студии не выводится, и для пробного
/// плагина это осознанный размен. Настоящему редактору документов придётся
/// раскрашивать свой набор ключами темы.
/// </para>
/// </remarks>
public sealed class CodeDocument : DocumentView
{
    private const string Theme = "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml";

    /// <summary>
    /// Чем оформление AvaloniaEdit зовёт то, что у студии зовётся иначе.
    /// </summary>
    /// <remarks>
    /// Оформление пакета написано под тему Fluent и спрашивает её ключи. Fluent
    /// в студии нет и не будет — под контролами стоит своя тема, — поэтому
    /// недостающие ключи плагин заводит у себя и берёт их значения из темы
    /// студии. Своего числа здесь не появляется: слева — чужое имя, справа —
    /// ключ темы.
    /// <para>
    /// Список ровно такой, какого хватает показанному редактору. Панель поиска
    /// спрашивает у Fluent своё, но ключами её было бы не спасти: её поле ввода —
    /// голый контрол Avalonia, а тема студии одевает свои. Она снята ниже вовсе.
    /// </para>
    /// </remarks>
    private static readonly (string Foreign, string Theirs)[] Bridge =
    [
        ("ControlContentThemeFontSize", "AxFontSize"),
        ("ContentControlThemeFontFamily", "AxFontFamilyMono"),
    ];

    private readonly Panel _host;

    /// <summary>Заводит вкладку по прочитанному файлу.</summary>
    /// <param name="filePath">Путь к файлу: по нему выбирается подсветка и подпись вкладки.</param>
    /// <param name="text">Содержимое файла.</param>
    public CodeDocument(string filePath, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);

        Title = Path.GetFileName(filePath);

        Editor = new TextEditor
        {
            Document = new TextDocument(text),
            IsReadOnly = true,
            ShowLineNumbers = true,
            WordWrap = false,
            SyntaxHighlighting = Colouring(Path.GetExtension(filePath)),
        };

        // Панель поиска снимается вместе с её сочетанием: поле ввода и кнопки у
        // неё — голые контролы Avalonia, а тема студии одевает только свои. В
        // студии панель выходила бы без поля и без фона: три значка видны, а
        // куда набирают — нет. Просмотрщику она не нужна; настоящему редактору
        // придётся одеть её своим оформлением.
        //
        // Заводится панель не в конструкторе, а когда редактор получает
        // оформление, — потому и снимается здесь, а не строкой выше.
        Editor.TemplateApplied += (_, _) => Editor.SearchPanel?.Uninstall();

        Editor[!TemplatedControl.BackgroundProperty] = new DynamicResourceExtension("AxSurfaceBaseBrush");
        Editor[!TemplatedControl.ForegroundProperty] = new DynamicResourceExtension("AxTextPrimaryBrush");
        Editor[!TemplatedControl.FontFamilyProperty] = new DynamicResourceExtension("AxFontFamilyMono");
        Editor[!TemplatedControl.FontSizeProperty] = new DynamicResourceExtension("AxFontSize");
        Editor[!TextEditor.LineNumbersForegroundProperty] = new DynamicResourceExtension("AxTextTertiaryBrush");

        var theme = new Uri(Theme);
        var style = new StyleInclude(theme) { Source = theme };

        // Недостающие ключи кладутся в словарь самого оформления, а не рядом с
        // контролом: StaticResource в разметке ищет по тому, что стояло вокруг
        // него в документе, и до ресурсов вкладки не доходит вовсе. Положить их
        // надо раньше, чем редактор спросит своё оформление, — потому и здесь.
        if (style.Loaded is Styles loaded)
        {
            foreach (var (foreign, theirs) in Bridge)
            {
                if (Application.Current?.TryGetResource(theirs, null, out var value) == true && value is not null)
                    loaded.Resources[foreign] = value;
            }
        }

        _host = new Panel();
        _host.Styles.Add(style);
        _host.Children.Add(Editor);
    }

    /// <inheritdoc/>
    public override Control Content => _host;

    /// <inheritdoc/>
    public override string Title { get; }

    /// <summary>Сам редактор; тестам и проверке живьём.</summary>
    internal TextEditor Editor { get; }

    /// <summary>Подсветка по расширению; пусто — показываем просто текст.</summary>
    /// <remarks>
    /// Набор идёт с AvaloniaEdit и знает расширения сам; разметка Avalonia ему
    /// незнакома, и <c>.axaml</c> с <c>.slnx</c> приходится называть XML руками.
    /// </remarks>
    private static IHighlightingDefinition? Colouring(string extension)
    {
        var manager = HighlightingManager.Instance;

        return manager.GetDefinitionByExtension(extension)
            ?? (extension.ToLowerInvariant() switch
            {
                ".axaml" or ".slnx" or ".csproj" or ".props" or ".targets" => manager.GetDefinition("XML"),
                ".json" => manager.GetDefinition("JavaScript"),
                _ => null,
            });
    }
}
