using System.Windows;
using System.Windows.Controls;
using PitMedic.Models;
using PitMedic.Services;

namespace PitMedic;

public sealed class ConfigurationRepairWindow : Window
{
    private readonly ComboBox _choices;
    private readonly TextBlock _description;
    private readonly Button _restore;
    private readonly MonitoringCoordinator _monitoring;
    private readonly GameKind? _game;
    private readonly Action<IncidentSummary> _openRequest;

    public ConfigurationRepairWindow(MonitoringCoordinator monitoring, GameKind? game, Action<IncidentSummary> openRequest)
    {
        _monitoring = monitoring; _game = game; _openRequest = openRequest;
        Title = "PitMedic · Repair tools";
        Width = 650; Height = 430; MinWidth = 560; MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontSize = 14;
        SetResourceReference(FontFamilyProperty, "BodyFont");
        SetResourceReference(BackgroundProperty, "PanelBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = game.HasValue ? "Repair a specific problem" : "Windows troubleshooting", FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = game.HasValue
                ? "Choose the symptom you are experiencing. Close the simulator and let updates finish before applying a repair."
                : "Choose a specific Windows problem. Close any running simulators and let updates finish. Each action requires approval.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18)
        });
        var choices = game is GameKind selectedGame
            ? UserRequestedRepairPolicy.OptionsFor(selectedGame).Select(x => new RepairChoice(x.Id, x.Title, x.Description, x.Files.Length > 0))
            : WindowsRepairPolicy.Options.Select(x => new RepairChoice(x.Id, x.Title, x.Summary, false));
        _choices = new ComboBox
        {
            ItemsSource = choices
                .Select(x => new ComboBoxItem { Content = x.Title, Tag = x }).ToArray(),
            SelectedIndex = 0, MinHeight = 38
        };
        _description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 20) };
        _choices.SelectionChanged += (_, _) => ShowDescription();
        panel.Children.Add(_choices); panel.Children.Add(_description);
        var actions = new WrapPanel();
        var apply = new Button { Content = "Review repair", Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 12, 12) };
        apply.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        apply.Click += (_, _) => Open(false);
        _restore = new Button { Content = "Restore last backup", Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Restore the latest completed backup for the selected repair. Current files are preserved before Restore." };
        _restore.Click += (_, _) => Open(true);
        if (!game.HasValue) _restore.Visibility = Visibility.Collapsed;
        actions.Children.Add(apply); actions.Children.Add(_restore); panel.Children.Add(actions);
        panel.Children.Add(new TextBlock
        {
            Text = "PitMedic records the operation. Retest the same symptom after it finishes; completing an operation does not establish that this was the cause.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12
        });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        ShowDescription();
    }

    private void ShowDescription()
    {
        var option = (_choices.SelectedItem as ComboBoxItem)?.Tag as RepairChoice;
        _description.Text = option?.Description ?? "";
        if (_restore is not null) _restore.IsEnabled = option?.HasRestore == true;
    }

    private void Open(bool restore)
    {
        if ((_choices.SelectedItem as ComboBoxItem)?.Tag is not RepairChoice option || restore && !option.HasRestore) return;
        if (_monitoring.CurrentRepair?.IsActive == true)
        { MessageBox.Show(this, "Wait for the current repair to finish.", "PitMedic"); return; }
        var busy = _game is GameKind game
            ? _monitoring.IsGameRunning(game) || game == GameKind.IRacing && IRacingUpdateGuard.IsBusy()
            : GameDefinition.Supported.Any(x => _monitoring.IsGameRunning(x.Kind)) || IRacingUpdateGuard.IsBusy();
        if (busy)
        { MessageBox.Show(this, "Close the simulator and let any update finish, then try again.", "PitMedic"); return; }
        try
        {
            var summary = _game is GameKind selectedGame
                ? _monitoring.CreateConfigurationRepairRequest(selectedGame, option.Id + (restore ? "-restore" : ""))
                : _monitoring.CreateWindowsRepairRequest(option.Id);
            Close();
            _openRequest(summary);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PitMedic", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private sealed record RepairChoice(string Id, string Title, string Description, bool HasRestore);
}
