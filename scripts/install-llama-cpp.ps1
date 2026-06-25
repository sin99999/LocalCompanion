# llama.cpp 公式ビルドを tools/llama-cpp に配置（初回セットアップ）
param(
    [ValidateSet("auto", "cuda", "cuda13", "cuda12", "cpu", "vulkan", "hip-radeon", "opencl-adreno")]
    [string]$Variant = "auto"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ToolsDir = Join-Path $Root "tools\llama-cpp"
$Marker = Join-Path $ToolsDir ".installed.json"

# 既知の不良ビルド（バージョン番号の固定優先はしない）
$Script:ExcludedCudaVersions = @("13.2")

function Find-LlamaServerExe {
    Get-ChildItem -Path $ToolsDir -Filter "llama-server.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}

$existing = Find-LlamaServerExe
if ($existing) {
    Write-Host "[OK] すでにインストール済み: $existing" -ForegroundColor Green
    exit 0
}

function Test-NvidiaGpu {
    try {
        $null = & nvidia-smi -L 2>$null
        return $LASTEXITCODE -eq 0
    } catch { return $false }
}

function Get-DisplayAdapterNames {
    $names = @()
    $keyPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
    if (Test-Path $keyPath) {
        Get-ChildItem $keyPath -ErrorAction SilentlyContinue | ForEach-Object {
            $desc = (Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue).DriverDesc
            if ($desc) { $names += [string]$desc }
        }
    }
    return $names
}

function Test-AmdRadeonGpu {
    $names = Get-DisplayAdapterNames
    return [bool]($names | Where-Object {
        $_ -match 'AMD|Radeon|ATI'
    } | Select-Object -First 1)
}

function Test-QualcommAdreno {
    $id = $env:PROCESSOR_IDENTIFIER
    if ($id -match 'Qualcomm|Snapdragon') { return $true }
    $names = Get-DisplayAdapterNames
    return [bool]($names | Where-Object { $_ -match 'Adreno|Qualcomm' } | Select-Object -First 1)
}

function Test-VulkanCapableGpu {
    $names = Get-DisplayAdapterNames
    return [bool]($names | Where-Object {
        $_ -notmatch 'Microsoft Basic Display' -and $_ -match 'NVIDIA|GeForce|AMD|Radeon|Intel|Arc|Adreno'
    } | Select-Object -First 1)
}

$isArm64 = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -in @(
    [System.Runtime.InteropServices.Architecture]::Arm64,
    [System.Runtime.InteropServices.Architecture]::Arm
)

if ($Variant -eq "auto") {
    if ($isArm64) {
        if (Test-QualcommAdreno) { $Variant = "opencl-adreno" } else { $Variant = "cpu" }
    }
    elseif (Test-NvidiaGpu) { $Variant = "cuda" }
    elseif (Test-AmdRadeonGpu) { $Variant = "hip-radeon" }
    elseif (Test-VulkanCapableGpu) { $Variant = "vulkan" }
    else { $Variant = "cpu" }
}
if ($Variant -eq "cuda13") { $Variant = "cuda" }

$release = Invoke-RestMethod "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest"
$tag = $release.tag_name
$assetNames = @($release.assets | ForEach-Object { $_.name })
$base = "https://github.com/ggml-org/llama.cpp/releases/download/$tag"

function Get-LatestCudaAsset([string]$namePattern, [int]$MajorFilter = 0) {
    $rx = [regex]::new($namePattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $bestName = $null
    $bestVer = $null
    foreach ($name in $assetNames) {
        $m = $rx.Match($name)
        if (-not $m.Success) { continue }
        if ($m.Groups["tag"].Value -ne $tag) { continue }
        $verText = $m.Groups["ver"].Value
        if ($Script:ExcludedCudaVersions -contains $verText) { continue }
        $ver = [version]$verText
        if ($MajorFilter -gt 0 -and $ver.Major -ne $MajorFilter) { continue }
        if ($null -eq $bestVer -or $ver -gt $bestVer) {
            $bestVer = $ver
            $bestName = $name
        }
    }
    return $bestName
}

$cudaVersion = $null
$mainZip = $null
$cudaDllZip = $null

switch ($Variant) {
    { $_ -in "cuda", "cuda12" } {
        $major = if ($Variant -eq "cuda12") { 12 } else { 0 }
        $mainZip = Get-LatestCudaAsset "^llama-(?<tag>.+)-bin-win-cuda-(?<ver>\d+\.\d+)-x64\.zip$" -MajorFilter $major
        if ($mainZip) {
            if ($mainZip -match "cuda-(\d+\.\d+)-") { $cudaVersion = $Matches[1] }
            $cudaDllZip = $assetNames | Where-Object { $_ -ieq "cudart-llama-bin-win-cuda-$cudaVersion-x64.zip" } | Select-Object -First 1
        }
    }
    "vulkan" { $mainZip = $assetNames | Where-Object { $_ -ieq "llama-$tag-bin-win-vulkan-x64.zip" } | Select-Object -First 1 }
    "hip-radeon" { $mainZip = $assetNames | Where-Object { $_ -ieq "llama-$tag-bin-win-hip-radeon-x64.zip" } | Select-Object -First 1 }
    "opencl-adreno" { $mainZip = $assetNames | Where-Object { $_ -ieq "llama-$tag-bin-win-opencl-adreno-arm64.zip" } | Select-Object -First 1 }
    default {
        $arch = if ($isArm64) { "arm64" } else { "x64" }
        $mainZip = $assetNames | Where-Object { $_ -ieq "llama-$tag-bin-win-cpu-$arch.zip" } | Select-Object -First 1
    }
}

if (-not $mainZip) {
    Write-Host "[!!] リリースに合う ZIP が見つかりません (variant=$Variant, tag=$tag)" -ForegroundColor Red
    exit 1
}

if ($cudaVersion) {
    Write-Host "[..] CUDA ビルド（リリース内最新）: $cudaVersion" -ForegroundColor Cyan
}

New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
$tmp = Join-Path $env:TEMP "llama-cpp-install-$tag"
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function Download-Zip($name) {
    $url = "$base/$name"
    $dest = Join-Path $tmp $name
    Write-Host "[..] 取得: $name" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
    Expand-Archive -Path $dest -DestinationPath $ToolsDir -Force
}

Download-Zip $mainZip
if ($cudaDllZip) { Download-Zip $cudaDllZip }

$exe = Find-LlamaServerExe
if (-not $exe) {
    Write-Host "[!!] llama-server.exe が見つかりません（展開先: $ToolsDir）" -ForegroundColor Red
    exit 1
}

@{
    tag = $tag
    variant = $Variant
    cudaVersion = $cudaVersion
    exe = $exe
    installedAt = (Get-Date).ToString("o")
} | ConvertTo-Json | Set-Content -Path $Marker -Encoding UTF8

Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "[OK] インストール完了: $exe" -ForegroundColor Green
Write-Host "     variant: $Variant ($tag)" -ForegroundColor DarkGray
