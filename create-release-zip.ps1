# ==============================================================================
# VPM Package Release Zip Generator
# dennokoworks Asset Importer
# ==============================================================================

[CmdletBinding()]
param (
    [string]$OutputDir = ".."
)

$ErrorActionPreference = "Stop"

# リポジトリルートのパス取得
$repoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Item .).FullName
}

$packageJsonPath = Join-Path $repoRoot "package.json"
if (-not (Test-Path $packageJsonPath)) {
    Write-Error "package.json が見つかりません: $packageJsonPath"
    exit 1
}

# package.json からパッケージ情報取得
$jsonText = [System.IO.File]::ReadAllText($packageJsonPath, [System.Text.Encoding]::UTF8)
$packageJson = ConvertFrom-Json $jsonText
$packageName = [string]$packageJson.name
$version = [string]$packageJson.version

if ([string]::IsNullOrWhiteSpace($packageName) -or [string]::IsNullOrWhiteSpace($version)) {
    Write-Error "package.json から name または version が取得できませんでした。"
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  VPM Package Zip Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Package: $packageName"
Write-Host "Version: $version"
Write-Host ""

# 出力ファイル名
$zipFileName = "$packageName-$version.zip"
$resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
if (-not (Test-Path $resolvedOutputDir)) {
    New-Item -ItemType Directory -Path $resolvedOutputDir | Out-Null
}
$zipFilePath = Join-Path $resolvedOutputDir $zipFileName

if (Test-Path $zipFilePath) {
    Remove-Item $zipFilePath -Force
    Write-Host "既存の zip ファイルを削除しました: $zipFileName" -ForegroundColor Gray
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# zip アーカイブ作成（ルート直下に package.json を配置）
$zipArchive = [System.IO.Compression.ZipFile]::Open($zipFilePath, [System.IO.Compression.ZipArchiveMode]::Create)

# 利用者に不要な開発用ファイル（リポジトリ設定・リリース手順・設計資料・VPM テンプレート）。
# .meta も一緒に除外する（アセット本体の無い .meta が残ると Unity が警告を出すため）
$excludedPaths = @(
    '.gitignore',
    '.claude',
    'vpm-repository-template.json',
    'Docs/RELEASE.md',
    'Docs/Impl'
)

$files = Get-ChildItem -Path $repoRoot -Recurse -File | Where-Object {
    $path = $_.FullName
    if ($path -match '[\\/]\.git([\\/]|$)') { return $false }
    if ($path -match '[\\/]\.vs([\\/]|$)') { return $false }
    if ($path -match '[\\/]\.idea([\\/]|$)') { return $false }
    if ($path -match '[\\/]\.claude([\\/]|$)') { return $false }
    if ($path -match 'create-release-zip') { return $false }
    if ($path -like '*.zip') { return $false }

    $relative = $path.Substring($repoRoot.Length + 1).Replace("\", "/")
    foreach ($excluded in $excludedPaths) {
        if ($relative -eq $excluded -or $relative -eq "$excluded.meta" -or $relative.StartsWith("$excluded/")) { return $false }
    }
    return $true
}

Write-Host "ファイルをアーカイブに追加中..." -ForegroundColor Yellow
$count = 0
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($repoRoot.Length + 1).Replace("\", "/")
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zipArchive,
        $file.FullName,
        $relativePath,
        [System.IO.Compression.CompressionLevel]::Optimal
    ) | Out-Null
    $count++
}

$zipArchive.Dispose()
Write-Host "計 $count 個のファイルを追加しました。" -ForegroundColor Green

# SHA256 ハッシュ計算
$sha256 = (Get-FileHash -Algorithm SHA256 $zipFilePath).Hash.ToLower()
$fileSize = (Get-Item $zipFilePath).Length
$fileSizeMb = [math]::Round($fileSize / 1MB, 2)

# クリップボードにコピーを試みる
try {
    Set-Clipboard -Value $sha256
    $copied = $true
} catch {
    $copied = $false
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  ZIP 生成が完了しました！" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ('出力先 : ' + $zipFilePath)
Write-Host ('サイズ : ' + $fileSizeMb + ' MB (' + $fileSize + ' bytes)')
Write-Host ('SHA256 : ' + $sha256) -ForegroundColor Magenta
if ($copied) {
    Write-Host '(SHA256ハッシュ値をクリップボードに自動コピーしました)' -ForegroundColor DarkCyan
}
Write-Host ''
Write-Host '--- vpm-repos/index.json 用設定 ---' -ForegroundColor Yellow
$q = [char]34
Write-Host ($q + 'url' + $q + ': ' + $q + 'https://github.com/dennoko/' + $packageName + '/releases/download/v' + $version + '/' + $zipFileName + $q + ',')
Write-Host ($q + 'zipSHA256' + $q + ': ' + $q + $sha256 + $q)
Write-Host ''
