namespace CopilotBridge.Core;

internal sealed record UsageModelRate(
    string Key,
    string DisplayName,
    decimal InputPerMillion,
    decimal OutputPerMillion);

internal sealed record UsageDeliveryProjection(
    string DocumentId,
    DateTimeOffset Timestamp,
    string Mode,
    string Model,
    int InputTokens,
    int OutputTokens,
    bool Completed,
    decimal? BaseCost);

internal sealed record UsageStatisticsDataset(IReadOnlyList<UsageDeliveryProjection> Deliveries);

internal sealed record UsageTrendPoint(
    DateTimeOffset Start,
    string Label,
    int Deliveries);

internal sealed record UsageModelBreakdown(
    string Model,
    int Deliveries,
    long InputTokens,
    long OutputTokens,
    long EquivalentTokens,
    decimal? EquivalentCost);

internal sealed record UsageModeBreakdown(string Mode, int Tasks, int Deliveries);

internal sealed record UsageStatisticsSnapshot(
    DateTimeOffset From,
    DateTimeOffset To,
    int Tasks,
    int Deliveries,
    int Completed,
    long VisibleInputTokens,
    long VisibleOutputTokens,
    long EquivalentTokens,
    long EquivalentTokenLow,
    long EquivalentTokenHigh,
    decimal EquivalentCost,
    decimal EquivalentCostLow,
    decimal EquivalentCostHigh,
    int UnpricedDeliveries,
    IReadOnlyList<UsageTrendPoint> Trend,
    IReadOnlyList<UsageModelBreakdown> Models,
    IReadOnlyList<UsageModeBreakdown> Modes)
{
    internal double CompletionRate => Deliveries == 0 ? 0 : (double)Completed / Deliveries;
}

internal static class UsageStatisticsCalculator
{
    internal const double DefaultMultiplier = 3.0;
    internal const double MinimumMultiplier = 1.0;
    internal const double MaximumMultiplier = 6.0;
    internal const double LowEstimateMultiplier = 2.0;
    internal const double HighEstimateMultiplier = 5.0;

    internal static readonly IReadOnlyList<UsageModelRate> PrototypeRates =
    [
        new("opus", "Claude Opus 4.8", 5m, 25m),
        new("gpt56", "GPT-5.6 Think deeper", 5m, 30m),
        new("gpt55", "GPT-5.5 Instant", 5m, 30m)
    ];

    private static readonly IReadOnlyDictionary<string, UsageModelRate> RatesByAlias = BuildRateAliases();

    internal static UsageStatisticsDataset Prepare(IEnumerable<ConversationDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var deliveries = new List<UsageDeliveryProjection>();
        foreach (var document in documents)
        {
            var mode = NormalizeMode(document.Mode);
            for (var index = 0; index < document.Turns.Count; index++)
            {
                var input = document.Turns[index];
                if (!input.Role.Equals("agent", StringComparison.OrdinalIgnoreCase)) continue;

                var output = index + 1 < document.Turns.Count &&
                             document.Turns[index + 1].Role.Equals("copilot", StringComparison.OrdinalIgnoreCase)
                    ? document.Turns[index + 1]
                    : null;
                var inputTokens = EstimateVisibleTokens(input.Markdown);
                var outputTokens = EstimateVisibleTokens(output?.Markdown ?? string.Empty);
                var rate = RateFor(output?.Model);
                deliveries.Add(new UsageDeliveryProjection(
                    document.Id,
                    input.Timestamp,
                    mode,
                    CanonicalModel(output?.Model, rate),
                    inputTokens,
                    outputTokens,
                    output is not null,
                    rate is null
                        ? null
                        : (inputTokens * rate.InputPerMillion + outputTokens * rate.OutputPerMillion) / 1_000_000m));
            }
        }
        return new UsageStatisticsDataset(deliveries.ToArray());
    }

    internal static UsageStatisticsSnapshot Calculate(
        IEnumerable<ConversationDocument> documents,
        DateTimeOffset from,
        DateTimeOffset to,
        double multiplier = DefaultMultiplier)
    {
        ValidateCalculation(from, to, multiplier);
        return Calculate(Prepare(documents), from, to, multiplier);
    }

