using System.Globalization;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector.Editors;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ArxisStudio.Modules.Inspector.Metadata;

/// <summary>
/// Метаданные Avalonia: разделы, порядок, скрытое и ограничения её свойств — у неё самой атрибутов
/// <c>[Category]</c> нет.
/// </summary>
/// <remarks>
/// <para>
/// Разделы — по порядку, в котором их ищут: окно, общее, макет, кисти, вид, текст, преобразование, взаимодействие,
/// подсказка, прокрутка, доступность, затем присоединённые, которые читает родитель, дизайн, прочее и скрытые.
/// </para>
/// <para>
/// Скрыто то, что в инспекторе не правят: шаблон, стили, ресурсы, переходы, жесты, родитель, границы и состояния
/// ввода; имя и классы стоят в шапке. Дополнительное — то, что нужно редко: тема, обрезка, округление раскладки,
/// направление, эффект, маска, метка. Подсказка и имя для диктора видны всегда: о них забывают, а без них
/// интерфейс глух.
/// </para>
/// </remarks>
public sealed class AvaloniaMetadata : InspectorMetadata
{
    /// <summary>Разделы по порядку.</summary>
    private static readonly string[] Order =
    [
        "window", "common", "layout", "brush", "appearance", "text", "transform", "interaction", "tooltip", "scroll",
        "accessibility", "parent", "design",
    ];

    /// <summary>Свойства, у которых в словаре модуля есть пояснение.</summary>
    private static readonly string[] Described =
    [
        "Width", "Height", "Margin", "Padding", "HorizontalAlignment", "VerticalAlignment", "Opacity", "IsEnabled",
        "IsHitTestVisible", "Focusable", "ToolTip.Tip", "AutomationProperties.Name", "AutomationProperties.HelpText",
    ];

    /// <summary>Начертания по числу, без синонимов: <c>Regular</c> — то же, что <c>Normal</c>.</summary>
    private static readonly FontWeight[] Weights =
    [
        FontWeight.Thin, FontWeight.ExtraLight, FontWeight.Light, FontWeight.SemiLight, FontWeight.Normal,
        FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold, FontWeight.ExtraBold, FontWeight.Black,
        FontWeight.ExtraBlack,
    ];

    /// <inheritdoc/>
    public override void Describe(InspectorMetadataBuilder metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        for (var at = 0; at < Order.Length; at++)
            metadata.Category(Order[at]).Title(Context.Strings["category." + Order[at]]).Order(at * 10).Icon(IconOf(Order[at]));

        metadata.Category("hidden").Title(Context.Strings["category.hidden"]).Order(10_000).Collapsed().Icon(AxIcons.PreviewOff);

        DescribeElement(metadata);
        DescribeLayout(metadata);
        DescribeBrushesAndText(metadata);
        DescribeContent(metadata);
        DescribeWindow(metadata);
        DescribeAlignment(metadata);
        DescribeFontsAndCursor(metadata);
        DescribeIcons(metadata);
        Explain(metadata);
    }

    /// <summary>Значок раздела на его полосе — как значок компонента у Unity.</summary>
    private static Geometry IconOf(string category) => category switch
    {
        "window" => AxIcons.Window,
        "layout" => AxIcons.Grid,
        "brush" => AxIcons.Palette,
        "appearance" => AxIcons.Preview,
        "text" => AxIcons.Text,
        "transform" => AxIcons.Transform,
        "interaction" => AxIcons.Pointer,
        "tooltip" => AxIcons.Bubble,
        "scroll" => AxIcons.Move,
        "accessibility" => AxIcons.Accessibility,
        "parent" => AxIcons.Structure,
        "design" => AxIcons.Template,
        _ => AxIcons.Component,
    };

