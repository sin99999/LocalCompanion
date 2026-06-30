# LocalCompanion.exe に Authenticode 署名を付与する（証明書がある場合のみ）
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishRoot,
    [string]$TimestampUrl = ""
)

$ErrorActionPreference = "Stop"

$pfxPath = $env:LOCALCOMPANION_SIGN_PFX_PATH
if ([string]::IsNullOrWhiteSpace($pfxPath)) {
    Write-Host "[skip] LOCALCOMPANION_SIGN_PFX_PATH が未設定のため署名をスキップします。" -ForegroundColor DarkGray
    exit 0
}

if (-not (Test-Path -LiteralPath $pfxPath)) {
    throw "証明書が見つかりません: $pfxPath"
}

if (-not (Test-Path -LiteralPath $PublishRoot)) {
    throw "publish フォルダがありません: $PublishRoot"
}

function Find-SignTool {
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALCOMPANION_SIGNTOOL_PATH)) {
        if (Test-Path -LiteralPath $env:LOCALCOMPANION_SIGNTOOL_PATH) {
            return $env:LOCALCOMPANION_SIGNTOOL_PATH
        }
        throw "signtool が見つかりません: $($env:LOCALCOMPANION_SIGNTOOL_PATH)"
    }

    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) {
        throw "Windows SDK が見つかりません。Visual Studio の「Windows SDK」または LOCALCOMPANION_SIGNTOOL_PATH を設定してください。"
    }

    $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object {
            $path = Join-Path $_.FullName "x64\signtool.exe"
            if (Test-Path -LiteralPath $path) { return $path }
        } | Select-Object -First 1

    if (-not $candidate) {
        throw "signtool.exe が見つかりません。Windows SDK をインストールするか LOCALCOMPANION_SIGNTOOL_PATH を設定してください。"
    }

    return $candidate
}

$signTool = Find-SignTool
$tsUrl = if ($TimestampUrl) { $TimestampUrl }
         elseif ($env:LOCALCOMPANION_SIGN_TIMESTAMP_URL) { $env:LOCALCOMPANION_SIGN_TIMESTAMP_URL }
         else { "http://timestamp.digicert.com" }

$password = $env:LOCALCOMPANION_SIGN_PFX_PASSWORD
if ([string]::IsNullOrWhiteSpace($password)) {
    $secure = Read-Host "証明書パスワード (LOCALCOMPANION_SIGN_PFX_PASSWORD)" -AsSecureString
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

$targets = @(
    (Join-Path $PublishRoot "LocalCompanion.exe")
) | Where-Object { Test-Path -LiteralPath $_ }

if ($targets.Count -eq 0) {
    throw "署名対象の exe がありません: $PublishRoot"
}

Write-Host "=== Authenticode sign ===" -ForegroundColor Cyan
Write-Host "signtool: $signTool" -ForegroundColor DarkGray
Write-Host "pfx: $pfxPath" -ForegroundColor DarkGray

foreach ($file in $targets) {
    & $signTool sign /fd SHA256 /f $pfxPath /p $password /tr $tsUrl /td SHA256 $file
    if ($LASTEXITCODE -ne 0) {
        throw "署名に失敗しました: $file"
    }
    Write-Host "[OK] $file" -ForegroundColor Green
}

Write-Host "署名が完了しました。" -ForegroundColor Green
