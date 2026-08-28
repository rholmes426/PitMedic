using System.Collections.ObjectModel;
using System.Windows;
using PitMedic.Models;
using PitMedic.Services;

namespace PitMedic;

public partial class RepairProgressWindow : Window
{
    private readonly MonitoringCoordinator _monitoring;
    private readonly ObservableCollection<string> _activity = new();
    private string _lastActivity = string.Empty;
    private bool _allowClose;

    public RepairProgressWindow(MonitoringCoordinator monitoring)
    {
        InitializeComponent();
        _monitoring = monitoring;
        ActivityList.ItemsSource = _activity;
        _monitoring.RepairStatusChanged += OnRepairStatusChanged;
        Closed += (_, _) => _monitoring.RepairStatusChanged -= OnRepairStatusChanged;
        Closing += (_, e) =>
        {
            if (_allowClose || _monitoring.CurrentRepair?.IsActive != true) return;
            e.Cancel = true;
            Hide();
        };

        if (_monitoring.CurrentRepair is RepairStatus current)
            Apply(current);
    }

    private void OnRepairStatusChanged(RepairStatus status) => Dispatcher.BeginInvoke(() => Apply(status));

    private void Apply(RepairStatus status)
    {
        RepairTitle.Text = status.Title;
        StageText.Text = string.IsNullOrWhiteSpace(status.Stage) ? status.Title : status.Stage;
        MessageText.Text = status.Message;
        DetailText.Text = status.Detail;
        RepairProgress.Value = status.Percent;
        PercentText.Text = $"{status.Percent}%";
        StepText.Text = status.TotalSteps > 0 ? $"Step {Math.Max(1, status.StepNumber)} of {status.TotalSteps}" : "Repair workflow";

        var elapsed = status.StartedAt == default ? TimeSpan.Zero : DateTimeOffset.Now - status.StartedAt;
        ElapsedText.Text = $"Elapsed {FormatDuration((int)Math.Max(0, elapsed.TotalSeconds))}";
        RemainingText.Text = status.IsComplete
            ? status.Success ? "Complete" : "Needs attention"
            : status.EstimatedSecondsRemaining <= 0 ? "Finishing..." : $"~{FormatDuration(status.EstimatedSecondsRemaining)}";

        var signature = $"{status.Stage}|{status.Message}|{status.Detail}";
        var activity = $"[{DateTime.Now:h:mm:ss tt}] {StageText.Text} — {status.Message}";
        if (!string.Equals(signature, _lastActivity, StringComparison.Ordinal))
        {
            _activity.Add(activity);
            _lastActivity = signature;
            while (_activity.Count > 60) _activity.RemoveAt(0);
            if (ActivityList.Items.Count > 0) ActivityList.ScrollIntoView(ActivityList.Items[ActivityList.Items.Count - 1]);
        }

        if (status.IsActive && IsVisible && (status.Stage.Equals("Starting Steam validation", StringComparison.OrdinalIgnoreCase)
            || status.Stage.Equals("Reacquiring clean content", StringComparison.OrdinalIgnoreCase)))
        {
            // Steam validation should stay behind the PitMedic repair experience. If Windows briefly
            // shifts focus while Steam handles its protocol, reclaim focus only while this window is visible.
            Activate();
        }

        if (status.IsComplete)
        {
            StateText.Text = status.Success ? "RESOLVED" : "ATTENTION";
            StateText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, status.Success ? "GoodTextBrush" : "WarnTextBrush");
            StatePill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, status.Success ? "GoodSoftBrush" : "WarnSoftBrush");
            RepairProgress.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, status.Success ? "GoodBrush" : "WarnBrush");
            BackgroundButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
            FooterText.Text = status.Success
                ? "The linked issue is now marked resolved. You can retry the simulator when ready."
                : "The repair log and recovery data were preserved for review.";
        }
        else
        {
            StateText.Text = "ACTIVE";
            StateText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "GoodTextBrush");
            StatePill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "GoodSoftBrush");
            RepairProgress.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentBrush");
        }
    }

    private static string FormatDuration(int seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}";
    }

    private void Background_Click(object sender, RoutedEventArgs e) => Hide();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling...";
        _monitoring.CancelRepair();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Close();
    }
}
