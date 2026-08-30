using System.Windows;
using PitMedic.Services;

namespace PitMedic;

public partial class AnonymousUsagePreviewWindow : Window
{
    private AnonymousUsagePreviewWindow(AnonymousUsageService anonymousUsage)
    {
        InitializeComponent();
        PayloadText.Text = anonymousUsage.BuildDataPreview();
    }

    public static void ShowFor(Window owner, AnonymousUsageService anonymousUsage)
    {
        var window = new AnonymousUsagePreviewWindow(anonymousUsage) { Owner = owner };
        window.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
