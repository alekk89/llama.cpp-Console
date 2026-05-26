# Release Hardening Audit

Audit date: 2026-05-26

## Executive Summary

Overall release posture: **v1.0.0 is published as an unsigned community
release with explicit SmartScreen and SHA-256 verification notes. Trusted
code-signing and broader clean-machine/hardware validation remain follow-up
hardening work, not hidden blockers for the current public release.**

The core release blockers from the full audit have been addressed in code:

- Release build and self-contained publish now verify on .NET SDK 8.0.421 / runtime 8.0.27.
- Automated release-hardening tests now cover concurrent SQLite access, corrupt settings recovery, deletion boundaries, and runtime host validation.
- SQLite access is serialized and settings saves are transactional.
- Corrupt settings are backed up before defaults are restored; corrupt DB files are quarantined and recreated.
- The workspace is fixed at process startup instead of being editable at runtime.
- Job IDs use GUIDs.
- Hugging Face downloads are bounded to the models folder, block duplicate destinations, reject unsafe local filenames and partial-file links, preflight disk space, and require expected-size or SHA-256 verification before model registration.
- Model serving now requires a strong API key even in local-only mode, and the persisted key is protected with current-user Windows data protection.
- Runtime source IDs loaded from custom JSON are sanitized, and recursive runtime deletes are path-bounded.
- WSL shutdown no longer uses a broad port-only kill.
- The WSL Linux page now detects WSL, installed non-Docker distros, the default distro, and shows focused WSL/Ubuntu install or update actions.
- Release publish omits PDB files and supports certificate signing with `-CertificateThumbprint` and `-RequireSigned`.
- App update checks are staged through the workspace cache and replace the portable exe only after the running process closes.
- App update staging verifies a matching SHA-256 companion asset when present and requires same-certificate signature continuity when the installed app is already signed.
- The WSL setup workflow now covers CPU, CUDA, and Vulkan prerequisites before source builds start.
- Per-model launch settings now include vision image token allowances and map them to llama.cpp server flags.

## Remaining External Hardening Work

### Clean Windows VM validation

- Severity: High
- Area: Installation and onboarding
- Status: Follow-up hardening
- Required result: Published app launches with no repository checkout, creates state, shows clear prerequisite guidance, and does not require a developer SDK.

### Trusted signing and distribution

- Severity: High for reducing Windows trust warnings
- Area: Distribution and trust
- Status: Portable single-exe publish and Inno Setup installer source exist; signing support exists; certificate is not present in this repo. The current public release is unsigned and labeled as such.
- Required result: A future trusted release is signed with a trusted certificate and distributed as a signed portable zip or installer with shortcut/uninstall flow.

### GitHub update feed

- Severity: Medium
- Area: Distribution
- Status: Update UI, staged installer, checksum verification, and signed-app signature continuity are implemented; the public repository and v1.0.0 asset naming are confirmed.
- Required result: Latest GitHub release contains `LlamaCppConsole-win-x64.zip`, matching SHA-256 companion assets, and release notes suitable for the completion popup.

### WSL and hardware matrix

- Severity: Medium
- Area: llama.cpp runtime/build support
- Status: Requires manual hardware coverage
- Required result: Validate missing WSL, missing distro, CPU build, missing Git/CMake/compiler, CUDA-visible WSL, Vulkan-visible WSL, and unsupported backend paths.
- Added support: The app can detect installed non-Docker distros and guide WSL install/update, Ubuntu install/update, CPU tools, CUDA Toolkit, and Vulkan tool setup from the WSL Linux page.

### Runtime/archive checksum verification

- Severity: Medium
- Area: Third-party binaries
- Status: Not implemented for future runtime archive downloads
- Required result: Any downloaded third-party runtime archive must be checksum or signature verified before registration.

## Automated Checks

Current passing checks:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-app.ps1 -Restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-app.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-vulnerabilities.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish-app.ps1
```

The latest local smoke test launched a published `LlamaCppConsole.exe`, confirmed the window title `llama.cpp Console v1.0`, and verified the local health endpoint.

## Edge Cases To Keep Testing

- No internet during Hugging Face search or download.
- Slow internet with cancellation during a large GGUF download.
- Interrupted app shutdown during model download or llama.cpp build.
- Disk full during download, build, extract, or SQLite write.
- Missing WSL, missing configured Ubuntu distro, or WSL disabled.
- Git, CMake, compiler, CUDA, or Vulkan missing inside Ubuntu.
- Permission denied for workspace, models, runtime, or cache folders.
- Invalid, partial, renamed, or moved GGUF model files.
- Missing or deleted llama-server executable after registration.
- Manually edited or corrupt SQLite/settings state.
- Unicode, spaces, long paths, and non-default drive letters.
- OpenCode missing, outdated, or misconfigured.

## Release Decision

v1.0.0 is acceptable as a clearly unsigned public community release. A future
trusted/stable Windows distribution should add Authenticode signing, broader
clean-machine smoke testing, and wider hardware matrix validation.
