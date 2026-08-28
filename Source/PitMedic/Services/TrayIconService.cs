using System.Diagnostics;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace PitMedic.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
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

        Icon icon;
        try
        {
            var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            icon = !string.IsNullOrWhiteSpace(exe) ? Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application : SystemIcons.Application;
        }
        catch { icon = SystemIcons.Application; }

        _icon = new Forms.NotifyIcon
        {
            Text = "PitMedic — Performance & Reliability Monitor",
            Icon = icon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowWindow();
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
    }
}
