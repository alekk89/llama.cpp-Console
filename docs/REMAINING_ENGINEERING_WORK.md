# Remaining Engineering Work

Last updated: 2026-05-26

## Immediate Internal Cleanup

These are codebase quality items that can continue locally without external input:

- Keep the source tree free of generated build output by using `clean-repo.ps1`; `dist` stays ignored and should contain only the current release artifact unless `-AllDist` is requested.
- Review remaining MainWindow partials opportunistically when a behavior change touches them; the largest mixed workflow files have now been split by responsibility.
- Continue tightening async/UI reliability:
  - keep fire-and-forget work behind `RunBackground`;
  - keep event-handler awaits behind `RunEventAsync`;
  - add cancellation paths where background work can outlive the current page.
- Keep architectural guard tests current as files are split or moved.

## Service-Level Cleanup

- Add integration-style `LlamaProcessSupervisor` tests around real process shutdown, stderr/stdout logging, and WSL kill fallback when a suitable test harness is available.
- Add more Hugging Face launch-suggestion behavior tests only when new option mappings are introduced.

## Product/Workflow Work Still Open

These are broader product items already implied by the release docs:

- Continue clean Windows VM validation for a published app with no repo checkout.
- Add trusted signing and broaden installer smoke testing on clean Windows VMs.
- Keep the GitHub update feed and release asset naming stable for future releases.
- WSL/hardware matrix validation across missing WSL, CPU build tools, CUDA-visible WSL, Vulkan-visible WSL, and unsupported backends.
- Benchmark WSL runtime source/build staging on `/mnt/<drive>` versus the distro's Linux filesystem; if Linux-side staging wins, preserve Windows-side metadata/cleanup semantics while copying only final runtime artifacts back to the workspace.
- Runtime archive checksum/signature verification before future downloaded binary runtime registration.

## Completed This Pass

- Added a Windows CI workflow, pinned SDK `global.json`, editor defaults, and a solution file.
- Added idempotent SQLite schema migration recording through a dedicated `StateStore.Migrations` partial.
- Added update SHA-256 companion asset verification and same-certificate update signature continuity for signed installs.
- Replaced the central model/runtime/runtime-repository `UiRow` projections with typed row view models.
- Split release-hardening tests into domain partials and added CI, migration, and update-checksum coverage.
- Split runtime source download/update/delete flow into `MainWindow.RuntimeSourceDownloads.cs`.
- Split runtime source build/delete/job tracking flow into `MainWindow.RuntimeBuildJobs.cs`.
- Kept `MainWindow.RuntimeBuilds.cs` focused on timer hooks and row event routing.
- Split launch setting capability visibility into `MainWindow.LaunchSettingsCapabilities.cs`.
- Split runtime choice and model action state into `MainWindow.LaunchSettingsRuntimeSelection.cs`.
- Kept `MainWindow.LaunchSettings.cs` focused on profile render/save/read/apply behavior.
- Split folder selection and app setting persistence into `MainWindow.FolderSettings.cs`.
- Split WSL/port launch preflight into `MainWindow.ModelRuntimePrerequisites.cs`.
- Split model runtime start/stop/loading/readiness lifecycle into `MainWindow.ModelRuntimeLifecycle.cs`.
- Kept `MainWindow.ModelRuntime.cs` focused on load/unload command entry points.
- Split per-grid column sizing into `MainWindow.GridColumnSizing.cs`, leaving `MainWindow.GridHelpers.cs` focused on generic grid construction and button columns.
- Split registered model row actions into `MainWindow.ModelRows.cs`.
- Split Hugging Face download history/resume/pause/stop/timer flow into `MainWindow.DownloadHistory.cs`.
- Kept `MainWindow.ModelDownloads.cs` focused on Hugging Face search and starting downloads.
- Split runtime metric counter, lifetime-token, and idle-unload bookkeeping into `MainWindow.RuntimeMetricCounters.cs`.
- Changed `LocalAppService` to track in-flight request handlers, observe handler completion, and wait briefly for active handlers during shutdown.
- Split OpenCode provider enablement helpers into `OpenCodeConfigService.Providers.cs`.
- Split OpenCode model envelope/export/import helpers into `OpenCodeConfigService.ModelEnvelopes.cs`.
- Kept `OpenCodeConfigService.Json.cs` focused on config file JSON I/O and low-level object helpers.
- Split `LlamaProcessSupervisor` WSL cleanup/path helpers into `LlamaProcessSupervisor.Wsl.cs`.
- Moved supervisor launch helper logic into `LlamaProcessSupervisor.Launch.cs`.
- Reused `LogFileService.RedactSensitiveText` for runtime log redaction instead of duplicating secret redaction regexes.
- Split Hugging Face launch suggestion config parsing into `HuggingFaceLaunchSettingsSuggester.Config.cs`.
- Split README command extraction into `HuggingFaceLaunchSettingsSuggester.CommandExtraction.cs`.
- Split shell tokenization into `HuggingFaceLaunchSettingsSuggester.ShellParsing.cs`.
- Added `ConfigFileSafetyService` for shared backup-before-write/delete and reparse-point rejection.
- Updated OpenCode config and markdown agent writes/deletes to use the shared config safety helper.
- Extracted runtime build action/path/message planning into `RuntimeBuildJobService.CreatePlan`.
- Added `LlamaProcessSupervisor` attach/load/stop state-transition coverage.
- Added Hugging Face launch-suggestion coverage for inline option values, quoted draft paths, config JSON, out-of-range context config, and llama-cli fallback.
- Added direct Hugging Face repo id and GGUF file URL parsing to search, including `huggingface.co` and `hf.co` URLs.
- Added safe Hugging Face model-card actions for selected search and download-history rows.
- Added compact GGUF-derived model manifest enrichment during downloaded/external model registration.
- Added Hugging Face search compatibility signals, including vision/mmproj, reasoning, MoE, config/tokenizer, and license indicators.
- Fixed model capability cache invalidation for added/removed mmproj projector files and covered explicit vision-launch validation.
- Added Hugging Face mmproj/projector companion metadata, safe validation, automatic verified companion download when available, and projector-specific service split.
- Added per-model dynamic-resolution vision image token allowances and llama-server `--image-min-tokens` / `--image-max-tokens` launch mapping.
- Added Ubuntu/WSL Vulkan runtime build presets, WSL setup/probe/preflight workflow, and backend plumbing through catalog rows, custom repositories, build commands, script flags, and runtime metadata inference.
- Added runtime build job Cancel and Retry actions with in-flight cancellation, WSL marker cleanup, retry payload parsing, and row-level enablement tests.
- Removed the redundant runtime build-job log-tail preview while keeping row-level log opening with safe workspace-log validation and redaction.
- Added running runtime build progress summaries from the latest meaningful build-log line.
- Added terminal runtime build-job cleanup with safe job-record deletion and workspace-log deletion.
- Tightened runtime build deletion so runtime files can only be deleted when the runtime is not active and not referenced by saved model launch settings.
- Added an Inno Setup installer source and build wrapper with preferred `D:\LlamaCppConsole` installs, editable install location, launch-after-install, existing-install reuse, and uninstall data preservation by default.
