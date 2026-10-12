using System.Globalization;
using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using ArxisStudio.Sdk;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ArxisStudio.Modules.UiDesigner.Inspection;

/// <summary>
/// Картинка формы — полем объекта, как у Unity: миниатюра и имя файла, ⊙ — картинки решения с поиском и миниатюрами,
/// крестик снимает атрибут, а картинку можно бросить из окна проекта.
/// </summary>
/// <remarks>
/// <para>
/// Члены картинки и значка (<see cref="ProjectImages.Types"/>) снимок дизайнера помечает подсказкой <see cref="Hint"/>, и
/// варианты к ним — картинки решения, ставшие ресурсами, — даёт цель дизайнера (<see cref="InspectorFieldContext.Suggest"/>):
/// поле цель не видит. Выбранное пишется текстом варианта — <c>/Assets/logo.png</c> своего проекта или
/// <c>avares://Сборка/…</c> чужого, — а картинку на форму ставит загрузка, через службу XAML.
/// </para>
/// <para>
/// Брошенный файл встаёт, только если он среди вариантов: файл, который не ресурс Avalonia, собранная форма не найдёт, —
/// бросок отклонён, и подсказка тяги говорит почему.
/// </para>
/// <para>
/// Миниатюры списка поле читает с диска само, как окно проекта, — у растровых картинок поставщика превью студии нет, — и
/// только у видных строк: картинок в решении бывают сотни.
/// </para>
/// </remarks>
[PropertyEditor(typeof(IImage), Hint = Hint)]
public sealed class ProjectImageEditor : PropertyEditor
{
    /// <summary>Подсказка редактора: член читает картинку по имени её ресурса.</summary>
    public const string Hint = "project-image";

    /// <inheritdoc/>
    public override InspectorField CreateField(InspectorFieldContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ImageField(context, Context.Strings);
    }

