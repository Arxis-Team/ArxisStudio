using ArxisStudio.Controls;
using ArxisStudio.Extensibility;
using ArxisStudio.Sdk;
using ArxisStudio.Sdk.Plugins;
using ArxisStudio.Shell;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ArxisStudio.Services;

/// <summary>
/// Полоса студии: элементы модулей и плагинов в трёх её местах.
/// </summary>
/// <remarks>
/// Двойник <see cref="StudioDock"/>: лента про плагины не знает, вся склейка
/// здесь. Кнопка и меню строятся по манифесту — сборку плагина для этого не
/// загружают, и кнопка спящего плагина стоит на месте, а щелчок будит его через
/// реестр команд. Свой контрол приходит готовым, в поверхности плагина.
/// <para>
/// У каждой записи есть хозяин, и снимают по хозяину, а не по манифесту — как
/// команды и панели. Типов плагина реестр не держит: у кнопок только делегаты
/// самой студии, у своего контрола — поверхность, которая уходит вместе с
/// хозяином.
/// </para>
/// <para>
/// Порядок в месте — хост, потом модули, потом плагины по идентификатору, а
/// внутри плагина — порядок манифеста. Чисел порядка в манифесте нет: они
/// обещали бы власть, которой у плагина нет. Разделители лента выводит сама там,
/// где меняется хозяин.
/// </para>
/// </remarks>
public sealed class StudioToolBar
{
    /// <summary>Имя хозяина у элементов самой студии.</summary>
    public const string Studio = "studio";

    private readonly ToolBarStrip _left;
    private readonly ToolBarStrip _center;
    private readonly ToolBarStrip _right;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private int _hosted;

    /// <summary>Заводит полосу над её лентами.</summary>
    /// <param name="left">Лента левого места.</param>
    /// <param name="center">Лента середины.</param>
    /// <param name="right">Лента правого места.</param>
    public StudioToolBar(ToolBarStrip left, ToolBarStrip center, ToolBarStrip right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _center = center;
        _right = right;
    }

    /// <summary>Студии есть что сказать о вкладе: не та команда, не тот значок, не тот элемент.</summary>
    public event EventHandler<string>? Complained;

    /// <summary>
    /// Чем вызывать команду по щелчку; null — кнопки молчат.
    /// </summary>
    /// <remarks>
    /// Делегат студии, а не плагина: так щелчок идёт той же дорогой, что вызов
    /// из кода, — через реестр, который будит спящего хозяина и приписывает
    /// падение виновнику.
    /// </remarks>
    public Func<string, bool>? Invoke { get; set; }

    /// <summary>Чем собирать дерево меню; null — меню пусты.</summary>
    public Func<IReadOnlyList<StudioMenuItem>>? Menu { get; set; }

    /// <summary>Ключ записи: хозяин и идентификатор из манифеста.</summary>
    /// <param name="owner">Плагин; null — сама студия.</param>
    /// <param name="itemId">Идентификатор элемента внутри плагина.</param>
    public static string Key(string? owner, string itemId) => $"{owner ?? Studio}:{itemId}";

