using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private void ShowSettings()
    {
        SetPage("Settings", "Application preferences.");
        _viewModel.Settings.ReplaceRows(SettingsPageRows());

        var root = Dock();
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var saveButton = Button("Save Settings", async (_, _) => await SaveSettingsAsync());
        Grid.SetColumn(saveButton, 0);
        toolbar.Children.Add(saveButton);

        var themeBar = Bar();
        themeBar.Margin = new Thickness(0);
        themeBar.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        themeBar.Children.Add(new TextBlock
        {
            Text = "Theme",
            Foreground = (System.Windows.Media.Brush)WpfApplication.Current.Resources["TextMuted"],
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6)
        });
        _themeCombo = LaunchCombo("system", "light", "dark");
        _themeCombo.Width = 110;
        _themeCombo.SelectedItem = AppPreferenceService.ThemeMode(_settings.ThemeMode);
        _themeCombo.SelectionChanged += (_, _) =>
        {
            var mode = AppPreferenceService.ThemeMode(ComboValue(_themeCombo));
            _settings = _settings with { ThemeMode = mode };
            ApplyTheme(mode);
            SetStatus("Theme preview applied. Save settings to keep it.");
        };
        themeBar.Children.Add(_themeCombo);
        Grid.SetColumn(themeBar, 2);
        toolbar.Children.Add(themeBar);
        System.Windows.Controls.DockPanel.SetDock(toolbar, System.Windows.Controls.Dock.Top);
        root.Children.Add(toolbar);

        var grid = new DataGrid { IsReadOnly = false, ItemsSource = _viewModel.Settings.Rows, RowHeight = 38 };
        PolishGrid(grid);
        var textStyle = (Style)WpfApplication.Current.Resources["GridCellText"];
        grid.Columns.Add(new DataGridTextColumn { Header = "Group", Binding = new WpfBinding(nameof(EditableSettingRow.Group)), IsReadOnly = true, ElementStyle = textStyle, MinWidth = 80, Width = new DataGridLength(120), CanUserResize = true });
        grid.Columns.Add(new DataGridTextColumn { Header = "Setting", Binding = new WpfBinding(nameof(EditableSettingRow.Label)), IsReadOnly = true, ElementStyle = textStyle, MinWidth = 110, Width = new DataGridLength(180), CanUserResize = true });
        grid.Columns.Add(SettingsValueColumn());
        grid.Columns.Add(SettingsSecretActionsColumn());
        AddButtonColumn(grid, "Action", nameof(EditableSettingRow.Action), nameof(EditableSettingRow.CanAction), SettingsRowAction_Click, .75, tooltipBinding: nameof(EditableSettingRow.ActionToolTip));
        root.Children.Add(GridFrame(grid));
        PageHost.Content = root;
    }

    private IReadOnlyList<SettingRowDefinition> SettingsPageRows() =>
    [
        new("Storage", "Cache", "cache", $"{DisplayFormatService.BytesOrZero(CacheMaintenanceService.Size(_settings.CacheRoot))} in {_settings.CacheRoot}", "readonly", Action: "Clear"),
        new("Window", "Minimize behavior", "minimizeBehavior", AppPreferenceService.MinimizeBehaviorLabel(_settings.MinimizeBehavior), "choice", AppPreferenceService.MinimizeBehaviorOptions()),
        new("Model", "Auto unload idle min", "autoUnloadIdleMinutes", _settings.AutoUnloadIdleMinutes.ToString()),
        new("Runtime", "Delete source after build", "deleteRuntimeSourceAfterSuccessfulBuild", AppPreferenceService.YesNoLabel(_settings.DeleteRuntimeSourceAfterSuccessfulBuild), "choice", AppPreferenceService.YesNoOptions()),
        new("Network", "Model access", "modelAccessMode", AppPreferenceService.ModelAccessModeLabel(_settings.ModelAccessMode), "choice", AppPreferenceService.ModelAccessModeOptions()),
        new("Network", "Port", "port", _settings.Port.ToString()),
        new("Network", "API key", "modelApiKey", _settings.ModelApiKey, "secret", Action: "Generate"),
        new("Logs", "Max log file MB", "maxLogFileSizeMb", _settings.MaxLogFileSizeMb.ToString())
    ];

    private async void SettingsRowAction_Click(object sender, RoutedEventArgs e)
    {
        await RunEventAsync(async () =>
        {
            if (sender is not WpfButton { Tag: EditableSettingRow row }) return;
            if (row.Key == "cache")
            {
                await ClearCacheAsync();
                return;
            }

            if (row.Key == "modelApiKey")
            {
                row.Value = ApiSecurity.GenerateHexToken(32);
                SetStatus("New model API key generated. Save settings to apply it.");
                return;
            }

            if (row.Type != "folder") return;
            var folder = PickFolder(row.Value);
            if (folder is null) return;

            row.Value = Path.GetFullPath(folder);
            SetStatus($"{row.Label} folder selected. Save settings to apply it.");
        });
    }

    private async void SettingsRevealSecretRow_Click(object sender, RoutedEventArgs e)
    {
        await RunEventAsync(() =>
        {
            if (sender is not WpfButton { Tag: EditableSettingRow { Type: "secret" } row }) return Task.CompletedTask;
            row.IsSecretVisible = !row.IsSecretVisible;
            SetStatus(row.IsSecretVisible ? "API key is visible in Settings." : "API key hidden.");
            return Task.CompletedTask;
        });
    }

    private async void SettingsCopySecretRow_Click(object sender, RoutedEventArgs e)
    {
        await RunEventAsync(() =>
        {
            if (sender is not WpfButton { Tag: EditableSettingRow { Type: "secret" } row }) return Task.CompletedTask;
            var value = (row.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                SetStatus("No API key is available to copy.");
                return Task.CompletedTask;
            }

            System.Windows.Clipboard.SetText(value);
            SetStatus("API key copied to clipboard.");
            return Task.CompletedTask;
        });
    }

    private async Task ClearCacheAsync()
    {
        if (!CacheMaintenanceService.IsSafeCacheRoot(_workspaceRoot, _settings.CacheRoot))
        {
            ThemedMessageBox.Show(this, "The cache folder is outside the app workspace or contains a junction/symlink, so it was not cleared.", "Clear cache", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var runningCacheJobs = _huggingFace?.ActiveDownloadCount > 0 || await HasRunningCacheJobAsync();
        if (runningCacheJobs)
        {
            ThemedMessageBox.Show(this, "Downloads or runtime builds are still using the cache. Stop or finish them before clearing it.", "Clear cache", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var size = await Task.Run(() => CacheMaintenanceService.Size(_settings.CacheRoot));
        if (size <= 0)
        {
            ThemedMessageBox.Show(this, "No cache files are currently stored.", "Clear cache", MessageBoxButton.OK, MessageBoxImage.Information);
            if (_viewModel.CurrentPage == "Settings") ShowSettings();
            return;
        }

        if (ThemedMessageBox.Show(this, $"Clear {DisplayFormatService.BytesOrZero(size)} from the app cache?\n\nRuntime sources, temporary build files, partial update downloads, and other disposable cache data will be removed.", "Clear cache", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await RunAsync("Clearing cache...", async () =>
        {
            await Task.Run(() => CacheMaintenanceService.ClearSafeCacheRoot(_workspaceRoot, _settings.CacheRoot));
            SetStatus($"Cleared cache ({DisplayFormatService.BytesOrZero(size)}).");
            if (_viewModel.CurrentPage == "Settings") ShowSettings();
        });
    }

    private async Task<bool> HasRunningCacheJobAsync()
    {
        if (_stateStore is null) return false;
        var jobs = await _stateStore.ListJobsAsync();
        return jobs.Any(job =>
            job.Status is JobStatus.Queued or JobStatus.Running or JobStatus.Paused
            && (job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase)
                || job.Kind.Contains("download", StringComparison.OrdinalIgnoreCase)));
    }

    private static DataGridTemplateColumn SettingsValueColumn()
    {
        var root = new FrameworkElementFactory(typeof(Grid));

        var textBox = new FrameworkElementFactory(typeof(WpfTextBox));
        textBox.SetBinding(WpfTextBox.TextProperty, new WpfBinding(nameof(EditableSettingRow.Value))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        textBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        textBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 6, 1));
        var textBoxStyle = new Style(typeof(WpfTextBox), (Style)WpfApplication.Current.Resources[typeof(WpfTextBox)]);
        var hideTextBoxForChoice = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "choice" };
        hideTextBoxForChoice.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        textBoxStyle.Triggers.Add(hideTextBoxForChoice);
        var hideTextBoxForReadonly = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "readonly" };
        hideTextBoxForReadonly.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        textBoxStyle.Triggers.Add(hideTextBoxForReadonly);
        var hideTextBoxForSecret = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "secret" };
        hideTextBoxForSecret.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        textBoxStyle.Triggers.Add(hideTextBoxForSecret);
        textBox.SetValue(FrameworkElement.StyleProperty, textBoxStyle);
        root.AppendChild(textBox);

        var combo = new FrameworkElementFactory(typeof(WpfComboBox));
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new WpfBinding(nameof(EditableSettingRow.Options)));
        combo.SetBinding(WpfComboBox.SelectedItemProperty, new WpfBinding(nameof(EditableSettingRow.Value))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        combo.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        combo.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 6, 1));
        var comboStyle = new Style(typeof(WpfComboBox), (Style)WpfApplication.Current.Resources[typeof(WpfComboBox)]);
        comboStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var showComboForChoice = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "choice" };
        showComboForChoice.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        comboStyle.Triggers.Add(showComboForChoice);
        combo.SetValue(FrameworkElement.StyleProperty, comboStyle);
        root.AppendChild(combo);

        var textBlock = new FrameworkElementFactory(typeof(TextBlock));
        textBlock.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(EditableSettingRow.DisplayValue)));
        textBlock.SetValue(TextBlock.ForegroundProperty, WpfApplication.Current.Resources["TextSoft"]);
        textBlock.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textBlock.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        textBlock.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 6, 1));
        var textBlockStyle = new Style(typeof(TextBlock));
        textBlockStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var showTextBlockForReadonly = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "readonly" };
        showTextBlockForReadonly.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        textBlockStyle.Triggers.Add(showTextBlockForReadonly);
        var showTextBlockForSecret = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "secret" };
        showTextBlockForSecret.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        textBlockStyle.Triggers.Add(showTextBlockForSecret);
        textBlock.SetValue(FrameworkElement.StyleProperty, textBlockStyle);
        root.AppendChild(textBlock);

        return new DataGridTemplateColumn
        {
            Header = "Value",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 240,
            CanUserResize = true,
            CellTemplate = new DataTemplate { VisualTree = root }
        };
    }

    private DataGridTemplateColumn SettingsSecretActionsColumn()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0));
        var rootStyle = new Style(typeof(StackPanel));
        rootStyle.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var showForSecret = new DataTrigger { Binding = new WpfBinding(nameof(EditableSettingRow.Type)), Value = "secret" };
        showForSecret.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        rootStyle.Triggers.Add(showForSecret);
        root.SetValue(FrameworkElement.StyleProperty, rootStyle);

        var revealButton = SettingsSecretActionButton(nameof(EditableSettingRow.RevealAction), nameof(EditableSettingRow.CanRevealAction), nameof(EditableSettingRow.RevealToolTip));
        revealButton.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler(SettingsRevealSecretRow_Click));
        root.AppendChild(revealButton);

        var copyButton = SettingsSecretActionButton(nameof(EditableSettingRow.CopyAction), nameof(EditableSettingRow.CanCopyAction), nameof(EditableSettingRow.CopyToolTip));
        copyButton.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler(SettingsCopySecretRow_Click));
        root.AppendChild(copyButton);

        return new DataGridTemplateColumn
        {
            Header = "Secret",
            Width = new DataGridLength(.95, DataGridLengthUnitType.Star),
            MinWidth = 132,
            CanUserResize = true,
            CellTemplate = new DataTemplate { VisualTree = root }
        };
    }

    private static FrameworkElementFactory SettingsSecretActionButton(string contentBinding, string enabledBinding, string tooltipBinding)
    {
        var button = new FrameworkElementFactory(typeof(WpfButton));
        button.SetBinding(ContentControl.ContentProperty, new WpfBinding(contentBinding));
        button.SetBinding(UIElement.IsEnabledProperty, new WpfBinding(enabledBinding));
        button.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(tooltipBinding));
        button.SetBinding(FrameworkElement.TagProperty, new WpfBinding("."));
        button.SetValue(ToolTipService.ShowOnDisabledProperty, true);
        button.SetValue(FrameworkElement.MinHeightProperty, 22.0);
        button.SetValue(System.Windows.Controls.Control.PaddingProperty, new Thickness(7, 1, 7, 2));
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
        button.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        button.SetValue(FrameworkElement.StyleProperty, new Style(typeof(WpfButton), (Style)WpfApplication.Current.Resources[typeof(WpfButton)]));
        return button;
    }
}
