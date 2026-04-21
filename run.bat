@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\BezelFreeCorrection\BezelFreeCorrection.csproj -c Debug
endlocal
