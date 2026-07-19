using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CopilotBridge.Core;

internal enum ShortcutPinResult
{
    Pinned,
    ManualActionRequired
}

internal sealed class ShortcutManager
{
    private const string ShortcutFileName = "Copilot Bridge.lnk";
    private readonly string _executablePath;
    private readonly string _programsDirectory;
    private readonly string _desktopDirectory;

    internal ShortcutManager(
        string? executablePath = null,
        string? programsDirectory = null,
        string? desktopDirectory = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定 Copilot Bridge 可执行文件路径。");
        _programsDirectory = programsDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        _desktopDirectory = desktopDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    internal string CreateStartMenuShortcut() => CreateShortcut(_programsDirectory);

    internal string CreateDesktopShortcut() => CreateShortcut(_desktopDirectory);

    internal ShortcutPinResult PinToTaskbar(out string shortcutPath)
    {
        shortcutPath = CreateStartMenuShortcut();
        return TryInvokePinVerb(shortcutPath, "taskbarpin", "固定到任务栏", "pin to taskbar")
            ? ShortcutPinResult.Pinned
            : ShortcutPinResult.ManualActionRequired;
    }

    internal ShortcutPinResult PinToStart(out string shortcutPath)
    {
        shortcutPath = CreateStartMenuShortcut();
        return TryInvokePinVerb(shortcutPath, "startpin", "固定到“开始”", "固定到开始", "pin to start")
            ? ShortcutPinResult.Pinned
            : ShortcutPinResult.ManualActionRequired;
    }

    internal static void OpenShortcutLocation(string shortcutPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{shortcutPath}\"",
            UseShellExecute = true
        });
    }

    private string CreateShortcut(string directory)
    {
        Directory.CreateDirectory(directory);
        var shortcutPath = Path.Combine(directory, ShortcutFileName);
        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。");
            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法启动 Windows 快捷方式服务。");
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = _executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? string.Empty;
            shortcut.Description = "Configure and diagnose Copilot Bridge";
            shortcut.IconLocation = $"{_executablePath},0";
            shortcut.Save();
            return shortcutPath;
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static bool TryInvokePinVerb(string shortcutPath, params string[] acceptedNames)
    {
        object? shellObject = null;
        object? folderObject = null;
        object? itemObject = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null) return false;
            dynamic shell = shellObject;
            folderObject = shell.NameSpace(Path.GetDirectoryName(shortcutPath));
            if (folderObject is null) return false;
            dynamic folder = folderObject;
            itemObject = folder.ParseName(Path.GetFileName(shortcutPath));
            if (itemObject is null) return false;
            dynamic item = itemObject;
            foreach (var candidate in item.Verbs())
            {
                object? verbObject = candidate;
                try
                {
                    dynamic verb = verbObject;
                    var normalized = NormalizeVerbName((string?)verb.Name);
                    if (!acceptedNames.Any(name => normalized.Equals(
                            NormalizeVerbName(name),
                            StringComparison.OrdinalIgnoreCase))) continue;
                    verb.DoIt();
                    return true;
                }
                finally
                {
                    ReleaseComObject(verbObject);
                }
            }
            return false;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(itemObject);
            ReleaseComObject(folderObject);
            ReleaseComObject(shellObject);
        }
    }

    private static string NormalizeVerbName(string? value) => string.Concat(
        (value ?? string.Empty).Where(character => character is not '&' and not '…')).Trim();

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
