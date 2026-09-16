using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using ArxisStudio.Icons;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Резкость набора на всех масштабах: сколько чернил ложится в пиксели целиком.
/// </summary>
/// <remarks>
/// Набор нарисован под пиксель, и в пиксели он ложится там, где на единицу сетки приходится целое
/// их число: при 100 % это один пиксель, при 200 % — два. Дробные масштабы — 125, 150, 175 % — так
/// не ложились никогда: полуклетки падали на четверти пикселя, у штриха не оставалось ядра, и
/// доля пикселей, закрашенных целиком, падала с 54 % при 100 % до 28 % при 125 %. Мелкий значок —
/// двенадцать точек на клетку 16, три четверти пикселя на единицу — не ложился вовсе: 1.3 %.
/// <para>
/// Лечит это не толщина обводки, а клетка: значок в шестнадцать точек занимает при 125, 150 и
/// 175 % ровно 20, 24 и 28 пикселей, мелкий — 12, 15, 18 и 21, и набор нарисован в этих клетках
/// тоже. Здесь стоит порог, ниже которого набор опускаться не должен: замер после пересчёта даёт
/// 51…64 %, порог — 48 %.
/// </para>
/// </remarks>
public class IconSharpnessTests
{
    /// <summary>Ниже этой доли сплошных пикселей набор не опускается ни на одном масштабе.</summary>
    private const double Floor = 0.48;

    /// <summary>Размер значка и масштаб экрана: обычный и мелкий на пяти ступенях.</summary>
    public static TheoryData<double, double> Screens
    {
        get
        {
            var data = new TheoryData<double, double>();

            foreach (var size in new[] { 16d, 12d })
            {
                foreach (var scaling in new[] { 1d, 1.25d, 1.5d, 1.75d, 2d })
                    data.Add(size, scaling);
            }

            return data;
        }
    }

    /// <summary>Набор ложится в пиксели на каждом масштабе, а не только на целых.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(Screens))]
    public void The_set_lands_on_pixels_at_every_scale(double size, double scaling)
    {
        var solid = 0;
        var covered = 0;

        foreach (var (_, data) in Icons())
        {
            var (one, edge) = Ink(data, size, scaling);

            solid += one;
            covered += one + edge;
        }

        var share = covered == 0 ? 0 : (double)solid / covered;

        Assert.True(
            share >= Floor,
            string.Create(
                CultureInfo.InvariantCulture,
                $"значок {size} при масштабе {scaling}: сплошных {share:P1} при пороге {Floor:P0} — набору нужна клетка под этот масштаб"));
    }

    /// <summary>
    /// Клетка берётся по числу пикселей, которое значок занял.
    /// </summary>
    /// <remarks>
    /// Правило простое, и проверяется оно на обоих размерах: 16 точек при 125 % — двадцать
    /// пикселей и клетка 20; 12 точек при 125 % — пятнадцать и клетка 15. Там, где клетки у набора
    /// нет, остаётся основная: путь клетки 16 при 200 % ложится в пиксели сам.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(16d, 1d, 16d)]
    [InlineData(16d, 1.25d, 20d)]
    [InlineData(16d, 1.5d, 24d)]
    [InlineData(16d, 1.75d, 28d)]
    [InlineData(16d, 2d, 16d)]
    [InlineData(12d, 1d, 12d)]
    [InlineData(12d, 1.25d, 15d)]
    [InlineData(12d, 1.5d, 18d)]
    [InlineData(12d, 1.75d, 21d)]
    [InlineData(12d, 2d, 24d)]
    public void An_icon_takes_the_cell_of_the_pixels_it_got(double size, double scaling, double cell)
    {
        var icon = Shown(AxIcons.Plus, size, scaling);

        Assert.Equal(cell, icon.Icon.Cell);

        icon.Window.Close();
    }

    /// <summary>Счётчик видит весь набор: иначе порог держался бы на десятке значков.</summary>
    [Fact]
    public void The_measure_sees_every_icon() => Assert.True(Icons().Count > 60, "набор пересчитан не весь");

    private static List<(string Name, Geometry Data)> Icons() =>
    [
        .. typeof(AxIcons)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(Geometry))
            .Select(property => (property.Name, (Geometry)property.GetValue(null)!)),
    ];

    /// <summary>Чернила значка белым на чёрном: сплошные пиксели и кайма.</summary>
    private static (int Solid, int Fringe) Ink(Geometry data, double size, double scaling)
    {
        var (_, window) = Shown(data, size, scaling);

        using var frame = window.CaptureRenderedFrame()!;
        using var pixels = frame.Lock();

        var solid = 0;
        var fringe = 0;

        for (var y = 0; y < pixels.Size.Height; y++)
        {
            for (var x = 0; x < pixels.Size.Width; x++)
            {
                // Белое на чёрном: все три канала равны, и порядок их в кадре не важен.
                var value = Marshal.ReadByte(pixels.Address, y * pixels.RowBytes + x * 4 + 1);

                if (value == 255)
                    solid++;
                else if (value > 0)
                    fringe++;
            }
        }

        window.Close();

        return (solid, fringe);
    }

    private static (AxIcon Icon, Window Window) Shown(Geometry data, double size, double scaling)
    {
        var icon = new AxIcon
        {
            Data = data,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        icon.Width = icon.Height = size;

        var window = new Window { Width = 32, Height = 32, Background = Brushes.Black, Content = icon };

        window.Show();
        window.SetRenderScaling(scaling);
        Dispatcher.UIThread.RunJobs();

        return (icon, window);
    }
}
