# 利用者向け ZIP（publish 出力フォルダ丸ごと）を dist に作成
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$DistDir = Join-Path $Root "dist\LocalCompanion"

# csproj の <Version> を ZIP 名に使う（アプリ内表示・exe プロパティと一致させる）
$csprojXml = [xml](Get-Content (Join-Path $Root "LocalCompanion.csproj") -Raw)
$Version = ($csprojXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $Version) { $Version = "0.0.0" }
$ZipPath = Join-Path $Root "dist\LocalCompanion-$Version-user.zip"

function Test-DistributionFolder {
    param([string]$Folder)

    if (-not (Test-Path $Folder)) {
        throw "配布フォルダがありません: $Folder"
    }

    $required = @(
        "LocalCompanion.exe",
        "appsettings.json",
        "vec0.dll",
        "Assets\AppIcon.ico",
        "scripts\install-llama-cpp.ps1",
        "scripts\llama-server.ps1",
        "scripts\stop-llama.ps1",
        "models\README.md",
        "characters\README.md",
        "LICENSE",
        "THIRD-PARTY-NOTICES.txt",
        "CHANGELOG.md",
        "docs\Troubleshooting.md",
        "docs\help\help.css",
        "docs\help\app-icon.png",
        "docs\help\licenses.ja.html",
        "docs\help\licenses.en.html",
        "docs\help\troubleshooting.ja.html",
        "docs\help\troubleshooting.en.html"
    )

    foreach ($rel in $required) {
        $full = Join-Path $Folder $rel
        if (-not (Test-Path $full)) {
            throw "配布フォルダに必要なファイルがありません: $rel"
        }
    }
}

Write-Host "=== package user ZIP ===" -ForegroundColor Cyan

& (Join-Path $Root "scripts\publish-win.ps1") -Configuration $Configuration -Platform $Platform -AlsoDist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Test-DistributionFolder -Folder $DistDir

$stageParent = Join-Path $Root "dist"
$zipItem = Join-Path $stageParent "LocalCompanion"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

# publish 直後はウイルス対策のスキャンで一時的にファイルが開かれることがあるためリトライする
$maxAttempts = 3
for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    try {
        Compress-Archive -Path $zipItem -DestinationPath $ZipPath -Force -ErrorAction Stop
        break
    }
    catch {
        if ($attempt -eq $maxAttempts) { throw }
        Write-Host "[..] 圧縮リトライ ($attempt/$maxAttempts): $($_.Exception.Message)" -ForegroundColor Yellow
        if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 10
    }
}

$sizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "[OK] $ZipPath (${sizeMb} MB)" -ForegroundColor Green
Write-Host ""
Write-Host "利用者手順:" -ForegroundColor Cyan
Write-Host "  1. ZIP を任意のフォルダに解凍" -ForegroundColor DarkGray
Write-Host "  2. 解凍先の LocalCompanion\LocalCompanion.exe を起動" -ForegroundColor DarkGray
Write-Host "  3. .NET 10 Desktop Runtime (x64) が必要（未インストールなら https://dotnet.microsoft.com/download/dotnet/10.0 ）" -ForegroundColor DarkGray
Write-Host "  4. 初回起動で llama.cpp / 既定モデルを自動 DL（ネット接続・空き容量が必要）" -ForegroundColor DarkGray
