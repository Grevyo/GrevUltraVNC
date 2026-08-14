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
            CursorStyleCatalog.Grev => CreateGrevPointer(brush),
            CursorStyleCatalog.ChatGpt => CreateChatGptPointer(brush),
            CursorStyleCatalog.Crosshair => CreateCrosshairPointer(brush),
            CursorStyleCatalog.Ring => CreateRingPointer(brush),
            CursorStyleCatalog.Diamond => CreateDiamondPointer(brush),
            CursorStyleCatalog.Pixel => CreatePixelPointer(brush),
            CursorStyleCatalog.SlimArrow => CreateSlimArrowPointer(brush),
            CursorStyleCatalog.Chevron => CreateChevronPointer(brush),
            CursorStyleCatalog.Target => CreateTargetPointer(brush),
            CursorStyleCatalog.Square => CreateSquarePointer(brush),
            CursorStyleCatalog.Bolt => CreateBoltPointer(brush),
            CursorStyleCatalog.Hand => CreateHandPointer(brush),
            _ => CreateArrowPointer(brush)
        };
    }

    public static FrameworkElement CreatePreview(string cursorStyle, Brush brush, double size = 28)
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
        // 1.1.4 redraw: a tighter, cleaner version of Grev's original wonky cyan-outline sketch.
        // It deliberately keeps the hooked head, long kinked body and bulbous tail so it still
        // feels handmade rather than turning into another ordinary arrow.
        var canvas = new Canvas { Width = 30, Height = 30, Tag = new Point(3, 3) };
        var path = new Shapes.Path
        {
            Data = Geometry.Parse("M 4,3 C 8,1 13,2 15,5 C 16,7 15,10 17,12 L 22,16 C 24,15 27,16 28,19 C 29,22 27,24 24,24 C 24,27 22,29 19,29 C 16,29 14,27 14,24 L 15,20 L 10,16 C 8,14 7,12 6,11 C 3,12 1,10 1,7 C 1,5 2,4 4,3 Z"),
            Stroke = brush,
            StrokeThickness = 2.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };
        canvas.Children.Add(path);
        return canvas;
    }

    private static FrameworkElement CreateChatGptPointer(Brush brush)
    {
        // ChatGPT Squiggle: an original smoother sibling to Grev's cursor. It uses a flowing
        // double-loop/ribbon silhouette rather than copying the Grev geometry or the OpenAI mark.
        var canvas = new Canvas { Width = 30, Height = 30, Tag = new Point(3, 3) };
        var path = new Shapes.Path
        {
            Data = Geometry.Parse("M 3,5 C 5,2 9,1 12,3 C 14,5 13,8 11,10 C 9,12 9,14 12,15 C 15,16 18,13 21,14 C 25,14 28,17 28,21 C 28,25 25,28 21,28 C 18,28 16,26 15,24 C 14,22 12,21 10,22 C 7,24 4,23 2,20 C 0,17 1,14 4,12 C 6,11 7,9 6,8 C 5,6 4,6 3,5 Z"),
            Stroke = brush,
            StrokeThickness = 2.6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };
        canvas.Children.Add(path);

        var inner = new Shapes.Path
        {
            Data = Geometry.Parse("M 8,8 C 10,6 13,7 13,10 C 13,12 12,13 11,14 M 17,19 C 19,17 22,18 22,21 C 22,23 20,24 18,23"),
            Stroke = brush,
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };
        canvas.Children.Add(inner);
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

    private static FrameworkElement CreateSlimArrowPointer(Brush brush)
    {
        var grid = new Grid { Width = 24, Height = 27, Tag = new Point(1, 1) };
        grid.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 1,1 L 2,23 L 7,17 L 11,26 L 14,24 L 10,16 L 20,16 Z"),
            Fill = Brushes.Transparent,
            Stroke = brush,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        });
        return grid;
    }

    private static FrameworkElement CreateChevronPointer(Brush brush)
    {
        var grid = new Grid { Width = 26, Height = 27, Tag = new Point(2, 2) };
        grid.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 2,2 L 23,12 L 14,15 L 18,24 L 14,26 L 10,17 L 4,23 Z"),
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

    private static FrameworkElement CreateTargetPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 28, Height = 28, Tag = new Point(14, 14) };
        var outer = new Shapes.Ellipse { Width = 20, Height = 20, Stroke = brush, StrokeThickness = 2.2 };
        Canvas.SetLeft(outer, 4);
        Canvas.SetTop(outer, 4);
        canvas.Children.Add(outer);
        var inner = new Shapes.Ellipse { Width = 8, Height = 8, Stroke = brush, StrokeThickness = 2 };
        Canvas.SetLeft(inner, 10);
        Canvas.SetTop(inner, 10);
        canvas.Children.Add(inner);
        var dot = new Shapes.Ellipse { Width = 3.5, Height = 3.5, Fill = brush };
        Canvas.SetLeft(dot, 12.25);
        Canvas.SetTop(dot, 12.25);
        canvas.Children.Add(dot);
        canvas.Children.Add(new Shapes.Line { X1 = 14, X2 = 14, Y1 = 0, Y2 = 5, Stroke = brush, StrokeThickness = 2 });
        canvas.Children.Add(new Shapes.Line { X1 = 14, X2 = 14, Y1 = 23, Y2 = 28, Stroke = brush, StrokeThickness = 2 });
        canvas.Children.Add(new Shapes.Line { X1 = 0, X2 = 5, Y1 = 14, Y2 = 14, Stroke = brush, StrokeThickness = 2 });
        canvas.Children.Add(new Shapes.Line { X1 = 23, X2 = 28, Y1 = 14, Y2 = 14, Stroke = brush, StrokeThickness = 2 });
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

    private static FrameworkElement CreateSquarePointer(Brush brush)
    {
        var canvas = new Canvas { Width = 26, Height = 26, Tag = new Point(13, 13) };
        var square = new Shapes.Rectangle
        {
            Width = 18,
            Height = 18,
            Stroke = brush,
            StrokeThickness = 2.5,
            RadiusX = 2,
            RadiusY = 2,
            Fill = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255))
        };
        Canvas.SetLeft(square, 4);
        Canvas.SetTop(square, 4);
        canvas.Children.Add(square);
        var dot = new Shapes.Ellipse { Width = 4, Height = 4, Fill = brush };
        Canvas.SetLeft(dot, 11);
        Canvas.SetTop(dot, 11);
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

    private static FrameworkElement CreateBoltPointer(Brush brush)
    {
        var grid = new Grid { Width = 24, Height = 28, Tag = new Point(4, 2) };
        grid.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 10,1 L 3,15 L 10,14 L 7,27 L 22,10 L 14,11 L 18,1 Z"),
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Round
        });
        return grid;
    }

    private static FrameworkElement CreateHandPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 28, Height = 29, Tag = new Point(9, 1) };
        var hand = new Shapes.Path
        {
            Data = Geometry.Parse("M 8,1 C 6,1 5,3 5,5 L 5,15 L 3,13 C 2,12 0,13 1,15 L 6,24 C 7,27 10,28 14,28 L 18,28 C 22,28 25,25 25,21 L 25,12 C 25,10 22,9 21,11 L 21,9 C 21,7 18,6 17,8 L 17,7 C 17,5 14,4 13,6 L 13,5 C 13,2 11,1 8,1 Z"),
            Fill = brush,
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Round
        };
        canvas.Children.Add(hand);
        return canvas;
    }
}
