# Build a clean Release folder for PayamAutoClick (run on Windows with MSBuild/VS).
# Usage (Developer PowerShell / VS Developer Command Prompt):
#   cd <repo>
#   .\publish-release.ps1
# Output: .\publish\PayamAutoClick-1.0.0.9\

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$msbuild = $null
$candidates = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe"
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $msbuild = $c; break }
}
if (-not $msbuild) {
    Write-Host "MSBuild not found. Open 'Developer PowerShell for VS' and run again," -ForegroundColor Red
    Write-Host "or publish from Visual Studio: Build -> Rebuild Solution (Release)." -ForegroundColor Yellow
    exit 1
}

Write-Host "Using MSBuild: $msbuild" -ForegroundColor Cyan
& $msbuild ".\AutoClickUI.csproj" /t:Clean,Build /p:Configuration=Release /p:Platform=AnyCPU /v:m
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$ver = "1.0.0.9"
$out = Join-Path $root "publish\PayamAutoClick-$ver"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

$src = Join-Path $root "bin\Release"
Copy-Item (Join-Path $src "PayamAutoClick.exe") $out
Copy-Item (Join-Path $src "PayamAutoClick.exe.config") $out -ErrorAction SilentlyContinue
Get-ChildItem $src -Filter "*.dll" | Copy-Item -Destination $out

# Do NOT ship old local API keys / share secrets into the install pack.
# Fresh payam_time_config.txt is created on first run with defaults (X-API-KEY=6iH47).

Write-Host ""
Write-Host "OK: Release package ready:" -ForegroundColor Green
Write-Host "  $out"
Write-Host ""
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "  1) Copy this folder to each PC (or to \\irn-st10\payamconf\AutoClick-1.0.0.9)"
Write-Host "  2) Run PayamAutoClick.exe"
Write-Host "  3) Settings -> X-API-KEY = 6iH47 -> Save -> Apply/Resync"
Write-Host "  4) Confirm Live Time phase-lock, then do a dry-run START"