using System.Diagnostics;
using System.Text;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class DistributionScriptTests
{
    [Fact]
    public void ReleaseMetadataIsUnifiedForV122()
    {
        var root = DistributionFixture.FindRepositoryRoot();
        Assert.Contains("[string]$Version = '1.2.2'", File.ReadAllText(Path.Combine(root, "distribution", "Build-Release.ps1")));
        Assert.Contains("<Version>1.2.2</Version>", File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "CopilotBridge.csproj")));
        Assert.Contains("\"version\": \"1.2.2\"", File.ReadAllText(Path.Combine(
            root,
            "distribution",
            "marketplace",
            "plugins",
            "copilot-bridge",
            ".codex-plugin",
            "plugin.json")));
        var upgradeScript = File.ReadAllText(Path.Combine(root, "distribution", "Test-IsolatedUpgrade.ps1"));
        Assert.Contains("$previousVersion -like '1.2.1*'", upgradeScript);
        Assert.Contains("$candidateVersion -like '1.2.2*'", upgradeScript);
        Assert.Contains("$rollbackVersion -like '1.2.1*'", upgradeScript);
        Assert.Contains("'CopilotBridge-Phase27'", upgradeScript);
        Assert.Contains("COPILOT_BRIDGE_SETTINGS_PATH", File.ReadAllText(Path.Combine(
            root,
            "distribution",
            "marketplace",
            "plugins",
            "copilot-bridge",
            "scripts",
            "start-mcp.ps1")));
    }

    [Fact]
    public void CopilotSkillStopsBeforeReviewWhenGuiModeDoesNotMatch()
    {
        var root = DistributionFixture.FindRepositoryRoot();
        var skillPaths = new[]
        {
            Path.Combine(root, ".agents", "skills", "copilot-consult", "SKILL.md"),
            Path.Combine(
                root,
                "distribution",
                "marketplace",
                "plugins",
                "copilot-bridge",
                "skills",
                "copilot-consult",
                "SKILL.md")
        };

        foreach (var path in skillPaths)
        {
            var skill = File.ReadAllText(path);
            Assert.Contains("collaborationMode` is not `review`, stop before calling `consult_copilot`", skill);
            Assert.Contains("call status again and proceed only when it reads `review`", skill);
            Assert.Contains("retryAction=none", skill);
        }
    }

    [Fact]
    public async Task FailedPluginUpgradeRestoresPreviousApplicationAndPlugin()
    {
        using var fixture = DistributionFixture.Create();
        fixture.WritePreviousInstall();

        var result = await fixture.RunInstallerAsync(failFirstPluginAdd: true);

        Assert.True(
            result.ExitCode != 0,
            $"Expected installer failure. Output: {result.Output} Error: {result.Error} Calls: {string.Join(" | ", File.ReadAllLines(fixture.CodexLogPath))}");
        Assert.Contains("Unable to install the Copilot Bridge plugin", result.Error);
        Assert.Equal("old-version", File.ReadAllText(fixture.InstalledExecutable));
        Assert.True(File.Exists(fixture.PreviousMarker));
        Assert.False(File.Exists(fixture.ShortcutPath));
        Assert.Empty(Directory.GetDirectories(fixture.InstallParent, ".CopilotBridge.backup-*"));
        Assert.Empty(Directory.GetDirectories(fixture.InstallParent, ".CopilotBridge.install-*"));

        var calls = File.ReadAllLines(fixture.CodexLogPath);
        Assert.Contains(calls, line => line.StartsWith("plugin remove ", StringComparison.Ordinal));
        Assert.Equal(2, calls.Count(line => line.StartsWith("plugin add ", StringComparison.Ordinal)));
        Assert.DoesNotContain(calls, line => line.StartsWith("plugin marketplace add ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UninstallerUsesStructuredMarketplaceOutput()
    {
        using var fixture = DistributionFixture.Create();
        fixture.WritePreviousInstall();
        File.WriteAllText(fixture.ShortcutPath, "test shortcut");

        var result = await fixture.RunUninstallerAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.False(Directory.Exists(fixture.InstallDirectory));
        Assert.False(File.Exists(fixture.ShortcutPath));
        var calls = File.ReadAllLines(fixture.CodexLogPath);
        Assert.Contains(calls, line => line.StartsWith("plugin remove ", StringComparison.Ordinal));
        Assert.Contains(calls, line => line.StartsWith("plugin marketplace remove ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarketplaceDiscoveryFailureLeavesInstallationUntouched()
    {
        using var fixture = DistributionFixture.Create();
        fixture.WritePreviousInstall();
        File.WriteAllText(fixture.ShortcutPath, "test shortcut");

        var result = await fixture.RunUninstallerAsync(failMarketplaceList: true);

        Assert.True(
            result.ExitCode != 0,
            $"Expected marketplace discovery failure. Output: {result.Output} Error: {result.Error} Calls: {string.Join(" | ", File.ReadAllLines(fixture.CodexLogPath))}");
        Assert.Contains("Unable to read configured Codex plugin marketplaces", result.Error);
        Assert.True(File.Exists(fixture.InstalledExecutable));
        Assert.True(File.Exists(fixture.ShortcutPath));
        var calls = File.ReadAllLines(fixture.CodexLogPath);
        Assert.Single(calls);
        Assert.StartsWith("plugin marketplace list", calls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedMarketplaceRemovalRestoresPluginAndLeavesInstallationUntouched()
    {
        using var fixture = DistributionFixture.Create();
        fixture.WritePreviousInstall();
        File.WriteAllText(fixture.ShortcutPath, "test shortcut");

        var result = await fixture.RunUninstallerAsync(failMarketplaceRemove: true);

        Assert.True(
            result.ExitCode != 0,
            $"Expected marketplace removal failure. Output: {result.Output} Error: {result.Error} Calls: {string.Join(" | ", File.ReadAllLines(fixture.CodexLogPath))}");
        Assert.Contains("Unable to remove the Copilot Bridge marketplace", result.Error);
        Assert.True(File.Exists(fixture.InstalledExecutable));
        Assert.True(File.Exists(fixture.ShortcutPath));
        var calls = File.ReadAllLines(fixture.CodexLogPath);
        Assert.Contains(calls, line => line.StartsWith("plugin remove ", StringComparison.Ordinal));
        Assert.Contains(calls, line => line.StartsWith("plugin marketplace remove ", StringComparison.Ordinal));
        Assert.Contains(calls, line => line.StartsWith("plugin add ", StringComparison.Ordinal));
    }

    private sealed class DistributionFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _packageDirectory;
        private readonly string _fakeBin;

        private DistributionFixture(string root)
        {
            _root = root;
            _packageDirectory = Path.Combine(root, "package");
            _fakeBin = Path.Combine(root, "bin");
            LocalAppData = Path.Combine(root, "local");
            InstallDirectory = Path.Combine(LocalAppData, "Programs", "CopilotBridge");
            InstallParent = Path.GetDirectoryName(InstallDirectory)!;
            StartMenuDirectory = Path.Combine(root, "start-menu");
            ShortcutPath = Path.Combine(StartMenuDirectory, "Copilot Bridge.lnk");
            InstalledExecutable = Path.Combine(InstallDirectory, "CopilotBridge.exe");
            PreviousMarker = Path.Combine(InstallDirectory, "previous.marker");
            CodexLogPath = Path.Combine(root, "codex-calls.log");

            Directory.CreateDirectory(Path.Combine(_packageDirectory, "app"));
            Directory.CreateDirectory(Path.Combine(
                _packageDirectory,
                "marketplace",
                ".agents",
                "plugins"));
            Directory.CreateDirectory(_fakeBin);
            Directory.CreateDirectory(StartMenuDirectory);

            var repositoryRoot = FindRepositoryRoot();
            File.Copy(
                Path.Combine(repositoryRoot, "distribution", "Install-CopilotBridge.ps1"),
                Path.Combine(_packageDirectory, "Install-CopilotBridge.ps1"));
            File.Copy(
                Path.Combine(repositoryRoot, "distribution", "Uninstall-CopilotBridge.ps1"),
                Path.Combine(_packageDirectory, "Uninstall-CopilotBridge.ps1"));
            File.WriteAllText(
                Path.Combine(_packageDirectory, "app", "CopilotBridge.exe"),
                "new-version");
            File.WriteAllText(
                Path.Combine(_packageDirectory, "marketplace", ".agents", "plugins", "marketplace.json"),
                "{}");
            File.WriteAllText(
                Path.Combine(_fakeBin, "codex.cmd"),
                FakeCodexScript.Replace("\n", "\r\n"),
                Encoding.ASCII);
        }

        internal string LocalAppData { get; }
        internal string InstallDirectory { get; }
        internal string InstallParent { get; }
        internal string StartMenuDirectory { get; }
        internal string ShortcutPath { get; }
        internal string InstalledExecutable { get; }
        internal string PreviousMarker { get; }
        internal string CodexLogPath { get; }

        internal static DistributionFixture Create() => new(Path.Combine(
            Path.GetTempPath(),
            "CopilotBridge.Tests",
            Guid.NewGuid().ToString("N")));

        internal void WritePreviousInstall()
        {
            Directory.CreateDirectory(InstallDirectory);
            File.WriteAllText(InstalledExecutable, "old-version");
            File.WriteAllText(PreviousMarker, "preserve-me");
        }

        internal Task<ProcessResult> RunInstallerAsync(bool failFirstPluginAdd) => RunPowerShellAsync(
            Path.Combine(_packageDirectory, "Install-CopilotBridge.ps1"),
            failFirstPluginAdd);

        internal Task<ProcessResult> RunUninstallerAsync(
            bool failMarketplaceList = false,
            bool failMarketplaceRemove = false) => RunPowerShellAsync(
            Path.Combine(_packageDirectory, "Uninstall-CopilotBridge.ps1"),
            false,
            failMarketplaceList,
            failMarketplaceRemove);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch
            {
                // A failed cleanup must not hide the assertion that explains the test failure.
            }
        }

        private async Task<ProcessResult> RunPowerShellAsync(
            string script,
            bool failFirstPluginAdd,
            bool failMarketplaceList = false,
            bool failMarketplaceRemove = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add("-StartMenuDirectory");
            startInfo.ArgumentList.Add(StartMenuDirectory);
            startInfo.Environment["LOCALAPPDATA"] = LocalAppData;
            startInfo.Environment["PATH"] = _fakeBin + Path.PathSeparator +
                                            Environment.GetEnvironmentVariable("PATH");
            startInfo.Environment["FAKE_CODEX_LOG"] = CodexLogPath;
            startInfo.Environment["FAKE_MARKETPLACE_ROOT"] = Path.Combine(
                    InstallDirectory,
                    "marketplace")
                .Replace('\\', '/');
            startInfo.Environment["FAKE_ADD_COUNT"] = Path.Combine(_root, "plugin-add-count");
            startInfo.Environment["FAKE_FAIL_FIRST_ADD"] = failFirstPluginAdd ? "1" : "0";
            var failMarketplaceListMarker = Path.Combine(_root, "fail-marketplace-list");
            var failMarketplaceRemoveMarker = Path.Combine(_root, "fail-marketplace-remove");
            if (failMarketplaceList) File.WriteAllText(failMarketplaceListMarker, "1");
            if (failMarketplaceRemove) File.WriteAllText(failMarketplaceRemoveMarker, "1");
            startInfo.Environment["FAKE_FAIL_MARKETPLACE_LIST"] = failMarketplaceListMarker;
            startInfo.Environment["FAKE_FAIL_MARKETPLACE_REMOVE"] = failMarketplaceRemoveMarker;

            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException("Could not start PowerShell.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }

        internal static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CopilotBridge.sln")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }

        private const string FakeCodexScript = """
            @echo off
            >>"%FAKE_CODEX_LOG%" echo %*
            if "%~1 %~2 %~3 %~4"=="plugin marketplace list --json" (
              if exist "%FAKE_FAIL_MARKETPLACE_LIST%" goto fail_marketplace_list
              echo {"marketplaces":[{"name":"copilot-bridge-team","root":"%FAKE_MARKETPLACE_ROOT%"}]}
              exit /b 0
            )
            if "%~1 %~2 %~3"=="plugin marketplace remove" (
              if exist "%FAKE_FAIL_MARKETPLACE_REMOVE%" goto fail_marketplace_remove
              exit /b 0
            )
            if "%~1 %~2 %~3"=="plugin marketplace add" exit /b 0
            if "%~1 %~2 %~3"=="plugin list --json" (
              echo {"installed":[{"pluginId":"copilot-bridge@copilot-bridge-team","installed":true}]}
              exit /b 0
            )
            if "%~1 %~2"=="plugin remove" exit /b 0
            if "%~1 %~2"=="plugin add" goto plugin_add
            exit /b 99

            :fail_marketplace_list
            exit /b 23

            :fail_marketplace_remove
            exit /b 29

            :plugin_add
            if not "%FAKE_FAIL_FIRST_ADD%"=="1" exit /b 0
            if exist "%FAKE_ADD_COUNT%" exit /b 0
            >"%FAKE_ADD_COUNT%" echo 1
            exit /b 17
            """;
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
