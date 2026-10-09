using System.Reflection;
using System.Runtime.CompilerServices;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector;
using ArxisStudio.Modules.Inspector.Catalog;
using ArxisStudio.Sdk;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Каталог вкладов инспектора: метаданные слоями, цепочка редактора от точного к общему, разделы для цели и уход
/// вкладов с выгрузкой хозяина (запись 362).
/// </summary>
/// <remarks>
/// Вклады-пробы — классы этой сборки, поданные каталогу набором руками: так видно каждое звено цепочки отдельно.
/// Дорогу настоящего плагина — сбор, шов, выгрузку — проходит последний тест.
/// </remarks>
[Collection(StudioStateCollection.Name)]
public class InspectorCatalogTests
{
    /// <summary>Слои сливаются по полю: плагин, назвавший свойству только диапазон, раздела модуля не теряет.</summary>
    [Fact]
    public void Layers_merge_field_by_field_and_a_plugin_tops_a_module()
    {
        var catalog = Catalog(metadata: [Module(new ModuleTable()), Plugin(new PluginTable())]);
        var width = Describe(catalog, typeof(Button), FakeTarget.Property("Width", typeof(double)));

        Assert.Equal("layout", width.Category);
        Assert.Equal("модуль", width.Description);
        Assert.Equal(0, width.Constraints.Minimum);
        Assert.Equal(500, width.Constraints.Maximum);

        var layout = catalog.Category("layout");

        Assert.Equal("Раскладка", layout.Title);
        Assert.Equal(5, layout.Order);
    }

    /// <summary>Внутри слоя сильнее описание более производного типа.</summary>
    [Fact]
    public void Within_a_layer_the_more_derived_type_wins()
    {
        var catalog = Catalog(metadata: [Module(new ModuleTable())]);
        var height = FakeTarget.Property("Height", typeof(double));

        Assert.Equal("button", Describe(catalog, typeof(Button), height).Category);
        Assert.Equal("control", Describe(catalog, typeof(Border), height).Category);
    }

    /// <summary>Таблица закрывает свойство для записи, но открыть не может: что источник не пишет, то не пишется.</summary>
    [Fact]
    public void A_table_can_close_a_property_for_writing_but_never_open_it()
    {
        var catalog = Catalog(metadata: [Module(new ModuleTable())]);

        Assert.True(Describe(catalog, typeof(Button), FakeTarget.Property("IsEnabled", typeof(bool)) with { IsReadOnly = true }).IsReadOnly);
        Assert.True(Describe(catalog, typeof(Button), FakeTarget.Property("Tag", typeof(object))).IsReadOnly);
    }

    /// <summary>
    /// Раздел с аргументом встаёт на место описанного и берёт его заголовок с аргументом; раздел автора, совпавший с
    /// описанным без учёта регистра, — он и есть.
    /// </summary>
    [Fact]
    public void A_category_with_an_argument_or_an_authors_spelling_is_the_described_one()
    {
        var catalog = Catalog(metadata: [Module(new CategoryTable())]);
        var parent = catalog.Category("parent/Grid");

        Assert.Equal("В Grid", parent.Title);
        Assert.Equal(110, parent.Order);
        Assert.Equal("layout", catalog.Category("Layout").Id);
        Assert.Equal("Behavior", catalog.Category("Behavior").Title);
    }

    /// <summary>Что таблица просит показывать всегда, каталог называет цели.</summary>
    [Fact]
    public void A_table_names_what_it_wants_shown_always()
    {
        var catalog = Catalog(metadata: [Module(new ModuleTable())]);

        Assert.Contains("ToolTip.Tip", catalog.Always(InspectorTypeInfo.Of(typeof(Button))));
        Assert.DoesNotContain("ToolTip.Tip", catalog.Always(new InspectorTypeInfo("Probe.Plain")));
    }

    /// <summary>
    /// Таблицу каталог спрашивает один раз, а заново — когда она попросила или сменился набор; и говорит об этом.
    /// </summary>
    [Fact]
    public void A_table_is_described_once_until_it_asks_or_the_set_changes()
    {
        var table = new ModuleTable();
        var set = new GivenSet<InspectorMetadata>([Module(table)]);
        var catalog = Catalog(metadataSet: set);
        var changes = 0;

        catalog.Changed += (_, _) => changes++;

        Describe(catalog, typeof(Button), FakeTarget.Property("Width", typeof(double)));
        Describe(catalog, typeof(Button), FakeTarget.Property("Height", typeof(double)));
        catalog.Category("layout");

        Assert.Equal(1, table.Described);

        table.Poke();
        Describe(catalog, typeof(Button), FakeTarget.Property("Width", typeof(double)));

        Assert.Equal(1, changes);
        Assert.Equal(2, table.Described);

        set.Raise();
        Describe(catalog, typeof(Button), FakeTarget.Property("Width", typeof(double)));

        Assert.Equal(2, changes);
        Assert.Equal(3, table.Described);
    }

