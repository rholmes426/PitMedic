using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

// Render the actual checked-in WPF views with deterministic example data.
// No production constructors, monitors, repair services, Steam or network calls run.
internal static class Program
{
    static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    static string source = "";
    static string output = "";

    [STAThread]
    static void Main(string[] args)
    {
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        source = Path.Combine(root, "Source", "PitMedic");
        output = Path.Combine(root, "Artifacts", "website-captures");
        Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(source, "App.xaml"))
            .Root!.Element(Wpf + "Application.Resources")!;
        var dictionary = new XElement(Wpf + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", X.NamespaceName), resources.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());

        var prompt = Load("RepairPromptWindow.xaml", 680, 780);
        Text(prompt, "RepairTitle", "Replace damaged LMU content");
        Text(prompt, "DiagnosisText", "LMU content read / decompression failure");
        Text(prompt, "RepairSummary", "Replace only the affected content, then ask Steam to validate and reacquire clean files.");
        Text(prompt, "EstimateText", "~8 min");
        Items(prompt, "AffectedList", new[] { @"Installed\Locations\Silverstone_2025" });
        Items(prompt, "ReferenceList", new[] { "Le Mans Ultimate Support: Common Fixes", "Le Mans Ultimate Community: targeted track replacement" });
        Save(prompt, "pitmedic-repair-carbon-lime.png");

        var progress = Load("RepairProgressWindow.xaml", 820, 690);
        Text(progress, "RepairTitle", "Replace damaged LMU content");
        Text(progress, "StageText", "Reacquiring clean content");
        Text(progress, "MessageText", "Steam validation is in progress.");
        Text(progress, "RemainingText", "~5:00");
        Text(progress, "StepText", "Step 3 of 5");
        Text(progress, "PercentText", "38%");
        Text(progress, "ElapsedText", "Elapsed 3:00");
        Text(progress, "DetailText", "Waiting for Steam to finish validating and reacquiring the affected content.");
        ((ProgressBar)progress.FindName("RepairProgress")).Value = 38;
        Items(progress, "ActivityList", new[] {
            "[10:00:00 AM] Preparing repair — Affected content identified.",
            "[10:00:15 AM] Recovery copy — Existing content preserved.",
            "[10:00:30 AM] Starting Steam validation — Request sent.",
            "[10:03:00 AM] Reacquiring clean content — Waiting for Steam."
        });
        Save(progress, "lmu-repair-progress-carbon-lime.png");

        var complete = Load("IncidentDetailsWindow.xaml", 860, 788);
        Text(complete, "TitleText", "Le Mans Ultimate");
        Text(complete, "TimeText", "Example finding · Le Mans Ultimate.exe");
        Text(complete, "OutcomeSummaryText", "Steam validation completed and the affected content was restored. The finding is marked resolved.");
        Text(complete, "NextStepText", "Launch Le Mans Ultimate and retry the affected track.");
        Text(complete, "RepairTimingText", "Repair completed · Duration 8 minutes");
        Text(complete, "CategoryText", "LMU content read / decompression failure");
        Text(complete, "PlainLanguageText", "Le Mans Ultimate could not read an installed track package. PitMedic identified the affected content and guided its replacement through Steam.");
        Items(complete, "RepairActivityList", new[] {
            new { Title = "Preserved recovery data", Detail = "Kept a recovery copy before replacing affected content.", Timestamp = new DateTime(2026, 10, 1, 10, 0, 0) },
            new { Title = "Requested Steam validation", Detail = "Steam validated the installation and reacquired clean content.", Timestamp = new DateTime(2026, 10, 1, 10, 1, 0) },
            new { Title = "Recorded the repair outcome", Detail = "Saved the repair history and marked the finding resolved.", Timestamp = new DateTime(2026, 10, 1, 10, 8, 0) }
        });
        Items(complete, "EvidenceList", new[] { @"Affected content: Installed\Locations\Silverstone_2025", "Example content-read failure used to demonstrate the repair review." });
        complete.FindName("ReferencesCard").As<FrameworkElement>().Visibility = Visibility.Collapsed;
        Save(complete, "lmu-repair-complete-carbon-lime.png");
        app.Shutdown();
    }

    static T As<T>(this object value) => (T)value;
    static void Text(Window w, string name, string value) => ((TextBlock)w.FindName(name)).Text = value;
    static void Items(Window w, string name, System.Collections.IEnumerable values) => ((ItemsControl)w.FindName(name)).ItemsSource = values;

    static Window Load(string name, int width, int height)
    {
        var document = XDocument.Load(Path.Combine(source, name));
        foreach (var node in document.Descendants())
        {
            node.Attribute(X + "Class")?.Remove();
            node.Attribute("Click")?.Remove();
            foreach (var attribute in node.Attributes().ToArray())
                if (attribute.Value.StartsWith("Assets/", StringComparison.Ordinal))
                    attribute.Value = new Uri(Path.Combine(source, attribute.Value)).AbsoluteUri;
        }
        var window = (Window)XamlReader.Parse(document.ToString());
        window.Width = width;
        window.Height = height;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.UseLayoutRounding = true;
        return window;
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    static void Save(Window window, string filename)
    {
        window.Show();
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        if (window.FindName("RepairNowButton") is Button primary)
        {
            var label = Descendants(primary).OfType<TextBlock>().FirstOrDefault(t => t.Text == "Repair now");
            if (label?.Foreground is not SolidColorBrush brush || brush.Color != Color.FromRgb(11, 15, 13))
                throw new InvalidOperationException("Primary button text must render dark on lime.");
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(output, filename);
        using (var stream = File.Create(path)) encoder.Save(stream);
        Console.WriteLine($"CAPTURE_BEGIN:{filename}");
        Console.WriteLine(Convert.ToBase64String(File.ReadAllBytes(path)));
        Console.WriteLine($"CAPTURE_END:{filename}");
        window.Close();
    }
}
