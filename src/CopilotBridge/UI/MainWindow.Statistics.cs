using System.Windows;
using System.Windows.Controls;
using CopilotBridge.Core;

namespace CopilotBridge.UI;

public partial class MainWindow
{
    private UsageStatisticsDataset _statisticsDataset = UsageStatisticsCalculator.Prepare([]);
    private bool _statisticsRefreshInProgress;

    private async void RefreshStatistics_Click(object sender, RoutedEventArgs e) =>
        await RefreshStatisticsAsync();

    private void StatisticsRange_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatisticsCustomRangePanel is null) return;
        StatisticsCustomRangePanel.Visibility = StatisticsRangeComboBox.SelectedIndex == 3
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_settingsAreLoaded && _activePage == "statistics" && StatisticsRangeComboBox.SelectedIndex != 3)
        {
            RenderStatistics();
        }
    }

    private void ApplyStatisticsRange_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsAreLoaded && _activePage == "statistics") RenderStatistics();
    }

    private void StatisticsMultiplier_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (StatisticsMultiplierText is null) return;
        StatisticsMultiplierText.Text = $"{StatisticsMultiplierSlider.Value:0.0}×";
        if (_settingsAreLoaded && _activePage == "statistics" && _statisticsDataset.Deliveries.Count > 0)
        {
            RenderStatistics();
        }
    }

    private async void SaveStatisticsMultiplier_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = _settings with { StatisticsTokenMultiplier = StatisticsMultiplierSlider.Value };
            await _settingsStore.SaveAsync(_settings);
            RenderStatistics();
            ShowNotice(T("统计倍率已保存。"), NoticeKind.Success);
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
    }

    private async Task RefreshStatisticsAsync()
    {
        if (_statisticsRefreshInProgress) return;
        _statisticsRefreshInProgress = true;
        try
        {
            StatisticsPeriodText.Text = T("正在读取本地记录");
            var workspace = _workspace;
            _statisticsDataset = await Task.Run(async () =>
                UsageStatisticsCalculator.Prepare(await workspace.GetConversationDocumentsAsync()));
            if (_activePage == "statistics") RenderStatistics();
        }
        catch (Exception exception)
        {
            ShowNotice(FriendlyMessage(exception), NoticeKind.Error);
        }
        finally
        {
            _statisticsRefreshInProgress = false;
        }
    }

    private void RenderStatistics()
    {
        var (from, to) = StatisticsDateRange();
        var multiplier = StatisticsMultiplierSlider.Value;
        var snapshot = UsageStatisticsCalculator.Calculate(_statisticsDataset, from, to, multiplier);

        StatisticsDeliveryText.Text = snapshot.Deliveries.ToString("N0");
        StatisticsTaskText.Text = string.Format(T("{0} 个协作任务"), snapshot.Tasks);
        StatisticsCompletedText.Text = $"{snapshot.Completed:N0} · {snapshot.CompletionRate:P0}";
        StatisticsTokenText.Text = FormatTokens(snapshot.EquivalentTokens);
        StatisticsTokenRangeText.Text = string.Format(
            T("可见输入 {0} · 输出 {1}；区间 {2}–{3}"),
            FormatTokens(snapshot.VisibleInputTokens),
            FormatTokens(snapshot.VisibleOutputTokens),
            FormatTokens(snapshot.EquivalentTokenLow),
            FormatTokens(snapshot.EquivalentTokenHigh));
        StatisticsCostText.Text = FormatCost(snapshot.EquivalentCost);
        StatisticsCostRangeText.Text = string.Format(
            T("估算区间 {0}–{1}"),
            FormatCost(snapshot.EquivalentCostLow),
            FormatCost(snapshot.EquivalentCostHigh));
        StatisticsPeriodText.Text = string.Format(
            T("{0} 至 {1} · 本地已保存记录"),
            snapshot.From.ToString("yyyy-MM-dd"),
            snapshot.To.ToString("yyyy-MM-dd"));
        StatisticsTrendGranularityText.Text = T((snapshot.To.Date - snapshot.From.Date).Days <= 30 ? "按日" : "按周");

        var maximum = Math.Max(1, snapshot.Trend.Max(point => point.Deliveries));
        StatisticsTrendItems.ItemsSource = snapshot.Trend.Select(point => new StatisticsTrendItem(
            point.Label,
            point.Deliveries == 0 ? 2 : Math.Max(8, point.Deliveries * 120d / maximum),
            string.Format(T("{0}：{1} 次交付"), point.Label, point.Deliveries))).ToArray();
        StatisticsModelItems.ItemsSource = snapshot.Models.Select(row => new StatisticsModelItem(
            row.Model,
            row.Deliveries.ToString("N0"),
            FormatTokens(row.InputTokens),
            FormatTokens(row.OutputTokens),
            row.EquivalentCost is null ? T("未定价") : FormatCost(row.EquivalentCost.Value))).ToArray();
        StatisticsModeItems.ItemsSource = snapshot.Modes.Select(row => new StatisticsModeItem(
            row.Mode,
            string.Format(T("{0} 个任务 · {1} 次交付"), row.Tasks, row.Deliveries))).ToArray();
        StatisticsCoverageText.Text = snapshot.UnpricedDeliveries == 0
            ? T("数据来源：Bridge 本地会话；已排除显式导入的网页历史，不扫描 Microsoft 365 历史。")
            : string.Format(
                T("数据来源：Bridge 本地会话；{0} 次交付的模型未知或未定价，未计入费用。"),
                snapshot.UnpricedDeliveries);
    }

    private (DateTimeOffset From, DateTimeOffset To) StatisticsDateRange()
    {
        var today = DateTimeOffset.Now.Date;
        if (StatisticsRangeComboBox.SelectedIndex == 0) return (today.AddDays(-6), today);
        if (StatisticsRangeComboBox.SelectedIndex == 1) return (today.AddDays(-29), today);
        if (StatisticsRangeComboBox.SelectedIndex == 3)
        {
            var from = StatisticsFromDatePicker.SelectedDate ?? today.AddDays(-29).Date;
            var to = StatisticsToDatePicker.SelectedDate ?? today.Date;
            if (to < from) (from, to) = (to, from);
            return (new DateTimeOffset(from.Date), new DateTimeOffset(to.Date));
        }

        var first = _statisticsDataset.Deliveries
            .Select(delivery => (DateTimeOffset?)delivery.Timestamp.Date)
            .Min() ?? today;
        return (first, today);
    }

    private static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000d:0.##}M",
        >= 1_000 => $"{tokens / 1_000d:0.##}K",
        _ => tokens.ToString("N0")
    };

    private static string FormatCost(decimal cost) => cost >= 1m ? $"${cost:0.00}" : $"${cost:0.0000}";

    private sealed record StatisticsTrendItem(string Label, double BarHeight, string Tooltip);
    private sealed record StatisticsModelItem(
        string Model,
        string Deliveries,
        string InputTokens,
        string OutputTokens,
        string Cost);
    private sealed record StatisticsModeItem(string Mode, string Summary);
}