    /// <summary>
    /// Значки типов — шапке цели, у которой своего нет: окно, пользовательский элемент, панель, текст, картинка, кнопка;
    /// остальным контролам — общий значок элемента.
    /// </summary>
    private static void DescribeIcons(InspectorMetadataBuilder metadata)
    {
        metadata.For<Control>().Icon(AxIcons.Component);
        metadata.For<TopLevel>().Icon(AxIcons.Window);
        metadata.For<UserControl>().Icon(AxIcons.Template);
        metadata.For<Avalonia.Controls.Panel>().Icon(AxIcons.Grid);
        metadata.For<TextBlock>().Icon(AxIcons.Text);
        metadata.For<TextBox>().Icon(AxIcons.Text);
        metadata.For<Image>().Icon(AxIcons.Image);
        metadata.For<Button>().Icon(AxIcons.Pointer);
    }

    private static void DescribeElement(InspectorMetadataBuilder metadata)
    {
        // Имя и классы — в шапке; данные и привязки — отдельным разделом, когда он появится.
        metadata.For<StyledElement>()
            .Hide("Name", "Classes", "DataContext", "Styles", "Resources", "TemplatedParent", "Parent")
            .Property(StyledElement.ThemeProperty, property => property.Category("appearance").Advanced());

        metadata.For<Visual>()
            .Hide("Bounds", "HasMirrorTransform", "Transitions")
            .Category("appearance", "IsVisible", "Opacity")
            .Property(Visual.OpacityProperty, property => property.Range(0, 1).Step(0.01).Decimals(2).Editor(SliderEditor.Hint))
            .Property(Visual.ClipToBoundsProperty, property => property.Category("layout").Order(100).Advanced())
            .Property(Visual.ClipProperty, property => property.Category("appearance").Advanced())
            .Property(Visual.OpacityMaskProperty, property => property.Category("brush").Order(100).Advanced())
            .Property(Visual.EffectProperty, property => property.Category("appearance").Advanced())
            .Property(Visual.FlowDirectionProperty, property => property.Category("appearance").Advanced())
            .Property(Visual.ZIndexProperty, property => property.Category("layout").Order(110).Advanced())
            .Property(Visual.CacheModeProperty, property => property.Category("appearance").Advanced())
            .Category("transform", "RenderTransform", "RenderTransformOrigin");

        metadata.For<InputElement>()
            .Hide("IsPointerOver", "IsFocused", "IsKeyboardFocusWithin", "IsEffectivelyEnabled", "KeyBindings",
                  "GestureRecognizers", "Gestures", "InputScope", "TextInputOptions")
            .Category("interaction", "IsEnabled", "IsHitTestVisible", "Focusable", "IsTabStop", "TabIndex", "Cursor")

            // Наибольшее int у TabIndex — «порядка нет»: поле показывает «Авто», а не 2147483647 (запись 373).
            .Property(InputElement.TabIndexProperty, property => property.Auto());

        metadata.For<Control>()
            .Hide("FocusAdorner", "DataTemplates")
            // После своего у InputElement: равный порядок у разных владельцев перемешивал строки раздела (запись 373).
            .Property(Control.ContextMenuProperty, property => property.Category("interaction").Order(10))
            .Property(Control.ContextFlyoutProperty, property => property.Category("interaction").Order(11))
            .Property(Control.TagProperty, property => property.Category("other").Advanced())

            // Часть шаблона, которой фокус отдаёт контрол-хозяин, — дело автора шаблона, а не формы: у ребёнка
            // UserControl оно стояло разделом «В TemplatedControl» (запись 372).
            .Property(TemplatedControl.IsTemplateFocusTargetProperty, property => property.Hidden())
            .Property(ToolTip.TipProperty, property => property.Category("tooltip").Order(0).Always())
            .Property(ToolTip.PlacementProperty, property => property.Category("tooltip").Order(1).Advanced())
            .Property(ToolTip.ShowDelayProperty, property => property.Category("tooltip").Order(2).Advanced())
            .Property(AutomationProperties.NameProperty, property => property.Category("accessibility").Order(0).Always())
            .Property(AutomationProperties.HelpTextProperty, property => property.Category("accessibility").Order(1).Always())
            .Property(AutomationProperties.AutomationIdProperty, property => property.Category("accessibility").Order(2).Advanced())
            .Property(ScrollViewer.HorizontalScrollBarVisibilityProperty, property => property.Category("scroll").Order(0))
            .Property(ScrollViewer.VerticalScrollBarVisibilityProperty, property => property.Category("scroll").Order(1));
    }

