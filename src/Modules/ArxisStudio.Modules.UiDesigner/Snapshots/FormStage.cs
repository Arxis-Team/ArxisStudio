using ArxisStudio.Controls;
using ArxisStudio.Modules.UiDesigner.Board;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.Modules.UiDesigner.Model;
using ArxisStudio.Surface.UiDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace ArxisStudio.Modules.UiDesigner.Snapshots;

/// <summary>
/// Сцена фонового снимка: карточка формы в окне, которое никогда не показывают.
/// </summary>
/// <remarks>
/// <para>
/// Форме нужен корень дерева: стили и шаблоны контрол получает, войдя в логическое дерево с корнем, а
/// карточка — та же, что у вкладки, с её темой, чтобы снимок вышел таким же. Окно даёт этот корень и без
/// показа: содержимое входит в его дерево, как только поставлено. На экране, в панели задач и в списке
/// окон студии непоказанного окна нет.
/// </para>
/// <para>
/// Раскладывает сцена себя сама, а не окно: непоказанное окно невидимо, и его проход раскладки не
/// трогает ничего внутри. Холста вкладки на сцене нет — снимается область формы внутри карточки, и
/// холсту в ней делать нечего.
/// </para>
/// <para>Сцена одна на дизайнер и живёт, пока он поднят: окно заводится один раз, а не на каждый снимок.</para>
/// </remarks>
internal sealed class FormStage : IDisposable
{
    private readonly AxWindow _window;
    private readonly Panel _scene;

    /// <summary>Заводит сцену.</summary>
    public FormStage()
    {
        Item = new UiDesignerFormItem();
        _scene = new Panel { Children = { Item } };
        _window = new AxWindow { ShowInTaskbar = false };

        // Тема карточки — та же, что у вкладки: её разметка берёт её словарём, сцена — так же. Словарь —
        // раньше содержимого: тему своего типа контрол ищет, входя в дерево, и без словаря нашёл бы тему
        // базового типа у студии.
        _window.Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null)
        {
            Source = new Uri("avares://ArxisStudio.Surface.UiDesigner/Themes/UiDesignerTheme.axaml"),
        });
        _window.Content = _scene;
    }

    /// <summary>Карточка формы.</summary>
    public UiDesignerFormItem Item { get; }

    /// <summary>
    /// Ставит форму на карточку, раскладывает сцену и снимает область формы.
    /// </summary>
    /// <param name="root">Корень показа.</param>
    /// <param name="application">Приложение формы; null — его нет.</param>
    /// <param name="declared">Размер, объявленный формой; где его нет — размер рамки темы, как у вкладки.</param>
    /// <param name="pixels">Длинная сторона снимка.</param>
    /// <returns>Снимок и размер области; null — снимать нечего: форма упала на раскладке или пуста.</returns>
    public (byte[] Picture, Size Size)? Take(Control root, Application? application, FormRoot declared, int pixels)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(declared);

        Item.ApplicationThemeVariant = application is null ? ThemeVariant.Default : LiveFormDocument.VariantOf(application);
        Item.Width = declared.Width ?? SheetControls.LengthOf(_scene, "AxFormFrameWidth");
        Item.Height = declared.Height ?? SheetControls.LengthOf(_scene, "AxFormFrameHeight");
        Item.ApplicationRoot = application;
        Item.Root = root;

        // Места — с запасом под форму: карточка встаёт своим размером.
        var room = new Size(Item.Width * 2, Item.Height * 2);

        _scene.Measure(room);
        _scene.Arrange(new Rect(room));

        if (Item.FaultMessage is not null || Face() is not { } face || FormPicture.Take(face, root, pixels) is not { } picture)
            return null;

        return (picture, face.Bounds.Size);
    }

    /// <summary>Снимает с карточки корень и приложение: объекты поколения сцена не держит дольше снимка.</summary>
    public void Clear()
    {
        Item.Root = null;
        Item.ApplicationRoot = null;
    }

    /// <summary>Закрывает окно сцены: у непоказанного окна тоже есть окно платформы.</summary>
    public void Dispose()
    {
        Clear();
        _window.Content = null;
        _window.Close();
    }

    /// <summary>Одалживает корень службе, если он на карточке; иначе одалживать нечего.</summary>
    /// <param name="root">Корень сессии.</param>
    public IDisposable? Lend(object root) => ReferenceEquals(Item.Root, root) ? Item.SuspendRoot() : null;

    /// <summary>Область формы на карточке — тот же шаблонный кусок, что снимает вкладка.</summary>
    private Border? Face() =>
        Item.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "PART_FormBackground");
}
