using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Icons;
using ArxisStudio.Inspector;
using ArxisStudio.Modules.UiDesigner.Documents;
using ArxisStudio.ProjectSystem;
using ArxisStudio.Sdk;
using ArxisStudio.Xaml;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Источник и тип данных — полем объекта, как Data Source у Unity: имя класса, ⊙ — модели проекта с поиском, крестик
/// снимает запись, а файл модели можно бросить из окна проекта.
/// </summary>
/// <remarks>
/// <para>
/// Строки раздела данных снимок дизайнера помечает подсказками <see cref="SourceHint"/> (источник и данные для
/// дизайна) и <see cref="TypeHint"/> (тип данных). Выбранное пишется записью класса — элементом со своим объявлением
/// (<see cref="DataClass"/>), — и правка формы пишет и элемент свойства, и тип данных одним шагом.
/// </para>
/// <para>
/// Варианты даёт цель (<see cref="InspectorFieldContext.Suggest"/>): собранные модели — классы, а не контролы,
/// конвертеры и шаблоны, — и у корня с <c>x:Class</c> первой — сама форма. Брошенный файл кода читает служба XAML
/// (<see cref="IStudioXamlDesign.ClassesInAsync"/>) — на входе тяги, не в потоке интерфейса; пока она читает, подсказка
/// тяги говорит об этом, а бросок дожидается ответа. Класс, которого форма не создаст или ещё не знает, отклонён, и
/// подсказка говорит почему; классов несколько — открывается выбор из них.
/// </para>
/// </remarks>
[PropertyEditor(typeof(object), Hint = SourceHint)]
[PropertyEditor(typeof(object), Hint = TypeHint)]
public sealed class DataEditor : PropertyEditor
{
    /// <summary>Подсказка редактора: строка пишет данные — классом или выражением.</summary>
    public const string SourceHint = "xaml-data";

    /// <summary>Подсказка редактора: строка пишет тип данных.</summary>
    public const string TypeHint = "xaml-data-type";

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var typeOnly = string.Equals(context.Property.Editor, TypeHint, StringComparison.Ordinal);

