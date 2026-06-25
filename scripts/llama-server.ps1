# llama-server を --mmproj 付きで起動（vision + チャット）
param(
    [string]$Model = "",
    [string]$Mmproj = "",
    [int]$Port = 8080,
    [int]$Context = 0,
    [int]$GpuLayers = 99
)

$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$SettingsPath = Join-Path $Root "appsettings.json"

function Get-JsonPath($key) {
    if (-not (Test-Path $SettingsPath)) { return $null }
    try {
        $j = Get-Content $SettingsPath -Raw | ConvertFrom-Json
        return $j.LlamaCompanion.$key
    } catch { return $null }
}

function Format-ProcessArguments([string[]]$tokens) {
    ($tokens | ForEach-Object {
        $t = "$_"
        if ($t -match '[\s"]') { '"' + ($t -replace '"', '""') + '"' } else { $t }
    }) -join ' '
}

function Test-LlamaModelReady([int]$Port = 8080) {
    try {
        $models = Invoke-RestMethod "http://127.0.0.1:$Port/v1/models" -TimeoutSec 3
        if ($models.data -and @($models.data).Count -gt 0) { return $true }
    } catch { }

    try {
        $props = Invoke-RestMethod "http://127.0.0.1:$Port/props" -TimeoutSec 3
        if ($props.model_path -and "$($props.model_path)" -ne "none") { return $true }
    } catch { }

    return $false
}

function Start-LlamaServerProcessHidden(
    [string]$ExePath,
    [string[]]$ServerArguments,
    [string]$WorkDir,
    [string]$LogPath
) {
    if (Test-Path $LogPath) { Remove-Item $LogPath -Force -ErrorAction SilentlyContinue }
    $errLog = "$LogPath.err"

    try {
        Start-Process -FilePath $ExePath -ArgumentList $ServerArguments -WorkingDirectory $WorkDir `
            -WindowStyle Hidden -RedirectStandardOutput $LogPath -RedirectStandardError $errLog `
            -ErrorAction Stop | Out-Null
        return
    } catch {
        Write-Host "[..] 非表示起動を CreateNoWindow で再試行します" -ForegroundColor DarkYellow
    }

    $si = New-Object System.Diagnostics.ProcessStartInfo
    $si.FileName = $ExePath
    $si.Arguments = (Format-ProcessArguments $ServerArguments)
    $si.WorkingDirectory = $WorkDir
    $si.CreateNoWindow = $true
    $si.UseShellExecute = $false
    [void][System.Diagnostics.Process]::Start($si)
}

$ModelsDir = Join-Path $Root "models"
$SelectionPath = Join-Path $ModelsDir "selection.json"

function Get-MmprojCandidates([string]$Dir) {
    @(Get-ChildItem -Path $Dir -Filter '*.gguf' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'mmproj*' -or $_.Name -like '*-mmproj.gguf' })
}

function Test-IsE2BMmproj([string]$Name) {
    return ($Name -ieq 'gemma-4-E2B-it-mmproj.gguf') -or ($Name -match '^mmproj-(F16|BF16)\.gguf$')
}

function Find-MmprojForModel([string]$ModelFileName, [string]$Dir) {
    $candidates = @(Get-MmprojCandidates $Dir)
    if ($candidates.Count -eq 0) { return $null }

    $want26 = $ModelFileName -match '26B'
    $wantE2 = $ModelFileName -match 'E2B'
    $wantE4 = ($ModelFileName -match 'E4B') -or (($ModelFileName -match '4B') -and -not $wantE2)

    if ($want26) {
        return ($candidates | Where-Object { $_.Name -match '26B' } | Select-Object -First 1)
    }
    if ($wantE2) {
        $google = $candidates | Where-Object { $_.Name -ieq 'gemma-4-E2B-it-mmproj.gguf' } | Select-Object -First 1
        if ($google) { return $google }
        foreach ($name in @('mmproj-F16.gguf', 'mmproj-BF16.gguf')) {
            $hit = $candidates | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if ($hit) { return $hit }
        }
        return ($candidates | Where-Object {
                $_.Name -notmatch 'E4B|26B|Uncensored' -and $_.Name -match '^mmproj-(F16|BF16)\.gguf$'
            } | Select-Object -First 1)
    }
    if ($wantE4) {
        return ($candidates | Where-Object { $_.Name -match 'E4B' -and $_.Name -notmatch '26B' } | Select-Object -First 1)
    }
    return $null
}

