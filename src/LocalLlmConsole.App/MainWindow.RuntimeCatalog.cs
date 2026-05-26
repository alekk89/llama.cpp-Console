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
    private async Task DetectAndRefreshRuntimesAsync()
    {
        if (_runtimes is null) return;
        await RunAsync("Detecting installed runtimes...", async () =>
        {
            var root = Path.GetFullPath(_settings.RuntimeRoot);
            await _runtimes.ScanAsync(root);
            _autoScannedRuntimeRoots.Add(root);
            await RefreshRuntimesAsync();
            await RefreshJobsAsync();
            await RefreshOverviewAsync();
        });
    }

    private async Task EnsureRuntimeRootScannedAsync()
    {
        if (_runtimes is null) return;
        var root = Path.GetFullPath(_settings.RuntimeRoot);
        if (_autoScannedRuntimeRoots.Add(root))
            await _runtimes.ScanAsync(root);
    }

    private async Task<Dictionary<string, List<string>>> ModelsByRuntimeAsync()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (_stateStore is null) return map;

        foreach (var model in await _stateStore.ListModelsAsync())
        {
            var profile = await _stateStore.GetModelLaunchSettingsAsync(model.Id);
            if (string.IsNullOrWhiteSpace(profile?.RuntimeId)) continue;
            if (!map.TryGetValue(profile.RuntimeId, out var models))
            {
                models = [];
                map[profile.RuntimeId] = models;
            }
            models.Add(model.Name);
        }

        foreach (var models in map.Values)
            models.Sort(StringComparer.OrdinalIgnoreCase);

        return map;
    }

    private void RefreshRuntimeBuildPresets(IReadOnlyList<RuntimeRecord> runtimes, IReadOnlyList<RuntimeSourceEntry> sources)
    {
        if (_runtimeBuildGrid is null) return;
        _viewModel.RuntimeBuilds.ReplacePresets(RuntimeBuildPresetRows(), runtimes, sources, _runtimeUpdateStates);
    }

    private IReadOnlyList<RuntimeBuildPreset> RuntimeBuildPresetRows()
        => RuntimeBuildCatalogService.PresetRows(_settings.RuntimeRoot);

    private RuntimeUpdateState? CurrentRuntimeUpdateState(string presetId, string localCommit)
    {
        if (!_runtimeUpdateStates.TryGetValue(presetId, out var state)) return null;
        return RuntimeMetadataService.CommitsMatch(state.LocalCommit, localCommit) ? state : null;
    }

    private IEnumerable<RuntimeSourceEntry> RuntimeSources()
        => RuntimeBuildCatalogService.Sources(_settings.RuntimeRoot);

    private string RuntimeSourceRoot() => RuntimeBuildCatalogService.SourceRoot(_settings.RuntimeRoot);
    private string RuntimeSourceDir(RuntimeBuildPreset preset) => RuntimeBuildCatalogService.SourceDir(_settings.RuntimeRoot, preset);

    private IReadOnlyList<RuntimeBuildPreset> CustomRuntimeBuildPresets()
        => RuntimeBuildCatalogService.ReadCustomPresets(_settings.RuntimeRoot);

    private async Task SaveCustomRuntimeBuildPresetsAsync(IReadOnlyList<RuntimeBuildPreset> presets)
        => await RuntimeBuildCatalogService.SaveCustomPresetsAsync(_settings.RuntimeRoot, presets);

    private async Task AddCustomRuntimeRepositoryAsync()
    {
        var preset = ShowCustomRuntimeRepositoryDialog();
        if (preset is null) return;

        var existing = RuntimeBuildPresetRows().FirstOrDefault(candidate => RuntimeBuildCatalogService.SameRepository(candidate, preset));
        if (existing is not null)
        {
            ThemedMessageBox.Show(this, $"That repository is already listed as:\n\n{existing.Label}", "Custom repository", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var customPresets = CustomRuntimeBuildPresets().ToList();
        customPresets.Add(preset);
        await SaveCustomRuntimeBuildPresetsAsync(customPresets);
        await RefreshRuntimesAsync();
        SetStatus($"Added custom runtime repository: {preset.Label}");
    }

    private async Task AddCustomRuntimeRepositoryFromRowAsync(RuntimeBuildPresetRow row)
    {
        var label = (row.Label ?? "").Trim();
        var repoUrl = (row.Source ?? "").Trim();
        var branch = (row.LatestLocal ?? "").Trim();
        var backend = RuntimeBuildBackendFromLabel(row.Backend);
        var cuda = backend == RuntimeBackend.Cuda;
        if (string.IsNullOrWhiteSpace(label))
        {
            SetStatus("Enter a display name for the custom runtime repository.");
            return;
        }
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            SetStatus("Enter an HTTPS Git repository URL for the custom runtime repository.");
            return;
        }
        var preset = new RuntimeBuildPreset(RuntimeBuildCatalogService.CustomPresetId(label, repoUrl, branch, RuntimeBuildCatalogService.BackendKey(backend)), label, repoUrl, branch, cuda, true, RuntimeBuildCatalogService.BackendKey(backend));
        if (!RuntimeBuildCatalogService.IsSafeUiCustomPreset(preset))
        {
            SetStatus("Custom runtime repository must be an HTTPS Git URL with a safe branch/ref. Local, file, and SSH sources are reserved for manual advanced configuration.");
            return;
        }
        var existing = RuntimeBuildPresetRows().FirstOrDefault(candidate => RuntimeBuildCatalogService.SameRepository(candidate, preset));
        if (existing is not null)
        {
            SetStatus($"That repository is already listed as {existing.Label}.");
            return;
        }

        var customPresets = CustomRuntimeBuildPresets().ToList();
        customPresets.Add(preset);
        await SaveCustomRuntimeBuildPresetsAsync(customPresets);
        await RefreshRuntimesAsync();
        SetStatus($"Added custom runtime repository: {preset.Label}");
    }

    private RuntimeBuildPreset? ShowCustomRuntimeRepositoryDialog()
    {
        RuntimeBuildPreset? result = null;
        var dialog = new Window
        {
            Title = "Add custom repository",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = true,
            ShowInTaskbar = false,
            MinWidth = 540
        };

        var root = new Border
        {
            Background = (System.Windows.Media.Brush)WpfApplication.Current.Resources["PanelBack"],
            BorderBrush = (System.Windows.Media.Brush)WpfApplication.Current.Resources["PanelBorderStrong"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        layout.Children.Add(new TextBlock
        {
            Text = "Add custom runtime repository",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)WpfApplication.Current.Resources["TextMain"],
            Margin = new Thickness(0, 0, 0, 14)
        });

        var fields = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });

        var nameBox = DialogTextBox("Example: My TurboQuant CUDA");
        var repoBox = DialogTextBox("https://github.com/user/repo.git");
        var branchBox = DialogTextBox("Optional, leave blank for repository default");
        var backendBox = new WpfComboBox
        {
            ItemsSource = new[] { "CUDA WSL", "Vulkan WSL", "CPU WSL" },
            SelectedIndex = 0,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
        };

        AddDialogRow(fields, 0, "Name", nameBox);
        AddDialogRow(fields, 1, "Repository", repoBox);
        AddDialogRow(fields, 2, "Branch", branchBox);
        AddDialogRow(fields, 3, "Backend", backendBox);

        Grid.SetRow(fields, 1);
        layout.Children.Add(fields);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var addButton = new WpfButton { Content = "Add", MinWidth = 90, IsDefault = true, Margin = new Thickness(7, 0, 0, 0) };
        var cancelButton = new WpfButton { Content = "Cancel", MinWidth = 90, IsCancel = true, Margin = new Thickness(7, 0, 0, 0) };
        SetButtonToolTip(addButton, "Add this custom runtime repository preset.");
        SetButtonToolTip(cancelButton, "Close without adding a repository.");
        addButton.Click += (_, _) =>
        {
            var label = nameBox.Text.Trim();
            var repoUrl = repoBox.Text.Trim();
            var branch = branchBox.Text.Trim();
            var backend = RuntimeBuildBackendFromLabel(backendBox.SelectedItem?.ToString() ?? "");
            var cuda = backend == RuntimeBackend.Cuda;
            if (string.IsNullOrWhiteSpace(label))
            {
                ThemedMessageBox.Show(dialog, "Give the repository a short display name.", "Custom repository", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(repoUrl))
            {
                ThemedMessageBox.Show(dialog, "Enter an HTTPS Git repository URL.", "Custom repository", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!RuntimeBuildCatalogService.IsHttpsGitSource(repoUrl))
            {
                ThemedMessageBox.Show(dialog, "Use an HTTPS Git URL without embedded credentials. Local, file, and SSH sources are reserved for manual advanced configuration.", "Custom repository", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(branch) && !RuntimeBuildCatalogService.IsSafeGitRefName(branch))
            {
                ThemedMessageBox.Show(dialog, "Branch names cannot start with '-' or contain shell/control characters.", "Custom repository", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = new RuntimeBuildPreset(RuntimeBuildCatalogService.CustomPresetId(label, repoUrl, branch, RuntimeBuildCatalogService.BackendKey(backend)), label, repoUrl, branch, cuda, true, RuntimeBuildCatalogService.BackendKey(backend));
            dialog.DialogResult = true;
        };
        actions.Children.Add(addButton);
        actions.Children.Add(cancelButton);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);

        root.Child = layout;
        dialog.Content = root;
        dialog.ShowDialog();
        return result;
    }

    private static RuntimeBackend RuntimeBuildBackendFromLabel(string label)
    {
        if (label.Contains("vulkan", StringComparison.OrdinalIgnoreCase)) return RuntimeBackend.Vulkan;
        if (label.Contains("cuda", StringComparison.OrdinalIgnoreCase)) return RuntimeBackend.Cuda;
        return RuntimeBackend.Cpu;
    }

    private static WpfTextBox DialogTextBox(string toolTip) => new()
    {
        ToolTip = toolTip,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
    };

    private static void AddDialogRow(Grid grid, int row, string label, FrameworkElement editor)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            Foreground = (System.Windows.Media.Brush)WpfApplication.Current.Resources["TextSoft"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 8)
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);

        editor.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
    }
}
