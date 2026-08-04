$ErrorActionPreference = "Stop"
$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$binDir = Join-Path $baseDir "bin"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

$frameworkDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $frameworkDir "csc.exe"

$refs = @(
    "System.dll", "System.Core.dll", "System.Drawing.dll",
    "System.Windows.Forms.dll", "System.Web.Extensions.dll",
    "System.Net.Http.dll", "Microsoft.VisualBasic.dll",
    "System.Xml.dll", "System.Xml.Linq.dll", "System.Data.dll"
) | ForEach-Object { "/reference:" + (Join-Path $frameworkDir $_) }

$srcs = @(
    "Program.cs", "MainForm.cs", "StockItemPanel.cs",
    "StockService.cs", "StockDataStore.cs",
    "Properties\AssemblyInfo.cs"
) | ForEach-Object { Join-Path $baseDir $_ }

$out = Join-Path $binDir "DesktopStock.exe"

$args = @(
    "/nologo", "/target:winexe",
    "/lib:$frameworkDir",
    "/out:$out"
) + $refs + $srcs

Write-Host "=== Compiling DesktopStock ==="
Write-Host "Output: $out"
Write-Host ""

& $csc $args

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Build SUCCESS!" -ForegroundColor Green
    Write-Host "Executable: $out"
    Write-Host ""
    Write-Host "Launching..."
    Start-Process $out
} else {
    Write-Host ""
    Write-Host "Build FAILED with exit code $LASTEXITCODE" -ForegroundColor Red
}