function Get-SelectionPaths {
    if (-not (Test-Path $SelectionPath)) { return $null }
    try {
        $s = Get-Content $SelectionPath -Raw | ConvertFrom-Json
        $modelName = $s.ModelFileName
        if (-not $modelName) { $modelName = $s.model }
        $mmprojName = $s.MmprojFileName
        if (-not $mmprojName) { $mmprojName = $s.mmproj }

        $m = $null
        $p = $null
        if ($modelName) {
            $mp = Join-Path $ModelsDir $modelName
            if (Test-Path $mp) { $m = $mp }
        }
        if ($mmprojName) {
            $pp = Join-Path $ModelsDir $mmprojName
            if (Test-Path $pp) { $p = $pp }
        }
        if ($m -and $p) {
            $mb = [System.IO.Path]::GetFileName($m)
            $pb = [System.IO.Path]::GetFileName($p)
            $m26 = $mb -match '26B'
            $p26 = $pb -match '26B'
            $mE2 = $mb -match 'E2B'
            $pE2 = $pb -match '^mmproj-(F16|BF16)\.gguf$'
            $mE4 = ($mb -match 'E4B') -or (($mb -match '4B') -and -not $mE2)
            $pE4 = $pb -match 'E4B'
            if (($m26 -and $pE4 -and -not $p26) -or ($mE4 -and $p26 -and -not $m26) -or ($mE2 -and $pE4) -or ($mE2 -and $p -and -not $pE2 -and $pb -notmatch '^mmproj-(F16|BF16)\.gguf$')) {
                Write-Host "[!!] selection.json の mmproj が本体と不一致 → 自動で探します" -ForegroundColor Yellow
                $p = $null
            }
        }
        if ($m -and -not $p) {
            $mb = [System.IO.Path]::GetFileName($m)
            $found = Find-MmprojForModel $mb $ModelsDir
            if ($found) {
                $p = $found.FullName
                Write-Host "[..] mmproj 自動選択: $($found.Name)" -ForegroundColor Cyan
            }
        }
        if ($m) { return @{ Model = $m; Mmproj = $p } }
    } catch { }
    return $null
}

if (-not $Model -or -not $Mmproj) {
    $sel = Get-SelectionPaths
    if ($sel) {
        if (-not $Model) { $Model = $sel.Model }
        if (-not $Mmproj) { $Mmproj = $sel.Mmproj }
    }
}

if (-not $Model) { $Model = Get-JsonPath "ModelGgufPath" }
if (-not $Mmproj) { $Mmproj = Get-JsonPath "MmprojGgufPath" }

function Get-UserDataDirectory {
    $configured = Get-JsonPath "DataDirectory"
    if (-not [string]::IsNullOrWhiteSpace($configured)) {
        $trimmed = $configured.Trim()
        if ([System.IO.Path]::IsPathRooted($trimmed)) { return $trimmed }
        return (Join-Path $Root $trimmed)
    }
    return Join-Path $Root "data"
}

function Get-CharacterContext {
    $dataDir = Get-UserDataDirectory
    $charPath = Join-Path $dataDir "character-settings.json"
    if (-not (Test-Path $charPath)) { return $null }
    try {
        $j = Get-Content $charPath -Raw | ConvertFrom-Json
        if ($j.contextLength) { return [int]$j.contextLength }
    } catch { }
    return $null
}

if ($Context -le 0) {
    $ctxChar = Get-CharacterContext
    if ($ctxChar) { $Context = $ctxChar }
    else {
        $ctxCfg = Get-JsonPath "ContextLength"
        $Context = if ($ctxCfg) { [int]$ctxCfg } else { 8192 }
    }
}

