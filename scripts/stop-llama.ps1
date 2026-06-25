# LocalCompanion が起動した llama-server を停止する（アプリ終了時・手動実行用）
param([switch]$Force)

$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Marker = Join-Path $Root "tools\llama-cpp\.localcompanion-managed"
$LegacyMarkers = @(
    (Join-Path $Root "tools\llama-cpp\.legacy-managed")
    (Join-Path $Root "tools\llama-cpp\.new2-managed")
)

function Stop-LlamaServerProcesses {
    $procs = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
    if (-not $procs) { return $false }
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    return $true
}

function Test-ManagedMarker([string]$path) {
    if (-not (Test-Path $path)) { return $false }
    try {
        return (Get-Content $path -Raw).Trim() -eq "1"
    } catch {
        return $false
    }
}

$shouldStop = $Force
if (-not $shouldStop) {
    $shouldStop = (Test-ManagedMarker $Marker)
    if (-not $shouldStop) {
        foreach ($legacy in $LegacyMarkers) {
            if (Test-ManagedMarker $legacy) {
                $shouldStop = $true
                break
            }
        }
    }
}

if ($shouldStop) {
    if (Stop-LlamaServerProcesses) {
        Write-Host "[OK] llama-server を終了しました" -ForegroundColor Green
    }
    Remove-Item $Marker -Force -ErrorAction SilentlyContinue
    foreach ($legacy in $LegacyMarkers) {
        Remove-Item $legacy -Force -ErrorAction SilentlyContinue
    }
}