    /// <summary>
    /// Редактор выбирается от точного к общему: подсказка, точный тип, обобщённое определение, базы, интерфейсы,
    /// текст.
    /// </summary>
    [Fact]
    public void The_editor_chain_goes_from_the_exact_type_to_the_general()
    {
        var brush = FakeTarget.Property("Background", typeof(SolidColorBrush));
        var editors = new List<IStudioContribution<PropertyEditor>>
        {
            Module<PropertyEditor>(new TextProbe()),
            Module<PropertyEditor>(new InterfaceProbe()),
            Module<PropertyEditor>(new BaseProbe()),
            Module<PropertyEditor>(new ExactProbe()),
            Module<PropertyEditor>(new HintProbe()),
        };

        Assert.IsType<HintProbe>(Chosen(editors, brush with { Editor = "probe" }));
        Assert.IsType<ExactProbe>(Chosen(editors, brush));

        editors.RemoveAll(static editor => Is<ExactProbe>(editor));
        Assert.IsType<BaseProbe>(Chosen(editors, brush));

        editors.RemoveAll(static editor => Is<BaseProbe>(editor));
        Assert.IsType<InterfaceProbe>(Chosen(editors, brush));

        editors.RemoveAll(static editor => Is<InterfaceProbe>(editor));
        Assert.IsType<TextProbe>(Chosen(editors, brush));

        Assert.IsType<GenericProbe>(Chosen(
            [Module<PropertyEditor>(new TextProbe()), Module<PropertyEditor>(new GenericProbe())],
            FakeTarget.Property("Items", typeof(List<int>))));
    }

    /// <summary>
    /// Свойство, которое не пишется, получает редактор «только чтение», а не поле своего типа: поле, в которое
    /// нельзя писать, не должно выглядеть полем.
    /// </summary>
    [Fact]
    public void A_read_only_property_takes_the_read_only_editor_before_a_typed_one()
    {
        var editors = new List<IStudioContribution<PropertyEditor>>
        {
            Module<PropertyEditor>(new ExactProbe()),
            Module<PropertyEditor>(new ReadOnlyProbe()),
        };

        Assert.IsType<ReadOnlyProbe>(Chosen(editors, FakeTarget.Property("Background", typeof(SolidColorBrush)) with { IsReadOnly = true }));
        Assert.IsType<ReadOnlyProbe>(Chosen(editors, FakeTarget.Property("Background", typeof(SolidColorBrush)) with { AcceptsText = false }));
    }

    /// <summary>
    /// На одной ступени не-запасной сильнее запасного, а среди равных плагин сильнее модуля: свою настройку плагин
    /// ставит поверх студийной.
    /// </summary>
    [Fact]
    public void On_one_step_a_proper_editor_beats_a_fallback_and_a_plugin_beats_a_module()
    {
        var brush = FakeTarget.Property("Background", typeof(SolidColorBrush));

        Assert.IsType<ExactProbe>(Chosen([Plugin<PropertyEditor>(new ExactFallbackProbe()), Module<PropertyEditor>(new ExactProbe())], brush));

        var chosen = Choose([Module<PropertyEditor>(new ExactProbe()), Plugin<PropertyEditor>(new ExactProbe())], brush);

        Assert.False(chosen?.Editor.IsBuiltIn, "на равной ступени модуль взял верх над плагином");
    }

    /// <summary>Редактор, отказавшийся от свойства, уступает следующему.</summary>
    [Fact]
    public void An_editor_that_declines_gives_way_to_the_next()
    {
        var brush = FakeTarget.Property("Background", typeof(SolidColorBrush));

        Assert.IsType<BaseProbe>(Chosen([Module<PropertyEditor>(new ExactProbe { Declines = true }), Module<PropertyEditor>(new BaseProbe())], brush));
    }

