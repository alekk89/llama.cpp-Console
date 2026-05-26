using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void HostExecutableResolverDoesNotReturnUnresolvedPathSearchNames()
    {
        Assert.Throws<FileNotFoundException>(() => HostExecutableResolver.ResolveOnPath($"definitely-missing-{Guid.NewGuid():N}.exe"));
    }


    [Fact]
    public void HostExecutableResolverIgnoresRelativePathEntries()
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalDirectory = Environment.CurrentDirectory;
        var root = CreateTempRoot();
        var relativeDirectory = "relative-tools";
        Directory.CreateDirectory(Path.Combine(root, relativeDirectory));
        File.WriteAllText(Path.Combine(root, relativeDirectory, "fake-tool.exe"), "");
        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("PATH", relativeDirectory);

            Assert.Throws<FileNotFoundException>(() => HostExecutableResolver.ResolveOnPath("fake-tool.exe"));
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }


    [Fact]
    public void EmbeddedBuildScriptUsesStdinAndChecksCudaRuntimeLibrary()
    {
        var script = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "tools", "Build-LlamaCppRuntime.ps1"));

        Assert.Contains(".local-llm-build.sh", script, StringComparison.Ordinal);
        Assert.Contains("UTF8Encoding $false", script, StringComparison.Ordinal);
        Assert.Contains("bash <build>", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$Script |", script, StringComparison.Ordinal);
        Assert.DoesNotContain("bash -lc $Script", script, StringComparison.Ordinal);
        Assert.Contains("libcudart", script, StringComparison.Ordinal);
        Assert.Contains("[switch] $Vulkan", script, StringComparison.Ordinal);
        Assert.Contains("Vulkan build dependencies", script, StringComparison.Ordinal);
        Assert.Contains("vulkaninfo --summary", script, StringComparison.Ordinal);
        Assert.Contains("Vulkan_GLSLC_EXECUTABLE", script, StringComparison.Ordinal);
        Assert.Contains("-DGGML_VULKAN=ON", script, StringComparison.Ordinal);
        Assert.Contains("server_path=$InstallQ/bin/llama-server", script, StringComparison.Ordinal);
        Assert.Contains("--version >/dev/null 2>&1", script, StringComparison.Ordinal);
        Assert.Contains("probe_ld_path", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-WslDistroName", script, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_CONSOLE_BUILD_MARKER", script, StringComparison.Ordinal);
        Assert.Contains("LOCAL_LLM_CONSOLE_BUILD_MARKER", script, StringComparison.Ordinal);
        Assert.DoesNotContain("exit \"`$build_status", script, StringComparison.Ordinal);
    }


    [Fact]
    public void CommandLineServiceQuotesPowerShellBashAndWslCleanupMarkers()
    {
        Assert.Equal("'a''b'", CommandLineService.PowerShellQuote("a'b"));
        Assert.Equal("'a'\"'\"'b'", CommandLineService.BashQuote("a'b"));
        Assert.Equal("second", CommandLineService.FirstNonBlankLine("\r\n  second  \nthird"));

        var cleanup = CommandLineService.WslKillByEnvironmentMarkerCommand("marker'1");
        var ubuntuInstall = WslSetupCommands.InstallUbuntuAndBuildToolsPowerShell("C:\\Windows\\System32\\wsl.exe");
        var deleteWsl = WslSetupCommands.DeleteWslPowerShell("C:\\Windows\\System32\\wsl.exe");
        var deleteUbuntu = WslSetupCommands.DeleteUbuntuPowerShell("C:\\Windows\\System32\\wsl.exe", "Ubuntu-24.04");

        Assert.Contains("LLAMA_CPP_CONSOLE_BUILD_MARKER=marker'\"'\"'1", cleanup, StringComparison.Ordinal);
        Assert.Contains("LOCAL_LLM_CONSOLE_BUILD_MARKER=marker'\"'\"'1", cleanup, StringComparison.Ordinal);
        Assert.Contains("/proc/[0-9]*/environ", cleanup, StringComparison.Ordinal);
        Assert.Contains("kill \"$pid\"", cleanup, StringComparison.Ordinal);
        Assert.Contains("--install -d 'Ubuntu-24.04'", ubuntuInstall, StringComparison.Ordinal);
        Assert.Contains("Installing llama.cpp CPU build tools", ubuntuInstall, StringComparison.Ordinal);
        Assert.Contains("'C:\\Windows\\System32\\wsl.exe' -d 'Ubuntu-24.04' -- bash -s", ubuntuInstall, StringComparison.Ordinal);
        Assert.Contains("DELETE WSL", deleteWsl, StringComparison.Ordinal);
        Assert.Contains("--unregister 'Ubuntu-24.04'", deleteUbuntu, StringComparison.Ordinal);
    }


    [Fact]
    public void UbuntuInstallerIncludesCmakeBuildTools()
    {
        var source = ReadMainWindowSources();

        Assert.Contains("cmake", WslSetupCommands.BuildToolsPackages, StringComparison.Ordinal);
        Assert.Contains("build-essential", WslSetupCommands.BuildToolsPackages, StringComparison.Ordinal);
        Assert.Contains("libcurl4-openssl-dev", WslSetupCommands.InstallBuildToolsCommand, StringComparison.Ordinal);
        Assert.Contains("Install CPU Tools", source, StringComparison.Ordinal);
        Assert.Contains("CPU build tools do not include CUDA", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "tools", "Build-LlamaCppRuntime.ps1")), StringComparison.Ordinal);
        Assert.Contains("Install CUDA", source, StringComparison.Ordinal);
        Assert.Contains("Install Vulkan", source, StringComparison.Ordinal);
        Assert.Contains("Delete WSL", source, StringComparison.Ordinal);
        Assert.Contains("Delete Ubuntu", source, StringComparison.Ordinal);
        Assert.Equal("Update CPU Tools", WslEnvironmentService.CpuToolsActionLabel(new WslToolSnapshot(true, false, false, "CPU OK", "CUDA missing", "Vulkan missing")));
        Assert.Equal("Update CUDA", WslEnvironmentService.CudaToolsActionLabel(new WslToolSnapshot(false, true, false, "CPU missing", "CUDA OK", "Vulkan missing")));
        Assert.Equal("Update Vulkan", WslEnvironmentService.VulkanToolsActionLabel(new WslToolSnapshot(false, false, true, "CPU missing", "CUDA missing", "Vulkan OK")));
        Assert.Equal("cuda-toolkit-13-2", WslSetupCommands.CudaToolkitPackage);
        Assert.Contains("cuda-keyring_1.1-1_all.deb", WslSetupCommands.InstallCudaToolkitCommand, StringComparison.Ordinal);
        Assert.Contains("/usr/local/cuda*/bin/nvcc", WslSetupCommands.InstallCudaToolkitCommand, StringComparison.Ordinal);
        Assert.Contains("libvulkan-dev", WslSetupCommands.VulkanToolsPackages, StringComparison.Ordinal);
        Assert.Contains("glslc", WslSetupCommands.InstallVulkanToolsCommand, StringComparison.Ordinal);
        Assert.Contains("vulkaninfo --summary", WslSetupCommands.InstallVulkanToolsCommand, StringComparison.Ordinal);
        Assert.Contains("ToolProbeCommand", source, StringComparison.Ordinal);
        Assert.Contains("libcudart", WslSetupCommands.ToolProbeCommand, StringComparison.Ordinal);
        Assert.Contains("VULKAN_SUMMARY", WslSetupCommands.ToolProbeCommand, StringComparison.Ordinal);
        Assert.Contains("CPU build tools do not include CUDA", WslSetupCommands.CudaToolkitPreflightCommand, StringComparison.Ordinal);
        Assert.Contains("libcudart", WslSetupCommands.CudaToolkitPreflightCommand, StringComparison.Ordinal);
        Assert.Contains("Vulkan build dependencies", WslSetupCommands.VulkanToolsPreflightCommand, StringComparison.Ordinal);
        Assert.Contains("vulkaninfo", WslSetupCommands.VulkanToolsPreflightCommand, StringComparison.Ordinal);
        Assert.Contains("CUDAToolkit_ROOT", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "tools", "Build-LlamaCppRuntime.ps1")), StringComparison.Ordinal);
        Assert.Contains("CMAKE_CUDA_COMPILER", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "tools", "Build-LlamaCppRuntime.ps1")), StringComparison.Ordinal);
        Assert.Contains("vulkan_cmake_args", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "tools", "Build-LlamaCppRuntime.ps1")), StringComparison.Ordinal);
    }


    [Fact]
    public void WslDistroParserHandlesDefaultUbuntu()
    {
        const string raw = """
          NAME              STATE           VERSION
        * Ubuntu-24.04      Stopped         2
          docker-desktop    Running         2
        """;

        var distros = WslEnvironmentService.ParseDistroList(raw);

        Assert.Single(distros);
        Assert.Equal("Ubuntu-24.04", distros[0].Name);
        Assert.True(distros[0].IsDefault);
        Assert.True(distros[0].IsUbuntu);
    }


    [Fact]
    public void WslEnvironmentServiceSummarizesSelectedDistrosAndProbeOutput()
    {
        var report = new WslEnvironmentReport(
            WslExeFound: true,
            WslWorking: true,
            Status: "WSL ready",
            Details: "",
            DefaultDistro: "Debian",
            RecommendedDistro: "Ubuntu-24.04",
            RecommendedAction: "",
            Distros:
            [
                new WslDistroInfo("Debian", "Running", "2", true, false),
                new WslDistroInfo("Ubuntu-24.04", "Stopped", "2", false, true)
            ]);

        var values = WslEnvironmentService.ParseKeyValueLines("CPU=1\nCPU_SUMMARY=CPU OK, CMake 3.28\nbad-line\nCUDA=0");
        var tools = WslEnvironmentService.ParseToolProbeOutput("CPU=1\nCPU_SUMMARY=CPU OK, CMake 3.28\nCUDA=0\nCUDA_SUMMARY=CUDA missing nvcc\nVULKAN=1\nVULKAN_SUMMARY=Vulkan OK, Microsoft Direct3D12");
        var unknownTools = WslEnvironmentService.UnknownToolSnapshot();

        Assert.Equal("Ubuntu-24.04", WslEnvironmentService.SelectedUbuntuDistroName(report, "missing"));
        Assert.Equal("Ubuntu-24.04", WslEnvironmentService.SelectedUbuntuDistroName(report, "Ubuntu-24.04"));
        Assert.Equal("Ubuntu-24.04 | WSL 2 | Stopped", WslEnvironmentService.SelectedDistroSummary(report, "Ubuntu-24.04"));
        Assert.Equal("missing (missing)", WslEnvironmentService.SelectedDistroSummary(report, "missing"));
        Assert.Equal("2 distro(s), 1 Ubuntu", WslEnvironmentService.InstalledDistroSummary(report));
        Assert.Equal("1", values["cpu"]);
        Assert.Equal("CPU OK, CMake 3.28", values["CPU_SUMMARY"]);
        Assert.False(values.ContainsKey("bad-line"));
        Assert.True(tools.CpuToolsInstalled);
        Assert.False(tools.CudaToolsInstalled);
        Assert.True(tools.VulkanToolsInstalled);
        Assert.Equal("CPU OK, CMake 3.28 | CUDA missing nvcc | Vulkan OK, Microsoft Direct3D12", WslEnvironmentService.ToolSummary(tools));
        Assert.Equal("Update CPU Tools", WslEnvironmentService.CpuToolsActionLabel(tools));
        Assert.Equal("Install CUDA", WslEnvironmentService.CudaToolsActionLabel(tools));
        Assert.Equal("Update Vulkan", WslEnvironmentService.VulkanToolsActionLabel(tools));
        Assert.Equal("CPU tools unknown | CUDA unknown | Vulkan unknown", WslEnvironmentService.ToolSummary(unknownTools));
        Assert.Contains("Ubuntu-24.04", WslEnvironmentService.CudaToolkitIncompleteMessage("Ubuntu-24.04", "CUDA missing nvcc"), StringComparison.Ordinal);
        Assert.Contains("CUDA missing nvcc", WslEnvironmentService.CudaToolkitIncompleteMessage("Ubuntu-24.04", "CUDA missing nvcc"), StringComparison.Ordinal);
        Assert.Contains("Ubuntu-24.04", WslEnvironmentService.VulkanToolsIncompleteMessage("Ubuntu-24.04", "Vulkan missing glslc"), StringComparison.Ordinal);
        Assert.Contains("Vulkan missing glslc", WslEnvironmentService.VulkanToolsIncompleteMessage("Ubuntu-24.04", "Vulkan missing glslc"), StringComparison.Ordinal);
        Assert.Contains("libcudart", WslSetupCommands.ToolProbeCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void WslDetectionTreatsRemovedWslAsInstallableAndStaysBounded()
    {
        const string removedWsl = "The Windows Subsystem for Linux is not installed. You can install by running 'wsl.exe --install'.";
        const string noDistros = "Windows Subsystem for Linux has no installed distributions.";
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "WslEnvironmentService.cs"));
        var mainWindow = ReadMainWindowSources();

        Assert.True(WslEnvironmentService.LooksLikeWslNotInstalled(removedWsl));
        Assert.False(WslEnvironmentService.LooksLikeWslNotInstalled(noDistros));
        Assert.Contains("Status: \"WSL not installed\"", source, StringComparison.Ordinal);
        Assert.Contains("WslExeFound: false", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(4)", source, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll(statusTask, listTask)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromSeconds(15));", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await AutoSelectDetectedWslDistroAsync();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RunBackground(AutoSelectDetectedWslDistroAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => _wslEnvironment.DetectAsync())", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_wslLinuxAutoRefreshDone", mainWindow, StringComparison.Ordinal);
        Assert.Contains("if (!_wslLinuxAutoRefreshDone)", mainWindow, StringComparison.Ordinal);
    }

}
