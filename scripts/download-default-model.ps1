# 初回のみ: models/ が空のとき既定 GGUF を Hugging Face から取得（LocalCompanion.exe 起動時に自動実行）
# 既定ファイル名は配布用ブートストラップ。差し替えはこのスクリプトを編集するか models/ に手動配置
# 一度マーカーを書いたら、ユーザーが削除・差し替えしても再 DL しない
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ModelsDir = Join-Path $Root "models"
$MarkerPath = Join-Path $ModelsDir ".default-model-bootstrap.json"
$DefaultFileName = "gemma-4-E2B_q4_0-it.gguf"
$DefaultUrl = "https://huggingface.co/google/gemma-4-E2B-it-qat-q4_0-gguf/resolve/main/gemma-4-E2B_q4_0-it.gguf?download=true"

function Get-ChatGgufCount {
    @(Get-ChildItem -Path $ModelsDir -Filter "*.gguf" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch '^mmproj' }).Count
}

function Write-BootstrapMarker([string]$status, [string]$detail = "") {
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    @{
        status = $status
        fileName = $DefaultFileName
        detail = $detail
        at = (Get-Date).ToString("o")
    } | ConvertTo-Json | Set-Content -Path $MarkerPath -Encoding UTF8
}

if (Test-Path $MarkerPath) {
    exit 0
}

$dest = Join-Path $ModelsDir $DefaultFileName
if (Test-Path $dest) {
    Write-BootstrapMarker "already_present"
    exit 0
}

if ((Get-ChatGgufCount) -gt 0) {
    Write-BootstrapMarker "skipped_existing_models"
    Write-Host "[..] models/ に既存 GGUF あり — 既定モデルの自動 DL はスキップ（初回のみ）" -ForegroundColor DarkGray
    exit 0
}

Write-Host "[..] 初回セットアップ: 既定モデルをダウンロードします" -ForegroundColor Cyan
Write-Host "     $DefaultFileName" -ForegroundColor Cyan
Write-Host "     （数 GB あり・初回のみ。時間がかかります）" -ForegroundColor DarkGray

try {
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    Invoke-WebRequest -Uri $DefaultUrl -OutFile $dest -UseBasicParsing
    if (-not (Test-Path $dest) -or (Get-Item $dest).Length -lt 1MB) {
        throw "ダウンロードファイルが不正です"
    }
    Write-BootstrapMarker "downloaded"
    Write-Host "[OK] 既定モデルを配置しました: models\$DefaultFileName" -ForegroundColor Green
    exit 0
} catch {
    Write-Host "[!!] 既定モデルのダウンロードに失敗: $_" -ForegroundColor Red
    Write-Host "     手動で models/ に .gguf を置くか、ネット接続を確認して LocalCompanion.exe を再起動" -ForegroundColor Yellow
    if (Test-Path $dest) { Remove-Item $dest -Force -ErrorAction SilentlyContinue }
    exit 1
}
