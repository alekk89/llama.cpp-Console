param(
  [string] $RepoUrl = "https://github.com/ggml-org/llama.cpp.git",
  [string] $Branch = "",
  [Parameter(Mandatory = $true)]
  [string] $SourceDir,
  [Parameter(Mandatory = $true)]
  [string] $BuildDir,
  [Parameter(Mandatory = $true)]
  [string] $InstallDir,
  [ValidateSet("wsl", "native")]
  [string] $Runtime = "wsl",
  [string] $WslDistro = "Ubuntu-24.04",
  [string] $WslExe = "",
  [string] $GitExe = "",
  [string] $CMakeExe = "",
  [string] $ProcessMarker = "",
  [switch] $Cuda,
  [switch] $Vulkan,
  [switch] $Clean,
  [switch] $NoUpdate
)

$ErrorActionPreference = "Stop"
if ($Cuda -and $Vulkan) { throw "Choose either -Cuda or -Vulkan, not both." }

function ConvertTo-WslPath {
  param([string] $Path)
  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
  $normalized = $Path -replace '/', '\'
  if ($normalized -match '^([A-Za-z]):\\(.*)$') {
    $drive = $Matches[1].ToLowerInvariant()
    $rest = $Matches[2] -replace '\\', '/'
    return "/mnt/$drive/$rest"
  }
  return ($Path -replace '\\', '/')
}

function Quote-Bash {
  param([string] $Value)
  return "'" + ($Value -replace "'", "'\''") + "'"
}

function Normalize-Id {
  param([string] $Value)
  $text = $Value.ToLowerInvariant() -replace '[^a-z0-9_.-]+', '-'
  $text = $text.Trim('-')
  if ([string]::IsNullOrWhiteSpace($text)) { return "llama-cpp-runtime" }
  return $text
}

function Resolve-Executable {
  param([string] $PathOrName, [string] $FallbackName)
  $candidate = if ([string]::IsNullOrWhiteSpace($PathOrName)) { $FallbackName } else { $PathOrName }
  if ([System.IO.Path]::IsPathRooted($candidate)) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "Executable not found: $candidate" }
    return $candidate
  }
  $pathValue = if ([string]::IsNullOrEmpty($env:PATH)) { "" } else { $env:PATH }
  foreach ($entry in ($pathValue -split [System.IO.Path]::PathSeparator)) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }
    $expanded = [Environment]::ExpandEnvironmentVariables($entry.Trim().Trim('"'))
    if (-not [System.IO.Path]::IsPathRooted($expanded)) { continue }
    $path = Join-Path $expanded $candidate
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      return [System.IO.Path]::GetFullPath($path)
    }
  }
  throw "Executable not found on PATH: $candidate"
}

function Resolve-WslExe {
  param([string] $PathOrName)
  if (-not [string]::IsNullOrWhiteSpace($PathOrName)) {
    return Resolve-Executable $PathOrName "wsl.exe"
  }

  $candidates = @()
  if (-not [string]::IsNullOrWhiteSpace($env:SystemRoot)) {
    $candidates += (Join-Path $env:SystemRoot "System32\wsl.exe")
    $candidates += (Join-Path $env:SystemRoot "Sysnative\wsl.exe")
  }
  if (-not [string]::IsNullOrWhiteSpace($env:WINDIR)) {
    $candidates += (Join-Path $env:WINDIR "System32\wsl.exe")
    $candidates += (Join-Path $env:WINDIR "Sysnative\wsl.exe")
  }
  foreach ($candidate in ($candidates | Select-Object -Unique)) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
      return [System.IO.Path]::GetFullPath($candidate)
    }
  }
  throw "wsl.exe was not found in the Windows system directory."
}

function Get-WslDistroNames {
  param([string] $ResolvedWslExe)
  try {
    $raw = & $ResolvedWslExe --list --quiet 2>$null
    return @($raw | ForEach-Object { ($_ -replace "`0", "").Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '^(docker-desktop|docker-desktop-data)$' })
  } catch {
    return @()
  }
}

function Resolve-WslDistroName {
  param([string] $ResolvedWslExe, [string] $RequestedDistro)
  $requested = if ([string]::IsNullOrWhiteSpace($RequestedDistro)) { "Ubuntu-24.04" } else { $RequestedDistro.Trim() }
  $distros = Get-WslDistroNames $ResolvedWslExe
  if ($distros.Count -eq 0) { return $requested }
  if ($distros | Where-Object { $_ -ieq $requested } | Select-Object -First 1) { return $requested }
  if ($requested -ine "Ubuntu-24.04") { return $requested }

  foreach ($preferred in @("Ubuntu-24.04", "Ubuntu-22.04", "Ubuntu")) {
    $match = $distros | Where-Object { $_ -ieq $preferred } | Select-Object -First 1
    if ($match) { return $match }
  }
  $ubuntu = $distros | Where-Object { $_ -like "*Ubuntu*" } | Select-Object -First 1
  if ($ubuntu) { return $ubuntu }
  return $requested
}

