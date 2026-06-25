# チャット用 GGUF に対応する vision mmproj が無ければ Hugging Face から取得
# config/mmproj-families.json のファミリー表 + HF 検索（改造 quant でも公式 mmproj を試行）
param(
    [Parameter(Mandatory = $true)]
    [string]$ModelFileName
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$ModelsDir = Join-Path $Root "models"
$MarkerPath = Join-Path $ModelsDir ".mmproj-bootstrap.json"
$RegistryPath = Join-Path $Root "config\mmproj-families.json"

function Get-Registry {
    if (-not (Test-Path $RegistryPath)) {
        return @{ version = 0; families = @(); repoPriorityPrefixes = @(); genericVisionPatterns = @() }
    }
    return (Get-Content $RegistryPath -Raw | ConvertFrom-Json)
}

function Get-ModelStem([string]$Name) {
    $base = [System.IO.Path]::GetFileNameWithoutExtension($Name)
    $cur = $base
    for ($i = 0; $i -lt 8; $i++) {
        $next = $cur -replace '-(?:UD-|IQ)?(?:Q[0-9][\w_.-]*|F16|F32|BF16|fp16|QAT|qat)$', ''
        if ($next -eq $cur -or [string]::IsNullOrWhiteSpace($next)) { break }
        $cur = $next
    }
    return $cur
}

function Test-LooksVisionCapable([string]$Name, $Registry) {
    if (Find-MatchedFamily $Name $Registry) { return $true }
    foreach ($pat in @($Registry.genericVisionPatterns)) {
        if ($Name -match $pat) { return $true }
    }
    return $false
}

function Find-MatchedFamily([string]$Name, $Registry) {
    foreach ($family in @($Registry.families)) {
        foreach ($ex in @($family.excludeAny)) {
            if ($Name -match [regex]::Escape($ex)) { continue 2 }
        }
        foreach ($token in @($family.matchAny)) {
            if ($Name -match [regex]::Escape($token)) { return $family }
        }
    }
    return $null
}

function Get-MmprojCandidates([string]$Dir) {
    @(Get-ChildItem -Path $Dir -Filter '*.gguf' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'mmproj*' -or $_.Name -like '*-mmproj.gguf' })
}

