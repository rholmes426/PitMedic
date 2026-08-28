using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitMedic.Models;

namespace PitMedic;

public partial class IncidentDetailsWindow : Window
{
    private readonly IncidentDetailsData _details;
    private readonly Action? _runAutomaticRepair;

    public IncidentDetailsWindow(IncidentDetailsData details, Action? runAutomaticRepair = null)
    {
        InitializeComponent();
        _details = details;
        _runAutomaticRepair = runAutomaticRepair;
        var incident = details.Incident;

        TitleText.Text = incident.Game;
        TimeText.Text = $"{incident.IncidentTime:MMM d, yyyy  ·  h:mm:ss tt}  ·  {incident.Executable}";
        CategoryText.Text = incident.Classification.Category;
        PlainLanguageText.Text = details.PlainLanguageExplanation;
        OutcomeHeadlineText.Text = details.OutcomeHeadline;
        OutcomeSummaryText.Text = details.ResolutionSummary;
        NextStepText.Text = details.NextStep;
        EvidenceList.ItemsSource = incident.Classification.Evidence.Count > 0
            ? incident.Classification.Evidence
            : new[] { "PitMedic preserved the simulator session record for comparison with any repeat occurrence." };

        ConfigureState();
        ConfigureRepairWork();
        ConfigureTiming();

        ReferencesList.ItemsSource = details.References;
        ReferencesCard.Visibility = details.References.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(details.BackupFolder) && Directory.Exists(details.BackupFolder))
        {
            RecoveryCard.Visibility = Visibility.Visible;
            var folderName = Path.GetFileName(details.BackupFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            RecoveryDetailText.Text = string.IsNullOrWhiteSpace(folderName)
                ? "The recovery copy is available from this finding."
                : $"Recovery set: {folderName}";
        }
    }

    private void ConfigureState()
    {
        if (_details.IsResolved)
        {
            SetState("RESOLVED", "✓", "GoodSoftBrush", "GoodBorderBrush", "GoodBrush", "GoodTextBrush");
            return;
        }

        if (_details.RepairInProgress)
        {
            SetState("REPAIR RUNNING", "↻", "InfoSoftBrush", "InfoBorderBrush", "TelemetryBrush", "InfoTextBrush");
            return;
        }

        if (_details.RepairFailed)
        {
            SetState("NEEDS ATTENTION", "!", "DangerSoftBrush", "DangerBorderBrush", "DangerBrush", "DangerTextBrush");
            return;
        }

        if (_details.RepairCancelled)
        {
            SetState("REPAIR CANCELLED", "!", "WarnSoftBrush", "WarnBorderBrush", "WarnBrush", "WarnTextBrush");
            return;
        }

        if (_details.RepairPlan is not null)
        {
            SetState("REPAIR AVAILABLE", "!", "WarnSoftBrush", "WarnBorderBrush", "AccentBrush", "WarnTextBrush");
            return;
        }

        SetState("CAPTURED", "i", "InfoSoftBrush", "InfoBorderBrush", "TelemetryBrush", "InfoTextBrush");
    }

