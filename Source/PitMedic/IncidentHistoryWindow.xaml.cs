using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PitMedic.Models;
using PitMedic.Services;

namespace PitMedic;

public partial class IncidentHistoryWindow : Window
{
    private readonly MonitoringCoordinator _monitoring;
    private readonly ObservableCollection<IncidentSummary> _items = new();

    public IncidentHistoryWindow(MonitoringCoordinator monitoring)
    {
        InitializeComponent();
        _monitoring = monitoring;
        HistoryList.ItemsSource = _items;
        Refresh();
        _monitoring.IncidentCreated += IncidentCreated;
        _monitoring.RepairStatusChanged += RepairChanged;
        Closed += (_, _) =>
        {
            _monitoring.IncidentCreated -= IncidentCreated;
            _monitoring.RepairStatusChanged -= RepairChanged;
        };
    }

    private void IncidentCreated(IncidentSummary _) => Dispatcher.BeginInvoke(new Action(Refresh));
    private void RepairChanged(RepairStatus status)
    {
        if (status.IsComplete) Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void Refresh()
    {
        _items.Clear();
        foreach (var incident in _monitoring.IncidentHistory()) _items.Add(incident);
        CountText.Text = $"{_items.Count} {(_items.Count == 1 ? "ISSUE" : "ISSUES")}";
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not IncidentSummary incident) return;
        ShowDetails(incident);
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Buttons already provide their own action; do not treat a button double-click as a row double-click.
        if (e.OriginalSource is DependencyObject origin && FindParent<Button>(origin) is not null) return;
        if (HistoryList.SelectedItem is IncidentSummary incident) ShowDetails(incident);
    }

    private void ShowDetails(IncidentSummary incident)
    {
        var details = _monitoring.GetIncidentDetails(incident.Folder);
        if (details is not null)
        {
            Action? runRepair = incident.RepairAvailable && !incident.IsResolved
                ? () =>
                {
                    new RepairPromptWindow(_monitoring, incident) { Owner = this }.ShowDialog();
                    Refresh();
                }
                : null;
            Func<bool>? acknowledgeFinding = !incident.IsResolved && !incident.IsDismissed
                ? () => _monitoring.AcknowledgeIncident(incident.Folder)
                : null;
            new IncidentDetailsWindow(details, runRepair, acknowledgeFinding) { Owner = this }.ShowDialog();
            Refresh();
            return;
        }

        MessageBox.Show(this,
            "PitMedic retained the issue evidence, but a user-friendly summary could not be reconstructed for this older record.",
            "Issue details unavailable",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