    /// <summary>Раздел встаёт у цели своего типа — и у наследников, если просил, — и только когда согласился.</summary>
    [Fact]
    public void A_section_goes_to_targets_of_its_type_when_it_agrees()
    {
        var catalog = Catalog(sections: [Module<InspectorSection>(new ButtonSection()), Module<InspectorSection>(new ContentSection())]);
        var content = new[] { FakeTarget.Property("Content", typeof(object)) };

        Assert.Equal(
            new[] { "Кнопка", "Содержимое" },
            catalog.SectionsFor(InspectorTypeInfo.Of(typeof(Button)), content).Select(Title));
        Assert.Equal(new[] { "Содержимое" }, catalog.SectionsFor(InspectorTypeInfo.Of(typeof(ContentControl)), content).Select(Title));
        Assert.Equal(new[] { "Содержимое" }, catalog.SectionsFor(InspectorTypeInfo.Of(typeof(RepeatButton)), content).Select(Title));
        Assert.Empty(catalog.SectionsFor(InspectorTypeInfo.Of(typeof(ContentControl)), []));
        Assert.Empty(catalog.SectionsFor(InspectorTypeInfo.Of(typeof(Border)), content));
    }

    /// <summary>
    /// Выгруженный плагин уносит свой редактор и свою таблицу: каталог говорит о перемене, свойство встаёт на
    /// редактор модуля, а контекст плагина уходит из процесса.
    /// </summary>
    [AvaloniaFact]
    public void An_unloaded_plugin_takes_its_editor_and_its_table_away()
    {
        using var studio = new InspectorStudio();
        var changes = 0;

        studio.Catalog.Changed += (_, _) => changes++;

        var context = RaiseAndDrop(studio);
        var kind = Kind();

        Assert.True(changes > 0, "каталог не сказал, что вклады ушли");
        Assert.True(studio.Catalog.EditorFor(kind)?.Editor.IsBuiltIn, "свойство не вернулось к редактору модуля");
        Assert.Null(studio.Catalog.Describe(new InspectorTypeInfo("Probe.Thing"), [kind])[0].Category);

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "каталог держит контекст выгруженного плагина");
    }

    /// <summary>Поднимает плагин с редактором и таблицей, проверяет, что каталог их видит, и снимает плагин.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RaiseAndDrop(InspectorStudio studio)
    {
        TestAssembly.EmitPlugin(studio.Plugins, "probe.kinds", "Probe.Kinds", source: """
            using ArxisStudio.Inspector;
            using ArxisStudio.Sdk;
            using Avalonia.Controls;

            namespace Probe.Kinds;

            public sealed class Entry : StudioPlugin
            {
            }

            [PropertyEditor("Probe.Kind")]
            public sealed class KindEditor : PropertyEditor
            {
                public override InspectorField CreateField(InspectorFieldContext context) => new KindField();
            }

            public sealed class KindField : InspectorField
            {
                public override Control View { get; } = new TextBlock();

                public override void Show(InspectorValue value)
                {
                }
            }

            public sealed class KindTable : InspectorMetadata
            {
                public override void Describe(InspectorMetadataBuilder metadata) =>
                    metadata.For("Probe.Thing").Property("Kind", property => property.Category("probe"));
            }
            """);

        studio.Raise();

        var kind = Kind();

        Assert.Equal("probe.kinds", studio.Catalog.EditorFor(kind)?.Editor.OwnerId);
        Assert.Equal("probe", studio.Catalog.Describe(new InspectorTypeInfo("Probe.Thing"), [kind])[0].Category);

        var context = new WeakReference(studio.Host.Loaded.Single(plugin => plugin.Installed.Id == "probe.kinds").Context);

        Assert.True(studio.Host.Drop("probe.kinds"));

        return context;
    }

    private static InspectorProperty Kind() => new("Kind", new InspectorTypeInfo("Probe.Kind"));

    private static InspectorCatalog Catalog(
        IReadOnlyList<IStudioContribution<PropertyEditor>>? editors = null,
        IReadOnlyList<IStudioContribution<InspectorSection>>? sections = null,
        IReadOnlyList<IStudioContribution<InspectorMetadata>>? metadata = null,
        GivenSet<InspectorMetadata>? metadataSet = null) =>
        new(new GivenContributions(
                new GivenSet<PropertyEditor>(editors ?? []),
                new GivenSet<InspectorSection>(sections ?? []),
                metadataSet ?? new GivenSet<InspectorMetadata>(metadata ?? [])),
            id => "«" + id + "»");

    private static InspectorProperty Describe(InspectorCatalog catalog, Type type, InspectorProperty property) =>
        Assert.Single(catalog.Describe(InspectorTypeInfo.Of(type), [property]));

    private static ChosenEditor? Choose(IReadOnlyList<IStudioContribution<PropertyEditor>> editors, InspectorProperty property) =>
        Catalog(editors: editors).EditorFor(property);

    private static PropertyEditor? Chosen(IReadOnlyList<IStudioContribution<PropertyEditor>> editors, InspectorProperty property) =>
        Choose(editors, property) is { Editor: Given<PropertyEditor> given } ? given.Value : null;

    private static bool Is<T>(IStudioContribution<PropertyEditor> editor) => ((Given<PropertyEditor>)editor).Value is T;

    private static string Title(IStudioContribution<InspectorSection> section) =>
        ((Given<InspectorSection>)section).Value.Title;

    private static Given<T> Module<T>(T value) where T : StudioContribution => new(value, builtIn: true);

    private static Given<T> Plugin<T>(T value) where T : StudioContribution => new(value, builtIn: false);

    private static Given<InspectorMetadata> Module(InspectorMetadata table) => new(table, builtIn: true);

    private static Given<InspectorMetadata> Plugin(InspectorMetadata table) => new(table, builtIn: false);

    /// <summary>Таблица модуля-пробы: разделы, производный тип, закрытие для записи, «всегда».</summary>
    public sealed class ModuleTable : InspectorMetadata
    {
        /// <summary>Сколько раз её спросили.</summary>
        public int Described { get; private set; }

        /// <inheritdoc/>
        public override void Describe(InspectorMetadataBuilder metadata)
        {
            Described++;

            metadata.Category("layout").Title("Макет").Order(5);
            metadata.For<Control>()
                .Property("Width", static property => property.Category("layout").Range(0, 100).Description("модуль"))
                .Property("Height", static property => property.Category("control"))
                .Property("IsEnabled", static property => property.ReadOnly(false))
                .Property("Tag", static property => property.ReadOnly())
                .Property("ToolTip.Tip", static property => property.Always());
            metadata.For<Button>().Property("Height", static property => property.Category("button"));
        }

        /// <summary>Просит каталог спросить её заново.</summary>
        public void Poke() => Invalidate();
    }

    /// <summary>Таблица разделов: макет и раздел родителя с аргументом.</summary>
    public sealed class CategoryTable : InspectorMetadata
    {
        /// <inheritdoc/>
        public override void Describe(InspectorMetadataBuilder metadata)
        {
            metadata.Category("layout").Title("Макет").Order(20);
            metadata.Category("parent").Title("В {0}").Order(110);
        }
    }

    /// <summary>Таблица плагина-пробы: только диапазон и заголовок раздела.</summary>
    public sealed class PluginTable : InspectorMetadata
    {
        /// <inheritdoc/>
        public override void Describe(InspectorMetadataBuilder metadata)
        {
            metadata.Category("layout").Title("Раскладка");
            metadata.For<Control>().Property("Width", static property => property.Range(0, 500));
        }
    }

    /// <summary>Редактор-проба: пустое поле; может отказаться от свойства.</summary>
    public abstract class ProbeEditor : PropertyEditor
    {
        /// <summary>Отказывается от всякого свойства.</summary>
        public bool Declines { get; init; }

        /// <inheritdoc/>
        public override bool Accepts(InspectorProperty property) => !Declines;

        /// <inheritdoc/>
        public override InspectorField CreateField(InspectorFieldContext context) => new Blank();

        private sealed class Blank : InspectorField
        {
            public override Control View { get; } = new TextBlock();

            public override void Show(InspectorValue value)
            {
            }
        }
    }

    /// <summary>По подсказке.</summary>
    [PropertyEditor(typeof(object), Hint = "probe")]
    public sealed class HintProbe : ProbeEditor
    {
    }

    /// <summary>Точный тип.</summary>
    [PropertyEditor(typeof(SolidColorBrush))]
    public sealed class ExactProbe : ProbeEditor
    {
    }

    /// <summary>Точный тип, запасной.</summary>
    [PropertyEditor(typeof(SolidColorBrush), Fallback = true)]
    public sealed class ExactFallbackProbe : ProbeEditor
    {
    }

    /// <summary>База и наследники.</summary>
    [PropertyEditor(typeof(Brush), ForDerived = true)]
    public sealed class BaseProbe : ProbeEditor
    {
    }

    /// <summary>Интерфейс и наследники.</summary>
    [PropertyEditor(typeof(IBrush), ForDerived = true)]
    public sealed class InterfaceProbe : ProbeEditor
    {
    }

    /// <summary>Обобщённое определение.</summary>
    [PropertyEditor("System.Collections.Generic.List`1")]
    public sealed class GenericProbe : ProbeEditor
    {
    }

    /// <summary>Текст — последняя ступень для того, что пишется текстом.</summary>
    [PropertyEditor(typeof(object), Hint = InspectorCatalog.TextHint, Fallback = true)]
    public sealed class TextProbe : ProbeEditor
    {
    }

    /// <summary>Только чтение.</summary>
    [PropertyEditor(typeof(object), Hint = InspectorCatalog.ReadOnlyHint, Fallback = true)]
    public sealed class ReadOnlyProbe : ProbeEditor
    {
    }

    /// <summary>Раздел-проба.</summary>
    public abstract class ProbeSection : InspectorSection
    {
        /// <inheritdoc/>
        public override InspectorSectionView CreateView(InspectorSectionContext context) => throw new NotSupportedException();
    }

    /// <summary>Раздел кнопки — только её самой.</summary>
    [InspectorSection(typeof(Button))]
    public sealed class ButtonSection : ProbeSection
    {
        /// <inheritdoc/>
        public override string Title => "Кнопка";
    }

    /// <summary>Раздел содержимого — у наследников тоже, и только у цели с содержимым.</summary>
    [InspectorSection(typeof(ContentControl), ForDerived = true)]
    public sealed class ContentSection : ProbeSection
    {
        /// <inheritdoc/>
        public override string Title => "Содержимое";

        /// <inheritdoc/>
        public override bool Accepts(InspectorTypeInfo type, IReadOnlyList<InspectorProperty> properties) =>
            properties.Any(static property => property.Id == "Content");
    }
}

