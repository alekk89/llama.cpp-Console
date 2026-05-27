This release turns the app into a multi-runtime, multi-model `llama.cpp`
console for Windows users. The normal path is now simple: install an official
prebuilt runtime, choose Windows or WSL per model, and load one or more models
on stable endpoints. Source builds are still available when you need custom or
advanced runtime work.

## Highlights

- Added official prebuilt `llama.cpp` runtime downloads as the main workflow.
- Added native Windows runtime support alongside Ubuntu/WSL runtimes.
- Added Intel Arc GPU support through SYCL runtime choices for Windows and WSL
  when upstream packages and the local driver/tool stack support them.
- Added multi-model loading: run more than one model at the same time on
  separate saved model ports when your hardware has enough capacity.
- Added stable per-model OpenCode endpoints using separate local providers, so
  OpenCode can address concurrently served models across app sessions.
- Source downloads and builds remain available behind **Runtimes > Show
  advanced**.
- Windows and WSL Linux setup pages moved under **Tools**, since they are now
  advanced setup/troubleshooting pages rather than the normal first-run path.
- The Overview page now focuses on loaded model sessions, model size, state,
  runtime, endpoint, and live metrics for the selected model.

## Recommended Workflow

1. Open **Runtimes**.
2. Install the official prebuilt runtime you want: Windows or WSL, then CPU,
   CUDA, Vulkan, or Intel Arc SYCL.
3. Open **Models**, download or register a GGUF model, and save its runtime and
   model port.
4. Open **Overview** and load one or more models.
5. Open **OpenCode** only if you want the app to write local model entries for
   OpenCode.

Use **Tools > Windows**, **Tools > WSL Linux**, and **Runtimes > Show advanced**
only for source builds, custom runtime branches, missing toolchains, or deeper
troubleshooting.

## Compatibility

- Windows 10/11 x64 desktop app.
- Native Windows `llama-server.exe` runtimes.
- Ubuntu/WSL `llama-server` runtimes.
- CPU fallback plus GPU runtimes for NVIDIA CUDA, Vulkan-capable devices, and
  Intel Arc SYCL where supported.

Official prebuilt package availability depends on the upstream `llama.cpp`
release assets. GPU runtime success also depends on local drivers and WSL GPU
visibility.

## Upgrade Notes

- Existing app data, models, runtimes, logs, cache, and settings are preserved
  by installer update/repair.
- Existing models can keep using their saved launch settings; per-model ports
  are now the preferred way to keep OpenCode endpoints stable.
- If you previously built runtimes from source, you can keep them or install the
  matching official prebuilt runtime from **Runtimes**.
