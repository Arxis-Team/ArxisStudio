using ArxisStudio.Shell.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using IOPath = System.IO.Path;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio;

/// <summary>
/// Главное окно студии: каркас с зонами панелей и канвой. Панели наполняются
/// начиная с M3, когда появится модель открытого проекта.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsStore? _settings;

    /// <summary>Создаёт окно без проекта — состояние каркаса.</summary>
    public MainWindow()
    {
        InitializeComponent();
        ThemeSwitch.SelectedIndex = Application.Current?.ActualThemeVariant == ThemeVariant.Light ? 1 : 0;

        CanvasDots.Loaded += (_, _) => ApplyDotGrid();
        ActualThemeVariantChanged += (_, _) => ApplyDotGrid();
    }

    /// <summary>Создаёт окно для открытого проекта.</summary>
    /// <param name="settings">Настройки студии.</param>
    /// <param name="projectPath">Путь к решению или проекту.</param>
    public MainWindow(ISettingsStore settings, string projectPath) : this()
    {
        _settings = settings;
        ProjectPath = projectPath;

        ProjectName.Text = IOPath.GetFileNameWithoutExtension(projectPath);
        StatusText.Text = projectPath;
        Title = $"{IOPath.GetFileNameWithoutExtension(projectPath)} — ArxisStudio";
    }

    /// <summary>Путь к открытому решению или проекту; null, если проект не открыт.</summary>
    public string? ProjectPath { get; }

    private void ApplyDotGrid()
    {
        var showGrid = _settings?.Current.ShowCanvasGrid ?? true;
        if (!showGrid)
        {
            CanvasDots.Background = null;
            return;
        }

        if (this.TryFindResource("AxDotColor", ActualThemeVariant, out var value) && value is Color color)
        {
            CanvasDots.Background = new VisualBrush
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                DestinationRect = new RelativeRect(0, 0, 20, 20, RelativeUnit.Absolute),
                Visual = new Border
                {
                    Width = 20,
                    Height = 20,
                    Child = new Ellipse
                    {
                        Width = 2,
                        Height = 2,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Fill = new SolidColorBrush(color),
                    },
                },
            };
        }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var theme = ThemeSwitch.SelectedIndex == 1 ? StudioTheme.Light : StudioTheme.Dark;
        StudioTheming.Apply(theme);

        if (_settings is not null)
        {
            _settings.Current.Theme = theme;
            _settings.Save();
        }
    }
}