function Resolve-GgufPath([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return $p }
    if ([System.IO.Path]::IsPathRooted($p)) { return $p }
    $fromRoot = Join-Path $Root $p
    if (Test-Path $fromRoot) { return $fromRoot }
    return $p
}

$Model = Resolve-GgufPath $Model
$Mmproj = Resolve-GgufPath $Mmproj

function Resolve-ModelAndMmproj([string]$ModelPath, [string]$MmprojPath) {
    if (-not [string]::IsNullOrWhiteSpace($ModelPath) -and (Test-Path $ModelPath)) {
        return @{ Model = $ModelPath; Mmproj = $MmprojPath }
    }

    $chat = @(Get-ChildItem -Path $ModelsDir -Filter "*.gguf" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch '^mmproj' })
    if ($chat.Count -ne 1) {
        return @{ Model = $ModelPath; Mmproj = $MmprojPath }
    }

    $chosen = $chat[0]
    $mmprojPath = $MmprojPath
    if ([string]::IsNullOrWhiteSpace($mmprojPath) -or -not (Test-Path $mmprojPath)) {
        $found = Find-MmprojForModel $chosen.Name $ModelsDir
        if ($found) { $mmprojPath = $found.FullName }
    }

    Write-Host "[..] モデル自動選択: $($chosen.Name)" -ForegroundColor Cyan
    if ($mmprojPath -and (Test-Path $mmprojPath)) {
        Write-Host "[..] mmproj 自動選択: $([System.IO.Path]::GetFileName($mmprojPath))" -ForegroundColor Cyan
    }

    try {
        New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
        $selObj = @{ ModelFileName = $chosen.Name }
        if ($mmprojPath -and (Test-Path $mmprojPath)) {
            $selObj.MmprojFileName = [System.IO.Path]::GetFileName($mmprojPath)
        }
        $selObj | ConvertTo-Json | Set-Content -Path $SelectionPath -Encoding UTF8
    } catch { }

    return @{ Model = $chosen.FullName; Mmproj = $mmprojPath }
}

function Find-LlamaServer {
    if ($env:LLAMA_SERVER_EXE -and (Test-Path $env:LLAMA_SERVER_EXE)) { return $env:LLAMA_SERVER_EXE }
    $toolsDir = Join-Path $Root "tools\llama-cpp"
    $bundled = Get-ChildItem -Path $toolsDir -Filter "llama-server.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if ($bundled) { return $bundled }
    $cmd = Get-Command "llama-server" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Ensure-LlamaServerExe {
    $found = Find-LlamaServer
    if ($found) { return $found }

    $installScript = Join-Path $Root "scripts\install-llama-cpp.ps1"
    if (Test-Path $installScript) {
        Write-Host "[..] llama-server 未検出 — 初回セットアップを実行します（数分・数百MB DL）" -ForegroundColor Yellow
        & $installScript
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        $found = Find-LlamaServer
    }

    if (-not $found) {
        Write-Host "[!!] llama-server が見つかりません。" -ForegroundColor Red
        Write-Host "     scripts\install-llama-cpp.ps1 を実行するか LLAMA_SERVER_EXE を設定" -ForegroundColor Yellow
        exit 1
    }

    return $found
}

$exe = Ensure-LlamaServerExe

$defaultModelScript = Join-Path $Root "scripts\download-default-model.ps1"
if (Test-Path $defaultModelScript) {
    & $defaultModelScript
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[..] 既定モデル未取得のまま続行します（UI は起動します）" -ForegroundColor DarkYellow
    }
}

$auto = Resolve-ModelAndMmproj -ModelPath $Model -MmprojPath $Mmproj
$Model = $auto.Model
$Mmproj = $auto.Mmproj

