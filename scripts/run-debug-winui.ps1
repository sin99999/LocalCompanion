# WinUI 開発版を起動（exe 直ダブルクリックは WASDK 未登録で落ちるため winapp 経由）
param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Out = Join-Path $Root "bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64"
$Exe = Join-Path $Out "LocalCompanion.exe"

$needsBuild = $Build -or -not (Test-Path $Exe)
if ($needsBuild) {
    if ($Build) {
        Write-Host "[..] -Build 指定のためビルドしています…" -ForegroundColor Yellow
    } else {
        Write-Host "[..] exe が無いためビルドしています…" -ForegroundColor Yellow
    }
    dotnet build (Join-Path $Root "LocalCompanion.csproj") -c Debug -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "起動: winapp run $Out" -ForegroundColor Cyan
winapp run $Out --detach
if ($LASTEXITCODE -ne 0) {
    Write-Host "[!!] winapp が失敗しました。Developer Mode と winapp CLI を確認してください。" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[OK] 起動しました（タスクバーにウィンドウが出ます）" -ForegroundColor Green
