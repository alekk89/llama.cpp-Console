using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimeSourceCleanupDefaultsOn()
    {
        var root = CreateTempRoot();

        var settings = AppSettings.CreateDefault(root);

        Assert.True(settings.DeleteRuntimeSourceAfterSuccessfulBuild);
    }


    [Fact]
    public void LlamaProcessSupervisorUsesCentralLogRedaction()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "LlamaProcessSupervisor.cs"));

        Assert.Contains("LogFileService.RedactSensitiveText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization\\s*:\\s*Bearer", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LlamaProcessSupervisorAttachLoadAndStopTransitionsAreExplicit()
    {
        using var supervisor = new LlamaProcessSupervisor();
        var root = CreateTempRoot();
        var runtime = new RuntimeRecord(
            "runtime-1",
            "Native CPU",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(root, "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        var settings = AppSettings.CreateDefault(root);

        supervisor.AttachExisting(runtime, "model-1", settings, Path.Combine(root, "runtime.log"), LlamaRuntimeState.Failed);

        Assert.True(supervisor.IsRunning);
        Assert.Equal("model-1", supervisor.ActiveModelId);
        Assert.Equal("runtime-1", supervisor.ActiveRuntimeId);
        Assert.Equal(LlamaRuntimeState.Loading, supervisor.State);
        Assert.True(supervisor.MarkLoadedIfRunning());
        Assert.Equal(LlamaRuntimeState.Loaded, supervisor.State);

        supervisor.Stop();

        Assert.False(supervisor.IsRunning);
        Assert.Equal("", supervisor.ActiveModelId);
        Assert.Equal("", supervisor.ActiveRuntimeId);
        Assert.Equal(LlamaRuntimeState.Stopped, supervisor.State);
        Assert.Null(supervisor.LastExitCode);
    }


    [Fact]
    public void RuntimeAdapterRejectsNetworkHostWithoutLanMode()
    {
        var request = ValidLaunchRequest() with { Host = "0.0.0.0" };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("localhost", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterAllowsNetworkHostWithExplicitLanModeAndApiKey()
    {
        var apiKey = new string('a', 32);
        var request = ValidLaunchRequest() with
        {
            Host = "0.0.0.0",
            AllowNetworkAccess = true,
            ApiKey = apiKey
        };

        var result = RuntimeAdapter.Validate(request);
        var args = RuntimeAdapter.BuildArgs(request);

        Assert.True(result.Ok);
        Assert.Contains("0.0.0.0", args);
        Assert.Contains("--api-key", args);
        Assert.Contains(apiKey, args);
    }


    [Fact]
    public void RuntimeAdapterRequiresApiKeyForModelServing()
    {
        var request = ValidLaunchRequest() with
        {
            Host = "0.0.0.0",
            AllowNetworkAccess = true,
            ApiKey = ""
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("API key", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterRejectsWeakApiKey()
    {
        var request = ValidLaunchRequest() with { ApiKey = "test-key" };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("32", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterRejectsExtremeLaunchValues()
    {
        var request = ValidLaunchRequest() with
        {
            ContextSize = int.MaxValue,
            BatchSize = int.MaxValue,
            MicroBatchSize = int.MaxValue,
            Threads = int.MaxValue
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("Context", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Batch", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Threads", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterBuildsLocalOnlyArgs()
    {
        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest());

        Assert.Contains("--host", args);
        Assert.Contains("127.0.0.1", args);
        Assert.Contains("--port", args);
        Assert.Contains("8081", args);
        Assert.Contains("--api-key", args);
    }


    [Fact]
    public void RuntimeAdapterValidatesVisionProjectorPairing()
    {
        var missing = RuntimeAdapter.Validate(ValidLaunchRequest() with { VisionMode = "on", VisionProjectorPath = "" });

        Assert.False(missing.Ok);
        Assert.Contains(missing.Errors, error => error.Contains("mmproj", StringComparison.OrdinalIgnoreCase));

        var args = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with
        {
            VisionMode = "on",
            VisionProjectorPath = "mmproj.gguf",
            VisionImageMinTokens = 256,
            VisionImageMaxTokens = 1024
        });
        Assert.Contains("--mmproj", args);
        Assert.Contains("mmproj.gguf", args);
        Assert.Contains("--image-min-tokens", args);
        Assert.Contains("256", args);
        Assert.Contains("--image-max-tokens", args);
        Assert.Contains("1024", args);

        var offArgs = RuntimeAdapter.BuildArgs(ValidLaunchRequest() with { VisionMode = "off" });
        Assert.Contains("--no-mmproj", offArgs);

        var invalid = RuntimeAdapter.Validate(ValidLaunchRequest() with { VisionImageMinTokens = 2048, VisionImageMaxTokens = 1024 });
        Assert.False(invalid.Ok);
        Assert.Contains(invalid.Errors, error => error.Contains("Image min tokens", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeAdapterBuildsSpeculativeSamplingAndRopeArgs()
    {
        var request = ValidLaunchRequest() with
        {
            SpeculativeType = "draft-mtp",
            SpecDraftModelPath = "draft.gguf",
            SpecDraftGpuLayers = 999,
            SpecDraftMinTokens = 1,
            SpecDraftMaxTokens = 4,
            SpecDraftPSplit = 0.2,
            SpecDraftPMin = 0.05,
            SpecDraftCacheTypeK = "q8_0",
            SpecDraftCacheTypeV = "q8_0",
            MaxTokens = 512,
            Seed = 1234,
            RepeatLastN = 128,
            RepeatPenalty = 1.08,
            PresencePenalty = 0.2,
            FrequencyPenalty = 0.1,
            RopeScaling = "yarn",
            RopeScale = 2,
            RopeFreqBase = 1_000_000,
            RopeFreqScale = 0.5
        };

        var args = RuntimeAdapter.BuildArgs(request);

        Assert.Contains("--spec-type", args);
        Assert.Contains("draft-mtp", args);
        Assert.Contains("--model-draft", args);
        Assert.Contains("draft.gguf", args);
        Assert.Contains("--n-gpu-layers-draft", args);
        Assert.Contains("999", args);
        Assert.Contains("--spec-draft-n-min", args);
        Assert.Contains("--spec-draft-n-max", args);
        Assert.Contains("--cache-type-k-draft", args);
        Assert.Contains("--cache-type-v-draft", args);
        Assert.Contains("--predict", args);
        Assert.Contains("512", args);
        Assert.Contains("--seed", args);
        Assert.Contains("1234", args);
        Assert.Contains("--repeat-last-n", args);
        Assert.Contains("--repeat-penalty", args);
        Assert.Contains("1.08", args);
        Assert.Contains("--presence-penalty", args);
        Assert.Contains("--frequency-penalty", args);
        Assert.Contains("--rope-scaling", args);
        Assert.Contains("yarn", args);
        Assert.Contains("--rope-scale", args);
        Assert.Contains("--rope-freq-base", args);
        Assert.Contains("--rope-freq-scale", args);
    }


    [Fact]
    public async Task RuntimeRegistryScanRegistersRuntimeOnceWhenRootContainsExecutable()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        Directory.CreateDirectory(runtimeRoot);
        await File.WriteAllTextAsync(Path.Combine(runtimeRoot, "llama-server.exe"), "fake exe", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtimes = await store.ListRuntimesAsync();

        Assert.Equal(1, count);
        var runtime = Assert.Single(runtimes);
        Assert.Equal(RuntimeMode.Native, runtime.Mode);
        Assert.Equal(RuntimeBackend.Cpu, runtime.Backend);
        Assert.Equal(Path.Combine(runtimeRoot, "llama-server.exe"), runtime.ExecutablePath);
    }


    [Fact]
    public async Task RuntimeRegistryInfersCudaFromNearbyRuntimeFiles()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var buildRoot = Path.Combine(runtimeRoot, "cuda-build");
        var binRoot = Path.Combine(buildRoot, "bin");
        var libRoot = Path.Combine(buildRoot, "lib");
        Directory.CreateDirectory(binRoot);
        Directory.CreateDirectory(libRoot);
        await File.WriteAllTextAsync(Path.Combine(binRoot, "llama-server"), "fake wsl binary", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(libRoot, "libcudart.so"), "fake cuda lib", TestContext.Current.CancellationToken);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var registry = new RuntimeRegistryService(store);

        var count = await registry.ScanAsync(runtimeRoot);
        var runtime = Assert.Single(await store.ListRuntimesAsync());

        Assert.Equal(1, count);
        Assert.Equal(RuntimeMode.Wsl, runtime.Mode);
        Assert.Equal(RuntimeBackend.Cuda, runtime.Backend);
        Assert.Equal(Path.Combine(binRoot, "llama-server"), runtime.ExecutablePath);
    }


    [Fact]
    public void RuntimeAdapterRejectsInvalidSpeculativeSettings()
    {
        var request = ValidLaunchRequest() with
        {
            SpeculativeType = "maybe-mtp",
            SpecDraftMinTokens = 8,
            SpecDraftMaxTokens = 4,
            SpecDraftPSplit = 2,
            RopeScaling = "banana"
        };

        var result = RuntimeAdapter.Validate(request);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Contains("Speculative type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Draft min tokens", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("Draft split", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("RoPE", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void RuntimeMetricsParseAndAggregatePrometheusSamples()
    {
        const string raw = """
        # HELP llama_tokens_predicted_total Predicted tokens.
        # TYPE llama_tokens_predicted_total counter
        llama_tokens_predicted_total 12
        llama_prompt_tokens_seconds{slot="0"} 3.5
        llama_kv_cache_usage_ratio NaN
        """;

        var samples = RuntimeMetrics.ParsePrometheus(raw);

        Assert.Equal(3, samples.Count);
        Assert.Equal(12, RuntimeMetrics.Sum(samples, ["tokens", "predicted", "total"], []));
        Assert.Equal(3.5, RuntimeMetrics.First(samples, ["prompt", "tokens", "seconds"], ["total"]));
        Assert.Null(RuntimeMetrics.First(samples, ["kv", "cache", "usage"], []));
        Assert.Equal("counter", samples.Single(sample => sample.Name == "llama_tokens_predicted_total").Type);
    }


    [Fact]
    public void RuntimeDashboardServiceParsesSlotsAndFormatsLabels()
    {
        const string raw = """
        [
          {
            "is_processing": true,
            "n_prompt_tokens_processed": 12,
            "n_decoded": 8,
            "n_prompt_tokens": "20",
            "n_ctx": 4096
          },
          {
            "next_token": [
              { "n_decoded": 5, "has_next_token": true }
            ],
            "prompt_tokens_processed": 3,
            "context_size": "2048"
          }
        ]
        """;

        var snapshot = RuntimeDashboardService.ParseSlotSnapshot(raw);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsProcessing);
        Assert.Equal(15, snapshot.PromptTokensProcessed);
        Assert.Equal(13, snapshot.GeneratedTokens);
        Assert.Equal(20, snapshot.PromptTokens);
        Assert.Equal(28, snapshot.ContextTokens);
        Assert.Equal(6144, snapshot.ContextSize);
        Assert.Equal(2, RuntimeDashboardService.DeltaRate(14, 10, 2, includeZero: false));
        Assert.Null(RuntimeDashboardService.DeltaRate(10, 10, 2, includeZero: false));
        Assert.Equal(0, RuntimeDashboardService.DeltaRate(10, 10, 2, includeZero: true));
        Assert.Equal(4, RuntimeDashboardService.WholePositiveDelta(7.9, 3.1));
        double? lifetimeCounter = 10;
        Assert.Equal(0, RuntimeDashboardService.WholePositiveDeltaAndRemember(null, ref lifetimeCounter));
        Assert.Equal(10, lifetimeCounter);
        Assert.Equal(5, RuntimeDashboardService.WholePositiveDeltaAndRemember(15.9, ref lifetimeCounter));
        Assert.Equal(15.9, lifetimeCounter);
        Assert.Equal(0, RuntimeDashboardService.WholePositiveDeltaAndRemember(2, ref lifetimeCounter));
        Assert.Equal(2, lifetimeCounter);
        Assert.True(RuntimeDashboardService.PositiveDelta(4, 3));
        Assert.Equal("Gen 13\nPrompt 15", RuntimeDashboardService.TokenSummaryLabel(13, 15));
        Assert.Equal("2.0 t/s (3.0 avg)", RuntimeDashboardService.RateLabel(2, 3));
        Assert.Equal("Context 6,144\nKV cache 50%, 28 tokens", RuntimeDashboardService.RuntimeSettingsLabel(.5, 28, 6144, 4096));
        var capturedAt = DateTimeOffset.Parse("2026-05-26T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("Gen 13\nPrompt 15\nLast known 5s ago", RuntimeDashboardService.WithLastKnownLine("Gen 13\nPrompt 15", capturedAt, capturedAt.AddSeconds(5)));
    }


    [Fact]
    public void DisplayFormatServiceFormatsMetricsBytesElapsedAndLongText()
    {
        Assert.Equal("0s", DisplayFormatService.Elapsed(TimeSpan.FromSeconds(-1)));
        Assert.Equal("59s", DisplayFormatService.Elapsed(TimeSpan.FromSeconds(59.9)));
        Assert.Equal("1m 05s", DisplayFormatService.Elapsed(TimeSpan.FromSeconds(65)));
        Assert.Equal("2h 03m 04s", DisplayFormatService.Elapsed(new TimeSpan(2, 3, 4)));
        Assert.Equal("1.5 KB", DisplayFormatService.Bytes(1536));
        Assert.Equal("", DisplayFormatService.Bytes(0));
        Assert.Equal("0 B", DisplayFormatService.BytesOrZero(0));
        Assert.Equal("12.346", DisplayFormatService.MetricNumber(12.3456));
        Assert.Equal("No release notes were provided.", DisplayFormatService.TrimForDisplay("", 100));
        Assert.Equal("abcdef\n\n...", DisplayFormatService.TrimForDisplay("abcdefgh", 6));
    }


    [Fact]
    public void GpuStatusServiceFormatsNvidiaSmiCsvLine()
    {
        var formatted = GpuStatusService.FormatNvidiaSmiCsvLine("0, NVIDIA RTX, 76, 62, 12288, 24576");

        Assert.Equal("GPU 0: 76% | 62C | 12.0/24.0 GiB", formatted);
    }


    [Fact]
    public void RuntimeEndpointServiceBuildsLocalAndLanUrls()
    {
        var root = CreateTempRoot();
        var local = AppSettings.CreateDefault(root) with
        {
            Host = "0.0.0.0",
            Port = 8081,
            ModelAccessMode = "local"
        };
        var lan = local with { ModelAccessMode = "lan", Host = "192.168.1.20" };

        Assert.Equal("http://127.0.0.1:8081", RuntimeEndpointService.LocalServerBaseUrl(local));
        Assert.Equal("http://127.0.0.1:8081/v1", RuntimeEndpointService.LocalOpenAiBaseUrl(local));
        Assert.Equal("http://192.168.1.20:8081/v1", RuntimeEndpointService.LanOpenAiBaseUrl(lan));
        Assert.Equal("http://192.168.1.20:8081/v1", RuntimeEndpointService.EndpointDisplay(lan));
        Assert.Equal("[::1]", RuntimeEndpointService.UrlHost("::1"));
    }


    [Fact]
    public void RuntimeEndpointServiceAddsBearerTokenWhenPresent()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = "  secret-token  " };

        using var request = RuntimeEndpointService.RuntimeGetRequest("http://127.0.0.1:8081/health", settings);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
    }


    [Fact]
    public void RuntimeEndpointServiceParsesServedModelsAndMatchesRegistrations()
    {
        const string json = """
        {
          "data": [
            { "id": "registered-id" },
            { "model": "D:\\models\\Qwen3-8B.gguf" },
            { "name": "Friendly Qwen" }
          ],
          "models": [ "plain-model" ]
        }
        """;
        var now = DateTimeOffset.UtcNow;
        var model = new ModelRecord("registered-id", "Friendly Qwen", @"D:\models\Qwen3-8B.gguf", OwnershipKind.External, "{}", now);

        var served = RuntimeEndpointService.ExtractServedModelIds(json).ToArray();

        Assert.Equal(["registered-id", @"D:\models\Qwen3-8B.gguf", "Friendly Qwen", "plain-model"], served);
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, "registered-id"));
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, @"D:\other\Qwen3-8B.gguf"));
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, "Friendly Qwen"));
        Assert.False(RuntimeEndpointService.ServedModelMatches(model, "other-model"));
    }


    [Fact]
    public void RuntimeMetadataServiceReadsManagedRuntimeMetadataAndCommits()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtime", "bin");
        Directory.CreateDirectory(runtimeRoot);
        var runtime = new RuntimeRecord(
            "runtime-1",
            "llama.cpp CUDA",
            RuntimeMode.Wsl,
            RuntimeBackend.Cuda,
            Path.Combine(runtimeRoot, "llama-server"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = Path.Combine(root, "runtime"),
                runtimeMetadata = new
                {
                    repoUrl = "https://github.com/ggml-org/llama.cpp",
                    commit = "abcdef1234567890"
                }
            }),
            DateTimeOffset.UtcNow);
        var sourceDir = Path.Combine(root, "source");
        var refDir = Path.Combine(sourceDir, ".git", "refs", "heads");
        Directory.CreateDirectory(refDir);
        File.WriteAllText(Path.Combine(sourceDir, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(refDir, "main"), "fedcba9876543210");

        Assert.Equal("official-cuda", RuntimeMetadataService.ManagedPresetId(runtime));
        Assert.Equal("official-vulkan", RuntimeMetadataService.ManagedPresetId(runtime with { Name = "llama.cpp Vulkan", Backend = RuntimeBackend.Vulkan }));
        Assert.Equal(Path.Combine(root, "runtime"), RuntimeMetadataService.Folder(runtime));
        Assert.Equal("abcdef1234567890", RuntimeMetadataService.Commit(runtime));
        Assert.True(RuntimeMetadataService.CommitsMatch("abcdef12", "abcdef1234567890"));
        Assert.Equal("abcdef123456", RuntimeMetadataService.ShortCommit("abcdef1234567890"));
        Assert.Equal("commit unavailable", RuntimeMetadataService.DisplayCommit(""));
        Assert.Equal("fedcba9876543210", RuntimeMetadataService.TryReadGitHeadCommit(sourceDir));
        Assert.Equal("123456789abcdef", RuntimeMetadataService.InferCommitFromText("build-123456789abcdef-path"));
    }


    [Fact]
    public async Task RuntimeBuildCatalogServicePersistsCustomPresetsAndReadsSources()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var custom = new RuntimeBuildPreset("", "My Runtime", "https://example.com/runtime.git", "main", true, Custom: true);

        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(runtimeRoot, [custom], TestContext.Current.CancellationToken);
        var loaded = Assert.Single(RuntimeBuildCatalogService.ReadCustomPresets(runtimeRoot));
        var sourceDir = RuntimeBuildCatalogService.SourceDir(runtimeRoot, loaded);
        Directory.CreateDirectory(Path.Combine(sourceDir, ".git"));
        File.WriteAllText(Path.Combine(sourceDir, ".git", "HEAD"), "abc123def4567890");
        var source = new RuntimeSourceEntry(loaded.Id, loaded.Label, loaded.RepoUrl, loaded.Branch, loaded.Cuda, sourceDir, "unknown", DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(
            RuntimeBuildCatalogService.SourceMetadataPath(sourceDir),
            System.Text.Json.JsonSerializer.Serialize(source),
            TestContext.Current.CancellationToken);

        var sources = RuntimeBuildCatalogService.Sources(runtimeRoot).ToList();
        var rows = RuntimeBuildCatalogService.PresetRows(runtimeRoot);

        Assert.True(loaded.Custom);
        Assert.StartsWith("custom-my-runtime-cuda-", loaded.Id, StringComparison.Ordinal);
        Assert.Contains(rows, preset => preset.Id == "official-cuda");
        Assert.Contains(rows, preset => preset.Id == "official-vulkan" && RuntimeBuildCatalogService.BuildBackend(preset) == RuntimeBackend.Vulkan);
        Assert.Equal("Vulkan WSL", RuntimeBuildCatalogService.BackendLabel(rows.Single(preset => preset.Id == "official-vulkan")));
        Assert.Contains(rows, preset => preset.Id == loaded.Id);
        Assert.Equal("abc123def4567890", RuntimeBuildCatalogService.SourceCommit(Assert.Single(sources)));
        Assert.True(RuntimeBuildCatalogService.IsAllowedGitSource("https://example.com/repo.git"));
        Assert.True(RuntimeBuildCatalogService.IsAllowedGitSource("ssh://git@example.com/repo.git"));
        Assert.True(RuntimeBuildCatalogService.IsAllowedGitSource(Path.GetTempPath()));
        Assert.False(RuntimeBuildCatalogService.IsAllowedGitSource("http://example.com/repo.git"));
        Assert.True(RuntimeBuildCatalogService.IsHttpsGitSource("https://example.com/repo.git"));
        Assert.False(RuntimeBuildCatalogService.IsHttpsGitSource("https://user:token@example.com/repo.git"));
        Assert.False(RuntimeBuildCatalogService.IsHttpsGitSource("ssh://git@example.com/repo.git"));
        Assert.False(RuntimeBuildCatalogService.IsHttpsGitSource(Path.GetTempPath()));
        Assert.True(RuntimeBuildCatalogService.IsSafeUiCustomPreset(custom));
        Assert.False(RuntimeBuildCatalogService.IsSafeUiCustomPreset(custom with { RepoUrl = "ssh://git@example.com/repo.git" }));
        Assert.True(RuntimeBuildCatalogService.IsSafeGitRefName("feature/runtime-build"));
        Assert.False(RuntimeBuildCatalogService.IsSafeGitRefName("bad branch"));
        Assert.Equal(["refs/heads/main", "main"], RuntimeBuildCatalogService.RemoteRefs(loaded));
        Assert.Equal("abcdef123", RuntimeBuildCatalogService.FirstLsRemoteCommit("abcdef123\trefs/heads/main\n"));
    }


    [Fact]
    public void RuntimeFileServiceRestrictsRuntimeDeletionToSafeFolders()
    {
        var root = CreateTempRoot();
        var runtimeRoot = Path.Combine(root, "runtimes");
        var managed = Path.Combine(runtimeRoot, "managed-runtime");
        var external = Path.Combine(root, "external-runtime");
        var packaged = Path.Combine(root, "packaged-runtime");
        Directory.CreateDirectory(Path.Combine(managed, "bin"));
        Directory.CreateDirectory(packaged);
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(managed, "bin", "llama-server.exe"), "");
        File.WriteAllText(Path.Combine(packaged, "llama-server.exe"), "");
        File.WriteAllText(Path.Combine(packaged, "local-llm-runtime.json"), """{"managedPresetId":"official-cpu"}""");
        var now = DateTimeOffset.UtcNow;
        var managedRuntime = new RuntimeRecord("managed", "Managed", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(managed, "bin", "llama-server.exe"), "{}", now);
        var externalRuntime = new RuntimeRecord("external", "External", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(external, "llama-server.exe"), "{}", now);

        Assert.True(RuntimeFileService.CanDeleteRuntimeFiles(managedRuntime, runtimeRoot, out var managedFolder, out _));
        Assert.Equal(managed, managedFolder);
        Assert.False(RuntimeFileService.CanDeleteRuntimeFiles(externalRuntime, runtimeRoot, out _, out var reason));
        Assert.Contains("outside the app runtimes folder", reason, StringComparison.Ordinal);
        Assert.True(RuntimeFileService.IsPackagedRuntimeFolderSafeToDelete(packaged));

        RuntimeFileService.DeleteRuntimeFiles(runtimeRoot, managed);

        Assert.False(Directory.Exists(managed));
        Assert.Throws<InvalidOperationException>(() => RuntimeFileService.DeleteRuntimeFiles(runtimeRoot, external));
    }


    [Fact]
    public async Task RuntimeBuildJobServiceBuildsPayloadRedactsUrlsAndStampsMetadata()
    {
        var root = CreateTempRoot();
        var installDir = Path.Combine(root, "runtime");
        Directory.CreateDirectory(installDir);
        await File.WriteAllTextAsync(Path.Combine(installDir, "local-llm-runtime.json"), """{"commit":"abc"}""", TestContext.Current.CancellationToken);
        var preset = new RuntimeBuildPreset("custom-cuda", "Custom CUDA", "https://fixture-user:fixture-pass@example.invalid/repo.git", "main", true, Custom: true);

        var payload = System.Text.Json.Nodes.JsonNode.Parse(RuntimeBuildJobService.Payload(preset, "build", installDir, "Queued.", "marker", "Ubuntu"))!.AsObject();
        await RuntimeBuildJobService.StampManagedMetadataAsync(installDir, preset, update: true);
        var logPath = Path.Combine(root, "runtime-build.log");
        await RuntimeBuildJobService.AppendJobLogAsync(logPath, JobStatus.Running, "build started", BoundedLogFile.MegabytesToBytes(1));
        await RuntimeBuildJobService.AppendRecoveryLogAsync(logPath, "recovered source", BoundedLogFile.MegabytesToBytes(1));
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(installDir, "local-llm-runtime.json"), TestContext.Current.CancellationToken))!.AsObject();
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);

        Assert.Equal("custom-cuda", payload["preset"]?.ToString());
        Assert.Equal("Custom CUDA", payload["label"]?.ToString());
        Assert.Equal(preset.RepoUrl, payload["repoUrl"]?.ToString());
        Assert.Equal("build", payload["action"]?.ToString());
        Assert.Equal("Ubuntu", payload["wslDistro"]?.ToString());
        Assert.Equal("marker", payload["processMarker"]?.ToString());
        Assert.Equal("https://redacted:redacted@example.invalid/repo.git", RuntimeBuildJobService.RedactCommandArgument(preset.RepoUrl));
        Assert.Equal("abc", metadata["commit"]?.ToString());
        Assert.Equal("custom-cuda", metadata["managedPresetId"]?.ToString());
        Assert.Equal("update", metadata["managedAction"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(metadata["managedInstalledAt"]?.ToString()));
        Assert.Contains("Running: build started", log, StringComparison.Ordinal);
        Assert.Contains("Recovery: recovered source", log, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeBuildJobServiceCreatesDeterministicBuildPlan()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "master", false);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source"), "abcdef1234567890", DateTimeOffset.UtcNow);

        var plan = RuntimeBuildJobService.CreatePlan(preset, update: false, source, settings, new DateTimeOffset(2026, 5, 26, 12, 34, 56, TimeSpan.Zero), "marker");

        Assert.Equal("build", plan.Action);
        Assert.Equal(source.SourceDir, plan.SourceDir);
        Assert.Equal(Path.Combine(settings.CacheRoot, "runtime-builds", "official-cpu-20260526-123456"), plan.BuildDir);
        Assert.Equal(Path.Combine(settings.RuntimeRoot, "official-cpu-20260526-123456"), plan.InstallDir);
        Assert.Equal("marker", plan.ProcessMarker);
        Assert.Contains("abcdef1", plan.QueuedMessage, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeBuildJobServiceParsesPayloadAndExposesJobControls()
    {
        var root = CreateTempRoot();
        var preset = new RuntimeBuildPreset("official-cuda", "Official CUDA", "https://example.com/llama.cpp.git", "master", true);
        var payloadJson = RuntimeBuildJobService.Payload(preset, "build", Path.Combine(root, "runtime"), "Building", "marker", "Ubuntu-24.04");
        var now = DateTimeOffset.UtcNow;
        var running = new JobRecord("job-1", "runtime-build", JobStatus.Running, payloadJson, Path.Combine(root, "logs", "job-1.log"), now, now);
        var failed = running with { Id = "job-2", Status = JobStatus.Failed };
        var completed = running with { Id = "job-3", Status = JobStatus.Completed };
        var completedDownload = running with { Id = "job-4", Kind = "runtime-source-download", Status = JobStatus.Completed };
        Directory.CreateDirectory(Path.GetDirectoryName(running.LogPath)!);
        File.WriteAllText(running.LogPath, "[2026-05-26T12:00:00Z] Running: Building\n[ 42%] Building CXX object llama.cpp\n");

        var payload = RuntimeBuildJobService.ParsePayload(payloadJson);
        var vm = new JobsViewModel();
        vm.ReplaceJobs([running, failed, completed]);

        Assert.NotNull(payload);
        Assert.Equal("official-cuda", payload.Preset.Id);
        Assert.Equal("Official CUDA", payload.Preset.Label);
        Assert.True(payload.Preset.Cuda);
        Assert.Equal("build", payload.Action);
        Assert.Equal("marker", payload.ProcessMarker);
        Assert.Equal("Ubuntu-24.04", payload.WslDistro);
        Assert.True(RuntimeBuildJobService.CanCancel(running));
        Assert.False(RuntimeBuildJobService.CanRetry(running));
        Assert.False(RuntimeBuildJobService.CanClear(running));
        Assert.Contains("Building CXX object", vm.RuntimeRows[0].C5, StringComparison.Ordinal);
        Assert.False(RuntimeBuildJobService.CanCancel(failed));
        Assert.True(RuntimeBuildJobService.CanRetry(failed));
        Assert.True(RuntimeBuildJobService.CanClear(failed));
        Assert.False(RuntimeBuildJobService.CanCancel(completed));
        Assert.False(RuntimeBuildJobService.CanRetry(completed));
        Assert.True(RuntimeBuildJobService.CanClear(completed));
        Assert.False(RuntimeBuildJobService.CanCancel(completedDownload));
        Assert.False(RuntimeBuildJobService.CanRetry(completedDownload));
        Assert.True(RuntimeBuildJobService.CanClear(completedDownload));
        Assert.Equal(3, vm.RuntimeRows.Count);
        Assert.True(vm.RuntimeRows[0].B2);
        Assert.False(vm.RuntimeRows[0].B3);
        Assert.False(vm.RuntimeRows[0].B4);
        Assert.False(vm.RuntimeRows[1].B2);
        Assert.True(vm.RuntimeRows[1].B3);
        Assert.True(vm.RuntimeRows[1].B4);
        Assert.Equal("Clear", vm.RuntimeRows[2].C9);
        Assert.True(vm.RuntimeRows[2].B4);
    }


    [Fact]
    public void RuntimeBuildToolServiceBuildsHiddenPowerShellCommand()
    {
        var preset = new RuntimeBuildPreset("custom-cuda", "Custom CUDA", "https://example.com/repo.git", "feature/runtime", true, Custom: true);

        var psi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            preset,
            "Ubuntu-24.04",
            "marker-1",
            @"C:\Windows\System32\wsl.exe",
            noUpdate: true);
        var args = psi.ArgumentList.ToArray();

        Assert.Equal("powershell.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardOutput);
        Assert.Contains("-RepoUrl", args);
        Assert.Contains("https://example.com/repo.git", args);
        Assert.Contains("-Branch", args);
        Assert.Contains("feature/runtime", args);
        Assert.Contains("-WslDistro", args);
        Assert.Contains("Ubuntu-24.04", args);
        Assert.Contains("-ProcessMarker", args);
        Assert.Contains("marker-1", args);
        Assert.Contains("-Cuda", args);
        Assert.Contains("-NoUpdate", args);
        Assert.Contains("-Clean", args);

        var vulkanPreset = new RuntimeBuildPreset("official-vulkan", "Official Vulkan", "https://example.com/repo.git", "master", false, Backend: "vulkan");
        var vulkanPsi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            vulkanPreset,
            "Ubuntu-24.04",
            "marker-2",
            @"C:\Windows\System32\wsl.exe",
            noUpdate: false);
        var vulkanArgs = vulkanPsi.ArgumentList.ToArray();

        Assert.Contains("-Vulkan", vulkanArgs);
        Assert.DoesNotContain("-Cuda", vulkanArgs);
    }


    [Fact]
    public async Task TrackedProcessRunnerCapturesOutputErrorAndStandardInput()
    {
        var runner = new TrackedProcessRunner();
        var psi = new System.Diagnostics.ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe());
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("$text = [Console]::In.ReadToEnd(); Write-Output $text.Trim(); [Console]::Error.WriteLine('runner-error')");

        var result = await runner.RunAsync(
            psi,
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken,
            "runner-output");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("runner-output", result.Output, StringComparison.Ordinal);
        Assert.Contains("runner-error", result.Error, StringComparison.Ordinal);
    }

}
