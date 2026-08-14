using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    private bool _compactPanelManualPosition;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;

        var point = e.GetPosition(this);
        if (point.Y < 6d || point.Y > 66d)
            return;

        if (IsInsideButton(e.OriginalSource as DependencyObject))
            return;

        _compactPanelManualPosition = true;

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse button is released between the preview event
            // and the native move loop. Leave the panel in manual mode and allow another drag.
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
                return true;

            source = source switch
            {
                FrameworkElement element when element.Parent is not null => element.Parent,
                FrameworkContentElement content when content.Parent is not null => content.Parent,
                _ => TryGetVisualParent(source)
            };
        }

        return false;
    }

    private static DependencyObject? TryGetVisualParent(DependencyObject source)
    {
        try
        {
            return VisualTreeHelper.GetParent(source);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