    /// <summary>Слово типа в заглушке, как у Unity: «Нет (Image)», «Нет (WindowIcon)».</summary>
    internal static string TypeWord(InspectorTypeInfo type)
    {
        var name = type.FullName[(type.FullName.LastIndexOf('.') + 1)..];

        return name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]) ? name[1..] : name;
    }

    /// <summary>Имя файла из записи: за последней косой; без неё — запись как есть.</summary>
    internal static string NameOf(string text)
    {
        var trimmed = text.Trim();
        var slash = trimmed.LastIndexOf('/');

        return slash >= 0 && slash < trimmed.Length - 1 ? Uri.UnescapeDataString(trimmed[(slash + 1)..]) : trimmed;
    }

    private sealed class ImageField : InspectorField
    {
        /// <summary>Файл крупнее этого миниатюрой не читается: список не ждёт декода фотографий.</summary>
        private const long MaxThumbnailBytes = 32 * 1024 * 1024;

        /// <summary>Сколько пикселей по длинной стороне читать миниатюре: с запасом на двойной масштаб экрана.</summary>
        private const int ThumbnailPixels = 64;

        private readonly InspectorFieldContext _context;
        private readonly IStudioStrings _strings;
        private readonly AxObjectField _field;
        private readonly ChoicePicker _picker;
        private readonly Panel _view;
        private readonly Dictionary<string, Bitmap?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
        private InspectorChoice? _dropped;
        private string? _shown;
        private string? _thumbnailOf;
        private bool _released;

        public ImageField(InspectorFieldContext context, IStudioStrings strings)
        {
            _context = context;
            _strings = strings;

            var readOnly = context.Property.IsReadOnly;

            _field = new AxObjectField
            {
                Size = AxControlSize.Compact,
                IsReadOnly = readOnly,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = string.Format(CultureInfo.CurrentCulture, strings["inspector.image.none"], TypeWord(context.Property.ValueType)),
                [AutomationProperties.NameProperty] = context.Property.Label,
            };
            _field.PickRequested += (_, _) => Open();
            _field.Cleared += (_, _) => _ = _context.WriteAsync(null);

            StudioDragDrop.SetAllowDrop(_field, !readOnly);
            StudioDragDrop.AddDragEnterHandler(_field, OnDragEnter);
            StudioDragDrop.AddDragOverHandler(_field, OnDragOver);
            StudioDragDrop.AddDragLeaveHandler(_field, OnDragLeave);
            StudioDragDrop.AddDropHandler(_field, OnDrop);

            _picker = new ChoicePicker(_field, strings["inspector.image.search"], Row, Choose);
            _view = new Panel { Children = { _field, _picker.Popup } };
        }

        public override Control View => _view;

        public override Control? FocusTarget => _field;

        /// <summary>Выбор картинок открыт — тестам и проверке.</summary>
        internal bool IsPicking => _picker.IsOpen;

        public override void Show(InspectorValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            _shown = value.IsMixed ? null : value.Source is InspectorValueSource.Expression ? value.Written ?? value.Text : value.Text;

            var written = value.Source is InspectorValueSource.Local or InspectorValueSource.Design or InspectorValueSource.Expression;

            _field.IsMixed = value.IsMixed;
            _field.Text = value.IsMixed
                ? null
                : written && !string.IsNullOrWhiteSpace(_shown)
                    ? NameOf(_shown)
                    : value.Shared is { } shared ? shared.GetType().Name : null;
            ToolTip.SetTip(_field, written ? _shown : null);
            ShowThumbnail(value);
        }

        /// <summary>
        /// Миниатюра поля: картинка с объекта, если цель её отдала, а иначе — файл варианта, который записан: так она есть
        /// и у значка окна, который не картинка.
        /// </summary>
        private void ShowThumbnail(InspectorValue value)
        {
            if (value.IsMixed || _released)
            {
                _thumbnailOf = null;
                _field.Thumbnail = null;
                return;
            }

            if (value.Shared is IImage image)
            {
                _thumbnailOf = null;
                _field.Thumbnail = image;
                return;
            }

            // Варианты цели спрашиваются, когда сменилась запись, а не на каждом показе значения.
            var shown = _shown;

            if (string.Equals(_thumbnailOf, shown, StringComparison.Ordinal))
                return;

            _thumbnailOf = shown;

            var file = string.IsNullOrWhiteSpace(shown)
                ? null
                : _context.Suggest(string.Empty).FirstOrDefault(choice => string.Equals(choice.Value, shown, StringComparison.Ordinal))?.Description;

            if (file is null)
            {
                _field.Thumbnail = null;
                return;
            }

            _ = FieldThumbnailAsync(file, shown);
        }

        private async Task FieldThumbnailAsync(string file, string? shown)
        {
            var bitmap = await ThumbnailOfAsync(file).ConfigureAwait(true);

            if (!_released && string.Equals(_shown, shown, StringComparison.Ordinal))
                _field.Thumbnail = bitmap;
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

            foreach (var thumbnail in _thumbnails.Values)
                thumbnail?.Dispose();

            _thumbnails.Clear();
        }

        /// <summary>Открывает выбор: картинки решения — у цели, когда спросили, с кареткой в поиске.</summary>
        private void Open()
        {
            if (_context.Property.IsReadOnly || _released)
                return;

            _picker.Open(
                _context.Suggest(string.Empty),
                _shown,
                _strings["inspector.image.empty"],
                typed => string.Format(CultureInfo.CurrentCulture, _strings["inspector.image.nothing"], typed));
        }

        /// <summary>Строка выбора: миниатюра, имя файла и запись вторичным тоном; полный путь — подсказкой.</summary>
        private Control Row(InspectorChoice choice)
        {
            var thumbnail = new Image
            {
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                [!Layoutable.WidthProperty] = new DynamicResourceExtension("AxObjectPickerThumbnailSize"),
                [!Layoutable.HeightProperty] = new DynamicResourceExtension("AxObjectPickerThumbnailSize"),
                [!Layoutable.MarginProperty] = new DynamicResourceExtension("AxGapIconTextThickness"),
            };
            var name = new TextBlock { Text = choice.Title ?? choice.Value, TextTrimming = TextTrimming.CharacterEllipsis };
            var path = new TextBlock { Text = choice.Value, TextTrimming = TextTrimming.CharacterEllipsis };

            AxText.SetTone(path, AxTextTone.Secondary);
            AxText.SetRole(path, AxTextRole.Small);
            DockPanel.SetDock(thumbnail, Dock.Left);

            var row = new DockPanel
            {
                Children =
                {
                    thumbnail,
                    new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, path } },
                },
            };

            ToolTip.SetTip(row, choice.Description);

            if (choice.Description is { Length: > 0 } file)
                _ = ThumbnailAsync(file, thumbnail);

            return row;
        }

        /// <summary>Миниатюра строки выбора; строка, ушедшая к другому файлу, её не получит.</summary>
        private async Task ThumbnailAsync(string file, Image target)
        {
            target.Tag = file;

            var bitmap = await ThumbnailOfAsync(file).ConfigureAwait(true);

            if (string.Equals(target.Tag as string, file, StringComparison.OrdinalIgnoreCase))
                target.Source = bitmap;
        }

        /// <summary>Миниатюра файла — из памяти поля или с диска в фоне; поле отпущено — пусто.</summary>
        private async Task<Bitmap?> ThumbnailOfAsync(string file)
        {
            if (_thumbnails.TryGetValue(file, out var known))
                return known;

            var bitmap = await Task.Run(() => Decode(file, ThumbnailPixels)).ConfigureAwait(true);

            if (_released)
            {
                bitmap?.Dispose();
                return null;
            }

            if (_thumbnails.TryGetValue(file, out var raced))
            {
                bitmap?.Dispose();
                return raced;
            }

            _thumbnails[file] = bitmap;

            return bitmap;
        }

        /// <summary>
        /// Читает картинку не крупнее <paramref name="pixels"/> по длинной стороне; не читается — пусто. Перехват
        /// широкий, как у окна проекта: файл положил кто угодно, и чем декодер ответит на мусор — его дело.
        /// </summary>
        private static Bitmap? Decode(string file, int pixels)
        {
            try
            {
                var info = new FileInfo(file);

                if (!info.Exists || info.Length > MaxThumbnailBytes)
                    return null;

                Bitmap full;

                using (var stream = File.OpenRead(file))
                    full = new Bitmap(stream);

                if (Math.Max(full.PixelSize.Width, full.PixelSize.Height) <= pixels)
                    return full;

                var wide = full.PixelSize.Width >= full.PixelSize.Height;

                full.Dispose();

                using var again = File.OpenRead(file);

                return wide
                    ? Bitmap.DecodeToWidth(again, pixels, BitmapInterpolationMode.HighQuality)
                    : Bitmap.DecodeToHeight(again, pixels, BitmapInterpolationMode.HighQuality);
            }
            catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
            {
                return null;
            }
        }

        /// <summary>Выбранное пишется; выбор к этому времени закрыт, и каретка вернулась в поле.</summary>
        /// <remarks>Нынешнее значение второй раз не пишется: запись того же текста — шаг истории без перемены (запись 396).</remarks>
        private void Choose(InspectorChoice choice)
        {
            if (!_released && !string.Equals(choice.Value, _shown, StringComparison.Ordinal))
                _ = _context.WriteAsync(choice.Value);
        }

        /// <summary>Что несут — разбирается раз за тягу: брошенный файл — тот ли вариант, что есть у цели.</summary>
        private void OnDragEnter(object? sender, StudioDragEventArgs e)
        {
            _dropped = null;

            if (e.Data.Files.FirstOrDefault(ProjectImages.IsImage) is { } file)
            {
                var full = Path.GetFullPath(file);

                _dropped = _context.Suggest(string.Empty)
                    .FirstOrDefault(choice => choice.Description is { } path && string.Equals(Path.GetFullPath(path), full, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void OnDragOver(object? sender, StudioDragEventArgs e)
        {
            e.Handled = true;

            if (_dropped is not { } choice)
            {
                _field.IsDropTarget = false;
                e.Hint = e.Data.Files.FirstOrDefault(ProjectImages.IsImage) is { } file
                    ? string.Format(CultureInfo.CurrentCulture, _strings["inspector.image.notResource"], Path.GetFileName(file))
                    : e.Data.Files.Count == 0 ? null : _strings["inspector.image.notImage"];
                return;
            }

            // Ссылка, а не копия: поле называет файл, а не забирает его.
            if ((e.AllowedEffects & DragDropEffects.Link) == 0)
                return;

            e.Effect = DragDropEffects.Link;
            e.Hint = string.Format(CultureInfo.CurrentCulture, _strings["inspector.image.drop"], choice.Title ?? choice.Value);
            _field.IsDropTarget = true;
        }

        private void OnDragLeave(object? sender, StudioDragEventArgs e)
        {
            _field.IsDropTarget = false;
            _dropped = null;
        }

        private void OnDrop(object? sender, StudioDragEventArgs e)
        {
            e.Handled = true;

            if (_dropped is { } choice && e.Effect == DragDropEffects.Link)
                _ = _context.WriteAsync(choice.Value);
        }
    }
}
