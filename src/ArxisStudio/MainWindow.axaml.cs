using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ThemeSwitch.SelectedIndex = 0;

        // Точечная сетка канвы: тайл 20×20 с точкой 1px (docs/design-spec.md §3).
        CanvasDots.Loaded += (_, _) => ApplyDotGrid();
        ActualThemeVariantChanged += (_, _) => ApplyDotGrid();
    }

    private void ApplyDotGrid()
    {
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
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ThemeSwitch.SelectedIndex == 1 ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