/// <summary>Вклад, поданный каталогу руками: зовётся напрямую, атрибуты читаются с его класса.</summary>
/// <param name="value">Вклад.</param>
/// <param name="builtIn">Вклад модуля, а не плагина.</param>
internal sealed class Given<T>(T value, bool builtIn) : IStudioContribution<T>
    where T : StudioContribution
{
    private readonly StudioInspector.DirectGuard<T> _direct = new(value);

    public T Value => value;

    public bool IsBuiltIn => builtIn;

    public string OwnerId => builtIn ? "arxis.inspector" : "probe.plugin";

    public bool IsReleased => false;

    public event EventHandler? Released
    {
        add { }
        remove { }
    }

    public IReadOnlyList<TAttribute> Attributes<TAttribute>() where TAttribute : Attribute =>
        value.GetType().GetCustomAttributes<TAttribute>(inherit: true).ToArray();

    public bool Run(string what, Action<T> call) => _direct.Run(what, call);

    public bool TryGet<TResult>(string what, Func<T, TResult> call, out TResult? result) => _direct.TryGet(what, call, out result);

    public Task<bool> RunAsync(string what, Func<T, Task> call) => _direct.RunAsync(what, call);

    public bool Farewell(string what, Action<T> call) => _direct.Farewell(what, call);

    public IStudioGuarded<TOther> Guard<TOther>(TOther other) where TOther : class => _direct.Guard(other);

    public Control Host(Control view) => view;
}

/// <summary>Набор вкладов, поданный руками; перемену объявляет тест.</summary>
/// <param name="items">Вклады.</param>
internal sealed class GivenSet<T>(IReadOnlyList<IStudioContribution<T>> items) : IStudioContributionSet<T>
    where T : StudioContribution
{
    public IReadOnlyList<IStudioContribution<T>> Items => items;

    public event EventHandler? Changed;

    /// <summary>Объявляет перемену состава.</summary>
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Сбор вкладов, отдающий поданные руками наборы.</summary>
internal sealed class GivenContributions(
    GivenSet<PropertyEditor> editors, GivenSet<InspectorSection> sections, GivenSet<InspectorMetadata> metadata) : IStudioContributions
{
    public IStudioContributionSet<T> Collect<T>() where T : StudioContribution
    {
        object set = typeof(T) == typeof(PropertyEditor) ? editors
            : typeof(T) == typeof(InspectorSection) ? sections
            : typeof(T) == typeof(InspectorMetadata) ? metadata
            : new GivenSet<T>([]);

        return (IStudioContributionSet<T>)set;
    }

    public IStudioGuarded<T> Guard<T>(T foreign) where T : class => new StudioInspector.DirectGuard<T>(foreign);
}
