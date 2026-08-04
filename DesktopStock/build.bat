@echo off
chcp 65001 >nul 2>&1
cd /d "%~dp0"

echo ================================
echo   DesktopStock - Build ^& Run
echo ================================
echo.

mkdir bin 2>nul

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found!
    pause
    exit /b 1
)

set LIB=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
if not exist "%LIB%\System.dll" set LIB=C:\Windows\Microsoft.NET\Framework\v4.0.30319

echo Compiling...
"%CSC%" /nologo /target:winexe /out:bin\DesktopStock.exe /lib:"%LIB%" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /reference:System.Net.Http.dll ^
  /reference:Microsoft.VisualBasic.dll ^
  /reference:System.Xml.dll ^
  /reference:System.Xml.Linq.dll ^
  /reference:System.Data.dll ^
  Program.cs MainForm.cs StockItemPanel.cs StockService.cs StockDataStore.cs EditCostQuantityForm.cs Properties\AssemblyInfo.cs

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [FAILED] Build failed!
    pause
    exit /b 1
)

echo.
echo [OK] Build successful!
echo Output: .\bin\DesktopStock.exe
echo.
echo Starting...
start "" "%~dp0bin\DesktopStock.exe"
exit /b 0
