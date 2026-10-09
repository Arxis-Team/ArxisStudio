using ArxisStudio.Extensibility;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.Inspector;
using ArxisStudio.Modules.Inspector.Panel;
using ArxisStudio.Sdk;
using ArxisStudio.Services;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Студия с инспектором, собранная так, как её собирает студия: хост, контексты, экспорт и реестр вкладов на
/// общем шве.
/// </summary>
/// <remarks>
/// Модуль поднимает хост, а его вклады — редакторы по умолчанию и таблицу Avalonia — собирает реестр, как
/// собирает их служба расширений после подъёма. Плагины — папкой каталога, своей у теста: их вклады ложатся
/// поверх модульных и уходят с выгрузкой.
/// </remarks>
internal sealed class InspectorStudio : IDisposable
{
    private readonly string _settings;
    private readonly List<InspectorPanel> _panels = [];

    /// <summary>Поднимает модуль инспектора.</summary>
    public InspectorStudio()
    {
        Root = TempFolder.Create("inspector");
        _settings = Path.Combine(Root, "plugin-settings.json");
        Contributions = new PluginContributionRegistry(Guard);
        Contexts = new StudioContextFactory(
            Log,
            Commands,
            projectPath: null,
            new Dictionary<Type, object>(),
            settings: new PluginSettingsStore(projectPath: null, userFile: _settings),
            guard: Guard,
            exports: Exports,
            contributions: Contributions);
        Host = new PluginHost(Contexts);

        Host.Unloading += (_, id) =>
        {
            Commands.RemoveOwnedBy(id);
            Exports.RemoveOwnedBy(id);
            Contributions.Remove(id);
            Contexts.Forget(id);
        };

        Module = Host.LoadBuiltIn(typeof(InspectorModule).Assembly);
        Assert.True(Module.IsLoaded, Module.Error);
        Contributions.Add(Module);

        Context = Module.Studio!;
        Service = Context.Inspector() as StudioInspector
            ?? throw new InvalidOperationException("инспектор не опубликован");
        Window = new Window { Width = 420, Height = 640 };
        Window.Show();
        Frame();
    }

    /// <summary>Папка теста; плагины — в её <c>plugins</c>.</summary>
    public string Root { get; }

    /// <summary>Папка плагинов.</summary>
    public string Plugins => Path.Combine(Root, "plugins");

    /// <summary>Журнал.</summary>
    public StudioLog Log { get; } = new();

    /// <summary>Шов сбоев.</summary>
    public PluginGuard Guard { get; } = new();

    /// <summary>Команды.</summary>
    public StudioCommands Commands { get; } = new();

    /// <summary>Экспорты.</summary>
    public StudioExportRegistry Exports { get; } = new();

    /// <summary>Реестр вкладов.</summary>
    public PluginContributionRegistry Contributions { get; }

    /// <summary>Фабрика контекстов.</summary>
    public StudioContextFactory Contexts { get; }

    /// <summary>Хост.</summary>
    public PluginHost Host { get; }

    /// <summary>Поднятый модуль.</summary>
    public LoadedPlugin Module { get; }

    /// <summary>Контекст модуля.</summary>
    public IStudioContext Context { get; }

    /// <summary>Служба показа — изнутри.</summary>
    public StudioInspector Service { get; }

    /// <summary>Каталог вкладов.</summary>
    public Modules.Inspector.Catalog.InspectorCatalog Catalog =>
        Service.Catalog ?? throw new InvalidOperationException("каталога нет");

    /// <summary>Окно, в котором стоит панель.</summary>
    public Window Window { get; }

    /// <summary>Словарь модуля.</summary>
    public IStudioStrings Strings => Context.Strings;

    /// <summary>Ставит панель инспектора в окно — как её ставит студия: тем же контекстом модуля.</summary>
    public InspectorPanel Panel()
    {
        var panel = new InspectorPanel();

        panel.Attach(Context);
        _panels.Add(panel);
        Window.Content = panel.Content;
        Frame();

        return panel;
    }

    /// <summary>Поднимает плагины своей папки.</summary>
    public IReadOnlyList<LoadedPlugin> Raise()
    {
        var raised = Host.LoadStartup(new PluginCatalog(Plugins).Scan());

        foreach (var plugin in raised)
        {
            Assert.True(plugin.IsLoaded, plugin.Error);
            Contributions.Add(plugin);
        }

        Frame();

        return raised;
    }

