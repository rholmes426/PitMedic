using System.Diagnostics;
using System.Windows;
using PitMedic.Models;
using PitMedic.Services;

namespace PitMedic;

public partial class RepairPromptWindow : Window
{
    private readonly MonitoringCoordinator _monitoring;
    private readonly IncidentSummary _summary;
    private readonly RepairPlan? _plan;
    private readonly string? _diagnosticLibraryUrl;

    public RepairPromptWindow(MonitoringCoordinator monitoring, IncidentSummary summary)
    {
        InitializeComponent();
        _monitoring = monitoring;
        _summary = summary;
        var incident = monitoring.GetIncident(summary.Folder);
        _plan = incident is not null ? RepairPlanner.TryCreateFromIncident(incident) : null;
        if (_plan is not null && _plan.References.Count == 0)
            _plan = _plan with { References = RepairKnowledgeBase.ReferencesForPlan(_plan.Id) };
        _diagnosticLibraryUrl = _plan is null ? null : RepairKnowledgeBase.DiagnosticLibraryUrlForPlan(_plan.Id);

        if (incident is null || _plan is null)
        {
            RepairTitle.Text = "No automatic repair available";
            DiagnosisText.Text = summary.Category;
            RepairSummary.Text = "PitMedic preserved the issue evidence, but this failure does not yet have a safe automatic repair playbook.";
            EstimateText.Text = "—";
            ApprovalNote.Text = "You can still open the evidence folder for the captured issue data.";
            RepairNowButton.IsEnabled = false;
            ReferencePanel.Visibility = Visibility.Collapsed;
            return;
        }

        RepairTitle.Text = _plan.Title;
        DiagnosisText.Text = incident.Classification.Category;
        RepairSummary.Text = _plan.Summary;
        EstimateText.Text = _plan.EstimatedMinutes <= 2 ? "under 2 min" : $"~{_plan.EstimatedMinutes} min";
        AffectedList.ItemsSource = _plan.AffectedContentRelativePaths.Select(p => $"Installed\\{p}").ToArray();
        AffectedContentPanel.Visibility = _plan.AffectedContentRelativePaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RepairBehaviorText.Text = CompanionRecoveryPolicy.IsSupportedRepairId(_plan.Id)
            ? _plan.Summary
            : "PitMedic follows the listed repair steps, preserves recovery data where the playbook changes files, and records every action with this finding.";
        ReferenceList.ItemsSource = _plan.References.Take(2).Select(r => $"{r.Source}: {r.Title}").ToArray();
        ReferencePanel.Visibility = _plan.References.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticLibraryButton.Visibility = _diagnosticLibraryUrl is null ? Visibility.Collapsed : Visibility.Visible;
        ApprovalNote.Text = _plan.EstimatedMinutes > 2
            ? "Because this repair is expected to take more than 2 minutes, PitMedic will only start it with your approval."
            : _plan.RequiresApproval
                ? "This recovery is quick, but PitMedic will only restart the companion software after you approve it."
                : "This repair is expected to be quick and reversible.";
    }

    private void RepairNow_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is null) return;
        if (_monitoring.BeginRepair(_summary.Folder))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ApprovalNote.Text = "A repair is already running, or PitMedic could not load this repair plan.";
        }
    }

    private void NotNow_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DiagnosticLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_diagnosticLibraryUrl)) return;
        try { Process.Start(new ProcessStartInfo(_diagnosticLibraryUrl) { UseShellExecute = true }); } catch { }
    }
}
