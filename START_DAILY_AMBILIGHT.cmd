@echo off
setlocal
cd /d "%~dp0"
set "RGB_STATE_FILE=%~dp0rgb_intensity.txt"
dotnet ".\csharp-ambient\bin\Release\net8.0-windows\AmbientBar.dll" --profile daily
