#Requires -Version 5.1
<#
.SYNOPSIS
  Publish HapiCopi (single-file, win-x64) + build Inno Setup installer.

.USAGE
  .\installer\build.ps1
  .\installer\build.ps1 -SkipInno   # chỉ publish, không đóng gói setup
#>
param(
    [switch]$SkipInno
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root "HapiCopi.csproj"))) {
    $root = $PSScriptRoot
    if (-not (Test-Path (Join-Path $root "HapiCopi.csproj"))) {
        throw "Không tìm thấy HapiCopi.csproj. Chạy từ repo root hoặc installer\."
    }
}

$publishDir = Join-Path $root "artifacts\publish"
$installerDir = Join-Path $root "artifacts\installer"
$iss = Join-Path $root "installer\HapiCopi.iss"

Write-Host "==> Publish Release win-x64 single-file self-contained" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish (Join-Path $root "HapiCopi.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish thất bại." }

$exe = Join-Path $publishDir "HapiCopi.exe"
$wv = Join-Path $publishDir "WebViewResources"
if (-not (Test-Path $exe)) { throw "Thiếu HapiCopi.exe trong $publishDir" }
if (-not (Test-Path (Join-Path $wv "index.html"))) { throw "Thiếu WebViewResources sau publish." }

Write-Host "Publish OK: $publishDir" -ForegroundColor Green
Get-ChildItem $publishDir -File | Select-Object Name, @{N='MB';E={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize

if ($SkipInno) {
    Write-Host "Bỏ qua Inno (-SkipInno). Có thể zip artifacts\publish để thử tay." -ForegroundColor Yellow
    exit 0
}

function Find-ISCC {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Get-Command ISCC -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    ) | Where-Object { $_ -and (Test-Path $_) }
    return $candidates | Select-Object -First 1
}

$iscc = Find-ISCC
if (-not $iscc) {
    Write-Host "Chưa có Inno Setup. Thử cài bằng winget..." -ForegroundColor Yellow
    winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    $iscc = Find-ISCC
}

if (-not $iscc) {
    Write-Host @"

Chưa tìm thấy ISCC.exe. Cài Inno Setup 6 rồi chạy lại:
  https://jrsoftware.org/isdl.php
  hoặc: winget install JRSoftware.InnoSetup

Publish đã xong tại: $publishDir
"@ -ForegroundColor Red
    exit 2
}

Write-Host "==> Build installer bằng $iscc" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC thất bại." }

$setup = Get-ChildItem $installerDir -Filter "HapiCopi-Setup*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "Installer OK: $($setup.FullName) ($([math]::Round($setup.Length/1MB,1)) MB)" -ForegroundColor Green
Write-Host "User chỉ cần tải file setup này → Cài / Gỡ trong Apps & features." -ForegroundColor Green
