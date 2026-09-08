using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace GrevUltraVNC;

public partial class GrevControlPanelWindow
{
    /// <summary>
    /// Section id to its header toggle and body. Ids are persisted, so renaming one
    /// only costs a user their collapsed state once.
    /// </summary>
    private IReadOnlyList<(string Id, ToggleButton Header, FrameworkElement Body)> PanelSections =>
    [
        ("screens", ViewerSectionButton, ViewerSectionBody),
        ("keys", RemoteKeysSectionButton, RemoteKeysSectionBody),
        ("tools", ToolsSectionButton, ToolsSectionBody),
        ("pc", PcSectionButton, PcSectionBody),
        ("people", PeopleSectionButton, PeopleSectionBody)
    ];

    private void TogglePanelSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton header || header.Tag is not string id)
            return;

        var section = PanelSections.FirstOrDefault(item => item.Id == id);
        if (section.Body is null)
            return;

        // The ToggleButton has already flipped by the time Click fires, so its state is
        // the intent and the body simply follows it.
        section.Body.Visibility = header.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SaveCollapsedSections();
    }

    /// <summary>Restores the sections the user had collapsed last time.</summary>
    private void ApplyStoredSectionState()
    {
        var collapsed = ParseCollapsedSections(_collaborationSettings.ControlPanelCollapsedSections);

        foreach (var (id, header, body) in PanelSections)
        {
            var expanded = !collapsed.Contains(id);
            header.IsChecked = expanded;
            body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SaveCollapsedSections()
    {
        var collapsed = PanelSections
            .Where(section => section.Header.IsChecked != true)
            .Select(section => section.Id);

        var stored = string.Join(",", collapsed);
        if (string.Equals(stored, _collaborationSettings.ControlPanelCollapsedSections, StringComparison.Ordinal))
            return;

        _collaborationSettings.ControlPanelCollapsedSections = stored;

        // The dashboard owns persistence; this is the same event the cursor picker uses.
        CollaborationSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static HashSet<string> ParseCollapsedSections(string? stored) =>
        new(
            (stored ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
}