function Find-MmprojForModel([string]$Name, [string]$Dir, $Registry) {
    $candidates = @(Get-MmprojCandidates $Dir)
    if ($candidates.Count -eq 0) { return $null }

    $family = Find-MatchedFamily $Name $Registry
    if ($family) {
        if ($family.mmproj.localName) {
            $exact = $candidates | Where-Object { $_.Name -ieq $family.mmproj.localName } | Select-Object -First 1
            if ($exact) { return $exact }
        }
        $token = [string]$family.sizeToken
        if ($token) {
            $byToken = $candidates | Where-Object { $_.Name -match [regex]::Escape($token) } | Select-Object -First 1
            if ($byToken) { return $byToken }
        }
        if ($token -ieq 'E2B') {
            foreach ($mm in @('gemma-4-E2B-it-mmproj.gguf', 'mmproj-F16.gguf', 'mmproj-BF16.gguf')) {
                $hit = $candidates | Where-Object { $_.Name -ieq $mm } | Select-Object -First 1
                if ($hit) { return $hit }
            }
        }
    }

    $stem = Get-ModelStem $Name
    if ($stem) {
        $hit = $candidates | Where-Object {
            $_.Name -match '(?i)mmproj' -and $_.Name -match [regex]::Escape($stem)
        } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    return $null
}

function Get-KnownMmprojSpec([string]$Name, $Registry) {
    $family = Find-MatchedFamily $Name $Registry
    if (-not $family -or -not $family.mmproj.url) { return $null }
    return @{
        Url = [string]$family.mmproj.url
        LocalName = [string]$family.mmproj.localName
        Label = [string]$family.id
        RepoId = [string]$family.mmproj.repoId
        Family = $family
    }
}

function Get-SearchTerms([string]$Name, $Registry) {
    $terms = [System.Collections.Generic.List[string]]::new()
    $family = Find-MatchedFamily $Name $Registry
    if ($family) {
        foreach ($t in @($family.hfSearch)) { [void]$terms.Add([string]$t) }
    }
    $base = [System.IO.Path]::GetFileNameWithoutExtension($Name)
    [void]$terms.Add($base)
    $stem = Get-ModelStem $Name
    if ($stem -and $stem -ne $base) { [void]$terms.Add($stem) }
    if ($stem -and $stem -notmatch 'GGUF$') { [void]$terms.Add("$stem-GGUF") }
    if ($base -match '(?i)(llava|qwen.*vl|gemma|minicpm|pixtral|moondream|smolvlm|internvl|cogvlm)') {
        [void]$terms.Add($Matches[1])
    }
    return @($terms | Select-Object -Unique)
}

function Pick-RemoteMmproj([string]$ModelName, [string[]]$RemoteFiles, $Family) {
    $names = @($RemoteFiles | ForEach-Object { [System.IO.Path]::GetFileName($_) })
    if ($Family -and $Family.mmproj.remotePrefer) {
        foreach ($prefer in @($Family.mmproj.remotePrefer)) {
            $hit = $names | Where-Object { $_ -ieq $prefer } | Select-Object -First 1
            if ($hit) { return $hit }
        }
        $token = [string]$Family.sizeToken
        if ($token) {
            $hit = $names | Where-Object { $_ -match [regex]::Escape($token) } | Select-Object -First 1
            if ($hit) { return $hit }
        }
    }
    foreach ($mm in @('mmproj-BF16.gguf', 'mmproj-F16.gguf', 'mmproj-f16.gguf', 'mmproj-bf16.gguf')) {
        $hit = $names | Where-Object { $_ -ieq $mm } | Select-Object -First 1
        if ($hit) { return $hit }
    }
    return ($names | Select-Object -First 1)
}

function Get-LocalMmprojName([string]$ModelName, [string]$RemoteFile, [string]$RepoId, $Family) {
    if ($Family -and $Family.mmproj.localName) { return [string]$Family.mmproj.localName }
    $remoteBase = [System.IO.Path]::GetFileName($RemoteFile)
    $generic = @('mmproj-F16.gguf', 'mmproj-BF16.gguf', 'mmproj-f16.gguf', 'mmproj-bf16.gguf')
    if ($generic -notcontains $remoteBase.ToLower()) { return $remoteBase }
    $slug = ($RepoId -split '/')[-1] -replace '[^\w\-]', '-'
    return "mmproj-$slug-$remoteBase"
}

function Score-Repo([string]$RepoId, $Registry) {
    $score = 0
    foreach ($prefix in @($Registry.repoPriorityPrefixes)) {
        if ($RepoId.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $score += 100
            break
        }
    }
    if ($RepoId -match 'GGUF') { $score += 10 }
    return $score
}

function Discover-MmprojFromHuggingFace([string]$Name, $Registry) {
    $family = Find-MatchedFamily $Name $Registry
    $seenRepos = @{}
    $allHits = @()

    foreach ($term in Get-SearchTerms $Name $Registry) {
        if ([string]::IsNullOrWhiteSpace($term)) { continue }
        $searchUrl = "https://huggingface.co/api/models?search=$([uri]::EscapeDataString($term))&limit=12"
        try {
            $results = @(Invoke-RestMethod -Uri $searchUrl -TimeoutSec 20 -ErrorAction Stop)
        } catch { continue }

        foreach ($hit in $results) {
            $repoId = if ($hit.id) { [string]$hit.id } elseif ($hit.modelId) { [string]$hit.modelId } else { "" }
            if ([string]::IsNullOrWhiteSpace($repoId) -or $seenRepos.ContainsKey($repoId)) { continue }
            $seenRepos[$repoId] = $true
            $allHits += [pscustomobject]@{ RepoId = $repoId; Score = (Score-Repo $repoId $Registry) }
        }
    }

    foreach ($item in ($allHits | Sort-Object Score -Descending)) {
        $repoId = $item.RepoId
        try {
            $detail = Invoke-RestMethod -Uri "https://huggingface.co/api/models/$repoId" -TimeoutSec 20 -ErrorAction Stop
        } catch { continue }

        $files = @($detail.siblings | ForEach-Object { [string]$_.rfilename })
        if ($files.Count -eq 0) { continue }

        $mmprojs = @($files | Where-Object { $_ -match '(?i)(^|/)mmproj.*\.gguf$' })
        if ($mmprojs.Count -eq 0) { continue }

        if (-not $family) {
            $hasModel = $files | Where-Object { [System.IO.Path]::GetFileName($_) -ieq $Name } | Select-Object -First 1
            $stem = Get-ModelStem $Name
            $hasStem = $files | Where-Object { [System.IO.Path]::GetFileName($_) -match [regex]::Escape($stem) } | Select-Object -First 1
            if (-not $hasModel -and -not $hasStem) { continue }
        }

        $picked = Pick-RemoteMmproj $Name $mmprojs $family
        if (-not $picked) { continue }

        $localName = Get-LocalMmprojName $Name $picked $repoId $family
        return @{
            Url = "https://huggingface.co/$repoId/resolve/main/$picked"
            LocalName = $localName
            Label = "HF: $repoId"
            RepoId = $repoId
        }
    }
    return $null
}

function Read-MarkerMap {
    if (-not (Test-Path $MarkerPath)) { return @{} }
    try {
        $raw = Get-Content $MarkerPath -Raw | ConvertFrom-Json
        $map = @{}
        if ($raw -is [System.Collections.IDictionary]) {
            foreach ($k in $raw.Keys) { $map[$k] = $raw[$k] }
        } elseif ($raw.PSObject.Properties) {
            foreach ($p in $raw.PSObject.Properties) { $map[$p.Name] = $p.Value }
        }
        return $map
    } catch { return @{} }
}

function Write-MarkerEntry([string]$model, [hashtable]$entry) {
    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    $map = Read-MarkerMap
    $map[$model] = $entry
    $map | ConvertTo-Json -Depth 6 | Set-Content -Path $MarkerPath -Encoding UTF8
}

function Invoke-MmprojDownload([hashtable]$spec) {
    $dest = Join-Path $ModelsDir $spec.LocalName
    Write-Host "[..] vision mmproj を取得します（$($spec.Label)）" -ForegroundColor Cyan
    Write-Host "     $($spec.LocalName)" -ForegroundColor Cyan
    Write-Host "     （数百 MB〜1 GB 程度・初回のみ）" -ForegroundColor DarkGray

    New-Item -ItemType Directory -Force -Path $ModelsDir | Out-Null
    Invoke-WebRequest -Uri $spec.Url -OutFile $dest -UseBasicParsing
    if (-not (Test-Path $dest) -or (Get-Item $dest).Length -lt 10MB) {
        throw "ダウンロードファイルが不正です"
    }
    return $dest
}

$Registry = Get-Registry
$RegistryVersion = [int]$Registry.version

if ([string]::IsNullOrWhiteSpace($ModelFileName) -or $ModelFileName -match '^mmproj') {
    exit 0
}

if (-not (Test-LooksVisionCapable $ModelFileName $Registry)) {
    exit 0
}

$existing = Find-MmprojForModel $ModelFileName $ModelsDir $Registry
if ($existing) {
    Write-MarkerEntry $ModelFileName @{
        status = "present"
        mmprojFileName = $existing.Name
        registryVersion = $RegistryVersion
        at = (Get-Date).ToString("o")
    }
    exit 0
}

$markers = Read-MarkerMap
if ($markers.ContainsKey($ModelFileName)) {
    $prev = $markers[$ModelFileName]
    $prevStatus = if ($prev.status) { [string]$prev.status } else { "" }
    $prevVersion = if ($prev.registryVersion) { [int]$prev.registryVersion } else { 0 }
    if ($prevStatus -eq "not_found" -and $prevVersion -ge $RegistryVersion) {
        exit 0
    }
    if ($prevStatus -in @("downloaded", "present") -and $prev.mmprojFileName) {
        $prevPath = Join-Path $ModelsDir ([string]$prev.mmprojFileName)
        if (Test-Path $prevPath) { exit 0 }
    }
}

$spec = Get-KnownMmprojSpec $ModelFileName $Registry
if (-not $spec) {
    Write-Host "[..] Hugging Face で vision mmproj を探しています… ($ModelFileName)" -ForegroundColor DarkGray
    $spec = Discover-MmprojFromHuggingFace $ModelFileName $Registry
}

if (-not $spec) {
    Write-MarkerEntry $ModelFileName @{
        status = "not_found"
        registryVersion = $RegistryVersion
        detail = "対応 mmproj が HF 上で見つかりませんでした（テキストのみ）"
        at = (Get-Date).ToString("o")
    }
    exit 0
}

try {
    $dest = Invoke-MmprojDownload $spec
    Write-MarkerEntry $ModelFileName @{
        status = "downloaded"
        mmprojFileName = $spec.LocalName
        url = $spec.Url
        repoId = $spec.RepoId
        registryVersion = $RegistryVersion
        at = (Get-Date).ToString("o")
    }
    Write-Host "[OK] vision mmproj を配置しました: models\$($spec.LocalName)" -ForegroundColor Green
    exit 0
} catch {
    Write-Host "[!!] vision mmproj の取得に失敗: $_" -ForegroundColor Red
    Write-Host "     テキストチャットのみ続行します（HF から手動で models/ に置いても可）" -ForegroundColor Yellow
    $dest = Join-Path $ModelsDir $spec.LocalName
    if (Test-Path $dest) { Remove-Item $dest -Force -ErrorAction SilentlyContinue }
    exit 0
}
