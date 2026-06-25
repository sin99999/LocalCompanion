param(
    [Parameter(Mandatory = $true)][string]$SourcePng,
    [Parameter(Mandatory = $true)][string]$DestIco,
    [int[]]$Sizes = @(256, 128, 64, 48, 32, 16)
)

$ErrorActionPreference = "Stop"

$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $python) {
    throw "python が見つかりません。Pillow で ICO を生成するために Python が必要です。"
}

$sizeArgs = ($Sizes | ForEach-Object { "($_, $_)" }) -join ", "
$py = @"
from PIL import Image
img = Image.open(r'$($SourcePng.Replace("'", "''"))').convert('RGBA')
sizes = [$sizeArgs]
img.save(r'$($DestIco.Replace("'", "''"))', format='ICO', sizes=sizes)
"@

& $python.Source -c $py
if ($LASTEXITCODE -ne 0) {
    throw "ICO の生成に失敗しました。"
}

Write-Host "[OK] $DestIco"
