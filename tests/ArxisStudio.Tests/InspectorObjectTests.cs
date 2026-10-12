using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Shell;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Объект в инспекторе (<see cref="IStudioInspector.Inspect"/>, контракт 1.1): свойства CLR, атрибуты .NET, запись
/// текстом, перемены по <see cref="INotifyPropertyChanged"/> и уход с выгрузкой хозяина (запись 365).
/// </summary>
/// <remarks>Выгрузка контекста плагина опирается на сборщик мусора — общий на процесс.</remarks>
[Collection(StudioStateCollection.Name)]
public class InspectorObjectTests
{
    /// <summary>
    /// Свойства объекта — открытые, что читаются, а подпись, раздел, пояснение, скрытость, дополнительность, только
    /// чтение, границы и значение по умолчанию — атрибутами .NET.
    /// </summary>
    [AvaloniaFact]
    public void An_inspected_object_shows_its_properties_with_their_dotnet_attributes()
    {
        var service = new StudioInspector(contributions: null);

        using var shown = service.Inspect(new Settings(), "Настройки");

        var target = Assert.IsAssignableFrom<InspectorTarget>(service.Current);

        Assert.Equal("Настройки", target.Header.Title);
        Assert.Equal(nameof(Settings), target.Header.Subtitle);
        Assert.Equal(typeof(Settings).FullName, target.Header.Detail);

        var greeting = Property(target, "Greeting");

        Assert.Equal("Приветствие", greeting.DisplayName);
        Assert.Equal("Common", greeting.Category);
        Assert.Equal("Что панель говорит первым", greeting.Description);

        var repeat = Property(target, "Repeat");

        Assert.Equal(1, repeat.Constraints.Minimum);
        Assert.Equal(10, repeat.Constraints.Maximum);
        Assert.Equal("3", repeat.DefaultText);
        Assert.True(Property(target, "Secret").IsHidden, "[Browsable(false)] не скрыл свойство");
        Assert.True(Property(target, "Tuning").IsAdvanced, "[EditorBrowsable(Advanced)] не сделал свойство дополнительным");
        Assert.True(Property(target, "Locked").IsReadOnly, "[ReadOnly(true)] не закрыл свойство");
        Assert.True(Property(target, "Count").IsReadOnly, "свойство без записи пишется");
        Assert.False(Property(target, "Accent").IsReadOnly, "цвет Avalonia не пишется текстом");
    }

    /// <summary>
    /// Значение — текстом, равное значению по умолчанию — незаданное; запись читает текст типом свойства, сброс
    /// возвращает значение по умолчанию, а текст, который не читается, не пишется.
    /// </summary>
    [AvaloniaFact]
    public async Task Values_are_written_as_text_and_a_reset_brings_the_default_back()
    {
        var service = new StudioInspector(contributions: null);
        var settings = new Settings();

        using var shown = service.Inspect(settings, "Настройки");

        var target = service.Current!;

        Assert.Equal("3", target.ValueOf("Repeat").Text);
        Assert.False(target.ValueOf("Repeat").IsWritten, "значение по умолчанию помечено заданным");

        Assert.True(await target.WriteAsync([new InspectorWrite("Repeat", "5")]));
        Assert.Equal(5, settings.Repeat);
        Assert.True(target.ValueOf("Repeat").IsWritten, "изменённое значение не помечено заданным");

        Assert.False(target.Check("Repeat", "много").IsValid);
        Assert.False(await target.WriteAsync([new InspectorWrite("Repeat", "много")]));
        Assert.Equal(5, settings.Repeat);

        Assert.True(await target.WriteAsync([new InspectorWrite("Repeat", null)]));
        Assert.Equal(3, settings.Repeat);

        Assert.True(await target.WriteAsync([new InspectorWrite("Accent", "#FF0000"), new InspectorWrite("Mood", "Bold")]));
        Assert.Equal(Colors.Red, settings.Accent);
        Assert.Equal(Mood.Bold, settings.Mood);

        Assert.False(await target.WriteAsync([new InspectorWrite("Locked", "другое")]));
        Assert.Equal("закрыто", settings.Locked);
    }

