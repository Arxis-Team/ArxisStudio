using ArxisStudio.Markup.Xaml;
using ArxisStudio.Modules.UiDesigner.Inspection;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Черновик привязки панели (запись 425): источник читается из обеих записей Avalonia, а пишется так, что меняется
/// только тронутое.
/// </summary>
public class BindingDraftTests
{
    /// <summary>Источник и путь — из короткой записи в начале пути и из длинной аргументами; уровень предка — с единицы.</summary>
    [Theory]
    [InlineData("{Binding Customer.Name}", "Data", null, null, 1, "Customer.Name")]
    [InlineData("{Binding}", "Data", null, null, 1, "")]
    [InlineData("{Binding #slider.Value}", "Element", "slider", null, 1, "Value")]
    [InlineData("{Binding Value, ElementName=slider}", "Element", "slider", null, 1, "Value")]
    [InlineData("{Binding $self.Width}", "Self", null, null, 1, "Width")]
    [InlineData("{Binding Width, RelativeSource={RelativeSource Self}}", "Self", null, null, 1, "Width")]
    [InlineData("{Binding $parent[Window].Title}", "Ancestor", null, "Window", 1, "Title")]
    [InlineData("{Binding $parent[Border;1].Tag}", "Ancestor", null, "Border", 2, "Tag")]
    [InlineData("{Binding Tag, RelativeSource={RelativeSource AncestorType=Border, AncestorLevel=2}}", "Ancestor", null, "Border", 2, "Tag")]
    [InlineData("{Binding $parent.Tag}", "Ancestor", null, null, 1, "Tag")]
    public void A_written_binding_reads_into_its_source_and_path(
        string written, string source, string? element, string? ancestor, int level, string path)
    {
        var draft = BindingDraft.Read(Binding(written));

        Assert.Equal(Enum.Parse<BindingSourceKind>(source), draft.Source);
        Assert.Equal(element, draft.ElementName);
        Assert.Equal(ancestor, draft.AncestorType);
        Assert.Equal(level, draft.AncestorLevel);
        Assert.Equal(path, draft.Path);
    }

    /// <summary>Тронутый аргумент — заново, остальное — текстом документа: и конвертер, и непонятный панели аргумент.</summary>
    [Fact]
    public void Only_what_was_touched_is_written_anew()
    {
        var original = Binding("{Binding Name,  Converter={StaticResource Upper}, FancyOption=1}");
        var draft = BindingDraft.Read(original).With("Mode", "TwoWay");

        Assert.Equal("{Binding Name,  Converter={StaticResource Upper}, FancyOption=1, Mode=TwoWay}", draft.Write(original));
        Assert.Equal("{Binding Title,  Converter={StaticResource Upper}, FancyOption=1}", (BindingDraft.Read(original) with { Path = "Title" }).Write(original));

        // Значение, которое документ взял в кавычки без нужды, панель не трогала — оно остаётся, как написано.
        var quoted = Binding("{Binding Name, FallbackValue='нет'}");

        Assert.Equal("{Binding Name, FallbackValue='нет', Mode=OneWay}", BindingDraft.Read(quoted).With("Mode", "OneWay").Write(quoted));
    }

    /// <summary>Сменённый источник уходит в путь короткой записью, а длинная запись прежнего источника снимается.</summary>
    [Fact]
    public void A_new_source_is_written_into_the_path()
    {
        var original = Binding("{Binding Value, ElementName=slider, Mode=OneWay}");
        var self = BindingDraft.Read(original) with { Source = BindingSourceKind.Self, Path = "Width" };

        Assert.Equal("{Binding $self.Width, Mode=OneWay}", self.Write(original));

        var ancestor = new BindingDraft { Source = BindingSourceKind.Ancestor, AncestorType = "Border", AncestorLevel = 2, Path = "Tag" };

        Assert.Equal("{Binding $parent[Border;1].Tag}", ancestor.Write(null));
    }

    /// <summary>Тот же источник, записанный длинно, — остаётся длинным: меняется только путь, без приставки.</summary>
    [Fact]
    public void A_long_source_kept_takes_the_path_without_a_prefix()
    {
        var original = Binding("{Binding Value, ElementName=slider}");

        Assert.Equal("{Binding Minimum, ElementName=slider}", (BindingDraft.Read(original) with { Path = "Minimum" }).Write(original));
    }