    /// <summary>Пропускает кадр: раскладка, отложенное, отрисовка.</summary>
    public void Frame()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var panel in _panels)
            panel.Release();

        Window.Close();
        Host.Dispose();
        TempFolder.Erase(Root);
    }
}

/// <summary>Цель-проба: свойства и значения в словаре, записи и шаги истории — в журнале.</summary>
internal sealed class FakeTarget : InspectorTarget
{
    private readonly Dictionary<string, InspectorValue> _values = new(StringComparer.Ordinal);
    private IReadOnlyList<InspectorProperty> _properties;

    /// <summary>Заводит цель.</summary>
    /// <param name="type">Тип цели.</param>
    /// <param name="properties">Её свойства.</param>
    public FakeTarget(Type type, params InspectorProperty[] properties)
    {
        Type = InspectorTypeInfo.Of(type);
        Header = new InspectorHeader(type.Name) { Subtitle = type.FullName };
        _properties = properties;
    }

    /// <inheritdoc/>
    public override InspectorHeader Header { get; }

    /// <inheritdoc/>
    public override InspectorTypeInfo Type { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<InspectorProperty> Properties => _properties;

    /// <inheritdoc/>
    public override IReadOnlyCollection<string>? WrittenIds => KnowsWritten
        ? _values.Where(static pair => pair.Value.IsWritten).Select(static pair => pair.Key).ToHashSet(StringComparer.Ordinal)
        : null;

    /// <summary>Цель знает заданное без значений.</summary>
    public bool KnowsWritten { get; set; } = true;

    /// <summary>Сколько раз спросили значение каждого свойства.</summary>
    public Dictionary<string, int> Asked { get; } = new(StringComparer.Ordinal);

    /// <summary>Записи — пачками, как их прислали.</summary>
    public List<IReadOnlyList<InspectorWrite>> Writes { get; } = [];

    /// <summary>Шаги истории: назад — <c>true</c>.</summary>
    public List<bool> Steps { get; } = [];

    /// <summary>Что попросили включить.</summary>
    public List<string> Included { get; } = [];

    /// <summary>Свойство-проба.</summary>
    /// <param name="id">Идентификатор.</param>
    /// <param name="type">Тип значения.</param>
    public static InspectorProperty Property(string id, Type type) =>
        new(id, InspectorTypeInfo.Of(type)) { Kind = InspectorPropertyKind.Styled };

    /// <inheritdoc/>
    public override InspectorValue ValueOf(string id)
    {
        Asked[id] = Asked.GetValueOrDefault(id) + 1;

        return _values.TryGetValue(id, out var value) ? value : new InspectorValue("0");
    }

    /// <inheritdoc/>
    public override Task<bool> WriteAsync(IReadOnlyList<InspectorWrite> writes, string? label = null)
    {
        Writes.Add(writes);

        foreach (var write in writes)
        {
            _values[write.PropertyId] = write.Text is null
                ? new InspectorValue("0")
                : new InspectorValue(write.Text) { Source = InspectorValueSource.Local };
        }

        Raise(InspectorChanges.Values, writes.Select(static write => write.PropertyId).ToArray());

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public override Task<bool> StepAsync(bool back)
    {
        Steps.Add(back);

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public override void Include(IReadOnlyCollection<string> ids) => Included.AddRange(ids);

    /// <summary>Ставит значение, как его поставила бы правка снаружи, и объявляет перемену.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="value">Значение.</param>
    public void Set(string id, InspectorValue value)
    {
        _values[id] = value;
        Raise(InspectorChanges.Values, [id]);
    }

    /// <summary>Ставит значение молча — до показа.</summary>
    /// <param name="id">Свойство.</param>
    /// <param name="value">Значение.</param>
    public void Put(string id, InspectorValue value) => _values[id] = value;

    /// <summary>Меняет состав свойств и объявляет об этом.</summary>
    /// <param name="properties">Новые свойства.</param>
    public void Reshape(params InspectorProperty[] properties)
    {
        _properties = properties;
        Raise(InspectorChanges.Properties);
    }

    /// <summary>Закрывает цель.</summary>
    public void Shut() => Close();
}