function Redact-Argument {
  param([string] $Value)
  $uri = $null
  if ([System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref] $uri) -and -not [string]::IsNullOrWhiteSpace($uri.UserInfo)) {
    $builder = [System.UriBuilder]::new($uri)
    $builder.UserName = "redacted"
    $builder.Password = "redacted"
    return $builder.Uri.AbsoluteUri
  }
  return $Value
}

function Invoke-Logged {
  param([string] $FilePath, [string[]] $Arguments)
  $SafeArguments = ($Arguments | ForEach-Object { Redact-Argument $_ }) -join ' '
  Write-Host "> $(Redact-Argument $FilePath) $SafeArguments"
  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "$FilePath failed with exit code $LASTEXITCODE"
  }
}

function Assert-SafeDirectoryRemovalTarget {
  param([string] $Path, [string] $Label)
  $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
  $root = ([System.IO.Path]::GetPathRoot($full)).TrimEnd('\', '/')
  if ([string]::IsNullOrWhiteSpace($full) -or $full -ieq $root) {
    throw "Refusing to clean unsafe $Label path: $Path"
  }
  if (-not (Test-Path -LiteralPath $full)) { return }
  $item = Get-Item -LiteralPath $full -Force
  if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing to clean $Label because it is a symlink or junction: $full"
  }
  $reparse = Get-ChildItem -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
    Select-Object -First 1
  if ($reparse) {
    throw "Refusing to clean $Label because it contains a symlink or junction: $($reparse.FullName)"
  }
}

function Test-AllowedGitSource {
  param([string] $Value)
  if ([string]::IsNullOrWhiteSpace($Value)) { return $true }
  if (Test-Path -LiteralPath $Value -PathType Container) { return $true }
  $uri = $null
  if ([System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref] $uri)) {
    return @("https", "file", "ssh") -contains $uri.Scheme.ToLowerInvariant()
  }
  return $false
}

function Test-SafeGitRefName {
  param([string] $Value)
  if ([string]::IsNullOrWhiteSpace($Value)) { return $true }
  if ($Value.StartsWith("-") -or $Value.Contains("..") -or $Value.EndsWith(".")) { return $false }
  return $Value -notmatch '[\x00-\x20~^:?*\[\\]'
}

if ($Runtime -eq "wsl") {
  $WslExe = Resolve-WslExe $WslExe
  $WslDistro = Resolve-WslDistroName $WslExe $WslDistro
} else {
  $GitExe = Resolve-Executable $GitExe "git.exe"
  $CMakeExe = Resolve-Executable $CMakeExe "cmake.exe"
}
if (-not (Test-AllowedGitSource $RepoUrl)) { throw "Only HTTPS, SSH, file, or existing local Git repository sources are allowed." }
if (-not (Test-SafeGitRefName $Branch)) { throw "Unsafe Git branch/ref name: $Branch" }

$SourceDir = [System.IO.Path]::GetFullPath($SourceDir)
$BuildDir = [System.IO.Path]::GetFullPath($BuildDir)
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
if ($Clean) { Assert-SafeDirectoryRemovalTarget $BuildDir "build directory" }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SourceDir), (Split-Path -Parent $BuildDir), (Split-Path -Parent $InstallDir) | Out-Null

