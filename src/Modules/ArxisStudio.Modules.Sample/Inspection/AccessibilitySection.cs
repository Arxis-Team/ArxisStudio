using ArxisStudio.Controls;
using ArxisStudio.Inspector;
using Avalonia.Controls;

namespace ArxisStudio.Modules.Sample.Inspection;

/// <summary>
/// Раздел доступности (образец): имя и пояснение для диктора — строками инспектора, а над ними предупреждение, когда
/// кнопке нечем назваться.
/// </summary>
/// <remarks>
/// <para>
/// Образец того, как плагин ставит в инспектор свой интерфейс — как <c>CustomEditor</c> у Unity, только для части
/// свойств. Раздел встаёт внутрь описанного раздела доступности (<see cref="Category"/>) и берёт себе две строки
/// (<see cref="Claims"/>): в списке они больше не стоят, а раздел строит их у контекста (<see cref="InspectorSectionContext.CreateRow"/>)
/// — с тем же редактором, подписью и пометкой заданного, что и в списке.
/// </para>
/// <para>
/// Предупреждение — то, чего строка сказать не может: кнопка без текста в содержимом и без
/// <c>AutomationProperties.Name</c> читается диктором как «кнопка». Раздел переспрашивает его на каждой перемене
/// цели (<see cref="InspectorSectionView.Refresh"/>).
/// </para>
/// </remarks>
[InspectorSection(typeof(Control), ForDerived = true)]
public sealed class AccessibilitySection : InspectorSection
{
    /// <summary>Имя элемента для диктора.</summary>
    public const string NameMember = "AutomationProperties.Name";

    /// <summary>Пояснение элемента для диктора.</summary>
    public const string HelpMember = "AutomationProperties.HelpText";

    /// <inheritdoc/>
    public override string Title => Context.Strings["sample.accessibility"];

    /// <inheritdoc/>
    public override string? Category => "accessibility";

    /// <inheritdoc/>
    public override IReadOnlyCollection<string> Claims => [NameMember, HelpMember];

    /// <inheritdoc/>
    public override bool Accepts(InspectorTypeInfo type, IReadOnlyList<InspectorProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return properties.Any(static property => property.Id == NameMember);
    }

    /// <inheritdoc/>
    public override InspectorSectionView CreateView(InspectorSectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new Names(context, Context.Strings["sample.accessibility.unnamed"]);
    }

    private sealed class Names : InspectorSectionView
    {
        private readonly InspectorSectionContext _context;
        private readonly AxBanner _warning;
        private readonly StackPanel _panel = new();

        public Names(InspectorSectionContext context, string warning)
        {
            _context = context;
            _warning = new AxBanner
            {
                Severity = AxBannerSeverity.Warning,
                Content = new TextBlock { Text = warning, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            };

            _panel.Children.Add(_warning);

            foreach (var id in (string[])[NameMember, HelpMember])
            {
                if (context.CreateRow(id) is { } row)
                    _panel.Children.Add(row);
            }

            Warn();
        }

        public override Control View => _panel;

        public override void Refresh(InspectorChangedEventArgs change) => Warn();

        /// <summary>Кнопке нечем назваться: ни текста в содержимом, ни имени для диктора.</summary>
        private void Warn()
        {
            var button = _context.Type.Is("Avalonia.Controls.Button");
            var named = !string.IsNullOrWhiteSpace(_context.ValueOf(NameMember).Text);
            var content = _context.ValueOf("Content");
            var titled = content.Source != InspectorValueSource.Expression && !string.IsNullOrWhiteSpace(content.Text);

            _warning.IsVisible = button && _context.Count == 1 && !named && !titled;
        }
    }
}