    /// <summary>Путь, написанный именем аргумента, остаётся им; формат с фигурной скобкой — в кавычках с экранированием.</summary>
    [Fact]
    public void A_named_path_stays_named_and_a_format_is_escaped()
    {
        var original = Binding("{Binding Path=Price}");
        var draft = BindingDraft.Read(original) with { Path = "Total" };

        Assert.Equal("{Binding Path=Total, StringFormat='{}{0:N2}'}", draft.With("StringFormat", "{0:N2}").Write(original));
        Assert.Equal("{0:N2}", BindingDraft.Read(Binding("{Binding Price, StringFormat='{}{0:N2}'}")).Value("StringFormat"));
    }

    /// <summary>Конвертер выражением — выражением; снятое значение уходит со своей запятой; имя расширения сменить можно.</summary>
    [Fact]
    public void A_converter_is_an_expression_and_a_removed_option_takes_its_separator()
    {
        var original = Binding("{Binding IsBusy, Mode=OneWay}");
        var draft = BindingDraft.Read(original).With("Converter", "{x:Static BoolConverters.Not}").With("Mode", null);

        Assert.Equal("{Binding IsBusy, Converter={x:Static BoolConverters.Not}}", draft.Write(original));
        Assert.Equal("{ReflectionBinding IsBusy}", BindingDraft.Read(original).With("Mode", null).Write(original, "ReflectionBinding"));
    }

    /// <summary>
    /// Точка в конце пути — шаг, которого ещё не сделали: список уже показывает члены, а запись её не берёт — Avalonia
    /// такой путь не прочтёт.
    /// </summary>
    [Fact]
    public void A_trailing_dot_is_a_step_not_yet_taken()
    {
        Assert.Equal("{Binding Customer}", new BindingDraft { Path = "Customer." }.Write(null));
        Assert.Equal("{Binding #slider}", new BindingDraft { Source = BindingSourceKind.Element, ElementName = "slider", Path = "." }.Write(null));

        var original = Binding("{Binding Value, ElementName=slider}");

        Assert.Equal("{Binding Minimum, ElementName=slider}", (BindingDraft.Read(original) with { Path = "Minimum." }).Write(original));
    }

    /// <summary>Вопрос службе о следующем шаге пути — привязка до последней точки, со своим источником.</summary>
    [Fact]
    public void A_probe_is_the_binding_up_to_the_step_not_yet_taken()
    {
        Assert.Equal("{Binding}", new BindingDraft().Probe(string.Empty));
        Assert.Equal("{Binding Customer}", new BindingDraft().Probe("Customer"));
        Assert.Equal("{Binding #slider}", new BindingDraft { Source = BindingSourceKind.Element, ElementName = "slider" }.Probe(string.Empty));
        Assert.Equal("{Binding $parent[Window].Content}", new BindingDraft { Source = BindingSourceKind.Ancestor, AncestorType = "Window" }.Probe("Content"));
    }

    /// <summary>
    /// Источники по имени — контролы: с именем — первыми, без имени — со свободным именем, которого нет ни в документе,
    /// ни среди членов класса корня; шаблон, его элементы и сам привязываемый не предлагаются, а предки — ближайший
    /// первым, и окно.
    /// </summary>
    [Fact]
    public void Elements_offer_their_names_and_a_free_one_for_the_unnamed()
    {
        var document = XamlDocument.Parse("""
            <UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:App">
              <UserControl.DataContext>
                <vm:Model x:Name="stackPanel" />
              </UserControl.DataContext>
              <StackPanel>
                <Slider x:Name="slider" />
                <Slider />
                <Border />
                <TextBlock />
                <ContentControl>
                  <ContentControl.ContentTemplate>
                    <DataTemplate>
                      <Slider />
                    </DataTemplate>
                  </ContentControl.ContentTemplate>
                </ContentControl>
              </StackPanel>
            </UserControl>
            """);
        var bound = XamlElementPath.Parse("/0/3").Resolve(document)!;

        // Модель в данных — не контрол: её не предлагают, а имя её занято.
        var choices = BindingSources.Elements(document, bound, ["border"], at => at.Resolve(document)?.Name.LocalName != "Model");

        Assert.Equal(
            ["slider", "userControl", "stackPanel2", "slider2", "border2", "contentControl"],
            choices.Select(static choice => choice.Name ?? choice.NewName));
        Assert.Equal(["StackPanel", "UserControl", "Window"], BindingSources.Ancestors(bound));
    }

    /// <summary>Значение с обеими кавычками в привязку не записать: запись отказывает, а не пишет нечитаемое.</summary>
    [Fact]
    public void A_value_with_both_quotes_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new BindingDraft { Path = "Name" }.With("FallbackValue", "it's \"x\"").Write(null));
    }

    private static XamlMarkupExtensionValue Binding(string text) => Assert.IsType<XamlMarkupExtensionValue>(XamlValue.Parse(text));
}