    /// <summary>
    /// Перемену объект объявляет сам — инспектор слышит её; отпущенная ручка показа закрывает цель и отписывается
    /// от объекта.
    /// </summary>
    [AvaloniaFact]
    public void A_change_the_object_announces_reaches_the_inspector_and_releasing_unsubscribes()
    {
        var service = new StudioInspector(contributions: null);
        var settings = new Settings();
        var shown = service.Inspect(settings, "Настройки");
        var target = service.Current!;
        var changes = new List<InspectorChangedEventArgs>();

        target.Changed += (_, e) => changes.Add(e);
        settings.Greeting = "Привет";

        Assert.Contains(changes, change => change.Touches("Greeting") && !change.Touches("Repeat"));
        Assert.Equal(1, settings.Subscribers);

        shown.Dispose();

        Assert.Null(service.Current);
        Assert.True(target.IsClosed, "отпущенная цель-объект не закрылась");
        Assert.Equal(0, settings.Subscribers);
    }

    /// <summary>
    /// Панель показывает объект теми же строками, что и элемент формы: подпись из атрибута, число с границами — и
    /// пишет набранное в объект.
    /// </summary>
    [AvaloniaFact]
    public void The_panel_shows_an_object_and_writes_what_is_typed_into_it()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var settings = new Settings();

        using var shown = studio.Service.Inspect(settings, "Настройки");

        studio.Frame();

        Assert.Equal("Настройки", panel.View!.Title.Text);

        var greeting = Assert.Single(panel.List!.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "Greeting");

        Assert.Equal("Приветствие", greeting.Row.Property.Label);

        var repeat = Assert.Single(panel.List.Items.OfType<PropertyCell>(), cell => cell.Row.Property.Id == "Repeat");

        // [Range] — ползунок с полем, как у Unity (запись 378).
        var range = Assert.IsType<AxRangeField>(Assert.IsType<PluginSurface>(repeat.Row.Content).Child);
        var field = range.NumberField;

        Assert.Equal(10, range.Maximum);

