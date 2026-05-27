using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void SettingsPageDoesNotExposeCacheFolder()
    {
        var source = ReadMainWindowSources();

        Assert.DoesNotContain("\"Cache folder\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cacheRoot\"", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowHasVisibleAppStatusLine()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();

        Assert.Contains("x:Name=\"AppStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Current action", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceStatusText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeStatusText", source, StringComparison.Ordinal);
        Assert.Contains("AppStatusText.Text", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.Yield", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowUsesLlamaCppConsoleBrandingAndIcon()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var iconPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Assets", "AppIcon.ico");

        Assert.Contains("Title=\"llama.cpp Console v1.1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AppDisplayName = \"llama.cpp Console\"", source, StringComparison.Ordinal);
        Assert.Contains("AppVersionLabel = \"v1.1\"", source, StringComparison.Ordinal);
        Assert.Contains("<AssemblyName>LlamaCppConsole</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Assets\\AppIcon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.True(new FileInfo(iconPath).Length > 1024);
    }


    [Fact]
    public void MainWindowCodeBehindStaysSplitByWorkflow()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var partialNames = Directory.EnumerateFiles(appRoot, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oversizedPartials = Directory.EnumerateFiles(appRoot, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => new { Name = Path.GetFileName(path), Lines = File.ReadAllLines(path).Length })
            .Where(file => !string.Equals(file.Name, "MainWindow.xaml.cs", StringComparison.OrdinalIgnoreCase) && file.Lines > 500)
            .Select(file => $"{file.Name}:{file.Lines}")
            .ToArray();

        Assert.True(File.ReadAllLines(mainWindowPath).Length < 300);
        Assert.Empty(oversizedPartials);
        Assert.Contains("MainWindow.State.cs", partialNames);
        Assert.Contains("MainWindow.Navigation.cs", partialNames);
        Assert.Contains("MainWindow.Help.cs", partialNames);
        Assert.Contains("MainWindow.Pages.cs", partialNames);
        Assert.Contains("MainWindow.FolderSettings.cs", partialNames);
        Assert.Contains("MainWindow.Wsl.cs", partialNames);
        Assert.Contains("MainWindow.WslActions.cs", partialNames);
        Assert.Contains("MainWindow.OpenCode.cs", partialNames);
        Assert.Contains("MainWindow.OpenCodeActions.cs", partialNames);
        Assert.Contains("MainWindow.OpenCodeAgents.cs", partialNames);
        Assert.Contains("MainWindow.OpenCodeFiles.cs", partialNames);
        Assert.Contains("MainWindow.OpenCodeLocalModels.cs", partialNames);
        Assert.Contains("MainWindow.ModelLaunchProfiles.cs", partialNames);
        Assert.Contains("MainWindow.LaunchSettings.cs", partialNames);
        Assert.Contains("MainWindow.LaunchSettingsCapabilities.cs", partialNames);
        Assert.Contains("MainWindow.LaunchSettingsPanel.cs", partialNames);
        Assert.Contains("MainWindow.LaunchSettingsRuntimeSelection.cs", partialNames);
        Assert.Contains("MainWindow.ModelDownloads.cs", partialNames);
        Assert.Contains("MainWindow.ModelRows.cs", partialNames);
        Assert.Contains("MainWindow.DownloadHistory.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeBuilds.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeSourceDownloads.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeBuildJobs.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeJobControls.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeBuildGit.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeDashboard.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeMetrics.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeMetricCounters.cs", partialNames);
        Assert.Contains("MainWindow.ModelRuntime.cs", partialNames);
        Assert.Contains("MainWindow.ModelRuntimeLifecycle.cs", partialNames);
        Assert.Contains("MainWindow.ModelRuntimePrerequisites.cs", partialNames);
        Assert.Contains("MainWindow.RuntimeSession.cs", partialNames);
        Assert.Contains("MainWindow.OverviewSelection.cs", partialNames);
        Assert.Contains("MainWindow.UiHelpers.cs", partialNames);
        Assert.Contains("MainWindow.MetricInlines.cs", partialNames);
        Assert.Contains("MainWindow.GridHelpers.cs", partialNames);
        Assert.Contains("MainWindow.GridColumnSizing.cs", partialNames);
        Assert.Contains("MainWindow.Theme.cs", partialNames);
        Assert.Contains("MainWindow.UiState.cs", partialNames);
    }


    [Fact]
    public void MainWindowUsesObservedBackgroundTasks()
    {
        var source = ReadMainWindowSources();

        Assert.DoesNotContain("_ = Refresh", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Monitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = CheckFor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Seed", source, StringComparison.Ordinal);
        Assert.Contains("RunBackground", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsivenessSensitiveFilesystemWorkLeavesDispatcher()
    {
        var mainWindowSource = ReadMainWindowSources();
        var modelCatalog = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "ModelCatalogService.cs"));
        var runtimeRegistry = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "RuntimeRegistryService.cs"));

        Assert.Contains("FindModelFilesAsync", modelCatalog, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => FindModelFiles", modelCatalog, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => MergeGgufManifest", modelCatalog, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => Directory.Delete", modelCatalog, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => CandidateRuntimeFolders(runtimeRoot).Take(1000).ToArray())", runtimeRegistry, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => CreateRuntimeRecord", runtimeRegistry, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => RuntimeSources().ToList())", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => Directory.EnumerateFiles(logRoot", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => LogFileService.Tail(path, 80000))", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("ScheduleSelectedModelLaunchSettingsRefresh", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("RenderSelectedModelLaunchSettingsDebouncedAsync", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(120", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => ModelCapabilityService.CacheKey(model)", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => ModelCapabilityService.Inspect(model)", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("OpenCodeModelLimitsAsync", mainWindowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeJobLogPreviewMaxChars", mainWindowSource, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", mainWindowSource, StringComparison.Ordinal);
    }


    [Fact]
    public void LightThemeUsesLayeredSurfacesAndElevation()
    {
        var appXaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml"));
        var source = ReadMainWindowSources();

        Assert.Contains("<DropShadowEffect", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MetricCard\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Content, RelativeSource={RelativeSource TemplatedParent}}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("private static string TooltipText(string text) => text;", source, StringComparison.Ordinal);
        Assert.Contains("MetricImportantValuePattern", source, StringComparison.Ordinal);
        Assert.Contains("SplitMetricLine", source, StringComparison.Ordinal);
        Assert.Contains("MetricShouldEmphasizeWholeLine", source, StringComparison.Ordinal);
        Assert.Contains("IsNeutralMetricStatus", source, StringComparison.Ordinal);
        Assert.Contains("MetricShouldRenderNeutralStatus", source, StringComparison.Ordinal);
        Assert.Contains("TryAddStatusNameMetricLine", source, StringComparison.Ordinal);
        Assert.Contains("MetricStatusNameBlock", source, StringComparison.Ordinal);
        Assert.Contains("var valueRows = new Grid { MinHeight = 34, Tag = label }", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(label, \"Model status\", StringComparison.Ordinal)", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(label, \"Runtime build\", StringComparison.Ordinal)", source, StringComparison.Ordinal);
        Assert.Contains("\"Loaded:\"", source, StringComparison.Ordinal);
        Assert.Contains("\"Loading\"", source, StringComparison.Ordinal);
        Assert.Contains("emphasizeLoadedStatus: _llama.IsRunning", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(normalized, \"None\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(normalized, \"Stopped\", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("text.StartsWith(\"Loading \", StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);
        Assert.Contains("MetricValueFont", source, StringComparison.Ordinal);
        Assert.Contains("Typography.SetNumeralAlignment(valueRun, FontNumeralAlignment.Tabular)", source, StringComparison.Ordinal);
        Assert.Contains("(\"AppBack\", \"#E5ECF3\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"PanelBack\", \"#FFFFFF\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"PanelBorder\", \"#B7C4D2\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"PanelBorderStrong\", \"#8799AC\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"GridRowAlt\", \"#EDF4FA\")", source, StringComparison.Ordinal);
        Assert.Contains("(\"Accent\", \"#126F5B\")", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowKeepsPolishedActionPlacementAndOverviewDiagnostics()
    {
        var source = ReadMainWindowSources();
        var overviewViewModel = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "ViewModels", "OverviewPageViewModel.cs"));
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("FolderStripActionsFirst(\n            \"Models folder\"", normalized, StringComparison.Ordinal);
        Assert.Contains("(\"Scan Models Folder\", async (_, _) => await RunAsync(\"Scanning models...\"", source, StringComparison.Ordinal);
        Assert.Contains("Button(\"Save Settings\"", source, StringComparison.Ordinal);
        Assert.Contains("Select the loading or loaded model to unload it.", source, StringComparison.Ordinal);
        Assert.Contains("Choose the loading or loaded model to unload it.", source, StringComparison.Ordinal);
        Assert.Contains("Stop the currently loading or loaded model", source, StringComparison.Ordinal);
        Assert.Contains("OpenHuggingFaceModelCardRow_Click", source, StringComparison.Ordinal);
        Assert.Contains("AddButtonColumn(_hfGrid, \"Card\", \"C8\", \"B2\", OpenHuggingFaceModelCardRow_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Button(\"Model Card\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSelectedHuggingFaceModelCard", source, StringComparison.Ordinal);
        Assert.Contains("(\"Signals\", \"C6\", 1.4)", source, StringComparison.Ordinal);
        Assert.Contains("FramedSection(\"Live Runtime Log\"", source, StringComparison.Ordinal);
        Assert.Contains("GridSection(\"All llama.cpp Metrics\"", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeDashboardModel = AddMetric(runtimeDashboard, \"Model status\", 0, 0)", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("GridSection(\"Loaded Model Sessions\"", StringComparison.Ordinal) < source.IndexOf("Text(\"Model Status\"", StringComparison.Ordinal));
        Assert.Contains("(\"Size\", \"C2\"", source, StringComparison.Ordinal);
        Assert.Contains("SessionStatusLabel", overviewViewModel, StringComparison.Ordinal);
        Assert.Contains("active.RuntimeName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("active.RuntimeName) ? \"Unknown runtime\" : $\"{active.RuntimeName}\\n{active.Endpoint}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("includeProgress: true", source, StringComparison.Ordinal);
        Assert.Contains("root.Children.Add(HorizontalGridSplitter(2))", source, StringComparison.Ordinal);
        Assert.Contains("BorderThickness = new Thickness(0)", source, StringComparison.Ordinal);
    }


    [Fact]
    public void ModelsGridUsesPerRowActionsOnly()
    {
        var source = ReadMainWindowSources();

        Assert.Contains("nameof(ModelGridRow.Name)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(ModelGridRow.Size)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(ModelGridRow.OpenFolderAction)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(ModelGridRow.CanDelete)", source, StringComparison.Ordinal);
        Assert.Contains("OpenModelFolderRow_Click", source, StringComparison.Ordinal);
        Assert.Contains("DeleteModelRow_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_deleteModelButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteSelectedModelAsync", source, StringComparison.Ordinal);
    }


    [Fact]
    public void HuggingFaceSearchKeepsDownloadActionVisibleAndSwitchesToHistory()
    {
        var source = ReadMainWindowSources();

        Assert.Contains("await ShowDownloadHistoryAsync(job.Id)", source, StringComparison.Ordinal);
        Assert.Contains("SelectDownloadHistoryJob", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[1].Width = new DataGridLength(1.85, DataGridLengthUnitType.Star)", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[5].Width = new DataGridLength(1.05, DataGridLengthUnitType.Star)", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[6].MinWidth = 96", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[6].Width = new DataGridLength(104)", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[7].Width = new DataGridLength(74)", source, StringComparison.Ordinal);
        Assert.Contains("AddButtonColumn(_hfGrid, \"Delete\", \"C10\", \"B4\", DeleteDownloadRow_Click", source, StringComparison.Ordinal);
        Assert.Contains("DeleteDownloadPartialFile", source, StringComparison.Ordinal);
        Assert.Contains("Completed model files are kept.", source, StringComparison.Ordinal);
        Assert.Contains("if (grid.Columns.Count < 10) return;", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowExposesAppUpdatesAndCacheClearing()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var themedMessageBox = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "ThemedMessageBox.cs"));

        Assert.Contains("x:Name=\"UpdatesNavButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HelpNavButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WindowsNavButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolsNavLabel\"", xaml, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("x:Name=\"AppStatusText\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"UpdatesNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"LogsNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"ToolsNavLabel\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"ToolsNavLabel\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"WindowsNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"WindowsNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"WslLinuxNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"LogsNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"UpdatesNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"UpdatesNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"HelpNavButton\"", StringComparison.Ordinal));
        Assert.Contains("CheckForAppUpdatesOnStartupAsync", source, StringComparison.Ordinal);
        Assert.Contains("InstallAppUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("SettingsPageRows", source, StringComparison.Ordinal);
        Assert.Contains("CacheMaintenanceService.Size(_settings.CacheRoot)", source, StringComparison.Ordinal);
        Assert.Contains("ClearCacheAsync", source, StringComparison.Ordinal);
        Assert.Contains("<RepositoryUrl>https://github.com/alekk89/llama.cpp-Console</RepositoryUrl>", project, StringComparison.Ordinal);

        var updatesStart = source.IndexOf("private void ShowUpdates()", StringComparison.Ordinal);
        Assert.True(updatesStart >= 0);
        Assert.True(
            source.IndexOf("actions.Children.Add(Button(_viewModel.Updates.ActionText", updatesStart, StringComparison.Ordinal)
            < source.IndexOf("FramedSection(\"Update Status\"", updatesStart, StringComparison.Ordinal));
        Assert.Contains("MaxHeight = DialogMaxHeight(owner)", themedMessageBox, StringComparison.Ordinal);
        Assert.Contains("DialogMessageMaxHeight", themedMessageBox, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", themedMessageBox, StringComparison.Ordinal);
    }


    [Fact]
    public void SettingsThemePreviewDoesNotRebuildSettingsPage()
    {
        var settings = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Settings.cs"));
        var handlerStart = settings.IndexOf("_themeCombo.SelectionChanged += (_, _) =>", StringComparison.Ordinal);
        var handlerEnd = settings.IndexOf("themeBar.Children.Add(_themeCombo);", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);
        var handler = settings[handlerStart..handlerEnd];
        Assert.Contains("ApplyTheme(mode);", handler, StringComparison.Ordinal);
        Assert.Contains("Theme preview applied. Save settings to keep it.", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowSettings()", handler, StringComparison.Ordinal);
    }


    [Fact]
    public void SettingsNumericEditsFailClosed()
    {
        var persistence = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.SettingsPersistence.cs"));
        var preferences = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "AppPreferenceService.cs"));

        Assert.Contains("TryIntValue", preferences, StringComparison.Ordinal);
        Assert.Contains("Port must be a whole number.", persistence, StringComparison.Ordinal);
        Assert.Contains("Auto unload idle min must be a whole number.", persistence, StringComparison.Ordinal);
        Assert.Contains("Max log file MB must be a whole number.", persistence, StringComparison.Ordinal);
        Assert.DoesNotContain("AppPreferenceService.IntValue", persistence, StringComparison.Ordinal);
        Assert.False(AppPreferenceService.TryIntValue("not-a-number", out _));
        Assert.True(AppPreferenceService.TryIntValue("42", out var value));
        Assert.Equal(42, value);
    }


    [Fact]
    public void HelpPageGuidesFirstRunWithoutRunningSetupInline()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();

        Assert.Contains("ShowHelp_Click", source, StringComparison.Ordinal);
        Assert.Contains("SetPage(\"Help\"", source, StringComparison.Ordinal);
        Assert.Contains("Step 1", source, StringComparison.Ordinal);
        Assert.Contains("Install an official runtime", source, StringComparison.Ordinal);
        Assert.Contains("official prebuilt llama.cpp runtime", source, StringComparison.Ordinal);
        Assert.Contains("Windows or WSL", source, StringComparison.Ordinal);
        Assert.Contains("CUDA, CPU, Vulkan, or Intel Arc SYCL", source, StringComparison.Ordinal);
        Assert.Contains("Open Runtimes", source, StringComparison.Ordinal);
        Assert.Contains("Scan Models Folder only if", source, StringComparison.Ordinal);
        Assert.Contains("Open OpenCode", source, StringComparison.Ordinal);
        Assert.Contains("NavigateFromHelp", source, StringComparison.Ordinal);
        Assert.Contains("ApplyPendingHelpFocus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Help: choose the highlighted CPU", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Help: in Build From Source (Advanced)", source, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("Grid.Row=\"2\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"HelpNavButton\"", StringComparison.Ordinal));
    }


    [Fact]
    public void MainWindowKeepsLogDeletionActionsAndReadableRuntimeJobRows()
    {
        var source = ReadMainWindowSources();
        var themedMessageBox = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "ThemedMessageBox.cs"));

        Assert.Contains("Delete Selected", source, StringComparison.Ordinal);
        Assert.Contains("Delete All Logs", source, StringComparison.Ordinal);
        Assert.Contains("DeleteLogRow_Click", source, StringComparison.Ordinal);
        Assert.Contains("DataGridSelectionMode.Extended", source, StringComparison.Ordinal);
        Assert.Contains("SelectedLogPaths", source, StringComparison.Ordinal);
        Assert.Contains("IsActiveRuntimeLog", source, StringComparison.Ordinal);
        Assert.Contains("SolidBrush(\"#F2F5F8\")", source, StringComparison.Ordinal);
        Assert.Contains("Use Log to inspect compiler, git, Windows, or WSL output.", source, StringComparison.Ordinal);
        Assert.Contains("OpenRuntimeJobLogRow_Click", source, StringComparison.Ordinal);
        Assert.Contains("AddButtonColumn(_runtimeJobsGrid, \"Log\"", source, StringComparison.Ordinal);
        Assert.Contains("private bool _showAdvancedRuntimes;", source, StringComparison.Ordinal);
        Assert.Contains("Button(_showAdvancedRuntimes ? \"Hide advanced\" : \"Show advanced\"", source, StringComparison.Ordinal);
        Assert.Contains("if (_showAdvancedRuntimes)", source, StringComparison.Ordinal);
        var showRuntimes = source.IndexOf("private void ShowRuntimes()", StringComparison.Ordinal);
        Assert.True(source.IndexOf("Button(_showAdvancedRuntimes ? \"Hide advanced\" : \"Show advanced\"", showRuntimes, StringComparison.Ordinal)
            < source.IndexOf("Build From Source (Advanced)", showRuntimes, StringComparison.Ordinal));
        Assert.DoesNotContain("Runtime Job Log Tail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadSelectedRuntimeJobLog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeJobLogBox", source, StringComparison.Ordinal);
        Assert.Contains("ClearRuntimeJobRow_Click", source, StringComparison.Ordinal);
        Assert.Contains("DeleteJobAsync(job.Id)", source, StringComparison.Ordinal);
        Assert.Contains("Update saved launch settings before deleting this runtime", source, StringComparison.Ordinal);
        Assert.Contains("nameof(RuntimeCatalogRow.DeleteToolTip)", source, StringComparison.Ordinal);
        Assert.Contains("ButtonToolTip", source, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticButtonToolTips", source, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ShowOnDisabledProperty", source, StringComparison.Ordinal);
        Assert.Contains("nameof(ModelGridRow.DeleteToolTip)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(RuntimeBuildPresetRow.DownloadToolTip)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(EditableSettingRow.ActionToolTip)", source, StringComparison.Ordinal);
        Assert.Contains("tooltipBinding: \"T1\"", source, StringComparison.Ordinal);
        Assert.Contains("DialogButtonToolTip", themedMessageBox, StringComparison.Ordinal);
        Assert.Contains("LogFileService.TryValidateWorkspaceLogFile(_workspaceRoot, job.LogPath", source, StringComparison.Ordinal);
        Assert.Contains("LogFileService.RedactSensitiveText(tail", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MinimizeBehaviorUsesExplicitTrayAndTaskbarModes()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var source = ReadMainWindowSources();

        Assert.Equal("taskbarOnly", settings.MinimizeBehavior);
        Assert.Equal(["Taskbar only", "Tray only", "Tray + taskbar"], AppPreferenceService.MinimizeBehaviorOptions());
        Assert.Equal("trayAndTaskbar", AppPreferenceService.MinimizeBehavior("Tray + taskbar"));
        Assert.Equal("lan", AppPreferenceService.ModelAccessMode("network access"));
        Assert.Equal("0.0.0.0", AppPreferenceService.RuntimeHostForAccessMode("lan"));
        Assert.True(AppPreferenceService.YesNoValue("on", fallback: false));
        Assert.True(AppPreferenceService.TryIntValue("42", out var parsed));
        Assert.Equal(42, parsed);
        Assert.False(AppPreferenceService.TryIntValue("bad", out _));
        Assert.Equal(10, AppPreferenceService.ClampedIntValue("99", fallback: 7, min: 1, max: 10));
        Assert.Contains("ShouldHideToTrayOnMinimize", source, StringComparison.Ordinal);
        Assert.Contains("ShouldShowTrayWithTaskbarOnMinimize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tray when running", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowConstrainsMaximizedWindowToWorkingArea()
    {
        var source = ReadMainWindowSources();

        Assert.Contains("ApplyWindowWorkAreaBounds", source, StringComparison.Ordinal);
        Assert.Contains("Forms.Screen.FromHandle", source, StringComparison.Ordinal);
        Assert.Contains("TransformFromDevice", source, StringComparison.Ordinal);
    }


    [Fact]
    public void OpenCodeLocalProviderCredentialsCanBeRefreshed()
    {
        var root = CreateTempRoot();
        var service = new OpenCodeConfigService(root);
        var configPath = Path.Combine(root, "opencode.jsonc");
        var model = new ModelRecord(
            "model",
            "Test Model",
            Path.Combine(root, "models", "test-model.gguf"),
            OwnershipKind.AppOwned,
            "{}",
            DateTimeOffset.UtcNow);
        var draft = service.CreateLocalModelDraft(configPath, model, "http://127.0.0.1:8081/v1", "old-key", 131_072, 32_768);
        service.SaveLocalModelSnippet(configPath, model, "http://127.0.0.1:8081/v1", "old-key", draft.Snippet, addAsNew: false);

        var updated = service.UpdateLocalProviderCredentials(configPath, "http://127.0.0.1:8090/v1", "new-key");
        var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!;
        var options = config["provider"]?[draft.ProviderId]?["options"];

        Assert.True(updated);
        Assert.Equal("http://127.0.0.1:8081/v1", options?["baseURL"]?.ToString());
        Assert.Equal("new-key", options?["apiKey"]?.ToString());
    }


    [Fact]
    public void OpenCodeLocalModelsUseSeparateProvidersForConcurrentEndpoints()
    {
        var root = CreateTempRoot();
        var service = new OpenCodeConfigService(root);
        var configPath = Path.Combine(root, "opencode.jsonc");
        var first = new ModelRecord("model-a", "First Model", Path.Combine(root, "models", "first-model.gguf"), OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);
        var second = new ModelRecord("model-b", "Second Model", Path.Combine(root, "models", "second-model.gguf"), OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);

        var firstDraft = service.CreateLocalModelDraft(configPath, first, "http://127.0.0.1:8081/v1", "key-a", 8192, 4096);
        var secondDraft = service.CreateLocalModelDraft(configPath, second, "http://127.0.0.1:8082/v1", "key-b", 8192, 4096);
        var firstId = service.SaveLocalModelSnippet(configPath, first, "http://127.0.0.1:8081/v1", "key-a", firstDraft.Snippet, addAsNew: false);
        var secondId = service.SaveLocalModelSnippet(configPath, second, "http://127.0.0.1:8082/v1", "key-b", secondDraft.Snippet, addAsNew: false);

        var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!;
        var providers = config["provider"]!;

        Assert.Equal(firstDraft.FullId, firstId);
        Assert.Equal(secondDraft.FullId, secondId);
        Assert.Equal("http://127.0.0.1:8081/v1", providers[firstDraft.ProviderId]?["options"]?["baseURL"]?.ToString());
        Assert.Equal("http://127.0.0.1:8082/v1", providers[secondDraft.ProviderId]?["options"]?["baseURL"]?.ToString());
        Assert.NotNull(providers[firstDraft.ProviderId]?["models"]?[firstDraft.ModelId]);
        Assert.NotNull(providers[secondDraft.ProviderId]?["models"]?[secondDraft.ModelId]);
    }


    [Fact]
    public void OpenCodeLocalModelsWithSameBasenameUseDistinctProviders()
    {
        var root = CreateTempRoot();
        var service = new OpenCodeConfigService(root);
        var configPath = Path.Combine(root, "opencode.jsonc");
        var first = new ModelRecord("model-a", "First Model", Path.Combine(root, "models", "first", "model.gguf"), OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);
        var second = new ModelRecord("model-b", "Second Model", Path.Combine(root, "models", "second", "model.gguf"), OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);

        var firstDraft = service.CreateLocalModelDraft(configPath, first, "http://127.0.0.1:8081/v1", "key-a", 8192, 4096);
        var secondDraft = service.CreateLocalModelDraft(configPath, second, "http://127.0.0.1:8082/v1", "key-b", 8192, 4096);
        service.SaveLocalModelSnippet(configPath, first, "http://127.0.0.1:8081/v1", "key-a", firstDraft.Snippet, addAsNew: false);
        service.SaveLocalModelSnippet(configPath, second, "http://127.0.0.1:8082/v1", "key-b", secondDraft.Snippet, addAsNew: false);

        var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!;
        var providers = config["provider"]!;

        Assert.NotEqual(firstDraft.ProviderId, secondDraft.ProviderId);
        Assert.NotEqual(firstDraft.ModelId, secondDraft.ModelId);
        Assert.StartsWith("local-llm-console-model-", firstDraft.ProviderId, StringComparison.Ordinal);
        Assert.StartsWith("local-llm-console-model-", secondDraft.ProviderId, StringComparison.Ordinal);
        Assert.Equal("http://127.0.0.1:8081/v1", providers[firstDraft.ProviderId]?["options"]?["baseURL"]?.ToString());
        Assert.Equal("http://127.0.0.1:8082/v1", providers[secondDraft.ProviderId]?["options"]?["baseURL"]?.ToString());
        Assert.NotNull(providers[firstDraft.ProviderId]?["models"]?[firstDraft.ModelId]);
        Assert.NotNull(providers[secondDraft.ProviderId]?["models"]?[secondDraft.ModelId]);
    }


    [Fact]
    public void OpenCodeLocalModelEditUpdateAndAddAsNewKeepPerModelEndpoint()
    {
        var root = CreateTempRoot();
        var service = new OpenCodeConfigService(root);
        var configPath = Path.Combine(root, "opencode.jsonc");
        var model = new ModelRecord("model", "Test Model", Path.Combine(root, "models", "test-model.gguf"), OwnershipKind.AppOwned, "{}", DateTimeOffset.UtcNow);
        var draft = service.CreateLocalModelDraft(configPath, model, "http://127.0.0.1:8084/v1", "key-a", 8192, 4096);
        var fullId = service.SaveLocalModelSnippet(configPath, model, "http://127.0.0.1:8084/v1", "key-a", draft.Snippet, addAsNew: false);
        var entry = new OpenCodeModelEntry(fullId, draft.ProviderId, draft.ModelId, "Test Model");
        var snippet = service.ReadModelSnippet(configPath, entry);
        var edited = System.Text.Json.Nodes.JsonNode.Parse(snippet)!.AsObject();
        edited["provider"]![draft.ProviderId]!["models"]![draft.ModelId]!["limit"]!["context"] = 16384;

        service.SaveModelSnippet(configPath, entry, edited.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var addedId = service.SaveLocalModelSnippet(configPath, model, "http://127.0.0.1:8084/v1", "key-a", draft.Snippet, addAsNew: true);

        var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!;
        var provider = config["provider"]![draft.ProviderId]!;

        Assert.Equal(draft.FullId, fullId);
        Assert.Equal($"{draft.ProviderId}/{draft.ModelId}-2", addedId);
        Assert.Equal("http://127.0.0.1:8084/v1", provider["options"]?["baseURL"]?.ToString());
        Assert.Equal("key-a", provider["options"]?["apiKey"]?.ToString());
        Assert.Equal("16384", provider["models"]?[draft.ModelId]?["limit"]?["context"]?.ToString());
        Assert.NotNull(provider["models"]?[$"{draft.ModelId}-2"]);
    }


    [Fact]
    public void PageViewModelsBuildStableRowsFromDomainState()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.UtcNow;
        var modelPath = Path.Combine(root, "models", "qwen-q4_k_m.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllBytes(modelPath, new byte[1536]);
        var model = new ModelRecord("model-1", "Qwen Test", modelPath, OwnershipKind.External, "{}", now);
        var modelsVm = new ModelsPageViewModel();

        modelsVm.ReplaceModels([model], active => active.Id == model.Id);

        Assert.Single(modelsVm.Rows);
        Assert.Equal("Qwen Test", modelsVm.Rows[0].Name);
        Assert.Equal("Q4_K_M", modelsVm.Rows[0].Quant);
        Assert.Equal("1.5 KB", modelsVm.Rows[0].Size);
        Assert.False(modelsVm.Rows[0].CanDelete);
        Assert.Equal(model.Id, modelsVm.Rows[0].Model.Id);

        var overviewVm = new OverviewPageViewModel();
        overviewVm.ReplaceModels(
        [
            new ModelRecord("model-b", "Beta Test", Path.Combine(root, "models", "beta.gguf"), OwnershipKind.External, "{}", now),
            model
        ]);

        Assert.Equal(2, overviewVm.ModelChoices.Count);
        Assert.Equal("Beta Test", overviewVm.ModelChoices[0].Name);
        Assert.Equal("Qwen Test", overviewVm.ModelChoices[1].Name);
        overviewVm.ReplaceSessions(
        [
            new LoadedModelSessionSnapshot(
                "session-a",
                "model-a",
                "Alpha",
                "runtime-a",
                "CUDA Windows",
                RuntimeMode.Native,
                RuntimeBackend.Cuda,
                AppSettings.CreateDefault(root) with { Port = 8081 },
                Path.Combine(root, "a.log"),
                now,
                "",
                11,
                LoadedModelSessionStatus.Warm,
                true,
                false,
                4096),
            new LoadedModelSessionSnapshot(
                "session-b",
                "model-b",
                "Beta",
                "runtime-b",
                "CUDA WSL",
                RuntimeMode.Wsl,
                RuntimeBackend.Cuda,
                AppSettings.CreateDefault(root) with { Port = 8082 },
                Path.Combine(root, "b.log"),
                now,
                "marker",
                0,
                LoadedModelSessionStatus.Running,
                true,
                true,
                8192)
        ]);

        Assert.Equal(2, overviewVm.SessionRows.Count);
        Assert.Contains("selected", overviewVm.SessionRows[0].C1, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("8 KB", overviewVm.SessionRows[0].C2);
        Assert.Equal("Loaded", overviewVm.SessionRows[0].C3);
        Assert.Equal("http://127.0.0.1:8082/v1", overviewVm.SessionRows[0].C4);
        Assert.Equal("Loaded", overviewVm.SessionRows[1].C3);

        var preset = new RuntimeBuildPreset("official-cuda", "Official CUDA", "https://example.com/llama.cpp.git", "main", true);
        var builtRuntime = new RuntimeRecord(
            "runtime-1",
            "llama.cpp CUDA",
            RuntimeMode.Wsl,
            RuntimeBackend.Cuda,
            Path.Combine(root, "runtimes", "official-cuda", "bin", "llama-server"),
            """{"managedPresetId":"official-cuda","commit":"abcdef1234567890","folder":"D:\\runtime"}""",
            now);
        var pendingSource = new RuntimeSourceEntry("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "main", false, Path.Combine(root, "source"), "fedcba9876543210", now);
        var launchSettingsVm = new LaunchSettingsViewModel();

        launchSettingsVm.ReplaceRuntimeChoices([builtRuntime]);

        Assert.Single(launchSettingsVm.RuntimeChoices);
        Assert.Equal("runtime-1", launchSettingsVm.RuntimeChoices[0].Id);
        Assert.Contains("llama.cpp CUDA", launchSettingsVm.RuntimeChoices[0].Label, StringComparison.Ordinal);
        Assert.Equal(RuntimeBackend.Cuda, launchSettingsVm.RuntimeChoices[0].Backend);

        var runtimesVm = new RuntimesPageViewModel();

        runtimesVm.ReplaceRuntimes([builtRuntime], [pendingSource], new Dictionary<string, List<string>> { [builtRuntime.Id] = ["Qwen Test"] }, runtime => runtime.Id == builtRuntime.Id);

        Assert.Equal(2, runtimesVm.Rows.Count);
        Assert.Equal("llama.cpp CUDA", runtimesVm.Rows[0].Name);
        Assert.Contains("Qwen Test", runtimesVm.Rows[0].Details, StringComparison.Ordinal);
        Assert.False(runtimesVm.Rows[0].CanDelete);
        Assert.Contains("Qwen Test", runtimesVm.Rows[0].DeleteToolTip, StringComparison.Ordinal);
        Assert.Contains("Unload the running model", runtimesVm.Rows[0].DeleteToolTip, StringComparison.Ordinal);
        Assert.Equal("Downloaded", runtimesVm.Rows[1].State);
        Assert.Equal("Build", runtimesVm.Rows[1].BuildAction);
        Assert.Equal(RuntimeCatalogRowKind.Source, runtimesVm.Rows[1].Kind);
        runtimesVm.ReplaceRuntimes([builtRuntime], [], new Dictionary<string, List<string>> { [builtRuntime.Id] = ["Qwen Test"] }, _ => false);
        Assert.False(runtimesVm.Rows[0].CanDelete);
        Assert.Contains("Update saved launch settings", runtimesVm.Rows[0].DeleteToolTip, StringComparison.Ordinal);
        Assert.Contains("Qwen Test", runtimesVm.Rows[0].DeleteToolTip, StringComparison.Ordinal);
        runtimesVm.ReplaceRuntimes([builtRuntime], [], new Dictionary<string, List<string>>(), _ => false);
        Assert.True(runtimesVm.Rows[0].CanDelete);
        Assert.Contains("Delete this runtime", runtimesVm.Rows[0].DeleteToolTip, StringComparison.Ordinal);

        var buildsVm = new RuntimeBuildsPageViewModel();
        var downloaded = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source-cuda"), "abcdef1234567890", now);
        var updateState = new RuntimeUpdateState(true, "abcdef1234567890", "abcdef9999999999", now);

        buildsVm.ReplacePresets([preset], [], [downloaded], new Dictionary<string, RuntimeUpdateState> { [preset.Id] = updateState });

        Assert.Equal(2, buildsVm.Rows.Count);
        Assert.Equal("Official CUDA", buildsVm.Rows[0].Label);
        Assert.Equal("CUDA WSL", buildsVm.Rows[0].Backend);
        Assert.Contains("update available", buildsVm.Rows[0].LatestLocal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Download", buildsVm.Rows[0].DownloadAction);
        Assert.True(buildsVm.Rows[1].IsCustomAdd);

        var packagesVm = new RuntimePackagesPageViewModel();
        var packagePreset = RuntimePackageCatalogService.PresetRows().First();
        var packageRuntime = builtRuntime with
        {
            Id = "package-runtime",
            Name = "Official llama.cpp CUDA Windows",
            Mode = RuntimeMode.Native,
            ExecutablePath = Path.Combine(root, "runtimes", "official-prebuilt-windows-cuda-b9354", "llama-server.exe"),
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = Path.Combine(root, "runtimes", "official-prebuilt-windows-cuda-b9354"),
                runtimeMetadata = new
                {
                    managedPackageId = packagePreset.Id,
                    managedPresetId = packagePreset.Id,
                    releaseTag = "b9354"
                }
            })
        };
        var packageUpdate = new RuntimePackageUpdateState(true, "b9354", "b9355", "https://example.com/release", "llama-b9355-bin-win-cuda-12.4-x64.zip, cudart-llama-bin-win-cuda-12.4-x64.zip", now);

        packagesVm.ReplacePresets([packagePreset], [packageRuntime], new Dictionary<string, RuntimePackageUpdateState> { [packagePreset.Id] = packageUpdate });

        Assert.Single(packagesVm.Rows);
        Assert.Equal("Official llama.cpp CUDA Windows", packagesVm.Rows[0].Label);
        Assert.Equal("CUDA Windows", packagesVm.Rows[0].Backend);
        Assert.Contains("update available", packagesVm.Rows[0].LatestRelease, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cudart", packagesVm.Rows[0].Assets, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Update", packagesVm.Rows[0].InstallAction);
        Assert.True(packagesVm.Rows[0].CanInstall);

        var lifetimeVm = new LifetimeMetricsViewModel();
        lifetimeVm.ReplaceRows([new TokenUsageRecord(model.Id, model.Name, 10, 15, now)]);

        Assert.Equal(2, lifetimeVm.Rows.Count);
        Assert.Equal("25", lifetimeVm.Rows[0].C4);
        Assert.Equal("Qwen Test", lifetimeVm.Rows[1].C1);
        Assert.Equal(model.Id, lifetimeVm.Rows[1].Data["ModelId"]?.ToString());

        var wslVm = new WslLinuxPageViewModel();
        var report = new WslEnvironmentReport(
            true,
            true,
            "WSL ready",
            "",
            "Debian",
            "Ubuntu-24.04",
            "Install Ubuntu.",
            [
                new WslDistroInfo("Debian", "Running", "2", true, false),
                new WslDistroInfo("Ubuntu-24.04", "Stopped", "2", false, true)
            ]);

        wslVm.ReplaceDistroRows(report, "Ubuntu-24.04");

        Assert.Equal("Ubuntu-24.04", wslVm.Rows[0].C2);
        Assert.Equal("Selected", wslVm.Rows[0].C6);
        Assert.False(wslVm.Rows[0].B1);
        Assert.Equal("Debian", wslVm.Rows[1].C2);

        var windowsVm = new WindowsPageViewModel();
        var windowsTools = new WindowsToolSnapshot(
            true,
            @"C:\Git\git.exe",
            true,
            @"C:\CMake\cmake.exe",
            true,
            "Visual Studio C++ tools",
            false,
            "",
            false,
            "nvcc.exe missing",
            true,
            "VULKAN_SDK: C:\\Vulkan",
            true,
            "Intel oneAPI ready",
            true);
        windowsVm.ReplaceToolRows(windowsTools);

        Assert.Equal(4, windowsVm.Rows.Count);
        Assert.Equal("CPU tools", windowsVm.Rows[0].C1);
        Assert.Equal("Ready", windowsVm.Rows[0].C2);
        Assert.Equal("CUDA tools", windowsVm.Rows[1].C1);
        Assert.Equal("Incomplete", windowsVm.Rows[1].C2);
        Assert.Equal("Vulkan tools", windowsVm.Rows[2].C1);
        Assert.Equal("Intel oneAPI", windowsVm.Rows[3].C1);
        Assert.Equal("Ready", windowsVm.Rows[3].C2);
        Assert.Equal("Intel GPU visible to sycl-ls", windowsVm.Rows[3].C4);
        Assert.Equal("Windows GPU build tools ready", WindowsEnvironmentService.Status(windowsTools));

        var hfFile = new HuggingFaceFile("owner/repo", "model-q4.gguf", "model-q4.gguf", "Q4_K_M", 1536, 1234)
        {
            HasVisionProjector = true,
            HasConfig = true,
            HasTokenizer = true,
            CapabilityHints = "vision,reasoning,moe",
            License = "apache-2.0"
        };
        var hfVm = new HuggingFacePageViewModel();
        hfVm.ReplaceSearchResults([hfFile], HuggingFaceInstallStateService.BuildInventory([]), Path.Combine(root, "models"));

        Assert.Single(hfVm.SearchRows);
        Assert.Equal("owner/repo", hfVm.SearchRows[0].C1);
        Assert.Equal("1.5 KB", hfVm.SearchRows[0].C4);
        Assert.Contains("Vision + mmproj", hfVm.SearchRows[0].C6, StringComparison.Ordinal);
        Assert.Contains("MoE", hfVm.SearchRows[0].C6, StringComparison.Ordinal);
        Assert.Contains("Config", hfVm.SearchRows[0].C6, StringComparison.Ordinal);
        Assert.Equal("Download", hfVm.SearchRows[0].C7);
        Assert.Equal("Card", hfVm.SearchRows[0].C8);
        Assert.Contains("Download this GGUF model", hfVm.SearchRows[0].T1, StringComparison.Ordinal);
        Assert.Contains("Hugging Face model card", hfVm.SearchRows[0].T2, StringComparison.Ordinal);
        Assert.True(hfVm.SearchRows[0].B1);
        Assert.True(hfVm.SearchRows[0].B2);

        var job = new JobRecord(
            "job-1",
            "huggingface-download",
            JobStatus.Running,
            System.Text.Json.JsonSerializer.Serialize(new DownloadJobPayload(hfFile, Path.Combine(root, "models", hfFile.Name), 512, 1024)),
            Path.Combine(root, "logs", "job.log"),
            now,
            now);
        hfVm.ReplaceDownloadHistory([job]);

        Assert.Single(hfVm.DownloadHistoryRows);
        Assert.Equal("Running", hfVm.DownloadHistoryRows[0].C1);
        Assert.Equal("50% (512 B)", hfVm.DownloadHistoryRows[0].C3);
        Assert.Contains("Pause this active model download", hfVm.DownloadHistoryRows[0].T2, StringComparison.Ordinal);
        Assert.Contains("Delete this download history entry", hfVm.DownloadHistoryRows[0].T4, StringComparison.Ordinal);
        Assert.True(hfVm.DownloadHistoryRows[0].B2);
        Assert.Equal("Delete", hfVm.DownloadHistoryRows[0].C10);
        Assert.True(hfVm.DownloadHistoryRows[0].B4);

        var jobsVm = new JobsViewModel();
        var runtimeJob = job with { Id = "runtime-job", Kind = "runtime-build", Status = JobStatus.Completed, PayloadJson = """{"message":"built"}""" };
        jobsVm.ReplaceJobs([job, runtimeJob]);

        Assert.Equal(2, jobsVm.Rows.Count);
        Assert.Single(jobsVm.RuntimeRows);
        Assert.Equal("Completed", jobsVm.RuntimeRows[0].C1);
        Assert.Equal("built", jobsVm.RuntimeRows[0].C5);
        Assert.Equal("Cancel", jobsVm.RuntimeRows[0].C7);
        Assert.Equal("Retry", jobsVm.RuntimeRows[0].C8);
        Assert.False(jobsVm.RuntimeRows[0].B2);
        Assert.False(jobsVm.RuntimeRows[0].B3);
        Assert.True(jobsVm.RuntimeRows[0].B4);
        Assert.Contains("Remove this finished runtime job", jobsVm.RuntimeRows[0].T4, StringComparison.Ordinal);

        var runtimeDownloadJob = runtimeJob with { Id = "runtime-download-job", Kind = "runtime-source-download" };
        jobsVm.ReplaceJobs([runtimeDownloadJob]);
        Assert.Equal("runtime-source-download", jobsVm.RuntimeRows[0].C2);
        Assert.True(jobsVm.RuntimeRows[0].B4);

        var logsVm = new LogsViewModel();
        var logRoot = Path.Combine(root, "logs");
        Directory.CreateDirectory(logRoot);
        var runtimeLog = Path.Combine(logRoot, "llama-server-test.log");
        var jobLog = Path.Combine(logRoot, "runtime-build-test.log");
        File.WriteAllText(runtimeLog, "runtime");
        File.WriteAllText(jobLog, "job");
        var jobsByPath = new Dictionary<string, JobRecord>(StringComparer.OrdinalIgnoreCase)
        {
            [LogFileService.NormalizePath(jobLog)] = runtimeJob with { LogPath = jobLog }
        };

        logsVm.ReplaceLogs(
            [new FileInfo(runtimeLog), new FileInfo(jobLog)],
            jobsByPath,
            runtimeLog,
            "Qwen Test");

        Assert.Equal(2, logsVm.Rows.Count);
        Assert.Contains(logsVm.Rows, row => row.C1 == "Model runtime" && row.C3 == "Current model: Qwen Test");
        Assert.Contains(logsVm.Rows, row => row.C1 == "Runtime build" && row.C3.Contains("Completed", StringComparison.Ordinal));

        var runtimeMetricsVm = new RuntimeMetricsViewModel();
        runtimeMetricsVm.ReplaceSamples(
        [
            new PrometheusSample("z_metric", "", 1.25, "", "gauge", "last"),
            new PrometheusSample("a_metric", "slot=1", 9, "raw", "counter", "first")
        ]);

        Assert.Equal("a_metric", runtimeMetricsVm.Rows[0].C1);
        Assert.Equal("raw", runtimeMetricsVm.Rows[0].C3);
        Assert.Equal("z_metric", runtimeMetricsVm.Rows[1].C1);
        Assert.Equal("1.25", runtimeMetricsVm.Rows[1].C3);

        var settingsVm = new SettingsPageViewModel();
        settingsVm.ReplaceRows(
        [
            new SettingRowDefinition("Network", "Model access", "modelAccessMode", "Local only", "choice", ["Local only", "LAN access"]),
            new SettingRowDefinition("Network", "API key", "modelApiKey", "", "secret", Action: "Generate"),
            new SettingRowDefinition("Logs", "Max log file MB", "maxLogFileSizeMb", "32")
        ]);
        var accessRow = settingsVm.Rows.Single(row => row.Key == "modelAccessMode");
        var apiKeyRow = settingsVm.Rows.Single(row => row.Key == "modelApiKey");

        Assert.Equal(3, settingsVm.Rows.Count);
        Assert.Equal("secret", apiKeyRow.Type);
        Assert.True(apiKeyRow.Value.Length >= 32);
        Assert.DoesNotContain(apiKeyRow.Value, apiKeyRow.DisplayValue, StringComparison.Ordinal);
        Assert.True(apiKeyRow.CanAction);
        apiKeyRow.Value = "";
        accessRow.Value = "LAN access";
        Assert.True(apiKeyRow.Value.Length >= 32);

        var openCodeVm = new OpenCodePageViewModel();
        openCodeVm.ReplaceLocalModels([model]);
        openCodeVm.ReplaceModels([new OpenCodeModelEntry("local/qwen", "local", "qwen", "Qwen")]);
        openCodeVm.ReplaceAgents([new OpenCodeAgentEntry("config:build", "build", OpenCodeAgentKind.Config, "opencode.jsonc", "build (config)")]);

        Assert.Single(openCodeVm.LocalModelChoices);
        Assert.Equal(2, openCodeVm.ModelChoices.Count);
        Assert.False(openCodeVm.ModelChoices[0].IsAddNew);
        Assert.True(openCodeVm.ModelChoices[^1].IsAddNew);
        Assert.Equal(2, openCodeVm.AgentChoices.Count);
        Assert.True(openCodeVm.AgentChoices[^1].IsAddNew);

        var updatesVm = new UpdatesPageViewModel();

        Assert.Equal("Check For Updates", updatesVm.ActionText);
        Assert.Contains("No update check", updatesVm.StatusDetails, StringComparison.Ordinal);

        updatesVm.SetLatestUpdate(new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.0",
            "Release v1.1.0",
            new string('x', 1900),
            "https://example.com/release",
            "LlamaCppConsole.exe",
            "https://example.com/download",
            123));

        Assert.True(updatesVm.HasAvailableUpdate);
        Assert.Equal("Install Update", updatesVm.NavigationText);
        Assert.Contains("v1.0 -> v1.1.0", updatesVm.StatusText, StringComparison.Ordinal);
        Assert.Contains("Release v1.1.0", updatesVm.LatestReleaseText, StringComparison.Ordinal);
        Assert.True(updatesVm.LatestReleaseText.Length < 1900);
    }


    [Fact]
    public void MainWindowViewModelTracksPageStatusAndBusyState()
    {
        var vm = new MainWindowViewModel();

        Assert.Equal("Overview", vm.CurrentPage);
        Assert.Equal("Starting...", vm.StatusText);
        Assert.True(vm.TryBeginBusy(out var busyMessage));
        Assert.Equal("", busyMessage);
        Assert.False(vm.TryBeginBusy(out busyMessage));
        Assert.Equal("Please wait: Starting...", busyMessage);
        Assert.True(vm.EndBusy());
        Assert.False(vm.EndBusy());

        vm.CurrentPage = "Models";
        vm.SetStatus("");

        Assert.Equal("Models", vm.CurrentPage);
        Assert.Equal("Ready", vm.DisplayStatusText);
    }

}
