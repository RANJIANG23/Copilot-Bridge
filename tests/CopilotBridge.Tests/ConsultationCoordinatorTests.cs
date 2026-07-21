using CopilotBridge.Core;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class ConsultationCoordinatorTests
{
    [Fact]
    public async Task PolicyGateDoesNotAcquireBrowserPage()
    {
        var root = CreateRoot();
        try
        {
            var acquired = 0;
            var coordinator = CreateCoordinator(root);
            var outcome = await coordinator.ConsultAsync(
                Settings(ConsultationPolicy.Disabled),
                new ConsultationCommand("test", "user_explicit"),
                async _ =>
                {
                    acquired++;
                    await Task.Yield();
                    return null!;
                });

            Assert.Equal("blocked", outcome.Status);
            Assert.Equal("blocked_by_policy", outcome.ErrorCode);
            Assert.Equal(0, acquired);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task ExhaustedBudgetDoesNotAcquireBrowserPage()
    {
        var root = CreateRoot();
        try
        {
            var state = new ConsultationStateStore(Path.Combine(root, "consultations.json"));
            await state.SaveAsync("existing", new ConsultationRecord
            {
                Mode = "assist",
                TurnCount = 1,
                TurnBudget = 1,
                PrimaryConversationUrl = "https://m365.cloud.microsoft/chat/existing"
            });
            var acquired = 0;
            var coordinator = CreateCoordinator(root, state);
            var outcome = await coordinator.ConsultAsync(
                Settings(),
                new ConsultationCommand("follow up", "user_explicit", "existing"),
                async _ =>
                {
                    acquired++;
                    await Task.Yield();
                    return null!;
                });

            Assert.Equal("blocked", outcome.Status);
            Assert.Equal("turn_budget_exhausted", outcome.ErrorCode);
            Assert.Equal(0, acquired);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task ModeMismatchDoesNotAcquireBrowserPage()
    {
        var root = CreateRoot();
        try
        {
            var state = new ConsultationStateStore(Path.Combine(root, "consultations.json"));
            await state.SaveAsync("existing", new ConsultationRecord
            {
                Mode = "review",
                TurnCount = 0,
                TurnBudget = 1
            });
            var acquired = 0;
            var coordinator = CreateCoordinator(root, state);
            var outcome = await coordinator.ConsultAsync(
                Settings(),
                new ConsultationCommand("follow up", "user_explicit", "existing"),
                async _ =>
                {
                    acquired++;
                    await Task.Yield();
                    return null!;
                });

            Assert.Equal("consultation_mode_mismatch", outcome.ErrorCode);
            Assert.Equal(0, acquired);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task BusyGateDoesNotQueueOrAcquireBrowserPage()
    {
        var root = CreateRoot();
        try
        {
            var leasePath = Path.Combine(root, "consultation.lock");
            using var held = ConsultationLease.TryAcquire(leasePath);
            Assert.NotNull(held);
            var acquired = 0;
            var coordinator = CreateCoordinator(root, leasePath: leasePath);
            var outcome = await coordinator.ConsultAsync(
                Settings(),
                new ConsultationCommand("test", "user_explicit"),
                async _ =>
                {
                    acquired++;
                    await Task.Yield();
                    return null!;
                });

            Assert.Equal("busy", outcome.ErrorCode);
            Assert.True(outcome.CanRetrySafely);
            Assert.Equal("new_consultation", outcome.RetryAction);
            Assert.Equal(0, acquired);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task FreshSingleReviewerConsultationRequiresBoundTabBeforePageAcquisition()
    {
        var root = CreateRoot();
        try
        {
            var acquired = 0;
            var coordinator = CreateCoordinator(root);
            var outcome = await coordinator.ConsultAsync(
                Settings() with { BoundConversationUrl = null },
                new ConsultationCommand("test", "user_explicit"),
                async _ =>
                {
                    acquired++;
                    await Task.Yield();
                    return null!;
                });

            Assert.Equal("tab_rebind_required", outcome.ErrorCode);
            Assert.Equal(0, acquired);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task FailedDiskSaveStillRetainsUnsafeStateInMemory()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(root);
            var blockingFile = Path.Combine(root, "not-a-directory");
            await File.WriteAllTextAsync(blockingFile, "block");
            var store = new ConsultationStateStore(Path.Combine(blockingFile, "consultations.json"));
            var expected = new ConsultationRecord
            {
                Mode = "assist",
                TurnCount = 1,
                TurnBudget = 2,
                Status = "submission_unknown"
            };

            await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync("unsafe", expected));
            var remembered = await store.FindAsync("unsafe");

            Assert.NotNull(remembered);
            Assert.Equal("submission_unknown", remembered.Status);
            Assert.Equal(1, remembered.TurnCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task InterleavedStoresDoNotOverwriteAnotherProcessNewerConsultationState()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "consultations.json");
            var firstProcess = new ConsultationStateStore(path);
            var secondProcess = new ConsultationStateStore(path);
            await firstProcess.SaveAsync("a", new ConsultationRecord
            {
                Mode = "assist",
                TurnCount = 1,
                TurnBudget = 3,
                PrimaryConversationUrl = "https://m365.cloud.microsoft/chat/a-1"
            });
            await secondProcess.SaveAsync("a", new ConsultationRecord
            {
                Mode = "assist",
                TurnCount = 2,
                TurnBudget = 3,
                PrimaryConversationUrl = "https://m365.cloud.microsoft/chat/a-2"
            });

            Assert.Equal(2, (await firstProcess.FindAsync("a"))!.TurnCount);
            await firstProcess.SaveAsync("b", new ConsultationRecord
            {
                Mode = "review",
                TurnCount = 1,
                TurnBudget = 1
            });

            var verificationProcess = new ConsultationStateStore(path);
            var a = await verificationProcess.FindAsync("a");
            var b = await verificationProcess.FindAsync("b");
            Assert.Equal(2, a!.TurnCount);
            Assert.EndsWith("a-2", a.PrimaryConversationUrl, StringComparison.Ordinal);
            Assert.Equal(1, b!.TurnCount);
        }
        finally { DeleteRoot(root); }
    }

    [Fact]
    public async Task HigherUnsafeTurnInMemoryWinsOverLegacyStateWithNewerLoadTimestamp()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "consultations.json");
            await File.WriteAllTextAsync(
                path,
                "{\"conversations\":{\"legacy\":\"https://m365.cloud.microsoft/chat/legacy\"}}");
            var store = new ConsultationStateStore(path);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync("legacy", new ConsultationRecord
                {
                    Mode = "assist",
                    TurnCount = 1,
                    TurnBudget = 2,
                    Status = "submission_unknown"
                }));
            }

            var remembered = await store.FindAsync("legacy");
            Assert.Equal(1, remembered!.TurnCount);
            Assert.Equal("submission_unknown", remembered.Status);
        }
        finally { DeleteRoot(root); }
    }

    private static ConsultationCoordinator CreateCoordinator(
        string root,
        ConsultationStateStore? state = null,
        string? leasePath = null) => new(
            new SettingsStore(Path.Combine(root, "settings.json")),
            state ?? new ConsultationStateStore(Path.Combine(root, "consultations.json")),
            CopilotBridge.Browser.ProviderSelectors.Load(),
            leasePath ?? Path.Combine(root, "consultation.lock"));

    private static BridgeSettings Settings(
        ConsultationPolicy policy = ConsultationPolicy.ManualOnly) => new()
        {
            ConsultationPolicy = policy,
            CollaborationMode = CollaborationMode.Assist,
            AssistTurnBudget = 1,
            BoundConversationUrl = "https://m365.cloud.microsoft/chat/bound"
        };

    private static string CreateRoot() => Path.Combine(
        Path.GetTempPath(),
        "CopilotBridge.Tests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