        field.Focus();
        field.SelectAll();
        studio.Window.KeyTextInput("7");
        studio.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, string.Empty);
        studio.Frame();

        Assert.Equal(7, settings.Repeat);
        Assert.True(repeat.Row.IsOverridden, "изменённое значение объекта не помечено заданным");
    }

    /// <summary>
    /// Объект выгруженного плагина уходит из инспектора, даже если плагин не отпустил ручку, и не держит его контекст
    /// загрузки — ни служба, ни открытая панель.
    /// </summary>
    [AvaloniaFact]
    public void An_object_of_an_unloaded_plugin_leaves_and_does_not_hold_its_context()
    {
        using var studio = new InspectorStudio();
        var panel = studio.Panel();
        var context = InspectFromPluginAndDrop(studio);

        studio.Frame();

        Assert.Null(studio.Service.Current);
        Assert.Empty(panel.List!.Items.OfType<PropertyCell>());

        for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "объект выгруженного плагина держит его контекст загрузки");
    }

    /// <summary>
    /// Текст значения и сравнение с умолчанием — код объекта плагина: их сбой — на счёт плагина, а не инспектора.
    /// </summary>
    /// <remarks>
    /// <c>ToString</c> и <c>Equals</c> своего типа плагин вправе переопределить, а звал их инспектор мимо шва объекта:
    /// бросивший <c>ToString</c> записывался на счёт модуля инспектора, и третий подряд отключал его (запись 395).
    /// </remarks>
    [AvaloniaFact]
    public void Text_and_equality_of_a_plugin_value_are_charged_to_the_plugin()
    {
        using var studio = new InspectorStudio();
        var failures = new List<PluginFailure>();

        studio.Guard.Failed += (_, failure) => failures.Add(failure);

        TestAssembly.EmitPlugin(studio.Plugins, "probe.touchy", "Probe.Touchy", source: """
            using ArxisStudio.Inspector;
            using ArxisStudio.Sdk;

            namespace Probe.Touchy;

            public sealed class Grumpy
            {
                public override string ToString() => throw new System.InvalidOperationException("не скажу");
            }

            public sealed class Entry : StudioPlugin
            {
                public override void Activate(IStudioContext context) => context.Inspector()!.Inspect(new Options(), "Плагин");
            }

            public sealed class Options
            {
                public Grumpy Mood { get; set; } = new();
            }
            """);

        studio.Raise();

        var target = studio.Service.Current!;

        Assert.Null(Record.Exception(() => target.ValueOf("Mood")));
        Assert.Contains(failures, failure => failure.PluginId == "probe.touchy");
        Assert.DoesNotContain(failures, failure => failure.PluginId == studio.Module.Installed.Id);
    }

    private static InspectorProperty Property(InspectorTarget target, string id) =>
        Assert.Single(target.Properties, property => property.Id == id);

    /// <summary>Поднимает плагин, который показывает свои настройки и ручку не отпускает, и снимает его.</summary>
    /// <remarks>Не встраиваемый: запись плагина в кадре вызывающего держала бы контекст живым.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference InspectFromPluginAndDrop(InspectorStudio studio)
    {
        TestAssembly.EmitPlugin(studio.Plugins, "probe.settings", "Probe.Settings", source: """
            using System.ComponentModel;
            using ArxisStudio.Inspector;
            using ArxisStudio.Sdk;

            namespace Probe.Settings;

            public enum Shade { Light, Dark }

            public readonly record struct Spot(int X, int Y);

            public sealed class Entry : StudioPlugin
            {
                public override void Activate(IStudioContext context) => context.Inspector()!.Inspect(new Options(), "Плагин");
            }

            public sealed class Options : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;

                [DefaultValue(2)]
                public int Level { get; set; } = 2;

                public Shade Shade { get; set; }

                public string Name { get; set; } = "плагин";

                // Значение своего типа: конвертеры TypeDescriptor о нём не спрашиваются, и оно только читается.
                public Spot Where { get; set; } = new(1, 2);

                public void Touch() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level)));
            }
            """);

        studio.Raise();

        Assert.Equal("Плагин", studio.Service.Current?.Header.Title);
        studio.Frame();

        var where = Assert.Single(studio.Service.Current!.Properties, property => property.Id == "Where");

        Assert.True(where.IsReadOnly, "значение плагина без конвертера пишется");
        Assert.Contains("X = 1", studio.Service.Current.ValueOf("Where").Text, StringComparison.Ordinal);

        var context = new WeakReference(studio.Host.Loaded.Single(plugin => plugin.Installed.Id == "probe.settings").Context);

        Assert.True(studio.Host.Drop("probe.settings"));

        return context;
    }

    /// <summary>Настройки, как их пишет плагин: атрибуты .NET и перемены о себе.</summary>
    private sealed class Settings : INotifyPropertyChanged
    {
        private string _greeting = "Здравствуй";
        private PropertyChangedEventHandler? _changed;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                _changed += value;
                Subscribers++;
            }
            remove
            {
                _changed -= value;
                Subscribers--;
            }
        }

        [DisplayName("Приветствие")]
        [Category("Common")]
        [Description("Что панель говорит первым")]
        public string Greeting
        {
            get => _greeting;
            set
            {
                _greeting = value;
                _changed?.Invoke(this, new PropertyChangedEventArgs(nameof(Greeting)));
            }
        }

        [Range(1, 10)]
        [DefaultValue(3)]
        public int Repeat { get; set; } = 3;

        public Color Accent { get; set; } = Colors.SteelBlue;

        public Mood Mood { get; set; }

        [Browsable(false)]
        public string Secret { get; set; } = string.Empty;

        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public double Tuning { get; set; }

        [ReadOnly(true)]
        public string Locked { get; set; } = "закрыто";

        public int Count => 4;

        [Browsable(false)]
        public int Subscribers { get; private set; }
    }

    /// <summary>Настроение — перечисление объекта.</summary>
    private enum Mood
    {
        Calm,
        Bold,
    }
}