        return new DataField(context, Context.Strings, Context.XamlDesign(), typeOnly);
    }

    /// <summary>
    /// Почему класс нельзя поставить; null — можно. Источнику нужен класс, который форма создаст; типу данных хватит
    /// собранной модели.
    /// </summary>
    /// <param name="type">Класс.</param>
    /// <param name="typeOnly">Для типа данных, а не для источника.</param>
    /// <param name="strings">Словарь модуля.</param>
    internal static string? Refusal(XamlDataTypeInfo type, bool typeOnly, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(strings);

        var key = type switch
        {
            { IsBuilt: false } => "inspector.data.notBuilt",
            { IsControl: true } => "inspector.data.isControl",
            { IsModel: false } => "inspector.data.notModel",
            { CanCreate: false } when !typeOnly => "inspector.data.cannotCreate",
            _ => null,
        };

        return key is null ? null : string.Format(CultureInfo.CurrentCulture, strings[key], type.Name);
    }

    /// <summary>Класс вариантом: запись — элемент со своим объявлением, подпись — имя, пояснение — пространство.</summary>
    /// <param name="type">Класс.</param>
    /// <param name="strings">Словарь модуля.</param>
    internal static InspectorChoice ChoiceOf(XamlDataTypeInfo type, IStudioStrings strings)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(strings);

        var written = new DataClass(type.XmlNamespace, type.Name, DataClass.PrefixFor(type.XmlNamespace, type.SuggestedPrefix));

        // Модель без INotifyPropertyChanged привязка прочтёт один раз: перемен её свойств форма не увидит.
        return new InspectorChoice(written.Text)
        {
            Title = type.Name,
            Description = type.Notifies || !type.IsData
                ? type.ClrNamespace
                : $"{type.ClrNamespace} · {strings["inspector.data.quiet"]}",
        };
    }

    private sealed class DataField : InspectorField
    {
        private readonly InspectorFieldContext _context;
        private readonly IStudioStrings _strings;
        private readonly IStudioXamlDesign? _design;
        private readonly bool _typeOnly;
        private readonly AxObjectField _field;
        private readonly ChoicePicker _picker;
        private readonly Panel _view;
        private string? _shown;
        private string? _dragged;
        private Task<IReadOnlyList<XamlDataTypeInfo>>? _reading;
        private bool _released;

        public DataField(InspectorFieldContext context, IStudioStrings strings, IStudioXamlDesign? design, bool typeOnly)
        {
            _context = context;
            _strings = strings;
            _design = design;
            _typeOnly = typeOnly;

            var readOnly = context.Property.IsReadOnly;

            _field = new AxObjectField
            {
                Size = AxControlSize.Compact,
                IsReadOnly = readOnly,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = strings["inspector.data.empty"],
                [AutomationProperties.NameProperty] = context.Property.Label,
            };
            _field.PickRequested += (_, _) => Open(null);
            _field.Cleared += (_, _) => _ = _context.WriteAsync(null);

            StudioDragDrop.SetAllowDrop(_field, !readOnly);
            StudioDragDrop.AddDragEnterHandler(_field, OnDragEnter);
            StudioDragDrop.AddDragOverHandler(_field, OnDragOver);
            StudioDragDrop.AddDragLeaveHandler(_field, OnDragLeave);
            StudioDragDrop.AddDropHandler(_field, OnDrop);

            _picker = new ChoicePicker(_field, strings["inspector.data.search"], Row, Choose)
            {
                // Конструктор модели — чужой код, и дизайнер зовёт его на каждом построении формы.
                Note = typeOnly ? null : strings["inspector.data.constructorNote"],
            };
            _view = new Panel { Children = { _field, _picker.Popup } };
        }

        public override Control View => _view;

        public override Control? FocusTarget => _field;

        /// <summary>Выбор классов открыт — тестам и проверке.</summary>
        internal bool IsPicking => _picker.IsOpen;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            var written = !value.IsMixed && value.IsWritten && value.Written is not null;

            _shown = written ? value.Written : null;
            _field.IsMixed = value.IsMixed;

            // Написанное — именем класса; ненаписанное — откуда данные пришли, заглушкой: «Main — от Window».
            _field.Text = written ? value.Text ?? value.Written : null;
            _field.PlaceholderText = written || value.IsMixed || string.IsNullOrEmpty(value.Text) ? _strings["inspector.data.empty"] : value.Text;
            ToolTip.SetTip(_field, written ? Tip(value.Written!) : value.Text);
        }

        public override void Release()
        {
            if (_released)
                return;

            _released = true;
            _picker.Close();

            StudioDragDrop.RemoveDragEnterHandler(_field, OnDragEnter);
            StudioDragDrop.RemoveDragOverHandler(_field, OnDragOver);
            StudioDragDrop.RemoveDragLeaveHandler(_field, OnDragLeave);
            StudioDragDrop.RemoveDropHandler(_field, OnDrop);
        }

        /// <summary>Подсказка написанного: полное имя класса, если запись его называет, иначе сама запись.</summary>
        private static string Tip(string written) =>
            DataClass.Parse(written) is { } type ? type.ClrName ?? $"{type.Name} ({type.XmlNamespace})" : written;

        /// <summary>Открывает выбор: классы цели — или те, что объявил брошенный файл.</summary>
        private void Open(IReadOnlyList<InspectorChoice>? only)
        {
            if (_context.Property.IsReadOnly || _released)
                return;

            _picker.Open(
                only ?? _context.Suggest(string.Empty),
                _shown,
                _strings["inspector.data.noModels"],
                typed => string.Format(CultureInfo.CurrentCulture, _strings["inspector.data.nothing"], typed));
        }

        /// <summary>Строка выбора: имя класса и его пространство вторичным тоном; у самой формы — значок окна.</summary>
        private static DockPanel Row(InspectorChoice choice)
        {
            var name = new TextBlock { Text = choice.Title ?? choice.Value, TextTrimming = TextTrimming.CharacterEllipsis };
            var detail = new TextBlock { Text = choice.Description, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = choice.Description is not null };
            var icon = new AxIcon
            {
                Data = choice.Icon ?? AxIcons.Component,
                VerticalAlignment = VerticalAlignment.Center,
                [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness"),
            };

            AxText.SetTone(detail, AxTextTone.Secondary);
            AxText.SetRole(detail, AxTextRole.Small);
            DockPanel.SetDock(icon, Dock.Left);

            var row = new DockPanel
            {
                Children =
                {
                    icon,
                    new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, detail } },
                },
            };

            ToolTip.SetTip(row, choice.Description);

            return row;
        }

        /// <summary>Выбранное пишется; нынешнее второй раз не пишется — шаг истории без перемены (запись 396).</summary>
        private void Choose(InspectorChoice choice)
        {
            if (!_released && !string.Equals(choice.Value, _shown, StringComparison.Ordinal))
                _ = _context.WriteAsync(choice.Value);
        }

        /// <summary>Классы, которые подходят строке, — вариантами.</summary>
        private List<XamlDataTypeInfo> Fitting(IReadOnlyList<XamlDataTypeInfo> found) =>
            [.. found.Where(type => Refusal(type, _typeOnly, _strings) is null)];

        /// <summary>Что несут — разбирается раз за тягу: файл кода читает служба, пока курсор идёт к полю.</summary>
        private void OnDragEnter(object? sender, StudioDragEventArgs e)
        {
            _dragged = e.Data.Files.FirstOrDefault(IsCode);
            _reading = _dragged is { } file && _design is { } design ? ReadAsync(design, file) : null;
        }

        private static async Task<IReadOnlyList<XamlDataTypeInfo>> ReadAsync(IStudioXamlDesign design, string file)
        {
            try
            {
                return await design.ClassesInAsync(CanonicalPath.Create(file)).ConfigureAwait(true);
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException
                                          or OperationCanceledException)
            {
                // Файл не читается или служба закрылась, пока читала: классов нет.
                return [];
            }
        }

        private void OnDragOver(object? sender, StudioDragEventArgs e)
        {
            e.Handled = true;
            _field.IsDropTarget = false;

            if (e.Data.Files.Count == 0)
                return;

            if (_dragged is null || _reading is null)
            {
                e.Hint = _strings["inspector.data.notCode"];
                return;
            }

            if (!_reading.IsCompletedSuccessfully)
            {
                e.Hint = _strings["inspector.data.reading"];
                return;
            }

            var found = _reading.Result;
            var fitting = Fitting(found);

            if (fitting.Count == 0)
            {
                e.Hint = found.Count == 0
                    ? string.Format(CultureInfo.CurrentCulture, _strings["inspector.data.noClass"], Path.GetFileName(_dragged))
                    : Refusal(found[0], _typeOnly, _strings);
                return;
            }

            // Ссылка, а не копия: поле называет класс, а не забирает файл.
            if ((e.AllowedEffects & DragDropEffects.Link) == 0)
                return;

            e.Effect = DragDropEffects.Link;
            e.Hint = fitting.Count > 1
                ? string.Format(CultureInfo.CurrentCulture, _strings["inspector.data.dropMany"], fitting.Count)
                : string.Format(CultureInfo.CurrentCulture, _strings[_typeOnly ? "inspector.data.dropType" : "inspector.data.drop"], fitting[0].Name);
            _field.IsDropTarget = true;
        }

        private void OnDragLeave(object? sender, StudioDragEventArgs e)
        {
            _field.IsDropTarget = false;
            _dragged = null;
            _reading = null;
        }

        /// <summary>Бросок ждёт ответа службы, если она ещё читает: курсор мог дойти до поля раньше неё.</summary>
        private void OnDrop(object? sender, StudioDragEventArgs e)
        {
            e.Handled = true;
            _field.IsDropTarget = false;

            if (_reading is { } reading)
                _ = DropAsync(reading);

            _dragged = null;
            _reading = null;
        }

        private async Task DropAsync(Task<IReadOnlyList<XamlDataTypeInfo>> reading)
        {
            var found = await reading.ConfigureAwait(true);

            if (_released)
                return;

            var fitting = Fitting(found);

            if (fitting.Count == 1)
            {
                Choose(ChoiceOf(fitting[0], _strings));
                return;
            }

            if (fitting.Count > 1)
            {
                Open([.. fitting.Select(type => ChoiceOf(type, _strings))]);
                return;
            }

            // Отказ, который подсказка тяги не успела сказать, — заметкой под полем.
            if (found.Count > 0 && Refusal(found[0], _typeOnly, _strings) is { } refusal)
                _context.Report(InspectorCheck.Invalid(refusal));
        }

        private static bool IsCode(string path) => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
    }
}
