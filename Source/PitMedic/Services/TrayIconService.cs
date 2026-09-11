using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace PitMedic.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _trayImage;
    private readonly MainWindow _window;
    private readonly Action _exit;

    public TrayIconService(MainWindow window, Action exit)
    {
        _window = window;
        _exit = exit;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open PitMedic", null, (_, _) => ShowWindow());
        menu.Items.Add("Settings", null, (_, _) => _window.Dispatcher.Invoke(_window.OpenSettingsDialog));
        menu.Items.Add("Issue history", null, (_, _) => _window.Dispatcher.Invoke(_window.OpenIncidentHistory));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _exit());

        _trayImage = LoadTrayImage();

        _icon = new Forms.NotifyIcon
        {
            Text = "PitMedic — Performance & Reliability Monitor",
            Icon = _trayImage,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowWindow();
    }

    private static Icon LoadTrayImage()
    {
        // Read the packaged image directly; shell associations may cache an older EXE icon.
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/PitMedic.ico", UriKind.Absolute));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var image = new Icon(stream, Forms.SystemInformation.SmallIconSize);
                return (Icon)image.Clone();
            }
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }

    private void ShowWindow()
    {
        _window.Dispatcher.Invoke(() =>
        {
            _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        });
    }


    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _trayImage.Dispose();
    }
}
