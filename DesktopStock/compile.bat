@echo off
chcp 65001 >nul
cd /d "%~dp0"
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" "%~dp0DesktopStock.csproj" /p:Configuration=Release /t:Build /v:normal /nologo
echo.
echo EXIT CODE: %ERRORLEVEL%
pause
