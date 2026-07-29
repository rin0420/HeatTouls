<#
.SYNOPSIS
    単体で動く HeatTouls.exe をビルドする。

.DESCRIPTION
    アイコンを生成してから WinUI 3 アプリを発行する。出力された
    dist\HeatTouls\HeatTouls.exe はダブルクリックするだけで動く。
    .NET ランタイムも Windows App SDK も同梱するので、利用者側の
    インストールは不要。

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Runtime win-arm64
    .\build.ps1 -SkipIcon
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$SkipIcon
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\HeatTouls\HeatTouls.csproj'
$iconTool = Join-Path $root 'tools\IconGen\IconGen.csproj'
$icon = Join-Path $root 'src\HeatTouls\Assets\HeatTouls.ico'
$outDir = Join-Path $root "dist\HeatTouls"

if (-not $SkipIcon) {
    Write-Host 'アイコンを生成しています...'
    dotnet run --project $iconTool -c Release -- $icon
    if ($LASTEXITCODE -ne 0) { throw 'アイコンの生成に失敗しました。' }
}

if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}

Write-Host "発行しています ($Runtime)..."
dotnet publish $project -c $Configuration -r $Runtime -o $outDir --nologo
if ($LASTEXITCODE -ne 0) { throw '発行に失敗しました。' }

$exe = Join-Path $outDir 'HeatTouls.exe'
if (-not (Test-Path $exe)) { throw "exe が見つかりません: $exe" }

$sizeMb = [math]::Round(((Get-ChildItem $outDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host ''
Write-Host "完成しました: $exe  (フォルダ全体で $sizeMb MB)"
Write-Host 'ダブルクリックで起動します。'
