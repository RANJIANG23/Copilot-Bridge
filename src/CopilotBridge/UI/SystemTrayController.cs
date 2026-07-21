using System.Drawing;
using Forms = System.Windows.Forms;

namespace CopilotBridge.UI;

internal static class TrayClosePolicy
{
    internal static bool ShouldHide(bool enabled, bool explicitExit, bool sessionEnding) =>
        enabled && !explicitExit && !sessionEnding;
}

internal sealed class SystemTrayController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private readonly Icon? _ownedIcon;

    internal SystemTrayController(Action openRequested, Action exitRequested)
    {
        _openItem = new Forms.ToolStripMenuItem();
        _exitItem = new Forms.ToolStripMenuItem();
        _openItem.Click += (_, _) => openRequested();
        _exitItem.Click += (_, _) => exitRequested();

        _menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        _menu.Items.Add(_openItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(_exitItem);

        var executablePath = Environment.ProcessPath;
        _ownedIcon = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : Icon.ExtractAssociatedIcon(executablePath);
        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _ownedIcon ?? SystemIcons.Application,
            Text = "Copilot Bridge"
        };
        _notifyIcon.DoubleClick += (_, _) => openRequested();
    }

    internal bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    internal void UpdateText(string openText, string exitText)
    {
        _openItem.Text = openText;
        _exitItem.Text = exitText;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _ownedIcon?.Dispose();
    }
}
