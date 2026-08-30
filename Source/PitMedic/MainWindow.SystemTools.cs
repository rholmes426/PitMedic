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
    }

    private static void OnMainWindowLoadedForSystemTools(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.InstallSystemToolsPanel();
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
}
