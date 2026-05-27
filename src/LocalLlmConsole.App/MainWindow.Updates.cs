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
    private void ShowUpdates()
    {
        SetPage("Updates", "App updates from GitHub releases.");
        var root = Stack();
        root.Margin = new Thickness(16);

        var actions = Bar();
        actions.Children.Add(Button(_viewModel.Updates.ActionText, async (_, _) =>
        {
            if (_viewModel.Updates.LatestUpdate is { IsAvailable: true } available)
                await InstallAppUpdateAsync(available, confirm: true);
            else
                await CheckForAppUpdatesAsync(manual: true);
        }));
        actions.Children.Add(Button("Open GitHub", (_, _) => OpenUrl(AppUpdateService.RepositoryUrl)));
        root.Children.Add(actions);

        root.Children.Add(FramedSection("Update Status", new TextBlock
        {
            Text = _viewModel.Updates.StatusDetails,
            Foreground = (System.Windows.Media.Brush)WpfApplication.Current.Resources["TextSoft"],
            TextWrapping = TextWrapping.Wrap
        }));

        if (_viewModel.Updates.LatestUpdate is { IsAvailable: true })
        {
            root.Children.Add(FramedSection("Latest Release", new TextBlock
            {
                Text = _viewModel.Updates.LatestReleaseText,
                Foreground = (System.Windows.Media.Brush)WpfApplication.Current.Resources["TextSoft"],
                TextWrapping = TextWrapping.Wrap
            }));
        }

        PageHost.Content = root;
    }

    private async Task CheckForAppUpdatesOnStartupAsync()
    {
        try
        {
            await CheckForAppUpdatesAsync(manual: false);
        }
        catch
        {
        }
    }

    private async Task CheckForAppUpdatesAsync(bool manual)
    {
        if (_viewModel.Updates.CheckInFlight) return;
        _viewModel.Updates.CheckInFlight = true;
        try
        {
            SetStatus("Checking for app updates...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var latestUpdate = await _appUpdates.CheckLatestAsync(cts.Token);
            _viewModel.Updates.SetLatestUpdate(latestUpdate);
            UpdateAppUpdateNavigation();
            if (_viewModel.CurrentPage == "Updates") ShowUpdates();

            if (latestUpdate.IsAvailable)
            {
                SetStatus($"Update available: {latestUpdate.LatestVersion}.");
                if (manual)
                {
                    var message = $"Update {latestUpdate.CurrentVersion} -> {latestUpdate.LatestVersion} is available.\n\n{latestUpdate.ReleaseName}\n\nInstall it now?";
                    if (ThemedMessageBox.Show(this, message, "Install update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        await InstallAppUpdateAsync(latestUpdate, confirm: false);
                }
            }
            else
            {
                SetStatus("No app updates available.");
                if (manual)
                    ThemedMessageBox.Show(this, $"No updates are available.\n\nCurrent version: {latestUpdate.CurrentVersion}", "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Update check failed: {ex.Message}");
            if (manual)
                ThemedMessageBox.Show(this, ex.Message, "Update check failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _viewModel.Updates.CheckInFlight = false;
        }
    }

    private async Task InstallAppUpdateAsync(AppUpdateInfo update, bool confirm)
    {
        if (!update.IsAvailable) return;
        if (string.IsNullOrWhiteSpace(update.AssetUrl))
        {
            ThemedMessageBox.Show(this, "The latest GitHub release does not include a portable Windows app asset.", "Install update", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (confirm && ThemedMessageBox.Show(this, $"Install {update.LatestVersion} now?\n\nThe app will close, replace the executable, and restart.", "Install update", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;

        await RunAsync("Preparing app update...", async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var plan = await _appUpdates.StageInstallAsync(update, _workspaceRoot, Environment.ProcessPath, cts.Token);
            _appUpdates.StartInstaller(plan, Environment.ProcessId);
            SetStatus("Update staged. Closing to install...");
        });

        Close();
    }

    private async Task ShowCompletedAppUpdateNoticeAsync()
    {
        var notice = await AppUpdateService.TryConsumeInstalledNoticeAsync(_workspaceRoot);
        if (notice is null) return;
        ThemedMessageBox.Show(
            this,
            $"Updated to {notice.Version}.\n\n{notice.ReleaseName}\n\n{DisplayFormatService.TrimForDisplay(notice.ReleaseNotes, 2600)}",
            "Update installed",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void UpdateAppUpdateNavigation()
        => UpdatesNavButton.Content = _viewModel.Updates.NavigationText;
}
