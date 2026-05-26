# llama.cpp Console

Windows-first desktop app for installing, configuring, and running local
`llama.cpp` models in Ubuntu/WSL.

This is an unofficial community project. It is not affiliated with, endorsed by,
or maintained by the `llama.cpp` or `ggml-org` projects.

![llama.cpp Console overview showing a loaded model, live token counters, GPU status, runtime logs, and llama.cpp metrics](docs/images/overview.png)

## What It Does

- Registers local GGUF models and app-managed downloaded models.
- Searches Hugging Face for GGUF files and downloads them with size or SHA-256
  verification before registration.
- Detects WSL, Ubuntu distros, CPU build tools, CUDA Toolkit, and Vulkan build
  prerequisites.
- Builds CPU, CUDA, or Vulkan `llama.cpp` runtimes inside Ubuntu/WSL.
- Starts and supervises `llama-server`, exposing its OpenAI-compatible `/v1`
  endpoint for local clients with per-model launch profiles.
- Shows live runtime metrics, token counters, logs, jobs, GPU summary, and model
  state in a WPF Overview page.
- Preserves last-known token metrics during short runtime metric gaps.
- Optionally writes local OpenCode provider/model snippets without making
  OpenCode a requirement.
- Stores settings, jobs, models, runtimes, migrations, and history in SQLite.

## Comparison

This app overlaps with other local-LLM tools, but it aims at a narrower Windows
workflow: managing `llama.cpp` builds and `llama-server` launches inside
Ubuntu/WSL without living in a terminal.

| Tool | Primary focus | How llama.cpp Console differs |
| --- | --- | --- |
| Ollama | Simple local model runner with CLI, app, model library, and local API. | Keeps you closer to raw `llama.cpp`/GGUF workflows: build CPU/CUDA/Vulkan runtimes in WSL, choose a runtime per model, and inspect logs/metrics directly. |
| LM Studio | Polished desktop model browser, chat UI, and local OpenAI-compatible server. | Focuses less on chat UX and more on WSL setup, source builds, runtime selection, launch profiles, and operational monitoring. |
| Jan | Open-source local AI platform with desktop, server/API, CLI, and assistant workflows. | Stays centered on Windows-managed `llama.cpp` in Ubuntu/WSL, plus optional OpenCode config helpers, instead of being a general assistant platform. |
| `llama-server` | Upstream `llama.cpp` OpenAI-compatible HTTP server. | Wraps `llama-server` with Windows UI for WSL/toolchain setup, source checkout/builds, model registration, per-model launch settings, logs, metrics, and update/install flow. |

## Safety Defaults

- The app control service binds to `127.0.0.1` only and requires a per-session
  bearer token for non-health API calls.
- Model serving defaults to `127.0.0.1`; LAN mode maps model serving to
  `0.0.0.0` only after an explicit Settings change.
- Model serving exposes the upstream `llama-server` OpenAI-compatible endpoint,
  not the app-local control API.
- Model serving requires a strong API key in both local-only and LAN modes.
- The model API key is protected with Windows current-user DPAPI at rest.
- Destructive deletes are bounded by app ownership and path-root checks.
- External/imported models are registration-only deletes by default.
- Hugging Face downloads reject unsafe Windows filenames, symlink/hardlink
  partials, and incomplete files.
- Corrupt settings are backed up before defaults are loaded.
- Corrupt SQLite database files are quarantined and recreated on startup.
- Installer updates, repairs, and default uninstalls preserve `data`, models,
  runtimes, cache, logs, and state unless the user explicitly chooses to delete
  app data during uninstall.

## End-User Distribution

End users should receive a release artifact, not the source tree.

Preferred artifact:

```text
dist\installer\LlamaCppConsole-Setup-1.0.0-win-x64.exe
```

Portable artifact:

```text
dist\LlamaCppConsole-win-x64\LlamaCppConsole.exe
```

Fresh installer defaults:

- `D:\LlamaCppConsole` when `D:` exists.
- `%LocalAppData%\Programs\LlamaCppConsole` when `D:` is unavailable.
- Existing installs reuse the previous install directory.

Portable runs create a workspace beside the executable when writable:

```text
LlamaCppConsole.exe
data\
  models\
  runtimes\
  cache\
  state\
  logs\
```

If the executable folder is not writable, the app falls back to
`%LocalAppData%\llama.cpp Console`. Override the workspace before launch with
`LLAMA_CPP_CONSOLE_WORKSPACE`. The legacy `LOCAL_LLM_CONSOLE_WORKSPACE` variable
is still accepted for pre-v1 test setups.

## Developer Prerequisites

- Windows 10/11 x64.
- PowerShell 5+.
- .NET 8 SDK.
- WSL with an Ubuntu distro for guided runtime builds.
- Git, CMake, compiler tools, and optional CUDA/Vulkan tools inside Ubuntu.
- Inno Setup 6 for installer builds.

If `dotnet` is not on `PATH`, point the scripts at an SDK explicitly:

```powershell
$env:LLAMA_CPP_CONSOLE_DOTNET = "C:\Path\To\dotnet.exe"
```

The legacy `LOCAL_LLM_CONSOLE_DOTNET` variable is also accepted.

## Build, Test, Publish

Run the local release gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-app.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-vulnerabilities.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-app.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1
```

CI runs the same gate on `windows-latest` through
[.github/workflows/ci.yml](.github/workflows/ci.yml). `global.json` pins the SDK
feature band used by CI and local scripts.

Signed release builds can be produced with a certificate thumbprint:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-app.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-installer.ps1 -CertificateThumbprint "<cert-thumbprint>" -RequireSigned
```

The publish and installer scripts write `.sha256` companion files beside the
generated binaries. The app updater requires a matching SHA-256 asset before
staging an update.

Launch a published local build:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start-app.ps1
```

## Repository Hygiene

Generated output is intentionally ignored: `bin`, `obj`, `TestResults`, `dist`,
logs, local workspaces, SQLite state, and model/checkpoint files.

Clean local build/test output while keeping the current `dist` package:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\clean-repo.ps1
```

Remove `dist` too:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\clean-repo.ps1 -AllDist
```

## Project Layout

- `src/LocalLlmConsole.App/` - WPF app, SQLite state, services, model/runtime
  management, process supervision, and UI pages.
- `src/LocalLlmConsole.App/tools/` - embedded runtime build helper extracted on
  demand into the app workspace.
- `tests/LocalLlmConsole.Tests/` - release-hardening tests for storage, safety,
  runtime validation, UI behavior, updates, and packaging.
- `installer/` - Inno Setup source.
- `docs/` - architecture notes, installer notes, audit notes, signing notes, and
  release-readiness checklist.

The source namespace remains `LocalLlmConsole`; the product and published
executable are `llama.cpp Console` and `LlamaCppConsole.exe`.

## Known Limitations

- Installer builds require Inno Setup 6 locally or
  `LLAMA_CPP_CONSOLE_INNO_SETUP`.
- WSL hardware coverage still needs validation across missing WSL, CPU-only,
  CUDA-visible, Vulkan-visible, and unsupported-backend machines.
- macOS/Linux desktop packaging is not a release target.

## License

This project is released under the MIT License. See [LICENSE](LICENSE).
