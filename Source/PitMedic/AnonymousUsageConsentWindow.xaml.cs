using System.Diagnostics;
using System.Windows;
using PitMedic.Services;

namespace PitMedic;

public partial class AnonymousUsageConsentWindow : Window
{
    private readonly AnonymousUsageService _anonymousUsage;

    public AnonymousUsageConsentWindow(AnonymousUsageService anonymousUsage)
    {
        InitializeComponent();
        _anonymousUsage = anonymousUsage;
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void NoThanks_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) =>
        AnonymousUsagePreviewWindow.ShowFor(this, _anonymousUsage);

    private void Privacy_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppInfo.PrivacyUrl) { UseShellExecute = true });
}
