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
            CursorStyleCatalog.Banana => CreateBananaPointer(brush),
            CursorStyleCatalog.Fish => CreateFishPointer(brush),
            CursorStyleCatalog.Ghost => CreateGhostPointer(brush),
            CursorStyleCatalog.Crown => CreateCrownPointer(brush),
            CursorStyleCatalog.Mug => CreateMugPointer(brush),
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
        // Restored exactly to the pre-1.1.4 Grev Squiggle geometry. Do not "clean this up" again:
        // the awkward shape is intentional and came from the supplied cyan-outline sketch.
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

    private static FrameworkElement CreateChatGptPointer(Brush brush)
    {
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

    private static FrameworkElement CreateBananaPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 30, Height = 30, Tag = new Point(5, 4) };
        canvas.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 5,4 C 7,10 11,17 18,21 C 22,23 26,23 28,20 C 24,27 17,29 11,25 C 5,21 2,13 3,7 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            Stroke = brush,
            StrokeThickness = 2.4,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        canvas.Children.Add(new Shapes.Line { X1 = 4, X2 = 7, Y1 = 4, Y2 = 2, Stroke = brush, StrokeThickness = 2.2 });
        return canvas;
    }

    private static FrameworkElement CreateFishPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 30, Height = 24, Tag = new Point(28, 12) };
        canvas.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 28,12 C 23,5 15,4 9,8 L 2,3 L 4,12 L 2,21 L 9,16 C 15,20 23,19 28,12 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            Stroke = brush,
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round
        });
        var eye = new Shapes.Ellipse { Width = 3, Height = 3, Fill = brush };
        Canvas.SetLeft(eye, 20);
        Canvas.SetTop(eye, 8);
        canvas.Children.Add(eye);
        return canvas;
    }

    private static FrameworkElement CreateGhostPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 28, Height = 30, Tag = new Point(14, 2) };
        canvas.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 14,2 C 7,2 3,7 3,14 L 3,27 L 8,23 L 12,27 L 16,23 L 20,27 L 25,23 L 25,14 C 25,7 21,2 14,2 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            Stroke = brush,
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round
        });
        var leftEye = new Shapes.Ellipse { Width = 3.5, Height = 4.5, Fill = brush };
        Canvas.SetLeft(leftEye, 9);
        Canvas.SetTop(leftEye, 11);
        canvas.Children.Add(leftEye);
        var rightEye = new Shapes.Ellipse { Width = 3.5, Height = 4.5, Fill = brush };
        Canvas.SetLeft(rightEye, 16);
        Canvas.SetTop(rightEye, 11);
        canvas.Children.Add(rightEye);
        return canvas;
    }

    private static FrameworkElement CreateCrownPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 30, Height = 27, Tag = new Point(15, 2) };
        canvas.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 3,7 L 9,13 L 14,3 L 19,13 L 27,6 L 24,23 L 5,23 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            Stroke = brush,
            StrokeThickness = 2.3,
            StrokeLineJoin = PenLineJoin.Round
        });
        canvas.Children.Add(new Shapes.Line { X1 = 6, X2 = 23, Y1 = 19, Y2 = 19, Stroke = brush, StrokeThickness = 2 });
        return canvas;
    }

    private static FrameworkElement CreateMugPointer(Brush brush)
    {
        var canvas = new Canvas { Width = 30, Height = 29, Tag = new Point(3, 5) };
        var mug = new Shapes.Rectangle
        {
            Width = 18,
            Height = 16,
            Stroke = brush,
            StrokeThickness = 2.3,
            RadiusX = 3,
            RadiusY = 3,
            Fill = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255))
        };
        Canvas.SetLeft(mug, 3);
        Canvas.SetTop(mug, 9);
        canvas.Children.Add(mug);
        var handle = new Shapes.Path
        {
            Data = Geometry.Parse("M 21,12 C 28,11 29,15 28,18 C 27,22 24,23 21,21"),
            Stroke = brush,
            StrokeThickness = 2.3,
            Fill = Brushes.Transparent,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        canvas.Children.Add(handle);
        canvas.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M 8,7 C 6,4 10,3 8,1 M 14,7 C 12,4 16,3 14,1"),
            Stroke = brush,
            StrokeThickness = 1.8,
            Fill = Brushes.Transparent,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        return canvas;
    }
}
