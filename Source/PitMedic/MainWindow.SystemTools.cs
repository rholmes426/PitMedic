using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PitMedic;

public partial class MainWindow
{
    private SystemToolsPanel? _systemToolsPanel;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoadedForSystemTools));

        // v0.6.0.1 wired this button directly to the browser. Intercept the routed
        // click at the Button class level before the legacy instance handler runs,
        // keeping the update download, verification, and install handoff in PitMedic.
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnButtonClickForInAppUpdate));
    }

    private static void OnMainWindowLoadedForSystemTools(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
        {
            window.InstallSystemToolsPanel();
            window.RenameUpdateAction();
        }
    }

    private static void OnButtonClickForInAppUpdate(object sender, RoutedEventArgs e)
    {
        if (e.Handled || sender is not Button button) return;
        var content = button.Content as string;
        if (!string.Equals(content, "Download update", StringComparison.Ordinal)
            && !string.Equals(content, "Install update", StringComparison.Ordinal))
            return;

        if (Window.GetWindow(button) is not MainWindow window || window._availableUpdate is null)
            return;

        e.Handled = true;
        window.OpenInAppUpdate(window._availableUpdate);
    }

    private void OpenInAppUpdate(PitMedic.Services.AvailableUpdate update)
    {
        var window = new UpdateInstallWindow(update) { Owner = this };
        window.ShowDialog();
    }

    private void RenameUpdateAction()
    {
        var button = FindVisualDescendant<Button>(this, candidate =>
            string.Equals(candidate.Content as string, "Download update", StringComparison.Ordinal));
        if (button is not null)
            button.Content = "Install update";
    }

    private void InstallSystemToolsPanel()
    {
        if (_systemToolsPanel is not null || HomeMonitoringSummary is null) return;

        var readinessCard = FindVisualAncestor<Border>(HomeMonitoringSummary);
        if (readinessCard?.Parent is not StackPanel homeStack) return;

        var index = homeStack.Children.IndexOf(readinessCard);
        if (index < 0) return;

        _systemToolsPanel = new SystemToolsPanel
        {
            Margin = readinessCard.Margin
        };

        homeStack.Children.RemoveAt(index);
        homeStack.Children.Insert(index, _systemToolsPanel);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent, Func<T, bool> predicate) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && predicate(match)) return match;
            var nested = FindVisualDescendant(child, predicate);
            if (nested is not null) return nested;
        }
        return null;
    }
}
