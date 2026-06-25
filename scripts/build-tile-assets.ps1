param(
    [Parameter(Mandatory = $true)][string]$SourcePng,
    [string]$AssetsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Assets"),
    [double]$LogoPadding = 0.12,
    [string]$SplashBackground = "#1C1C1C"
)

$ErrorActionPreference = "Stop"

$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $python) {
    throw "python が見つかりません。Pillow が必要です（pip install Pillow）。"
}

$source = $SourcePng.Replace("'", "''")
$assets = $AssetsDir.Replace("'", "''")
$padding = $LogoPadding.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$splashBg = $SplashBackground

$py = @"
from PIL import Image

def parse_color(value):
    value = value.lstrip('#')
    if len(value) == 6:
        r = int(value[0:2], 16)
        g = int(value[2:4], 16)
        b = int(value[4:6], 16)
        return (r, g, b, 255)
    raise ValueError('unsupported color')

def fit_logo(source, size, background=(0, 0, 0, 0), padding=0.12):
    canvas = Image.new('RGBA', size, background)
    max_w = int(size[0] * (1 - padding * 2))
    max_h = int(size[1] * (1 - padding * 2))
    scale = min(max_w / source.width, max_h / source.height)
    target = (max(1, int(source.width * scale)), max(1, int(source.height * scale)))
    resized = source.resize(target, Image.Resampling.LANCZOS)
    x = (size[0] - target[0]) // 2
    y = (size[1] - target[1]) // 2
    canvas.paste(resized, (x, y), resized)
    return canvas

src_path = r'$source'
assets = r'$assets'
logo = Image.open(src_path).convert('RGBA')
splash_bg = parse_color('$splashBg')

outputs = {
    'StoreLogo.png': (50, 50, (0, 0, 0, 0)),
    'Square44x44Logo.scale-200.png': (88, 88, (0, 0, 0, 0)),
    'Square44x44Logo.targetsize-24_altform-unplated.png': (24, 24, (0, 0, 0, 0)),
    'Square44x44Logo.targetsize-48_altform-lightunplated.png': (48, 48, (0, 0, 0, 0)),
    'Square150x150Logo.scale-200.png': (300, 300, (0, 0, 0, 0)),
    'Wide310x150Logo.scale-200.png': (620, 300, (0, 0, 0, 0)),
    'LockScreenLogo.scale-200.png': (48, 48, (0, 0, 0, 0)),
    'SplashScreen.scale-200.png': (1240, 600, splash_bg),
}

for name, (w, h, bg) in outputs.items():
    pad = 0.10 if name.startswith('SplashScreen') else $padding
    image = fit_logo(logo, (w, h), bg, pad)
    image.save(f'{assets}/{name}', format='PNG')
    print('wrote', name, f'{w}x{h}')
"@

& $python.Source -c $py
if ($LASTEXITCODE -ne 0) {
    throw "タイル資産の生成に失敗しました。"
}

Write-Host "[OK] tile assets -> $AssetsDir"
