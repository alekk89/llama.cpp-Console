
namespace LocalLlmConsole.Services;

public sealed record RuntimeBuildPreset(string Id, string Label, string RepoUrl, string Branch, bool Cuda, bool Custom = false, string Backend = "");

public sealed record RuntimeSourceEntry(string PresetId, string Label, string RepoUrl, string Branch, bool Cuda, string SourceDir, string Commit, DateTimeOffset DownloadedAt, string Backend = "");

public sealed record RuntimeUpdateState(bool HasUpdate, string LocalCommit, string RemoteCommit, DateTimeOffset CheckedAt);

public static class RuntimeBuildCatalogService
{
    public static readonly RuntimeBuildPreset[] DefaultPresets =
    [
        new("official-cpu", "Official llama.cpp CPU", "https://github.com/ggml-org/llama.cpp.git", "master", false),
        new("official-cuda", "Official llama.cpp CUDA", "https://github.com/ggml-org/llama.cpp.git", "master", true),
        new("official-vulkan", "Official llama.cpp Vulkan", "https://github.com/ggml-org/llama.cpp.git", "master", false, Backend: "vulkan"),
        new("atomic-turboquant-cuda", "Atomic TurboQuant CUDA", "https://github.com/AtomicBot-ai/atomic-llama-cpp-turboquant.git", "", true),
        new("ik-llama-cuda", "ik_llama.cpp CUDA", "https://github.com/ikawrakow/ik_llama.cpp.git", "", true),
        new("thetom-turboquant-cuda", "TheTom TurboQuant CUDA", "https://github.com/TheTom/llama-cpp-turboquant.git", "", true)
    ];