    private static void DescribeLayout(InspectorMetadataBuilder metadata)
    {
        metadata.For<Layoutable>()
            .Category("layout", "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin",
                      "HorizontalAlignment", "VerticalAlignment")
            .Property(Layoutable.WidthProperty, property => property.Auto().Range(0, double.PositiveInfinity))
            .Property(Layoutable.HeightProperty, property => property.Auto().Range(0, double.PositiveInfinity))
            .Property(Layoutable.MinWidthProperty, property => property.Range(0, double.PositiveInfinity))
            .Property(Layoutable.MinHeightProperty, property => property.Range(0, double.PositiveInfinity))
            .Property(Layoutable.UseLayoutRoundingProperty, property => property.Category("layout").Order(120).Advanced())
            .Hide("DesiredSize", "IsMeasureValid", "IsArrangeValid");
    }

    private static void DescribeBrushesAndText(InspectorMetadataBuilder metadata)
    {
        metadata.For<TemplatedControl>()
            .Hide("Template")
            .Category("brush", "Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius")
            .Property(TemplatedControl.PaddingProperty, property => property.Category("layout").Order(20))
            .Category("text", "FontFamily", "FontSize", "FontWeight", "FontStyle", "FontStretch")
            .Property(TemplatedControl.FontSizeProperty, property => property.Range(1, 1000).Step(1))
            .Property(TemplatedControl.BorderThicknessProperty, property => property.Range(0, double.PositiveInfinity))
            .Property(TemplatedControl.CornerRadiusProperty, property => property.Range(0, double.PositiveInfinity));

        metadata.For<Border>()
            .Hide("Child")
            .Category("brush", "Background", "BorderBrush", "BorderThickness", "CornerRadius")
            .Property(Border.PaddingProperty, property => property.Category("layout").Order(20))
            .Property(Border.BoxShadowProperty, property => property.Category("appearance"));

        metadata.For<Avalonia.Controls.Panel>()
            .Hide("Children")
            .Property(Avalonia.Controls.Panel.BackgroundProperty, property => property.Category("brush"));

        metadata.For<TextBlock>()
            .Hide("Inlines", "TextLayout")
            .Property(TextBlock.TextProperty, property => property.Category("common").Order(0).Multiline())
            .Category("text", "FontFamily", "FontSize", "FontWeight", "FontStyle", "FontStretch", "TextAlignment",
                      "TextWrapping", "TextTrimming", "LineHeight", "LetterSpacing", "TextDecorations")
            .Property(TextBlock.FontSizeProperty, property => property.Range(1, 1000).Step(1))
            .Category("brush", "Background", "Foreground")
            .Property(TextBlock.PaddingProperty, property => property.Category("layout").Order(20));

        metadata.For<TextBox>()
            .Category("common", "Text", "PlaceholderText", "IsReadOnly", "AcceptsReturn", "MaxLength")
            .Property(TextBox.TextAlignmentProperty, property => property.Category("text").Order(5))
            .Property(TextBox.TextWrappingProperty, property => property.Category("text").Order(6))
            .Property(TextBox.CaretBrushProperty, property => property.Category("brush").Advanced())
            .Property(TextBox.SelectionBrushProperty, property => property.Category("brush").Advanced());
    }

    private static void DescribeContent(InspectorMetadataBuilder metadata)
    {
        metadata.For<ContentControl>()
            .Property(ContentControl.ContentProperty, property => property.Category("common").Order(0))
            .Hide("ContentTemplate", "Presenter")
            .Property(ContentControl.HorizontalContentAlignmentProperty, property => property.Category("layout").Order(30))
            .Property(ContentControl.VerticalContentAlignmentProperty, property => property.Category("layout").Order(31));

        metadata.For<StackPanel>()
            .Category("common", "Orientation", "Spacing");
    }

