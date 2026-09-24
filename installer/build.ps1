#Requires -Version 5.1
<#
.SYNOPSIS
  Publish HapiCopi (single-file, win-x64) + build Inno Setup installer.

.USAGE
  .\installer\build.ps1
  .\installer\build.ps1 -SkipInno          # chỉ publish, không đóng gói setup
  .\installer\build.ps1 -Version 1.1.0     # ghi đè số phiên bản lấy từ .csproj
  .\installer\build.ps1 -SkipLocalFeed     # không xuất bản vào nguồn cập nhật trên máy
  .\installer\build.ps1 -FeedDir D:\feed   # xuất bản vào nguồn khác (thư mục LAN, thư mục web…)
#>
param(
    [switch]$SkipInno,
    [string]$Version,
    [switch]$SkipLocalFeed,
    [string]$FeedDir
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
$csproj = Join-Path $root "HapiCopi.csproj"

if (-not $Version) {
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if (-not $Version) { throw "Không xác định được số phiên bản. Dùng -Version hoặc đặt <Version> trong .csproj." }
$Version = $Version.ToString().Trim()
Write-Host "==> Phiên bản: $Version" -ForegroundColor Cyan

if (-not $FeedDir) {
    $FeedDir = Join-Path $env:LOCALAPPDATA "HapiCopi\Releases"
}

Write-Host "==> Publish Release win-x64 single-file self-contained" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
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
& $iscc "/DMyAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC thất bại." }

$setup = Get-Item (Join-Path $installerDir "HapiCopi-Setup-$Version.exe")
Write-Host "Installer OK: $($setup.FullName) ($([math]::Round($setup.Length/1MB,1)) MB)" -ForegroundColor Green

# Bản không gắn số để ai cần "tải bản mới nhất" thì lấy đúng một tên cố định.
Copy-Item $setup.FullName (Join-Path $installerDir "HapiCopi-Setup.exe") -Force

if ($SkipLocalFeed) {
    Write-Host "Bỏ qua xuất bản vào nguồn cập nhật (-SkipLocalFeed)." -ForegroundColor Yellow
    exit 0
}

# ============================
# XUẤT BẢN VÀO NGUỒN CẬP NHẬT
# ============================
# App đọc versions.json trong thư mục này. Cùng định dạng đó đặt lên hosting https sau cũng chạy,
# nên chuyển từ thử trên máy sang phát hành thật chỉ là đổi đường dẫn.

Write-Host "==> Xuất bản vào nguồn cập nhật: $FeedDir" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $FeedDir -Force | Out-Null

$assetName = "HapiCopi-Setup-$Version.exe"
Copy-Item $setup.FullName (Join-Path $FeedDir $assetName) -Force

$hash = (Get-FileHash -Path $setup.FullName -Algorithm SHA256).Hash
$manifestPath = Join-Path $FeedDir "versions.json"

$releases = @()
if (Test-Path $manifestPath) {
    try {
        $existing = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($existing.releases) {
            $releases = @($existing.releases | Where-Object { $_.version -ne $Version })
        }
    } catch {
        Write-Host "versions.json cũ không đọc được, tạo lại từ đầu." -ForegroundColor Yellow
    }
}

$releases += [pscustomobject]@{
    version  = $Version
    released = (Get-Date -Format 'yyyy-MM-dd')
    notes    = ""
    file     = $assetName
    size     = $setup.Length
    sha256   = $hash
}

$sorted = @($releases | Sort-Object -Property @{ Expression = { [version]$_.version } } -Descending)

[pscustomobject]@{ app = "HapiCopi"; releases = $sorted } |
    ConvertTo-Json -Depth 5 |
    Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "versions.json OK — $($sorted.Count) bản trong nguồn:" -ForegroundColor Green
for ($i = 0; $i -lt $sorted.Count; $i++) {
    # Kênh xếp theo vị trí, khớp với cách UpdateManager phân loại.
    $kenh = "Bản cũ"
    if ($i -eq 0 -and $sorted.Count -gt 1) { $kenh = "Thử nghiệm" }
    if ($i -eq 1 -or $sorted.Count -eq 1) { $kenh = "Khuyến nghị" }
    Write-Host ("   v{0,-10} {1}" -f $sorted[$i].version, $kenh)
}

Write-Host ""
Write-Host "User chỉ cần tải file setup này → Cài / Gỡ trong Apps & features." -ForegroundColor Green
Write-Host "Trong app: khay hệ thống → Phiên bản & Cập nhật." -ForegroundColor Green