    /// <summary>
    /// Ставит элемент в полосу.
    /// </summary>
    /// <param name="owner">Чей элемент; null — самой студии.</param>
    /// <param name="declared">Объявление из манифеста.</param>
    /// <param name="content">
    /// Готовый контрол для вида <c>custom</c>; null — место занято, контрол
    /// придёт, когда плагин поднимут. У кнопки и меню не нужен.
    /// </param>
    /// <remarks>
    /// Повторный вызов с тем же ключом ничего не пересобирает: спящий плагин
    /// получает кнопки при старте, а поднявшись, объявляет их снова — и должен
    /// увидеть их стоящими. Свой контрол при повторе заменяется: это дорога
    /// перезагрузки.
    /// </remarks>
    public void Add(InstalledPlugin? owner, PluginToolBarItem declared, Control? content = null)
    {
        ArgumentNullException.ThrowIfNull(declared);

        var key = Key(owner?.Id, declared.Id);

        if (_entries.TryGetValue(key, out var existing))
        {
            if (content is null)
                return;

            existing.View = content;
            existing.Button = null;
            Apply(existing);
            Rebuild(existing.Slot);

            return;
        }

        var entry = new Entry
        {
            Key = key,
            OwnerKey = owner?.Id ?? Studio,
            OwnerId = owner?.Id ?? string.Empty,
            Rank = owner is null ? 0 : owner.IsBuiltIn ? 1 : 2,
            Index = IndexOf(owner, declared),
            Slot = Slot(key, declared.Slot),
            Declared = declared,
        };

        if (declared.IsCustom)
        {
            entry.View = content;
        }
        else if (declared.IsMenu)
        {
            entry.View = entry.Button = MenuButton(owner, declared);
        }
        else if (declared.IsButton)
        {
            if (declared.Command is not { Length: > 0 })
            {
                Complained?.Invoke(this, $"У кнопки {key} не названа команда — кнопка не поставлена");
                return;
            }

            entry.View = entry.Button = CommandButton(owner, declared);
        }
        else
        {
            Complained?.Invoke(this, $"У элемента {key} незнакомый вид «{declared.Kind}» — элемент не поставлен");
            return;
        }

        _entries[key] = entry;
        Apply(entry);
        Rebuild(entry.Slot);
    }

    /// <summary>Снимает один элемент.</summary>
    /// <param name="owner">Чей элемент; null — самой студии.</param>
    /// <param name="itemId">Идентификатор из манифеста.</param>
    public void Remove(string? owner, string itemId)
    {
        if (!_entries.Remove(Key(owner, itemId), out var gone))
            return;

        Rebuild(gone.Slot);
    }

