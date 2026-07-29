namespace CopilotBridge.UI;

internal readonly record struct DesktopLayout(
    double SidebarWidth,
    double ProjectColumnWidth,
    double ConversationColumnWidth,
    bool ShowSecondarySidebarText)
{
    internal static DesktopLayout ForWidth(double width) => width < 1180
        ? new DesktopLayout(220, 160, 190, false)
        : new DesktopLayout(264, 220, 280, true);
}