    private static void DescribeWindow(InspectorMetadataBuilder metadata)
    {
        metadata.For<Window>()
            .Category("window", "Title", "SizeToContent", "WindowStartupLocation", "CanResize", "ShowInTaskbar",
                      "Topmost", "WindowState", "SystemDecorations", "Icon")
            .Advanced("ExtendClientAreaToDecorationsHint", "ExtendClientAreaChromeHints", "ExtendClientAreaTitleBarHeightHint");

        metadata.For<TopLevel>()
            .Property(TopLevel.TransparencyLevelHintProperty, property => property.Category("window").Order(20).Advanced())
            .Hide("ClientSize", "FrameSize", "RenderScaling", "PlatformSettings", "ActualThemeVariant", "IsVisible");
    }

    /// <summary>
    /// Выравнивание — сегментами-значками в том порядке, в каком его ищут: к началу, по центру, к концу, растянуть.
    /// Подпись значка говорит диктору и подсказке, что он значит.
    /// </summary>
    private void DescribeAlignment(InspectorMetadataBuilder metadata)
    {
        InspectorChoice[] horizontal =
        [
            Choice("Left", "choice.left", AxIcons.AlignLeft),
            Choice("Center", "choice.center", AxIcons.AlignHorizontalCenter),
            Choice("Right", "choice.right", AxIcons.AlignRight),
            Choice("Stretch", "choice.stretch", AxIcons.StretchHorizontal),
        ];
        InspectorChoice[] vertical =
        [
            Choice("Top", "choice.top", AxIcons.AlignTop),
            Choice("Center", "choice.center", AxIcons.AlignVerticalCenter),
            Choice("Bottom", "choice.bottom", AxIcons.AlignBottom),
            Choice("Stretch", "choice.stretch", AxIcons.StretchVertical),
        ];

        metadata.For<Layoutable>()
            .Property(Layoutable.HorizontalAlignmentProperty, property => property.Choices(horizontal))
            .Property(Layoutable.VerticalAlignmentProperty, property => property.Choices(vertical));

        metadata.For<ContentControl>()
            .Property(ContentControl.HorizontalContentAlignmentProperty, property => property.Choices(horizontal))
            .Property(ContentControl.VerticalContentAlignmentProperty, property => property.Choices(vertical));
    }

    private InspectorChoice Choice(string value, string title, Avalonia.Media.Geometry icon) =>
        new(value) { Title = Context.Strings[title], Icon = icon };

    /// <summary>
    /// Шрифт — списком шрифтов системы (поле — <see cref="FontEditor"/>), набрать можно и свой; начертание — без
    /// синонимов, с числом (<c>Normal · 400</c>); курсор — одним из стандартных.
    /// </summary>
    private static void DescribeFontsAndCursor(InspectorMetadataBuilder metadata)
    {
        InspectorChoice[] weights =
        [
            .. Weights.Select(static weight => new InspectorChoice(weight.ToString())
            {
                Title = string.Create(CultureInfo.InvariantCulture, $"{weight} · {(int)weight}"),
            }),
        ];
        InspectorChoice[] cursors = [.. Enum.GetNames<StandardCursorType>().Select(static name => new InspectorChoice(name))];
        InspectorChoice[] fonts = [.. FontEditor.SystemFamilies().Select(static name => new InspectorChoice(name))];

        metadata.For<Control>()
            .Property("FontWeight", property => property.Choices(weights).ChoicesOnly())
            .Property("FontFamily", property => property.Choices(fonts));

        metadata.For<InputElement>()
            .Property(InputElement.CursorProperty, property => property.Choices(cursors).ChoicesOnly());
    }

    /// <summary>Пояснения — по-русски и по-английски из словаря модуля.</summary>
    private void Explain(InspectorMetadataBuilder metadata)
    {
        foreach (var id in Described)
        {
            var text = Context.Strings["property." + id];

            metadata.For<Control>().Property(id, property => property.Description(text));
        }
    }
}