    /// <summary>Снимает всё, что поставил этот хозяин, — вместе с состоянием.</summary>
    /// <param name="owner">Чьи элементы убираем.</param>
    public void RemoveOwnedBy(string owner)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);

        var gone = _entries.Values
            .Where(entry => string.Equals(entry.OwnerId, owner, StringComparison.Ordinal))
            .ToList();

        foreach (var entry in gone)
            _entries.Remove(entry.Key);

        foreach (var slot in gone.Select(entry => entry.Slot).Distinct(StringComparer.Ordinal))
            Rebuild(slot);
    }

    /// <summary>
    /// Меняет состояние элемента; пропущенный флаг остаётся как был.
    /// </summary>
    /// <param name="owner">Чей элемент; null — самой студии.</param>
    /// <param name="itemId">Идентификатор из манифеста.</param>
    /// <param name="isEnabled">Доступен ли элемент.</param>
    /// <param name="isVisible">Показан ли элемент.</param>
    /// <param name="isChecked">Включён ли инструмент — только у кнопки.</param>
    /// <remarks>
    /// Зовут и из фоновых задач: вне потока интерфейса вызов откладывается в
    /// него целиком — контролы трогают только там. Незнакомый ключ — замечание,
    /// не исключение: полоса не то место, из-за которого стоит падать.
    /// </remarks>
    public void Update(
        string? owner, string itemId, bool? isEnabled = null, bool? isVisible = null, bool? isChecked = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Update(owner, itemId, isEnabled, isVisible, isChecked));
            return;
        }

        var key = Key(owner, itemId);

        if (!_entries.TryGetValue(key, out var entry))
        {
            Complained?.Invoke(this, $"Элемент {key} не объявлен в манифесте — менять нечего");
            return;
        }

        if (isEnabled is { } enabled)
            entry.IsEnabled = enabled;

        if (isChecked is { } on)
        {
            if (entry.Declared.IsButton)
                entry.IsChecked = on;
            else
                Complained?.Invoke(this, $"Элемент {key} — не кнопка, включённым он не бывает");
        }

        var shown = entry.IsVisible;

        if (isVisible is { } visible)
            entry.IsVisible = visible;

        Apply(entry);

        if (shown != entry.IsVisible)
            Rebuild(entry.Slot);
    }

    /// <summary>Ключи элементов, стоящих в месте, в порядке показа.</summary>
    /// <param name="slot">Место: <c>left</c>, <c>center</c> или <c>right</c>.</param>
    public IReadOnlyList<string> Shown(string slot) =>
        Strip(Slot(null, slot)).Children
            .OfType<Control>()
            .Where(child => child is not AxDivider)
            .Select(view => _entries.Values.First(entry => ReferenceEquals(entry.View, view)).Key)
            .ToList();

    private void Rebuild(string slot)
    {
        var ordered = _entries.Values
            .Where(entry => string.Equals(entry.Slot, slot, StringComparison.Ordinal) && entry.View is not null && entry.IsVisible)
            .OrderBy(entry => entry.Rank)
            .ThenBy(entry => entry.OwnerId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Index)
            .Select(entry => (entry.OwnerKey, entry.View!))
            .ToList();

        Strip(slot).Place(ordered);
    }

    private static void Apply(Entry entry)
    {
        if (entry.View is { } view)
            view.IsEnabled = entry.IsEnabled;

        if (entry.Button is { } button)
            button.IsChecked = entry.IsChecked;
    }

    private int IndexOf(InstalledPlugin? owner, PluginToolBarItem declared)
    {
        if (owner is null)
            return _hosted++;

        var index = owner.Manifest?.Contributions.ToolBar.IndexOf(declared) ?? -1;

        // Объявление не из манифеста — порядок по очереди подачи.
        return index >= 0
            ? index
            : _entries.Values.Count(entry => string.Equals(entry.OwnerId, owner.Id, StringComparison.Ordinal));
    }

    private string Slot(string? key, string? asked)
    {
        if (asked is null || string.Equals(asked.Trim(), "right", StringComparison.OrdinalIgnoreCase))
            return "right";

        if (string.Equals(asked.Trim(), "left", StringComparison.OrdinalIgnoreCase))
            return "left";

        if (string.Equals(asked.Trim(), "center", StringComparison.OrdinalIgnoreCase))
            return "center";

        // Незнакомое место — как у стороны панели: умолчание и слово об этом.
        if (key is not null)
            Complained?.Invoke(this, $"У элемента {key} незнакомое место «{asked}» — поставлен справа");

        return "right";
    }

    private ToolBarStrip Strip(string slot) => slot switch
    {
        "left" => _left,
        "center" => _center,
        _ => _right,
    };

    private ToolBarButton CommandButton(InstalledPlugin? owner, PluginToolBarItem declared)
    {
        var button = Button(owner, declared);
        var command = declared.Command!;

        // Замыкание держит реестр и строку — ничего из сборки плагина.
        button.Click += (_, _) =>
        {
            if (Invoke is not { } invoke || !invoke(command))
                Complained?.Invoke(this, $"Команду {command} никто не обрабатывает");
        };

        return button;
    }

    private ToolBarButton MenuButton(InstalledPlugin? owner, PluginToolBarItem declared)
    {
        var button = Button(owner, declared);
        var flyout = new AxMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };

        // Меню собирается на каждом открытии: так смена языка и подъём новых
        // плагинов доходят до него без подписок и без хранения дерева.
        flyout.Opening += (_, _) => Fill(flyout, owner, declared);
        button.Flyout = flyout;

        return button;
    }

    private void Fill(MenuFlyout flyout, InstalledPlugin? owner, PluginToolBarItem declared)
    {
        flyout.Items.Clear();

        IReadOnlyList<StudioMenuItem> level = Menu?.Invoke() ?? [];

        if (declared.Menu is { Length: > 0 } path)
        {
            // Путь режется до перевода, а переводится посегментно — как в
            // StudioMenu: переведённая строка вполне может содержать косую.
            var strings = owner?.Strings ?? PluginStrings.Studio;
            var segments = path
                .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(strings.Resolve);

            foreach (var segment in segments)
            {
                var branch = level.FirstOrDefault(item => !item.IsCommand && string.Equals(item.Title, segment, StringComparison.Ordinal));

                if (branch is null)
                {
                    Complained?.Invoke(this, $"Ветки «{segment}» в меню нет — меню {Key(owner?.Id, declared.Id)} пусто");
                    level = [];
                    break;
                }

                level = branch.Children;
            }
        }

        foreach (var item in level)
            flyout.Items.Add(Build(item));
    }

    private AxMenuItem Build(StudioMenuItem source)
    {
        var item = new AxMenuItem { Header = source.Title };

        if (source.CommandId is { } command)
        {
            item.Click += (_, _) =>
            {
                if (Invoke is not { } invoke || !invoke(command))
                    Complained?.Invoke(this, $"Команду {command} никто не обрабатывает");
            };
        }

        foreach (var child in source.Children)
            item.Items.Add(Build(child));

        return item;
    }

    /// <summary>
    /// Кнопка по объявлению: со значком — иконочная, без — текстовая.
    /// </summary>
    /// <remarks>
    /// Значок, который не разобрался, кнопку не отменяет: она становится
    /// текстовой, а о значке остаётся замечание. Без значка и без подписи
    /// остаётся вопросительный глиф — пустой кнопки в полосе быть не должно.
    /// </remarks>
    private ToolBarButton Button(InstalledPlugin? owner, PluginToolBarItem declared)
    {
        var strings = owner?.Strings ?? PluginStrings.Studio;
        var glyph = ToolBarIcons.Resolve(declared.Icon, out var problem);

        if (problem is not null)
            Complained?.Invoke(this, $"{Key(owner?.Id, declared.Id)}: {problem}");

        var title = declared.Title is { Length: > 0 } text ? text : null;

        if (glyph is null && title is null)
            glyph = AxIcons.Question;

        var button = new ToolBarButton();

        if (glyph is not null)
        {
            button.Classes.Add("icon");
            button.Content = new AxIcon { Data = glyph };
        }
        else
        {
            button.Classes.Add("ghost");
            button.Classes.Add("compact");

            if (declared.IsMenu)
            {
                var label = new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

                Text(label, TextBlock.TextProperty, title!, strings);

                button.Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        label,
                        new AxIcon { Classes = { "small" }, Data = AxIcons.ChevronDown },
                    },
                };
            }
            else
            {
                Text(button, ContentControl.ContentProperty, title!, strings);
            }
        }

        if (title is not null)
        {
            Text(button, ToolTip.TipProperty, title, strings);
            Text(button, AutomationProperties.NameProperty, title, strings);
        }

        return button;
    }

    /// <summary>
    /// Подпись — живой привязкой, если это ключ, и строкой, если нет.
    /// </summary>
    /// <remarks>
    /// Тот же приём, что у заголовка панели в раскладке: текст вклада показывает
    /// не его автор, а студия, и переводить его при смене языка — её забота.
    /// </remarks>
    private static void Text(AvaloniaObject target, AvaloniaProperty property, string text, PluginStrings strings)
    {
        if (PluginStrings.IsKey(text, out var key))
            target.Bind(property, strings.Text(key));
        else
            target.SetValue(property, text);
    }

    private sealed class Entry
    {
        public required string Key { get; init; }

        public required string OwnerKey { get; init; }

        public required string OwnerId { get; init; }

        public required int Rank { get; init; }

        public required int Index { get; init; }

        public required string Slot { get; init; }

        public required PluginToolBarItem Declared { get; init; }

        public Control? View { get; set; }

        public ToolBarButton? Button { get; set; }

        public bool IsEnabled { get; set; } = true;

        public bool IsVisible { get; set; } = true;

        public bool IsChecked { get; set; }
    }
}

/// <summary>
/// Полоса глазами одного плагина.
/// </summary>
/// <remarks>
/// Реестр один на студию, а хозяин у каждой записи свой: контракт SDK о хозяине
/// не говорит, и подставить его может только тот, кто выдаёт плагину контекст, —
/// как у команд и экспортов. Чужие элементы отсюда недостижимы по построению.
/// </remarks>
/// <param name="registry">Общая полоса.</param>
/// <param name="pluginId">Чьи элементы меняет эта обёртка.</param>
public sealed class PluginToolBar(StudioToolBar registry, string pluginId) : IStudioToolBar
{
    /// <inheritdoc/>
    public void Update(string itemId, bool? isEnabled = null, bool? isVisible = null, bool? isChecked = null) =>
        registry.Update(pluginId, itemId, isEnabled, isVisible, isChecked);
}