    private void SetState(string label, string icon, string background, string border, string dot, string foreground)
    {
        StateText.Text = label;
        OutcomeIcon.Text = icon;
        StatePill.SetResourceReference(Border.BackgroundProperty, background);
        StatePill.SetResourceReference(Border.BorderBrushProperty, border);
        StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dot);
        StateText.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        OutcomeCard.SetResourceReference(Border.BackgroundProperty, background);
        OutcomeCard.SetResourceReference(Border.BorderBrushProperty, border);
        OutcomeIcon.SetResourceReference(TextBlock.ForegroundProperty, foreground);
    }

    private void ConfigureRepairWork()
    {
        if (_details.RepairAttempted)
        {
            RepairWorkHeading.Text = "WHAT PITMEDIC DID";
            RepairActivityList.Visibility = Visibility.Visible;
            RepairActivityList.ItemsSource = _details.RepairActivity;
            NoChangesPanel.Visibility = _details.RepairActivity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            RepairWorkStatus.Text = _details.IsResolved
                ? "COMPLETED"
                : _details.RepairInProgress ? "IN PROGRESS"
                : _details.RepairCancelled ? "CANCELLED"
                : _details.RepairFailed ? "INCOMPLETE"
                : "RECORDED";
            RepairWorkStatus.SetResourceReference(TextBlock.ForegroundProperty,
                _details.IsResolved ? "GoodTextBrush"
                : _details.RepairFailed ? "DangerTextBrush"
                : _details.RepairInProgress ? "InfoTextBrush"
                : "WarnTextBrush");
        }
        else
        {
            RepairWorkHeading.Text = _details.RepairPlan is null ? "WHAT PITMEDIC DID" : "WHAT PITMEDIC WILL DO";
            RepairWorkStatus.Text = _details.RepairPlan is null ? "NO CHANGES" : "PENDING APPROVAL";
            RepairWorkStatus.SetResourceReference(TextBlock.ForegroundProperty,
                _details.RepairPlan is null ? "MutedBrush" : "WarnTextBrush");
            RepairActivityList.Visibility = Visibility.Collapsed;
            NoChangesPanel.Visibility = Visibility.Visible;
        }

        var canRunRepair = _details.RepairPlan is not null
            && !_details.IsResolved
            && !_details.RepairInProgress
            && _runAutomaticRepair is not null;
        ProposedRepairPanel.Visibility = canRunRepair ? Visibility.Visible : Visibility.Collapsed;
        RunAutomaticRepairButton.Visibility = canRunRepair ? Visibility.Visible : Visibility.Collapsed;

        if (_details.RepairPlan is not null)
        {
            var minutes = Math.Max(1, _details.RepairPlan.EstimatedMinutes);
            ProposedRepairSummary.Text = $"{_details.RepairPlan.Summary} Expected time: about {minutes} minute{(minutes == 1 ? string.Empty : "s")}.";
            ProposedActionsList.ItemsSource = _details.RepairPlan.Steps;
        }
    }

    private void ConfigureTiming()
    {
        if (_details.RepairStarted.HasValue && _details.RepairUpdated.HasValue)
        {
            var elapsed = _details.RepairUpdated.Value - _details.RepairStarted.Value;
            RepairTimingText.Text = $"Repair started {_details.RepairStarted.Value:h:mm:ss tt}  ·  last updated {_details.RepairUpdated.Value:h:mm:ss tt}  ·  {FormatDuration(elapsed)}";
            return;
        }

        if (_details.RepairStarted.HasValue)
        {
            RepairTimingText.Text = $"Repair started {_details.RepairStarted.Value:h:mm:ss tt}";
            return;
        }

        if (_details.RepairPlan is not null)
        {
            var minutes = Math.Max(1, _details.RepairPlan.EstimatedMinutes);
            RepairTimingText.Text = $"Estimated repair time: about {minutes} minute{(minutes == 1 ? string.Empty : "s")}";
            return;
        }

        RepairTimingText.Visibility = Visibility.Collapsed;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60) return $"{Math.Max(1, Math.Round(duration.TotalSeconds)):0} sec";
        return $"{Math.Max(1, Math.Round(duration.TotalMinutes)):0} min";
    }

    private void RunAutomaticRepair_Click(object sender, RoutedEventArgs e)
    {
        if (_runAutomaticRepair is null) return;
        var action = _runAutomaticRepair;
        Close();
        action();
    }

    private void Reference_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not RepairReference reference || string.IsNullOrWhiteSpace(reference.Url)) return;
        try { Process.Start(new ProcessStartInfo(reference.Url) { UseShellExecute = true }); } catch { }
    }

    private void RecoveryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_details.BackupFolder) || !Directory.Exists(_details.BackupFolder)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", _details.BackupFolder) { UseShellExecute = true }); } catch { }
    }

    private void EvidenceFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", _details.Incident.IncidentFolder) { UseShellExecute = true }); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
