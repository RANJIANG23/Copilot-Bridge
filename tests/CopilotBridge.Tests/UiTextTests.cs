using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using CopilotBridge.Core;
using CopilotBridge.UI;
using Xunit;

namespace CopilotBridge.Tests;

public sealed class UiTextTests
{
    [Fact]
    public void ApplyTranslatesSupportedContentAndAutomationMetadataInBothDirections()
    {
        RunOnSta(() =>
        {
            var button = new Button { Content = "设置" };
            var comboBoxItem = new ComboBoxItem { Content = "中文" };
            var comboBox = new ComboBox { Items = { comboBoxItem } };
            var radioButton = new RadioButton { Content = "关闭" };
            var checkBox = new CheckBox { Content = "系统托盘" };
            var menuItem = new MenuItem { Header = "删除" };
            var bindingSource = new TextBlock { Tag = "设置", ToolTip = "状态提示" };
            var accessibleButton = new Button { Content = "概览", ToolTip = "关闭提示" };
            BindingOperations.SetBinding(
                accessibleButton,
                AutomationProperties.NameProperty,
                new Binding(nameof(FrameworkElement.Tag)) { Source = bindingSource });
            BindingOperations.SetBinding(
                accessibleButton,
                AutomationProperties.HelpTextProperty,
                new Binding(nameof(FrameworkElement.ToolTip)) { Source = bindingSource });

            var panel = new StackPanel
            {
                Children = { button, comboBox, radioButton, checkBox, menuItem, accessibleButton }
            };
            var window = new Window
            {
                Title = "Copilot Bridge",
                Content = panel,
                Width = 800,
                Height = 600,
                Left = -10000,
                Top = -10000,
                Opacity = 0,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                UiText.Apply(window, AppLanguage.English);

                Assert.Equal("Copilot Bridge", window.Title);
                Assert.Equal("Settings", button.Content);
                Assert.Equal("Chinese", comboBoxItem.Content);
                Assert.Equal("Disabled", radioButton.Content);
                Assert.Equal("System tray", checkBox.Content);
                Assert.Equal("Delete", menuItem.Header);
                Assert.Equal("Close notification", accessibleButton.ToolTip);
                Assert.Equal("Settings", AutomationProperties.GetName(accessibleButton));
                Assert.Equal("Status notification", AutomationProperties.GetHelpText(accessibleButton));
                Assert.True(BindingOperations.IsDataBound(accessibleButton, AutomationProperties.NameProperty));
                Assert.True(BindingOperations.IsDataBound(accessibleButton, AutomationProperties.HelpTextProperty));

                UiText.Apply(window, AppLanguage.Chinese);

                Assert.Equal("Copilot Bridge", window.Title);
                Assert.Equal("设置", button.Content);
                Assert.Equal("中文", comboBoxItem.Content);
                Assert.Equal("关闭", radioButton.Content);
                Assert.Equal("系统托盘", checkBox.Content);
                Assert.Equal("删除", menuItem.Header);
                Assert.Equal("关闭提示", accessibleButton.ToolTip);
                Assert.Equal("设置", AutomationProperties.GetName(accessibleButton));
                Assert.Equal("状态提示", AutomationProperties.GetHelpText(accessibleButton));
                Assert.True(BindingOperations.IsDataBound(accessibleButton, AutomationProperties.NameProperty));
                Assert.True(BindingOperations.IsDataBound(accessibleButton, AutomationProperties.HelpTextProperty));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
