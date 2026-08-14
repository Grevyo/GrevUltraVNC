using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace GrevUltraVNC.Services;

public static class CursorVisualFactory
{
    public static FrameworkElement CreatePointer(string cursorStyle, Brush brush)
    {
        return CursorStyleCatalog.Normalize(cursorStyle) switch
        {
            CursorStyleCatalog.Arrow => CreateArrowPointer(brush),
            CursorStyleCatalog.Crosshair => CreateCrosshairPointer(brush),
            CursorStyleCatalog.Ring => CreateRingPointer(brush),
            CursorStyleCatalog.Diamond => CreateDiamondPointer(brush),
            CursorStyleCatalog.Pixel => CreatePixelPointer(brush),
            _ => CreateGrevPointer(brush)
        };
    }

    public static FrameworkElement CreatePreview(string cursorStyle, Brush brush, double size = 34)
    {
        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            Child = CreatePointer(cursorStyle, brush),
            IsHitTestVisible = false
        };
    }

    private static FrameworkElement CreateGrevPointer(Brush brush)
    {
        // Recreated from the supplied cyan-outline cursor sketch as a scalable WPF vector.
        var canvas = new Canvas { Width = 34, Height = 34, Tag = new Point(3, 3) };
        var path = new Shapes.Path
        {
            Data = Geometry.Parse("M 8,4 C 13,1 23,2 27,6 L 29,14 C 33,19 39,25 47,31 L 57,30 C 64,29 72,31 77,36 C 82,41 83,49 80,55 C 77,61 71,64 65,64 C 63,70 58,75 52,78 C 46,81 39,79 35,75 C 31,71 30,65 32,59 L 37,52 L 29,45 C 24,40 20,34 17,31 L 11,31 C 7,31 4,28 3,24 L 1,14 C 1,9 3,6 8,4 Z"),
            Stroke = brush,
            StrokeThickness = 4.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            Stretch = Stretch.Uniform,
            Width = 32,
            Height = 32
        };
        Canvas.SetLeft(path, 1);
        Canvas.SetTop(path, 1);
        canvas.Children.Add(path);
        return canvas;
    }

    private static FrameworkElement CreateArrowPointer(Brush brush)
    {
        var grid = new Grid { Width = 26, Height = 28, Tag = new Point(1, 1) };
        grid.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 2,23 L 8,17 L 13,27 L 18,24 L 13,15 L 22,15 Z"),
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Round
        });
        return grid;
    }

    private static FrameworkElement CreateCrosshairPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 28, Height = 28, Tag = new Point(14, 14) };
        var ring = new Shapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Stroke = brush,
            StrokeThickness = 2.5
        };
        Canvas.SetLeft(ring, 6);
        Canvas.SetTop(ring, 6);
        canvas.Children.Add(ring);
        canvas.Children.Add(new Shapes.Line { X1 = 1, X2 = 27, Y1 = 14, Y2 = 14, Stroke = brush, StrokeThickness = 2 });
        canvas.Children.Add(new Shapes.Line { X1 = 14, X2 = 14, Y1 = 1, Y2 = 27, Stroke = brush, StrokeThickness = 2 });
        return canvas;
    }

    private static FrameworkElement CreateRingPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 26, Height = 26, Tag = new Point(13, 13) };
        var ring = new Shapes.Ellipse
        {
            Width = 20,
            Height = 20,
            Stroke = brush,
            StrokeThickness = 3,
            Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255))
        };
        Canvas.SetLeft(ring, 3);
        Canvas.SetTop(ring, 3);
        canvas.Children.Add(ring);
        return canvas;
    }

    private static FrameworkElement CreateDiamondPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 26, Height = 26, Tag = new Point(13, 13) };
        canvas.Children.Add(new Shapes.Polygon
        {
            Points = new PointCollection
            {
                new(13, 1), new(25, 13), new(13, 25), new(1, 13)
            },
            Stroke = brush,
            StrokeThickness = 2.5,
            Fill = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)),
            StrokeLineJoin = PenLineJoin.Round
        });
        var dot = new Shapes.Ellipse { Width = 5, Height = 5, Fill = brush };
        Canvas.SetLeft(dot, 10.5);
        Canvas.SetTop(dot, 10.5);
        canvas.Children.Add(dot);
        return canvas;
    }

    private static FrameworkElement CreatePixelPointer(Brush brush)
    {
        var grid = new Grid { Width = 27, Height = 29, Tag = new Point(1, 1) };
        grid.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 1,22 L 6,22 L 6,17 L 10,17 L 15,28 L 20,25 L 15,15 L 23,15 L 23,11 L 18,11 L 18,8 L 13,8 L 13,5 L 8,5 L 8,1 Z"),
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Miter
        });
        return grid;
    }
}