if ($Runtime -eq "wsl") {
  $WslSource = ConvertTo-WslPath $SourceDir
  $WslBuild = ConvertTo-WslPath $BuildDir
  $WslInstall = ConvertTo-WslPath $InstallDir
  $Repo = Quote-Bash $RepoUrl
  $UseDefaultBranch = [string]::IsNullOrWhiteSpace($Branch)
  $BranchQ = if ($UseDefaultBranch) { "" } else { Quote-Bash $Branch }
  $SourceQ = Quote-Bash $WslSource
  $BuildQ = Quote-Bash $WslBuild
  $InstallQ = Quote-Bash $WslInstall
  $MarkerExport = if ([string]::IsNullOrWhiteSpace($ProcessMarker)) { ":" } else { "export LLAMA_CPP_CONSOLE_BUILD_MARKER=$(Quote-Bash $ProcessMarker); export LOCAL_LLM_CONSOLE_BUILD_MARKER=$(Quote-Bash $ProcessMarker)" }
  $CudaPreflight = if ($Cuda) {
@'
if command -v nvcc >/dev/null 2>&1; then
  cuda_nvcc=$(command -v nvcc)
elif [ -x /usr/local/cuda/bin/nvcc ]; then
  cuda_nvcc=/usr/local/cuda/bin/nvcc
else
  cuda_nvcc=
  for candidate in /usr/local/cuda*/bin/nvcc; do
    if [ -x "$candidate" ]; then
      cuda_nvcc="$candidate"
      break
    fi
  done
fi
if [ -n "$cuda_nvcc" ]; then
  "$cuda_nvcc" --version | head -n 4
  cuda_root=$(cd "$(dirname "$cuda_nvcc")/.." && pwd)
  export CUDA_HOME="$cuda_root"
  export CUDAToolkit_ROOT="$cuda_root"
  export PATH="$cuda_root/bin:$PATH"
  if [ -d "$cuda_root/targets/x86_64-linux/lib" ]; then
    export LD_LIBRARY_PATH="$cuda_root/targets/x86_64-linux/lib:${LD_LIBRARY_PATH:-}"
  fi
  cuda_cmake_args=(-DCUDAToolkit_ROOT="$cuda_root" -DCMAKE_CUDA_COMPILER="$cuda_nvcc")
else
  cuda_cmake_args=()
fi
cuda_lib=$(find /usr/local/cuda* /usr/lib /usr/lib/x86_64-linux-gnu -maxdepth 5 \( -name 'libcudart.so' -o -name 'libcudart.so.*' \) 2>/dev/null | head -n 1 || true)
if [ -z "$cuda_lib" ] && command -v ldconfig >/dev/null 2>&1; then
  cuda_lib=$(ldconfig -p | awk '/libcudart\.so/{print $NF; exit}')
fi
if [ -z "$cuda_nvcc" ] || [ -z "$cuda_lib" ]; then
  if command -v nvidia-smi >/dev/null 2>&1; then nvidia-smi -L 2>/dev/null || true; fi
  if [ -z "$cuda_nvcc" ]; then echo "CUDA compiler nvcc was not found inside this WSL distro." >&2; fi
  if [ -z "$cuda_lib" ]; then echo "CUDA runtime library libcudart was not found inside this WSL distro." >&2; fi
  echo "CPU build tools do not include CUDA. Use WSL Linux > Install CUDA, or install the NVIDIA CUDA Toolkit/runtime development packages in Ubuntu/WSL manually, then retry." >&2
  exit 2
fi
'@
  } else {
    ":"
  }
  $VulkanPreflight = if ($Vulkan) {
@'
missing_vulkan=()
if command -v glslc >/dev/null 2>&1; then
  vulkan_glslc=$(command -v glslc)
else
  vulkan_glslc=
  missing_vulkan+=("glslc")
fi
if command -v vulkaninfo >/dev/null 2>&1; then :; else missing_vulkan+=("vulkaninfo"); fi
if [ -f /usr/include/vulkan/vulkan.h ]; then :; else missing_vulkan+=("libvulkan-dev"); fi
if [ -d /usr/include/spirv ] || [ -d /usr/include/SPIRV ]; then :; else missing_vulkan+=("spirv-headers"); fi
vulkan_lib=$(ldconfig -p 2>/dev/null | awk '/libvulkan\.so/{print $NF; exit}')
if [ -z "$vulkan_lib" ] && [ -f /usr/lib/x86_64-linux-gnu/libvulkan.so ]; then
  vulkan_lib=/usr/lib/x86_64-linux-gnu/libvulkan.so
fi
if [ -z "$vulkan_lib" ]; then missing_vulkan+=("libvulkan.so"); fi
if [ ${#missing_vulkan[@]} -ne 0 ]; then
  echo "Vulkan build dependencies were not complete inside this WSL distro: ${missing_vulkan[*]}" >&2
  echo "Use WSL Linux > Install Vulkan, or install libvulkan-dev, glslc, spirv-headers, vulkan-tools, and a Vulkan driver/device manually, then retry." >&2
  exit 2
fi
if ! vulkaninfo --summary; then
  echo "Vulkan tools are installed, but vulkaninfo could not see a usable Vulkan driver/device inside this WSL distro." >&2
  echo "Install or update the Windows GPU driver with WSL Vulkan support, then retry." >&2
  exit 2
fi
vulkan_cmake_args=(-DVulkan_GLSLC_EXECUTABLE="$vulkan_glslc")
if [ -n "$vulkan_lib" ]; then vulkan_cmake_args+=(-DVulkan_LIBRARY="$vulkan_lib"); fi
'@
  } else {
    ":"
  }
  $Defs = @(
    "-DCMAKE_BUILD_TYPE=Release",
    "-DLLAMA_BUILD_SERVER=ON",
    "-DLLAMA_BUILD_TESTS=OFF",
    "-DLLAMA_TESTS_INSTALL=OFF",
    "-DLLAMA_BUILD_EXAMPLES=OFF",
    "-DLLAMA_BUILD_APP=OFF",
    "-DCMAKE_INSTALL_PREFIX=$WslInstall"
  )
  if ($Cuda) { $Defs += "-DGGML_CUDA=ON" }
  if ($Vulkan) { $Defs += "-DGGML_VULKAN=ON" }
  $DefArgs = ($Defs | ForEach-Object { Quote-Bash $_ }) -join " "
  $CleanBlock = if ($Clean) { "rm -rf $BuildQ" } else { ":" }
  $CloneBlock = if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
    "test -d $SourceQ || { echo 'SourceDir does not exist and RepoUrl is empty.' >&2; exit 2; }"
  } elseif ($UseDefaultBranch) {
    "if [ ! -d $SourceQ ]; then git clone --depth 1 $Repo $SourceQ; fi"
  } else {
    "if [ ! -d $SourceQ ]; then git clone --depth 1 --branch $BranchQ $Repo $SourceQ; fi"
  }
  $UpdateBlock = if ($NoUpdate -or [string]::IsNullOrWhiteSpace($RepoUrl)) {
    ":"
  } elseif ($UseDefaultBranch) {
    "if [ -d $SourceQ/.git ]; then git -C $SourceQ fetch --all --tags && git -C $SourceQ pull --ff-only; fi"
  } else {
    "if [ -d $SourceQ/.git ]; then git -C $SourceQ fetch --all --tags && git -C $SourceQ checkout $BranchQ && git -C $SourceQ pull --ff-only; fi"
  }

$Script = @"
set -e
$MarkerExport
$CudaPreflight
$VulkanPreflight
$CloneBlock
$UpdateBlock
$CleanBlock
mkdir -p $BuildQ $InstallQ
generator_args=()
if command -v ninja >/dev/null 2>&1; then
  generator_args=(-G Ninja)
elif command -v ninja-build >/dev/null 2>&1; then
  generator_args=(-G Ninja -DCMAKE_MAKE_PROGRAM="`$(command -v ninja-build)")
fi
if [ `${#generator_args[@]} -gt 0 ]; then echo "Using CMake generator: Ninja"; fi
cmake -S $SourceQ -B $BuildQ "`${generator_args[@]}" $DefArgs "`${cuda_cmake_args[@]}" "`${vulkan_cmake_args[@]}"
build_status=0
cmake --build $BuildQ --config Release --target install --parallel `$(nproc) || build_status=`$?
server_path=$InstallQ/bin/llama-server
server_lib=$InstallQ/lib
if [ ! -f "`$server_path" ]; then
  echo "Build finished but llama-server was not installed at `$server_path." >&2
  if [ "`$build_status" -eq 0 ]; then build_status=1; fi
  exit `$build_status
fi
probe_ld_path="`$server_lib:`${LD_LIBRARY_PATH:-}"
if ! LD_LIBRARY_PATH="`$probe_ld_path" "`$server_path" --version >/dev/null 2>&1; then
  echo "Build finished but installed llama-server failed a startup smoke test." >&2
  if [ "`$build_status" -eq 0 ]; then build_status=1; fi
  exit `$build_status
fi
if [ "`$build_status" -ne 0 ]; then
  echo "Build command returned exit code `$build_status after installing and validating llama-server; continuing." >&2
fi
"@
  New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
  $BuildScriptPath = Join-Path $BuildDir ".local-llm-build.sh"
  $BuildScriptText = ($Script -replace "`r`n", "`n") -replace "`r", "`n"
  [System.IO.File]::WriteAllText($BuildScriptPath, $BuildScriptText, (New-Object System.Text.UTF8Encoding $false))
  $WslBuildScript = ConvertTo-WslPath $BuildScriptPath
  $BuildExitCode = 0
  try {
    Write-Host "> $(Redact-Argument $WslExe) -d $WslDistro -- bash <build>"
    & $WslExe -d $WslDistro -- bash $WslBuildScript
    $BuildExitCode = $LASTEXITCODE
  } finally {
    Remove-Item -LiteralPath $BuildScriptPath -Force -ErrorAction SilentlyContinue
  }
  if ($BuildExitCode -ne 0) {
    [Console]::Error.WriteLine("WSL build failed with exit code $BuildExitCode")
    exit $BuildExitCode
  }
  $Commit = (& $WslExe -d $WslDistro -- bash -lc "git -C $(Quote-Bash $WslSource) rev-parse --short=12 HEAD 2>/dev/null || echo latest").Trim()
  $RuntimePath = (Join-Path $InstallDir "bin") -replace '\\', '/'
  $WslRuntimePath = "$WslInstall/bin"
} else {
  if (-not (Test-Path -LiteralPath $SourceDir)) {
    if ([string]::IsNullOrWhiteSpace($RepoUrl)) { throw "SourceDir does not exist and RepoUrl is empty." }
    $CloneArgs = @("clone", "--depth", "1")
    if (-not [string]::IsNullOrWhiteSpace($Branch)) { $CloneArgs += @("--branch", $Branch) }
    $CloneArgs += @($RepoUrl, $SourceDir)
    Invoke-Logged $GitExe $CloneArgs
  }
  if (-not $NoUpdate -and -not [string]::IsNullOrWhiteSpace($RepoUrl) -and (Test-Path -LiteralPath (Join-Path $SourceDir ".git"))) {
    Invoke-Logged $GitExe @("-C", $SourceDir, "fetch", "--all", "--tags")
    if (-not [string]::IsNullOrWhiteSpace($Branch)) { Invoke-Logged $GitExe @("-C", $SourceDir, "checkout", $Branch) }
    Invoke-Logged $GitExe @("-C", $SourceDir, "pull", "--ff-only")
  }
  if ($Clean -and (Test-Path -LiteralPath $BuildDir)) {
    Remove-Item -LiteralPath $BuildDir -Recurse -Force
  }
  New-Item -ItemType Directory -Force -Path $BuildDir, $InstallDir | Out-Null
  $Defs = @(
    "-DCMAKE_BUILD_TYPE=Release",
    "-DLLAMA_BUILD_SERVER=ON",
    "-DLLAMA_BUILD_TESTS=OFF",
    "-DLLAMA_TESTS_INSTALL=OFF",
    "-DLLAMA_BUILD_EXAMPLES=OFF",
    "-DLLAMA_BUILD_APP=OFF",
    "-DCMAKE_INSTALL_PREFIX=$InstallDir"
  )
  if ($Cuda) { $Defs += "-DGGML_CUDA=ON" }
  if ($Vulkan) { $Defs += "-DGGML_VULKAN=ON" }
  $ConfigureArgs = @("-S", $SourceDir, "-B", $BuildDir) + $Defs
  Invoke-Logged $CMakeExe $ConfigureArgs
  Invoke-Logged $CMakeExe @("--build", $BuildDir, "--config", "Release", "--target", "install", "--parallel", [string][Environment]::ProcessorCount)
  $ServerExe = Join-Path $InstallDir "bin\llama-server.exe"
  if (-not (Test-Path -LiteralPath $ServerExe)) { throw "Build finished but llama-server.exe was not found at $ServerExe" }
  try {
    $Commit = (& $GitExe -C $SourceDir rev-parse --short=12 HEAD 2>$null).Trim()
  } catch {
    $Commit = "latest"
  }
  if ([string]::IsNullOrWhiteSpace($Commit)) { $Commit = "latest" }
  $RuntimePath = (Join-Path $InstallDir "bin") -replace '\\', '/'
  $WslRuntimePath = ""
}

$Flavor = if ($Cuda) { "cuda" } elseif ($Vulkan) { "vulkan" } else { "cpu" }
$Id = Normalize-Id "llama-cpp-$Commit-$Runtime-$Flavor"
$Metadata = [ordered]@{
  id = $Id
  name = "llama.cpp $Commit $Runtime $Flavor"
  runtime = $Runtime
  wslDistro = if ($Runtime -eq "wsl") { $WslDistro } else { "" }
  path = $RuntimePath
  wslPath = $WslRuntimePath
  repoUrl = $RepoUrl
  branch = $Branch
  sourcePath = ($SourceDir -replace '\\', '/')
  buildPath = ($BuildDir -replace '\\', '/')
  commit = $Commit
  build = "$Runtime-$Flavor"
  versionText = $Commit
  tags = @("built", $Runtime, $Flavor)
  requiredFlags = @()
}

$MetadataPath = Join-Path $InstallDir "local-llm-runtime.json"
$Metadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $MetadataPath -Encoding UTF8
Write-Host "Wrote runtime metadata: $MetadataPath"
