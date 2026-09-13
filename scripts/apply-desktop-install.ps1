# 手元で ZIP を焼いたあとの作業用。公開の説明・利用者向け文書には出さない。
# data / tools / 個人の設定・モデル・キャラは消さない。
param(
    [string]$SourceDir = "",
    [switch]$SkipStop
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $SourceDir) {
    $SourceDir = Join-Path $Root "dist\LocalCompanion"
}

function Test-LcInstallDir {
    param([string]$Dir)

    if ([string]::IsNullOrWhiteSpace($Dir) -or -not (Test-Path -LiteralPath $Dir)) {
        return $false
    }

    $exe = Join-Path $Dir "LocalCompanion.exe"
    $scripts = Join-Path $Dir "scripts"
    return (Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath $scripts)
}

function Get-DesktopLcInstalls {
    $desktop = [Environment]::GetFolderPath("Desktop")
    if ([string]::IsNullOrWhiteSpace($desktop) -or -not (Test-Path -LiteralPath $desktop)) {
        return @()
    }

    $found = New-Object System.Collections.Generic.List[string]
    foreach ($item in Get-ChildItem -LiteralPath $desktop -Directory -Force -ErrorAction SilentlyContinue) {
        if (Test-LcInstallDir $item.FullName) {
            $found.Add($item.FullName)
        }

        $nested = Join-Path $item.FullName "LocalCompanion"
        if (Test-LcInstallDir $nested) {
            $found.Add($nested)
        }
    }

    return $found.ToArray()
}

function Stop-LcAtInstall {
    param([string]$InstallDir)

    $procs = Get-Process -Name "LocalCompanion" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase)
        }

    foreach ($proc in $procs) {
        Write-Host "[..] 起動中の LC を止める: PID $($proc.Id)" -ForegroundColor Yellow
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    if ($procs) {
        Start-Sleep -Seconds 1
    }
}

function Copy-DistOntoInstall {
    param(
        [string]$Source,
        [string]$Dest
    )

    # /MIR は使わない（個人ファイルを消す）。
    & robocopy $Source $Dest /E /IS /IT /NFL /NDL /NJH /NJS /NP /R:2 /W:1 `
        /XD data tools `
        /XF appsettings.json appsettings.local.json selection.json `
        .default-model-bootstrap.json .mmproj-bootstrap.json *.db *.gguf
    $code = $LASTEXITCODE
    if ($code -ge 8) {
        throw "デスクトップへのコピーに失敗しました (robocopy $code): $Dest"
    }
}

if (-not (Test-Path -LiteralPath $SourceDir)) {
    Write-Host "[..] dist が無いのでデスクトップ適用をスキップ: $SourceDir" -ForegroundColor DarkGray
    exit 0
}

$exe = Join-Path $SourceDir "LocalCompanion.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "[..] dist に exe が無いのでデスクトップ適用をスキップ" -ForegroundColor DarkGray
    exit 0
}

$installs = @(Get-DesktopLcInstalls)
if ($installs.Count -eq 0) {
    Write-Host "[..] デスクトップに LC の解凍フォルダは無かった" -ForegroundColor DarkGray
    exit 0
}

Write-Host "=== apply desktop LC ===" -ForegroundColor Cyan
foreach ($install in $installs) {
    if (-not $SkipStop) {
        Stop-LcAtInstall -InstallDir $install
    }

    Copy-DistOntoInstall -Source $SourceDir -Dest $install
    Write-Host "[OK] デスクトップへ適用: $install" -ForegroundColor Green
}

exit 0
