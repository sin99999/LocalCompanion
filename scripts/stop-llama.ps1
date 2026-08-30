# LocalCompanion が起動した llama-server を停止する（アプリ終了時・手動実行用）
# 既定は記録 PID（.localcompanion-llama-pid）を止める。-Force のときだけ名前で全 llama-server を止める。
param([switch]$Force)

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ToolsDir = Join-Path $Root "tools\llama-cpp"
$Marker = Join-Path $ToolsDir ".localcompanion-managed"
$PidFile = Join-Path $ToolsDir ".localcompanion-llama-pid"
$LegacyMarkers = @(
    (Join-Path $ToolsDir ".legacy-managed")
    (Join-Path $ToolsDir ".new2-managed")
)

function Test-ManagedMarker([string]$path) {
    if (-not (Test-Path $path)) { return $false }
    try {
        return (Get-Content $path -Raw).Trim() -eq "1"
    } catch {
        return $false
    }
}

function Stop-TrackedPid {
    if (-not (Test-Path $PidFile)) { return $false }
    try {
        $raw = (Get-Content $PidFile -Raw).Trim()
        $id = 0
        if (-not [int]::TryParse($raw, [ref]$id) -or $id -le 0) { return $false }
        $proc = Get-Process -Id $id -ErrorAction SilentlyContinue
        if (-not $proc) { return $false }
        if ($proc.ProcessName -ne "llama-server") { return $false }
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        return $true
    } catch {
        return $false
    }
}

function Stop-LlamaServerProcesses {
    $procs = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
    if (-not $procs) { return $false }
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    return $true
}

$hasMarker = (Test-ManagedMarker $Marker)
if (-not $hasMarker) {
    foreach ($legacy in $LegacyMarkers) {
        if (Test-ManagedMarker $legacy) {
            $hasMarker = $true
            break
        }
    }
}

$hasPid = Test-Path $PidFile
$shouldStop = $Force -or $hasMarker -or $hasPid

if ($shouldStop) {
    $stopped = Stop-TrackedPid
    if ($Force) {
        if (Stop-LlamaServerProcesses) { $stopped = $true }
    } elseif (-not $stopped -and $hasMarker) {
        if (Stop-LlamaServerProcesses) { $stopped = $true }
    }

    if ($stopped) {
        Write-Host "[OK] llama-server を終了しました" -ForegroundColor Green
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
    Remove-Item $Marker -Force -ErrorAction SilentlyContinue
    foreach ($legacy in $LegacyMarkers) {
        Remove-Item $legacy -Force -ErrorAction SilentlyContinue
    }
}
