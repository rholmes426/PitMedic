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
    private readonly GameKind _game;
    private readonly Action<IncidentSummary> _openRequest;

    public ConfigurationRepairWindow(MonitoringCoordinator monitoring, GameKind game, Action<IncidentSummary> openRequest)
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
        panel.Children.Add(new TextBlock { Text = "Repair a specific problem", FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Choose the symptom you are experiencing. Close the simulator and let updates finish before applying a repair.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18)
        });
        _choices = new ComboBox
        {
            ItemsSource = UserRequestedRepairPolicy.OptionsFor(game)
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
        var option = (_choices.SelectedItem as ComboBoxItem)?.Tag as ConfigurationRepairOption;
        _description.Text = option?.Description ?? "";
        if (_restore is not null) _restore.IsEnabled = option is not null && option.Files.Length > 0;
    }

    private void Open(bool restore)
    {
        if ((_choices.SelectedItem as ComboBoxItem)?.Tag is not ConfigurationRepairOption option) return;
        if (_monitoring.CurrentRepair?.IsActive == true)
        { MessageBox.Show(this, "Wait for the current repair to finish.", "PitMedic"); return; }
        if (_monitoring.IsGameRunning(_game) || _game == GameKind.IRacing && IRacingUpdateGuard.IsBusy())
        { MessageBox.Show(this, "Close the simulator and let any update finish, then try again.", "PitMedic"); return; }
        try
        {
            var summary = _monitoring.CreateConfigurationRepairRequest(_game, option.Id + (restore ? "-restore" : ""));
            Close();
            _openRequest(summary);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "PitMedic", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
}
