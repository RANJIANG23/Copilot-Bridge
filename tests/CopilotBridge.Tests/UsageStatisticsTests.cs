using CopilotBridge.Core;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class UsageStatisticsTests
{
    [Fact]
    public void VisibleTokenEstimateHandlesChineseEnglishAndPunctuation()
    {
        Assert.Equal(4, UsageStatisticsCalculator.EstimateVisibleTokens("数据统计"));
        Assert.Equal(2, UsageStatisticsCalculator.EstimateVisibleTokens("abcdefgh"));
        Assert.Equal(4, UsageStatisticsCalculator.EstimateVisibleTokens("数据ab!!"));
        Assert.Equal(0, UsageStatisticsCalculator.EstimateVisibleTokens(string.Empty));
    }

    [Fact]
    public void SnapshotExcludesImportedHistoryAndCountsReviewAsOneTaskTwoDeliveries()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.FromHours(8));
        var review = new ConversationDocument
        {
            Mode = "review",
            Turns =
            [
                new ConversationTurn(now, "agent", "complexity"),
                new ConversationTurn(now, "copilot", "ok", "Opus", "verified"),
                new ConversationTurn(now, "agent", "evidence"),
                new ConversationTurn(now, "copilot", "ok", "GPT 5.6 Think deeper", "verified")
            ]
        };
        var imported = new ConversationDocument
        {
            Mode = "history_import",
            Turns =
            [
                new ConversationTurn(now, "user", "old"),
                new ConversationTurn(now, "copilot", "old", null, "unknown")
            ]
        };

        var snapshot = UsageStatisticsCalculator.Calculate([review, imported], now, now);

        Assert.Equal(1, snapshot.Tasks);
        Assert.Equal(2, snapshot.Deliveries);
        Assert.Equal(2, snapshot.Completed);
        Assert.Equal(1, snapshot.Models.Single(row => row.Model == "Claude Opus 4.8").Deliveries);
        Assert.Equal(1, snapshot.Models.Single(row => row.Model == "GPT-5.6 Think deeper").Deliveries);
        Assert.Equal(0, snapshot.UnpricedDeliveries);
    }

    [Fact]
    public void SnapshotCountsBridgeFollowUpInsideImportedConversationWithoutCountingImportedTurns()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.FromHours(8));
        var document = new ConversationDocument
        {
            Mode = "history_import",
            Turns =
            [
                new ConversationTurn(now, "user", "old user turn"),
                new ConversationTurn(now, "copilot", "old reply", null, "unknown"),
                new ConversationTurn(now, "agent", "new Bridge follow-up"),
                new ConversationTurn(now, "copilot", "new reply", "Opus", "verified")
            ]
        };

        var snapshot = UsageStatisticsCalculator.Calculate([document], now, now);

        Assert.Equal(1, snapshot.Tasks);
        Assert.Equal(1, snapshot.Deliveries);
        Assert.Equal(1, snapshot.Completed);
        Assert.Equal(1, snapshot.Models.Single(row => row.Model == "Claude Opus 4.8").Deliveries);
        Assert.Equal("History import", Assert.Single(snapshot.Modes).Mode);
    }

    [Fact]
    public void SnapshotCompletionRateIncludesConfirmedDeliveryWithoutReply()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.FromHours(8));
        var document = new ConversationDocument
        {
            Turns =
            [
                new ConversationTurn(now, "agent", "completed request"),
                new ConversationTurn(now, "copilot", "completed reply", "Opus", "verified"),
                new ConversationTurn(now, "agent", "timed out request", null, "reply_timeout")
            ]
        };

        var snapshot = UsageStatisticsCalculator.Calculate([document], now, now);

        Assert.Equal(2, snapshot.Deliveries);
        Assert.Equal(1, snapshot.Completed);
        Assert.Equal(0.5, snapshot.CompletionRate);
    }

    [Fact]
    public void ReviewModeBreakdownCountsIncompleteRoundsPerConversation()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.FromHours(8));
        ConversationDocument PartialReview(string request) => new()
        {
            Mode = "review",
            Turns = [new ConversationTurn(now, "agent", request, null, "reply_timeout", "complexity")]
        };

        var snapshot = UsageStatisticsCalculator.Calculate(
            [PartialReview("first"), PartialReview("second")],
            now,
            now);

        Assert.Equal(2, snapshot.Tasks);
        var review = Assert.Single(snapshot.Modes);
        Assert.Equal("Review", review.Mode);
        Assert.Equal(2, review.Tasks);
        Assert.Equal(2, review.Deliveries);
    }

    [Fact]
    public void SnapshotAppliesMultiplierAndLeavesUnknownModelsUnpriced()
    {
        var now = DateTimeOffset.Now;
        var document = new ConversationDocument
        {
            Mode = "assist",
            Turns =
            [
                new ConversationTurn(now, "agent", "abcdefgh"),
                new ConversationTurn(now, "copilot", "数据", "深度思考", "verified")
            ]
        };

        var snapshot = UsageStatisticsCalculator.Calculate([document], now, now, 3);

        Assert.Equal(1, snapshot.Tasks);
        Assert.Equal(1, snapshot.Deliveries);
        Assert.Equal(12, snapshot.EquivalentTokens);
        Assert.Equal(1, snapshot.UnpricedDeliveries);
        Assert.Equal(0m, snapshot.EquivalentCost);
        Assert.Null(snapshot.Models.Single(row => row.Model.Contains("深度思考")).EquivalentCost);
    }

    [Fact]
    public void SnapshotUsesPrototypeRatesForPricedModels()
    {
        var now = DateTimeOffset.Now;
        var document = new ConversationDocument
        {
            Turns =
            [
                new ConversationTurn(now, "agent", "abcd"),
                new ConversationTurn(now, "copilot", "数据", "Opus", "verified")
            ]
        };

        var snapshot = UsageStatisticsCalculator.Calculate([document], now, now, 3);

        Assert.Equal(0.000165m, snapshot.EquivalentCost);
        Assert.Equal(9, snapshot.EquivalentTokens);
    }

    [Fact]
    public void PreparedDatasetDoesNotRescanConversationTextWhenMultiplierChanges()
    {
        var now = DateTimeOffset.Now;
        var turns = new[]
        {
            new ConversationTurn(now, "agent", "abcd"),
            new ConversationTurn(now, "copilot", "data", "Opus", "verified")
        };
        var dataset = UsageStatisticsCalculator.Prepare([new ConversationDocument { Turns = turns }]);
        var delivery = Assert.Single(dataset.Deliveries);
        Assert.Equal(1, delivery.InputTokens);
        Assert.Equal(1, delivery.OutputTokens);

        turns[0] = turns[0] with { Markdown = new string('x', 40_000) };
        turns[1] = turns[1] with { Markdown = new string('y', 40_000) };
        var first = UsageStatisticsCalculator.Calculate(dataset, now, now, 2);
        var second = UsageStatisticsCalculator.Calculate(dataset, now, now, 4);

        Assert.Equal(2, first.VisibleInputTokens + first.VisibleOutputTokens);
        Assert.Equal(first.VisibleInputTokens, second.VisibleInputTokens);
        Assert.Equal(first.VisibleOutputTokens, second.VisibleOutputTokens);
        Assert.Equal(first.EquivalentTokens * 2, second.EquivalentTokens);
        Assert.Equal(first.EquivalentCost * 2, second.EquivalentCost);
    }

    [Theory]
    [InlineData("Claude Opus 4.9")]
    [InlineData("Opus 5")]
    [InlineData("GPT 5.7 Think deeper")]
    [InlineData("Future Instant")]
    public void FutureOrAmbiguousModelsRemainUnpriced(string model)
    {
        var now = DateTimeOffset.Now;
        var snapshot = UsageStatisticsCalculator.Calculate(
            [new ConversationDocument
            {
                Turns =
                [
                    new ConversationTurn(now, "agent", "request"),
                    new ConversationTurn(now, "copilot", "reply", model, "verified")
                ]
            }],
            now,
            now);

        Assert.Equal(1, snapshot.UnpricedDeliveries);
        Assert.Equal(0m, snapshot.EquivalentCost);
        var row = snapshot.Models.Single(item => item.Deliveries == 1);
        Assert.Equal($"{model}（未定价）", row.Model);
        Assert.Null(row.EquivalentCost);
    }

    [Theory]
    [InlineData("Claude Opus 4.8", "Claude Opus 4.8")]
    [InlineData("gpt_5_6_think_deeper", "GPT-5.6 Think deeper")]
    [InlineData("GPT 5.5 Instant", "GPT-5.5 Instant")]
    public void ControlledModelAliasesUsePrototypeRates(string model, string displayName)
    {
        var now = DateTimeOffset.Now;
        var snapshot = UsageStatisticsCalculator.Calculate(
            [new ConversationDocument
            {
                Turns =
                [
                    new ConversationTurn(now, "agent", "request"),
                    new ConversationTurn(now, "copilot", "reply", model, "verified")
                ]
            }],
            now,
            now);

        Assert.Equal(0, snapshot.UnpricedDeliveries);
        Assert.True(snapshot.EquivalentCost > 0);
        Assert.Equal(1, snapshot.Models.Single(item => item.Model == displayName).Deliveries);
    }

    [Theory]
    [InlineData(1, 1, 5)]
    [InlineData(6, 2, 6)]
    public void SelectedMultiplierAlwaysFallsInsideDisplayedEstimateRange(
        double selected,
        double expectedLow,
        double expectedHigh)
    {
        var now = DateTimeOffset.Now;
        var document = new ConversationDocument
        {
            Turns =
            [
                new ConversationTurn(now, "agent", "abcd"),
                new ConversationTurn(now, "copilot", "data", "Opus", "verified")
            ]
        };

        var selectedSnapshot = UsageStatisticsCalculator.Calculate([document], now, now, selected);
        var lowSnapshot = UsageStatisticsCalculator.Calculate([document], now, now, expectedLow);
        var highSnapshot = UsageStatisticsCalculator.Calculate([document], now, now, expectedHigh);

        Assert.Equal(lowSnapshot.EquivalentCost, selectedSnapshot.EquivalentCostLow);
        Assert.Equal(highSnapshot.EquivalentCost, selectedSnapshot.EquivalentCostHigh);
        Assert.InRange(
            selectedSnapshot.EquivalentCost,
            selectedSnapshot.EquivalentCostLow,
            selectedSnapshot.EquivalentCostHigh);
    }

    [Fact]
    public void StatisticsNavigationSitsBetweenOverviewAndConversationManagement()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml"));

        var overview = xaml.IndexOf("x:Name=\"OverviewNav\"", StringComparison.Ordinal);
        var statistics = xaml.IndexOf("x:Name=\"StatisticsNav\"", StringComparison.Ordinal);
        var history = xaml.IndexOf("x:Name=\"HistoryNav\"", StringComparison.Ordinal);
        Assert.True(overview >= 0 && overview < statistics && statistics < history);
        Assert.Contains("x:Name=\"StatisticsPanel\"", xaml);
        Assert.Contains("x:Name=\"StatisticsMultiplierSlider\"", xaml);
    }

    [Fact]
    public void ConversationSelectionUsesAnExplicitDragHandle()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "UI", "MainWindow.xaml.cs"));

        Assert.Contains("Tag=\"ConversationDragHandle\"", xaml);
        Assert.Contains("PreviewMouseLeftButtonUp=\"ConversationListBox_PreviewMouseLeftButtonUp\"", xaml);
        Assert.Contains("HasElementTag(e.OriginalSource as DependencyObject, \"ConversationDragHandle\")", codeBehind);
    }

    [Fact]
    public void McpAdapterDelegatesConsultationLifecycleToCoordinator()
    {
        var root = FindRepositoryRoot();
        var mcp = File.ReadAllText(Path.Combine(root, "src", "CopilotBridge", "Mcp", "CopilotBridgeTools.cs"));
        var mcpConsult = Slice(mcp, "public async Task<ConsultResponse> ConsultAsync", "public async ValueTask DisposeAsync");

        Assert.Contains("_consultationCoordinator.ConsultAsync", mcpConsult);
        Assert.DoesNotContain("new CollaborationRunner", mcpConsult);
        Assert.DoesNotContain("new CollaborationContext", mcpConsult);
        Assert.DoesNotContain("new ConsultationRecord", mcpConsult);
    }

    [Fact]
    public async Task SettingsPersistStatisticsMultiplier()
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            await store.SaveAsync(new BridgeSettings { StatisticsTokenMultiplier = 4.5 });

            Assert.Equal(4.5, (await store.LoadAsync()).StatisticsTokenMultiplier);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WorkspaceLoadsStatisticsDocumentsInOneBatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var first = await store.CreateConversationAsync(ConversationWorkspaceStore.StandaloneProjectId, "first");
            var second = await store.CreateConversationAsync(ConversationWorkspaceStore.StandaloneProjectId, "second");

            var documents = await store.GetConversationDocumentsAsync();

            Assert.Equal(2, documents.Count);
            Assert.Contains(documents, document => document.Id == first.Id);
            Assert.Contains(documents, document => document.Id == second.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WorkspaceStatisticsReadDoesNotCreateOrMigrateWorkspaceContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        var missingRoot = root + "-missing";
        try
        {
            var missingDocuments = await new ConversationWorkspaceStore(missingRoot)
                .GetConversationDocumentsAsync();
            Assert.Empty(missingDocuments);
            Assert.False(Directory.Exists(missingRoot));

            var legacyDirectory = Path.Combine(root, "收件箱");
            var legacyFile = Path.Combine(legacyDirectory, "conversation-legacy.md");
            Directory.CreateDirectory(legacyDirectory);
            await File.WriteAllTextAsync(legacyFile, "legacy sentinel");

            var documents = await new ConversationWorkspaceStore(root).GetConversationDocumentsAsync();

            Assert.Empty(documents);
            Assert.True(Directory.Exists(legacyDirectory));
            Assert.Equal("legacy sentinel", await File.ReadAllTextAsync(legacyFile));
            Assert.False(Directory.Exists(Path.Combine(root, ConversationWorkspaceStore.StandaloneProjectId)));
            Assert.False(Directory.Exists(Path.Combine(root, ".bridge")));
            Assert.Equal(new[] { legacyFile }, Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(missingRoot)) Directory.Delete(missingRoot, true);
        }
    }

    [Theory]
    [InlineData("assist", 1, "Assist")]
    [InlineData("outsource", 1, "Outsource")]
    [InlineData("review", 2, "Review")]
    public async Task AppendRunRecordsActualModeAfterHistoryImport(
        string collaborationMode,
        int responseCount,
        string expectedMode)
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var now = DateTimeOffset.Now;
            var document = await store.CreateConversationAsync(ConversationWorkspaceStore.StandaloneProjectId);
            document = document with
            {
                Mode = "history_import",
                Turns =
                [
                    new ConversationTurn(now, "user", "historic request"),
                    new ConversationTurn(now, "copilot", "historic reply", null, "unknown")
                ]
            };
            await store.SaveAsync(document);
            var responses = Enumerable.Range(0, responseCount)
                .Select(index => new ReviewerResult(
                    responseCount == 2 ? (index == 0 ? "complexity" : "evidence") : "primary",
                    $"new request {index}",
                    new AssistResult(
                        "Opus",
                        $"new reply {index}",
                        $"https://m365.cloud.microsoft/chat/conversation/{document.Id}-{index}",
                        1,
                        1,
                        false)))
                .ToArray();
            var result = new CollaborationRunResult(
                responses,
                1,
                responses[0].Result.ConversationUrl,
                responseCount == 2 ? responses[0].Result.ConversationUrl : null,
                responseCount == 2 ? responses[1].Result.ConversationUrl : null);

            document = await store.AppendRunAsync(
                document,
                result,
                collaborationMode: collaborationMode);
            var snapshot = UsageStatisticsCalculator.Calculate([document], now, DateTimeOffset.Now);

            Assert.Equal(collaborationMode, document.Mode);
            var mode = Assert.Single(snapshot.Modes);
            Assert.Equal(expectedMode, mode.Mode);
            Assert.Equal(1, mode.Tasks);
            Assert.Equal(responseCount, mode.Deliveries);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AppendIncompleteDeliveryRecordsActualModeAfterHistoryImport()
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var document = await store.CreateConversationAsync(ConversationWorkspaceStore.StandaloneProjectId);
            document = document with { Mode = "history_import" };
            await store.SaveAsync(document);

            document = await store.AppendIncompleteDeliveryAsync(
                document,
                "submitted request",
                "reply_timeout",
                collaborationMode: "outsource");

            Assert.Equal("outsource", document.Mode);
            var mode = Assert.Single(UsageStatisticsCalculator.Calculate(
                [document],
                document.Turns[0].Timestamp,
                document.Turns[0].Timestamp).Modes);
            Assert.Equal("Outsource", mode.Mode);
            Assert.Equal(1, mode.Deliveries);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WorkspacePersistsConfirmedIncompleteDeliveryForStatistics()
    {
        var root = Path.Combine(Path.GetTempPath(), "CopilotBridge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConversationWorkspaceStore(root);
            var document = await store.CreateConversationAsync(
                ConversationWorkspaceStore.StandaloneProjectId,
                "timeout");

            document = await store.AppendIncompleteDeliveryAsync(
                document,
                "request that was submitted",
                "reply_timeout",
                "primary",
                "https://m365.cloud.microsoft/chat/conversation/timeout");
            var loaded = await store.FindAsync(document.Id);

            Assert.NotNull(loaded);
            var turn = Assert.Single(loaded.Turns);
            Assert.Equal("agent", turn.Role);
            Assert.Equal("reply_timeout", turn.ModelStatus);
            var snapshot = UsageStatisticsCalculator.Calculate([loaded], turn.Timestamp, turn.Timestamp);
            Assert.Equal(1, snapshot.Deliveries);
            Assert.Equal(0, snapshot.Completed);
            Assert.Equal(0, snapshot.CompletionRate);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CopilotBridge.sln"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