    internal static UsageStatisticsSnapshot Calculate(
        UsageStatisticsDataset dataset,
        DateTimeOffset from,
        DateTimeOffset to,
        double multiplier = DefaultMultiplier)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ValidateCalculation(from, to, multiplier);

        var normalizedFrom = from.Date;
        var normalizedTo = to.Date.AddDays(1).AddTicks(-1);
        var deliveries = dataset.Deliveries
            .Where(item => item.Timestamp >= normalizedFrom && item.Timestamp <= normalizedTo)
            .ToArray();
        var modeRows = BuildModeRows(deliveries);
        var inputTokens = deliveries.Sum(item => (long)item.InputTokens);
        var outputTokens = deliveries.Sum(item => (long)item.OutputTokens);
        var visibleTokens = inputTokens + outputTokens;
        var baseCost = deliveries.Sum(item => item.BaseCost ?? 0m);
        var lowMultiplier = Math.Min(multiplier, LowEstimateMultiplier);
        var highMultiplier = Math.Max(multiplier, HighEstimateMultiplier);

        return new UsageStatisticsSnapshot(
            normalizedFrom,
            normalizedTo,
            modeRows.Sum(item => item.Tasks),
            deliveries.Length,
            deliveries.Count(item => item.Completed),
            inputTokens,
            outputTokens,
            ScaleTokens(visibleTokens, multiplier),
            ScaleTokens(visibleTokens, lowMultiplier),
            ScaleTokens(visibleTokens, highMultiplier),
            ScaleCost(baseCost, multiplier),
            ScaleCost(baseCost, lowMultiplier),
            ScaleCost(baseCost, highMultiplier),
            deliveries.Count(item => item.BaseCost is null),
            BuildTrend(deliveries, normalizedFrom, normalizedTo),
            BuildModelRows(deliveries, multiplier),
            modeRows);
    }

    internal static int EstimateVisibleTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var tokens = 0d;
        var latinRun = 0;

        void FlushLatin()
        {
            if (latinRun == 0) return;
            tokens += Math.Ceiling(latinRun / 4d);
            latinRun = 0;
        }

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                FlushLatin();
                continue;
            }

            if (IsCjk(character))
            {
                FlushLatin();
                tokens += 1;
            }
            else if (char.IsLetterOrDigit(character))
            {
                latinRun++;
            }
            else
            {
                FlushLatin();
                tokens += 0.5;
            }
        }

        FlushLatin();
        return Math.Max(1, (int)Math.Ceiling(tokens));
    }

    private static IReadOnlyList<UsageTrendPoint> BuildTrend(
        IReadOnlyCollection<UsageDeliveryProjection> deliveries,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var totalDays = Math.Max(1, (to.Date - from.Date).Days + 1);
        var bucketDays = totalDays <= 31 ? 1 : 7;
        var points = new List<UsageTrendPoint>();
        for (var cursor = from.Date; cursor <= to.Date; cursor = cursor.AddDays(bucketDays))
        {
            var bucketEnd = cursor.AddDays(bucketDays);
            points.Add(new UsageTrendPoint(
                cursor,
                cursor.ToString("MM-dd"),
                deliveries.Count(item => item.Timestamp >= cursor && item.Timestamp < bucketEnd)));
        }
        return points;
    }

    private static IReadOnlyList<UsageModelBreakdown> BuildModelRows(
        IReadOnlyCollection<UsageDeliveryProjection> deliveries,
        double multiplier)
    {
        var rows = deliveries.GroupBy(item => item.Model, StringComparer.Ordinal)
            .Select(group =>
            {
                var input = group.Sum(item => (long)item.InputTokens);
                var output = group.Sum(item => (long)item.OutputTokens);
                return new UsageModelBreakdown(
                    group.Key,
                    group.Count(),
                    input,
                    output,
                    ScaleTokens(input + output, multiplier),
                    group.All(item => item.BaseCost is not null)
                        ? ScaleCost(group.Sum(item => item.BaseCost ?? 0m), multiplier)
                        : null);
            }).ToList();

        foreach (var rate in PrototypeRates)
        {
            if (rows.All(row => !row.Model.Equals(rate.DisplayName, StringComparison.Ordinal)))
            {
                rows.Add(new UsageModelBreakdown(rate.DisplayName, 0, 0, 0, 0, 0m));
            }
        }

        return rows.OrderByDescending(row => row.Deliveries)
            .ThenBy(row => row.Model, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<UsageModeBreakdown> BuildModeRows(
        IReadOnlyCollection<UsageDeliveryProjection> deliveries) =>
        deliveries.GroupBy(item => (item.DocumentId, item.Mode))
            .Select(group => new UsageModeBreakdown(
                group.Key.Mode,
                group.Key.Mode == "Review" ? (group.Count() + 1) / 2 : group.Count(),
                group.Count()))
            .GroupBy(item => item.Mode, StringComparer.Ordinal)
            .Select(group => new UsageModeBreakdown(
                group.Key,
                group.Sum(item => item.Tasks),
                group.Sum(item => item.Deliveries)))
            .OrderByDescending(item => item.Deliveries)
            .ThenBy(item => item.Mode, StringComparer.Ordinal)
            .ToArray();

    private static UsageModelRate? RateFor(string? model) =>
        string.IsNullOrWhiteSpace(model)
            ? null
            : RatesByAlias.GetValueOrDefault(model.Trim());

    private static void ValidateCalculation(DateTimeOffset from, DateTimeOffset to, double multiplier)
    {
        if (to < from) throw new ArgumentException("Statistics end date must not precede start date.");
        if (!double.IsFinite(multiplier) || multiplier is < MinimumMultiplier or > MaximumMultiplier)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }
    }

    private static string CanonicalModel(string? model, UsageModelRate? rate)
    {
        if (rate is not null) return rate.DisplayName;
        return string.IsNullOrWhiteSpace(model) ? "未知模型（未定价）" : $"{model.Trim()}（未定价）";
    }

    private static IReadOnlyDictionary<string, UsageModelRate> BuildRateAliases()
    {
        var rates = PrototypeRates.ToDictionary(rate => rate.Key, StringComparer.Ordinal);
        return new Dictionary<string, UsageModelRate>(StringComparer.OrdinalIgnoreCase)
        {
            ["opus"] = rates["opus"],
            ["Claude Opus"] = rates["opus"],
            ["Opus 4.8"] = rates["opus"],
            ["Claude Opus 4.8"] = rates["opus"],
            ["opus_4_8"] = rates["opus"],
            ["gpt56"] = rates["gpt56"],
            ["GPT 5.6 Think deeper"] = rates["gpt56"],
            ["GPT-5.6 Think deeper"] = rates["gpt56"],
            ["gpt_5_6_think_deeper"] = rates["gpt56"],
            ["gpt55"] = rates["gpt55"],
            ["GPT 5.5 Instant"] = rates["gpt55"],
            ["GPT-5.5 Instant"] = rates["gpt55"],
            ["GPT 5.5 快速响应"] = rates["gpt55"],
            ["GPT-5.5 快速响应"] = rates["gpt55"],
            ["gpt_5_5_instant"] = rates["gpt55"]
        };
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = mode?.Trim() ?? string.Empty;
        return normalized.ToLowerInvariant() switch
        {
            "assist" => "Assist",
            "outsource" => "Outsource",
            "review" => "Review",
            "history_import" => "History import",
            "" => "Unknown",
            _ => $"{normalized} (unknown)"
        };
    }

    private static long ScaleTokens(long visibleTokens, double multiplier) =>
        (long)Math.Round(visibleTokens * multiplier, MidpointRounding.AwayFromZero);

    private static decimal ScaleCost(decimal visibleCost, double multiplier) =>
        visibleCost * (decimal)multiplier;

    private static bool IsCjk(char value) =>
        value is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';
}