if ($Model -and (Test-Path $Model)) {
    $ensureMmprojScript = Join-Path $Root "scripts\ensure-mmproj.ps1"
    if (Test-Path $ensureMmprojScript) {
        $modelBaseForMmproj = [System.IO.Path]::GetFileName($Model)
        & $ensureMmprojScript -ModelFileName $modelBaseForMmproj
        if (-not ($Mmproj -and (Test-Path $Mmproj))) {
            $foundMmproj = Find-MmprojForModel $modelBaseForMmproj $ModelsDir
            if ($foundMmproj) {
                $Mmproj = $foundMmproj.FullName
                Write-Host "[..] mmproj 自動選択: $($foundMmproj.Name)" -ForegroundColor Cyan
                try {
                    $selMm = @{ ModelFileName = $modelBaseForMmproj; MmprojFileName = $foundMmproj.Name } | ConvertTo-Json
                    Set-Content -Path $SelectionPath -Value $selMm -Encoding UTF8
                } catch { }
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Model) -or -not (Test-Path $Model)) {
    Write-Host "[..] models/ にチャット用 GGUF がありません — llama-server はスキップして UI を起動します" -ForegroundColor Yellow
    Write-Host "     .gguf を models/ に置き、⚙ で「このモデルで保存」してください" -ForegroundColor DarkYellow
    exit 0
}

$modelSizeGb = (Get-Item $Model).Length / 1GB
$serverArgsExtra = @()
$hasMmproj = $Mmproj -and (Test-Path $Mmproj)
# 大容量 multimodal モデル向け VRAM 節約（並列スロットと ctx 調整）
$modelBase = [System.IO.Path]::GetFileName($Model)
$mmprojBase = if ($hasMmproj) { [System.IO.Path]::GetFileName($Mmproj) } else { "" }
if ($modelBase -match '26B' -and $mmprojBase -match 'E4B' -and $mmprojBase -notmatch '26B') {
    $fix = Get-ChildItem -Path $ModelsDir -Filter "mmproj*26B*.gguf" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $fix) { $fix = Get-ChildItem -Path $ModelsDir -Filter "mmproj-gemma-4-26B*.gguf" -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if ($fix) {
        Write-Host "[..] E4B 用 mmproj を 26B 用に差し替え: $($fix.Name)" -ForegroundColor Yellow
        $Mmproj = $fix.FullName
        $hasMmproj = $true
        $mmprojBase = $fix.Name
        try {
            $selFix = @{ ModelFileName = $modelBase; MmprojFileName = $fix.Name } | ConvertTo-Json
            Set-Content -Path $SelectionPath -Value $selFix -Encoding UTF8
        } catch { }
    } else {
        Write-Host "[!!] 26B 本体に E4B 用 mmproj は使えません（n_embd 不一致）" -ForegroundColor Red
        Write-Host "     Hugging Face から 26B 用 mmproj を models/ に置くか、⚙ で mmproj を「なし」に" -ForegroundColor Yellow
        exit 1
    }
}

if ($modelBase -match 'E2B' -and $hasMmproj -and ($mmprojBase -match 'E4B|Uncensored|Gemma-4-E4B' -or -not (Test-IsE2BMmproj $mmprojBase))) {
    $fix = Find-MmprojForModel $modelBase $ModelsDir
    if ($fix) {
        Write-Host "[..] E2B 本体に合わない mmproj を差し替え: $($fix.Name)" -ForegroundColor Yellow
        Write-Host "     （E4B 用 mmproj は n_embd 不一致で llama-server が落ちます）" -ForegroundColor DarkYellow
        $Mmproj = $fix.FullName
        $hasMmproj = $true
        $mmprojBase = $fix.Name
        try {
            $selFix = @{ ModelFileName = $modelBase; MmprojFileName = $fix.Name } | ConvertTo-Json
            Set-Content -Path $SelectionPath -Value $selFix -Encoding UTF8
        } catch { }
    } else {
        Write-Host "[!!] E2B 用 mmproj がありません" -ForegroundColor Red
        Write-Host "     Hugging Face から E2B 用 mmproj（gemma-4-E2B-it-mmproj.gguf 等）を models/ に置いてください" -ForegroundColor Yellow
        exit 1
    }
}

if ($modelSizeGb -ge 10 -and $hasMmproj) {
    Write-Host "[..] 大容量モデル (${modelSizeGb:N1}GB) + mmproj — 並列1・fit で VRAM 使用量を調整します" -ForegroundColor Cyan
    $serverArgsExtra = @("-np", "1", "--fit", "on", "--fit-target", "2048")
    $heavyCtxCap = 12288
    if ($Context -gt $heavyCtxCap) {
        Write-Host "[..] コンテキスト $Context -> $heavyCtxCap（26B+vision 向け。足りなければ ⚙キャラで下げる）" -ForegroundColor Yellow
        $Context = $heavyCtxCap
    }
} elseif ($modelSizeGb -ge 10) {
    Write-Host "[..] 大容量モデル (${modelSizeGb:N1}GB) — 並列1 で起動" -ForegroundColor Cyan
    $serverArgsExtra = @("-np", "1")
}

$ctxMarker = Join-Path $Root "tools\llama-cpp\.last-ctx"
$modelMarker = Join-Path $Root "tools\llama-cpp\.last-model"
$managedMarker = Join-Path $Root "tools\llama-cpp\.localcompanion-managed"
$legacyManagedMarkers = @(
    (Join-Path $Root "tools\llama-cpp\.legacy-managed")
    (Join-Path $Root "tools\llama-cpp\.new2-managed")
)

function Set-ManagedFlag([string]$value) {
    $dir = Split-Path $managedMarker -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    Set-Content -Path $managedMarker -Value $value -Encoding ASCII
    foreach ($legacy in $legacyManagedMarkers) {
        Remove-Item $legacy -Force -ErrorAction SilentlyContinue
    }
}
$modelKey = ($Model | ForEach-Object { $_.ToLowerInvariant() }) + "|" + ($Mmproj | ForEach-Object { $_.ToLowerInvariant() })
function Stop-LlamaServerProcesses {
    Get-Process -Name "llama-server" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

$serverAlreadyUp = Test-LlamaModelReady -Port $Port

if ($serverAlreadyUp) {
    $lastCtx = 0
    if (Test-Path $ctxMarker) {
        try { $lastCtx = [int](Get-Content $ctxMarker -Raw).Trim() } catch { $lastCtx = 0 }
    }
    $lastModel = ""
    if (Test-Path $modelMarker) {
        try { $lastModel = (Get-Content $modelMarker -Raw).Trim() } catch { $lastModel = "" }
    }
    if ($lastCtx -eq $Context -and $lastModel -eq $modelKey) {
        Write-Host "[OK] すでに起動中 (ctx=$Context): http://127.0.0.1:$Port" -ForegroundColor Green
        Set-ManagedFlag "1"
        exit 0
    }
    if ($lastModel -ne $modelKey) {
        Write-Host "[..] モデル変更のため llama-server を再起動します" -ForegroundColor Yellow
    } else {
        Write-Host "[..] コンテキスト $lastCtx -> $Context のため llama-server を再起動します" -ForegroundColor Yellow
    }
    Stop-LlamaServerProcesses
}

# --chat-template-kwargs は Start-Process 経由だと JSON が壊れて即終了するため使わない。
# reasoning: auto + deepseek 形式で API の reasoning_content に思考を載せる（推論OFF時は API が none を送る）
$reasoningArgs = @("--reasoning", "auto", "--reasoning-format", "deepseek")
if ($modelBase -match 'E2B|26B|gemma-4') {
    $reasoningArgs += @("--reasoning-budget", "4096")
}
$serverArgs = @(
    "-m", $Model,
    "--host", "127.0.0.1",
    "--port", $Port,
    "-c", $Context,
    "-ngl", $GpuLayers,
    "--jinja",
    "--embeddings",
    "--pooling", "last"
) + $reasoningArgs + $serverArgsExtra

if ($Mmproj -and (Test-Path $Mmproj)) {
    $serverArgs += @("--mmproj", $Mmproj)
    Write-Host "[..] vision: mmproj あり" -ForegroundColor Cyan
} else {
    Write-Host "[!!] mmproj 未指定またはファイルなし — 画像入力は使えません" -ForegroundColor Yellow
    Write-Host "     vision 用 mmproj は Hugging Face から models/ に置くか MmprojGgufPath を設定" -ForegroundColor DarkYellow
}

if ($Context -gt 24576) {
    Write-Host "[!!] コンテキスト $Context は上限超過のため 16384 に制限します" -ForegroundColor Yellow
    Write-Host "     ⚙キャラで 24576 以下に保存するとこの警告は出ません" -ForegroundColor DarkYellow
    $Context = 16384
}

Write-Host "[..] llama-server 起動" -ForegroundColor Cyan
Write-Host "     model: $Model"
Write-Host "     ctx:   $Context"
Write-Host "     url:   http://127.0.0.1:$Port"

$exeDir = Split-Path $exe -Parent
$logFile = Join-Path $Root "tools\llama-cpp\llama-server.log"
try {
    Start-LlamaServerProcessHidden -ExePath $exe -ServerArguments $serverArgs -WorkDir $exeDir -LogPath $logFile
} catch {
    Write-Host "[!!] llama-server のプロセス起動に失敗: $_" -ForegroundColor Red
    exit 1
}

# 大きい ctx / 7GB 級モデルは初回ロードに数分かかる（Windows PowerShell 5.x 互換）
$waitSec = [int]($Context / 64)
if ($waitSec -lt 180) { $waitSec = 180 }
if ($waitSec -gt 600) { $waitSec = 600 }
Write-Host "[..] モデル読み込み待ち（最大 ${waitSec} 秒、初回は 2〜3 分かかることあり）..." -ForegroundColor DarkGray

$deadline = (Get-Date).AddSeconds($waitSec)
$lastProgress = [DateTime]::MinValue
while ((Get-Date) -lt $deadline) {
    try {
        if (-not (Test-LlamaModelReady -Port $Port)) {
            throw "model not ready"
        }
        Write-Host "[OK] llama-server 起動完了" -ForegroundColor Green
        $markerDir = Split-Path $ctxMarker -Parent
        if (-not (Test-Path $markerDir)) { New-Item -ItemType Directory -Force -Path $markerDir | Out-Null }
        Set-Content -Path $ctxMarker -Value $Context -Encoding ASCII
        Set-Content -Path $modelMarker -Value $modelKey -Encoding UTF8
        Set-ManagedFlag "1"
        exit 0
    } catch {
        if (((Get-Date) - $lastProgress).TotalSeconds -ge 15) {
            $running = Get-Process -Name "llama-server" -ErrorAction SilentlyContinue
            if ($running) {
                Write-Host "[..] まだ読み込み中…（GPU メモリ確保に時間がかかっています）" -ForegroundColor DarkGray
            } else {
                Write-Host "[!!] llama-server プロセスが終了しました" -ForegroundColor Red
                Write-Host "     よくある原因: 起動オプションエラー / VRAM 不足 / ポート 8080 が使用中" -ForegroundColor DarkYellow
                if (Test-Path $logFile) {
                    $tail = Get-Content $logFile -Tail 15 -ErrorAction SilentlyContinue
                    if ($tail) {
                        Write-Host "--- ログ末尾 ---" -ForegroundColor DarkYellow
                        $tail | ForEach-Object { Write-Host $_ }
                    }
                }
                Write-Host "対処: タスクマネで llama-server を終了 → LocalCompanion.exe を再起動" -ForegroundColor Yellow
                Write-Host "      まだダメならコンテキスト長を 8192 に下げて ⚙キャラで保存" -ForegroundColor Yellow
                exit 1
            }
            $lastProgress = Get-Date
        }
        Start-Sleep -Seconds 2
    }
}

Write-Host "[!!] 起動タイムアウト（${waitSec} 秒）" -ForegroundColor Red
if (Test-Path $logFile) {
    Write-Host "--- ログ末尾 ---" -ForegroundColor DarkYellow
    Get-Content $logFile -Tail 15 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
}
Write-Host "対処: コンテキスト長を 8192 に下げる / タスクマネで llama-server を終了 → LocalCompanion.exe を再起動" -ForegroundColor Yellow
exit 1
