# dotnet build（Cursor / PowerShell の日本語文字化け対策付き）
param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$BuildArgs
)

$ErrorActionPreference = "Stop"
chcp 65001 | Out-Null
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Proj = Join-Path $Root "LocalCompanion.csproj"

$dotnetArgs = @(
    "build", $Proj,
    "-c", $Configuration,
    "-p:Platform=$Platform"
) + $BuildArgs

Push-Location $Root
try {
    & dotnet @dotnetArgs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
