# LocalCompanion の配布用 publish（exe 直起動・言語リソースは ja/en のみ）
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputDir = "",
    [switch]$AlsoDist,
    # 302MB 級の完全自己完結（.NET + WinApp SDK 同梱）。通常の公開 ZIP では使わない。
    [switch]$BundleAllRuntimes
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Proj = Join-Path $Root "LocalCompanion.csproj"

if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\win-x64"
}

$DistDir = Join-Path $Root "dist\LocalCompanion"

function Remove-ExtraLocaleFolders {
    param([string]$PublishRoot)

    $keep = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @("en", "en-us", "en-GB", "ja", "ja-JP")) {
        [void]$keep.Add($name)
    }

    $appDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @("scripts", "models", "characters", "Microsoft.UI.Xaml")) {
        [void]$appDirs.Add($name)
    }

    foreach ($dir in Get-ChildItem -LiteralPath $PublishRoot -Directory) {
        if ($appDirs.Contains($dir.Name)) { continue }
        if ($dir.Name -notmatch '^[a-z]{2,3}(-[A-Za-z0-9]+)*$') { continue }
        if ($keep.Contains($dir.Name)) { continue }
        Remove-Item -LiteralPath $dir.FullName -Recurse -Force
    }
}

function Remove-PublishArtifacts {
    param([string]$PublishRoot)

    foreach ($pattern in @("*.pdb", "*.build.appxrecipe")) {
        Get-ChildItem -LiteralPath $PublishRoot -Recurse -Filter $pattern -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }
}

function Copy-DistributionDocs {
    param([string]$PublishRoot)

    foreach ($rel in @("THIRD-PARTY-NOTICES.txt", "CHANGELOG.md")) {
        $src = Join-Path $Root $rel
        if (-not (Test-Path $src)) {
            throw "配布用ドキュメントがありません: $rel"
        }
        Copy-Item $src (Join-Path $PublishRoot $rel) -Force
    }

    $docsSrc = Join-Path $Root "docs\Troubleshooting.md"
    if (-not (Test-Path $docsSrc)) {
        throw "配布用ドキュメントがありません: docs\Troubleshooting.md"
    }
    $docsDir = Join-Path $PublishRoot "docs"
    New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
    Copy-Item $docsSrc (Join-Path $docsDir "Troubleshooting.md") -Force

    $helpSrc = Join-Path $Root "docs\help"
    if (-not (Test-Path $helpSrc)) {
        throw "配布用ドキュメントがありません: docs\help"
    }
    $helpDest = Join-Path $PublishRoot "docs\help"
    New-Item -ItemType Directory -Force -Path $helpDest | Out-Null
    Copy-Item (Join-Path $helpSrc "*") $helpDest -Recurse -Force
}

function Test-PublicAppsettings {
    param([string]$SettingsPath)

    if (-not (Test-Path $SettingsPath)) {
        throw "appsettings.json がありません: $SettingsPath"
    }

    $json = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $lc = $json.LlamaCompanion
    $vv = $json.Voicevox
    $pathKeys = @(
        @{ Section = "LlamaCompanion"; Name = "DataDirectory"; Value = $lc.DataDirectory },
        @{ Section = "LlamaCompanion"; Name = "ModelsDirectory"; Value = $lc.ModelsDirectory },
        @{ Section = "LlamaCompanion"; Name = "CharactersDirectory"; Value = $lc.CharactersDirectory },
        @{ Section = "LlamaCompanion"; Name = "ModelGgufPath"; Value = $lc.ModelGgufPath },
        @{ Section = "LlamaCompanion"; Name = "MmprojGgufPath"; Value = $lc.MmprojGgufPath },
        @{ Section = "Voicevox"; Name = "EngineExePath"; Value = $vv.EngineExePath }
    )

    foreach ($entry in $pathKeys) {
        $value = [string]$entry.Value
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        if ($value -match '^[a-zA-Z]:\\|^\\\\') {
            throw "公開向け appsettings に絶対パスが含まれています ($($entry.Section).$($entry.Name)): $value"
        }
    }
}

Write-Host "=== publish LocalCompanion WinUI ($Platform / $Configuration) ===" -ForegroundColor Cyan
Write-Host "出力: $OutputDir" -ForegroundColor DarkGray
if ($BundleAllRuntimes) {
    Write-Host "モード: 完全自己完結 (.NET + WinApp SDK 同梱)" -ForegroundColor Yellow
}
else {
    Write-Host "モード: .NET 10 Desktop Runtime 前提 + WinApp SDK 同梱" -ForegroundColor DarkGray
}

Test-PublicAppsettings -SettingsPath (Join-Path $Root "appsettings.json")

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$publishArgs = @(
    "publish", $Proj,
    "-c", $Configuration,
    "-p:Platform=$Platform",
    "-p:PublishTrimmed=false",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-o", $OutputDir
)

if ($BundleAllRuntimes) {
    $publishArgs += @("-p:SelfContained=true", "-p:WindowsAppSDKSelfContained=true")
}
else {
    $publishArgs += @("-p:SelfContained=false", "-p:WindowsAppSDKSelfContained=true")
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$built = Join-Path $OutputDir "LocalCompanion.exe"
if (-not (Test-Path $built)) {
    Write-Host "[!!] publish に失敗しました: $built" -ForegroundColor Red
    exit 1
}

Remove-ExtraLocaleFolders -PublishRoot $OutputDir
Remove-PublishArtifacts -PublishRoot $OutputDir
Copy-DistributionDocs -PublishRoot $OutputDir
Test-PublicAppsettings -SettingsPath (Join-Path $OutputDir "appsettings.json")

$iconPath = Join-Path $OutputDir "Assets\AppIcon.ico"
if (-not (Test-Path $iconPath)) {
    throw "publish 出力にアイコンがありません: Assets\AppIcon.ico"
}

$fileCount = (Get-ChildItem $OutputDir -Recurse -File).Count
$sizeMb = [math]::Round(((Get-ChildItem $OutputDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "[OK] $built" -ForegroundColor Green
Write-Host "[OK] $fileCount files, ${sizeMb} MB (locales en/ja)" -ForegroundColor DarkGray

if ($AlsoDist) {
    if (Test-Path $DistDir) { Remove-Item $DistDir -Recurse -Force }
    Copy-Item $OutputDir $DistDir -Recurse -Force
    Write-Host "[OK] dist → $DistDir" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "起動: publish フォルダ内の LocalCompanion.exe（同フォルダに scripts / models / characters が必要）" -ForegroundColor Green
Write-Host "公開 ZIP: .\scripts\package-user-zip.ps1" -ForegroundColor Cyan
if (-not $BundleAllRuntimes) {
    Write-Host "前提: .NET 10 Desktop Runtime（https://dotnet.microsoft.com/download/dotnet/10.0）" -ForegroundColor DarkGray
}