    public static IReadOnlyList<RuntimeBuildPreset> PresetRows(string runtimeRoot)
    {
        var rows = new List<RuntimeBuildPreset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in DefaultPresets.Concat(ReadCustomPresets(runtimeRoot)))
        {
            if (seen.Add(preset.Id))
                rows.Add(preset);
        }
        return rows;
    }

    public static IEnumerable<RuntimeSourceEntry> Sources(string runtimeRoot)
    {
        var root = SourceRoot(runtimeRoot);
        if (!Directory.Exists(root)) yield break;

        foreach (var metadataPath in Directory.EnumerateFiles(root, "local-llm-runtime-source.json", SearchOption.AllDirectories))
        {
            var source = ReadSource(metadataPath);
            if (source is not null) yield return source;
        }
    }

    public static RuntimeSourceEntry? ReadSource(string metadataPath)
    {
        try
        {
            return JsonSerializer.Deserialize<RuntimeSourceEntry>(File.ReadAllText(metadataPath));
        }
        catch
        {
            return null;
        }
    }

    public static string SourceRoot(string runtimeRoot) => Path.Combine(runtimeRoot, "runtime-sources");

    public static string SourceDir(string runtimeRoot, RuntimeBuildPreset preset) => Path.Combine(SourceRoot(runtimeRoot), preset.Id);

    public static string SourceMetadataPath(string sourceDir) => Path.Combine(sourceDir, "local-llm-runtime-source.json");

    public static string CustomRepositoriesPath(string runtimeRoot) => Path.Combine(runtimeRoot, "custom-runtime-repositories.json");

    public static IReadOnlyList<RuntimeBuildPreset> ReadCustomPresets(string runtimeRoot)
    {
        var path = CustomRepositoriesPath(runtimeRoot);
        if (!File.Exists(path)) return [];

        try
        {
            return (JsonSerializer.Deserialize<List<RuntimeBuildPreset>>(File.ReadAllText(path)) ?? [])
                .Where(preset => !string.IsNullOrWhiteSpace(preset.Label) && !string.IsNullOrWhiteSpace(preset.RepoUrl))
                .Where(IsSafePreset)
                .Select(preset =>
                {
                    var branch = preset.Branch?.Trim() ?? "";
                    var rawId = string.IsNullOrWhiteSpace(preset.Id)
                        ? CustomPresetId(preset.Label, preset.RepoUrl, branch, BackendKey(preset))
                        : preset.Id;
                    var id = ModelCatalogService.SafeId(rawId);
                    return preset with { Id = id, Branch = branch, Custom = true, Backend = BackendKey(preset) };
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static async Task SaveCustomPresetsAsync(string runtimeRoot, IReadOnlyList<RuntimeBuildPreset> presets, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(runtimeRoot);
        var customPresets = presets
            .Where(preset => preset.Custom)
            .OrderBy(preset => preset.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        await File.WriteAllTextAsync(CustomRepositoriesPath(runtimeRoot), JsonSerializer.Serialize(customPresets, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    public static bool CanDownloadPreset(
        IReadOnlyList<RuntimeRecord> installed,
        IReadOnlyList<RuntimeSourceEntry> downloaded,
        RuntimeUpdateState? checkedState)
    {
        if (checkedState?.HasUpdate == true) return true;
        return installed.Count == 0 && downloaded.Count == 0;
    }

    public static string DownloadButtonLabel(
        IReadOnlyList<RuntimeSourceEntry> downloaded,
        IReadOnlyList<RuntimeRecord> installed,
        RuntimeUpdateState? checkedState)
    {
        if (checkedState?.HasUpdate == true) return "Download";
        if (downloaded.Count > 0) return "Downloaded";
        if (installed.Count > 0) return "Built";
        return "Download";
    }

    public static string LocalStatusLabel(
        IReadOnlyList<RuntimeSourceEntry> downloaded,
        IReadOnlyList<RuntimeRecord> installed,
        bool commitUnavailable)
    {
        if (commitUnavailable) return "Version unknown";
        if (downloaded.Count == 0 && installed.Count == 0) return "Not downloaded";
        if (downloaded.Count > 0 && installed.Count == 0) return "Downloaded";
        if (downloaded.Count == 0 && installed.Count > 0) return installed.Count == 1 ? "Built" : $"{installed.Count} built";
        return $"{downloaded.Count} downloaded, {installed.Count} built";
    }

    public static string LatestLocalCommitLabel(IReadOnlyList<RuntimeSourceEntry> downloaded, IReadOnlyList<RuntimeRecord> installed)
    {
        var commit = LatestLocalCommitValue(downloaded, installed);
        var source = downloaded.FirstOrDefault();
        if (source is not null)
            return string.IsNullOrWhiteSpace(commit)
                ? $"downloaded source, local commit unavailable - {source.DownloadedAt.ToLocalTime():g}"
                : $"downloaded {RuntimeMetadataService.DisplayCommit(commit)} - {source.DownloadedAt.ToLocalTime():g}";
        var latest = installed.FirstOrDefault();
        if (latest is null) return "";
        return string.IsNullOrWhiteSpace(commit)
            ? $"built runtime, local commit unavailable - {latest.UpdatedAt.ToLocalTime():g}"
            : $"built {RuntimeMetadataService.DisplayCommit(commit)} - {latest.UpdatedAt.ToLocalTime():g}";
    }

    public static string LatestLocalCommitValue(IReadOnlyList<RuntimeSourceEntry> downloaded, IReadOnlyList<RuntimeRecord> installed)
    {
        var source = downloaded.FirstOrDefault();
        if (source is not null) return SourceCommit(source);
        var latest = installed.FirstOrDefault();
        return latest is null ? "" : RuntimeMetadataService.Commit(latest);
    }

    public static string SourceCommit(RuntimeSourceEntry source)
    {
        if (!string.IsNullOrWhiteSpace(source.Commit) && !string.Equals(source.Commit, "unknown", StringComparison.OrdinalIgnoreCase))
            return source.Commit;

        var gitCommit = RuntimeMetadataService.TryReadGitHeadCommit(source.SourceDir);
        if (!string.IsNullOrWhiteSpace(gitCommit)) return gitCommit;
        return RuntimeMetadataService.InferCommitFromText(source.SourceDir);
    }

    public static IReadOnlyList<string> RemoteRefs(RuntimeBuildPreset preset)
        => string.IsNullOrWhiteSpace(preset.Branch)
            ? ["HEAD"]
            : [$"refs/heads/{preset.Branch}", preset.Branch];

    public static string FirstLsRemoteCommit(string output)
        => (output ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "")
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "";

    public static string CustomPresetId(string label, string repoUrl, string branch, bool cuda)
        => CustomPresetId(label, repoUrl, branch, cuda ? "cuda" : "cpu");

    public static string CustomPresetId(string label, string repoUrl, string branch, string backend)
    {
        var backendKey = NormalizeBuildBackend(backend);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{repoUrl}|{branch}|{backendKey}".ToLowerInvariant()));
        var hash = Convert.ToHexString(bytes)[..8].ToLowerInvariant();
        return ModelCatalogService.SafeId($"custom-{label}-{backendKey}-{hash}");
    }

    public static bool SameRepository(RuntimeBuildPreset left, RuntimeBuildPreset right)
        => string.Equals(NormalizeRepositoryText(left.RepoUrl), NormalizeRepositoryText(right.RepoUrl), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Branch?.Trim() ?? "", right.Branch?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            && BuildBackend(left) == BuildBackend(right);

    public static RuntimeBackend BuildBackend(RuntimeBuildPreset preset)
        => ParseBuildBackend(preset.Backend, preset.Cuda);

    public static RuntimeBackend BuildBackend(RuntimeSourceEntry source)
        => ParseBuildBackend(source.Backend, source.Cuda);

    public static string BackendKey(RuntimeBuildPreset preset)
        => BackendKey(BuildBackend(preset));

    public static string BackendKey(RuntimeBackend backend) => backend switch
    {
        RuntimeBackend.Cuda => "cuda",
        RuntimeBackend.Vulkan => "vulkan",
        _ => "cpu"
    };

    public static string BackendLabel(RuntimeBuildPreset preset)
        => BuildBackend(preset) switch
        {
            RuntimeBackend.Cuda => "CUDA WSL",
            RuntimeBackend.Vulkan => "Vulkan WSL",
            _ => "CPU WSL"
        };

    public static string BackendLabel(RuntimeSourceEntry source)
        => BuildBackend(source) switch
        {
            RuntimeBackend.Cuda => "CUDA",
            RuntimeBackend.Vulkan => "Vulkan",
            _ => "CPU"
        };

    public static string NormalizeBuildBackend(string backend)
        => (backend ?? "").Trim().ToLowerInvariant() switch
        {
            "cuda" => "cuda",
            "vulkan" => "vulkan",
            _ => "cpu"
        };

    private static RuntimeBackend ParseBuildBackend(string backend, bool cuda)
        => NormalizeBuildBackend(backend) switch
        {
            "cuda" => RuntimeBackend.Cuda,
            "vulkan" => RuntimeBackend.Vulkan,
            _ when cuda => RuntimeBackend.Cuda,
            _ => RuntimeBackend.Cpu
        };

    public static string NormalizeRepositoryText(string value)
        => (value ?? "").Trim().TrimEnd('/', '\\');

    public static bool IsSafePreset(RuntimeBuildPreset preset)
        => IsAllowedGitSource(preset.RepoUrl)
            && (string.IsNullOrWhiteSpace(preset.Branch) || IsSafeGitRefName(preset.Branch));

    public static bool IsSafeUiCustomPreset(RuntimeBuildPreset preset)
        => IsHttpsGitSource(preset.RepoUrl)
            && (string.IsNullOrWhiteSpace(preset.Branch) || IsSafeGitRefName(preset.Branch));

    public static bool IsHttpsGitSource(string repoUrl)
    {
        var value = (repoUrl ?? "").Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(uri.UserInfo);
    }

    public static bool IsAllowedGitSource(string repoUrl)
    {
        var value = (repoUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (Directory.Exists(value)) return true;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public static bool IsSafeGitRefName(string branch)
    {
        var value = (branch ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.StartsWith("-", StringComparison.Ordinal)) return false;
        if (value.Contains("..", StringComparison.Ordinal) || value.EndsWith(".", StringComparison.Ordinal)) return false;
        return value.All(ch => !char.IsControl(ch) && ch is not ' ' and not '~' and not '^' and not ':' and not '?' and not '*' and not '[' and not '\\');
    }
}
