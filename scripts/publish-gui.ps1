#!/usr/bin/env pwsh
# 构建和发布 WhereFrom GUI 应用

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$appProject = Join-Path $repoRoot "src" "WhereFrom.App" "WhereFrom.App.csproj"
$artifactsDir = Join-Path $repoRoot "artifacts"

Write-Host "Building WhereFrom GUI..." -ForegroundColor Cyan

# 清理旧的 artifacts
if (Test-Path $artifactsDir) {
    Write-Host "Cleaning artifacts directory..." -ForegroundColor Yellow
    Remove-Item -Path $artifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null

# 发布 x64 版本
Write-Host "`nPublishing x64 version..." -ForegroundColor Green
dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false

$publishDir = Join-Path $repoRoot "src" "WhereFrom.App" "bin" "Release" "net10.0-windows10.0.22621.0" "win-x64" "publish"
$outputZip = Join-Path $artifactsDir "WhereFrom-GUI-0.1.0-win-x64.zip"

if (Test-Path $publishDir) {
    Write-Host "Creating ZIP package..." -ForegroundColor Green
    Compress-Archive -Path "$publishDir\*" -DestinationPath $outputZip -Force

    # 生成 SHA256 校验和
    $hash = Get-FileHash -Path $outputZip -Algorithm SHA256
    $hashFile = Join-Path $artifactsDir "WhereFrom-GUI-SHA256SUMS.txt"
    "$($hash.Hash)  $(Split-Path $outputZip -Leaf)" | Out-File -FilePath $hashFile -Encoding utf8

    Write-Host "`nGUI package created successfully:" -ForegroundColor Green
    Write-Host "  Package: $outputZip" -ForegroundColor White
    Write-Host "  SHA256:  $hashFile" -ForegroundColor White

    $zipSize = (Get-Item $outputZip).Length / 1MB
    Write-Host "  Size:    $([math]::Round($zipSize, 2)) MB" -ForegroundColor White
} else {
    Write-Error "Publish directory not found: $publishDir"
    exit 1
}

Write-Host "`nDone!" -ForegroundColor Cyan
