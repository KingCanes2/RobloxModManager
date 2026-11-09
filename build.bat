@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET 8 SDK is required to build Roblox Mod Manager.
    echo Download it from https://dotnet.microsoft.com/download/dotnet/8.0 ^(choose "SDK", x64^), install it, then run this file again.
    pause
    exit /b 1
)

echo Building Roblox Mod Manager...
dotnet publish src\RobloxModManager\RobloxModManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if errorlevel 1 (
    echo.
    echo The build failed. Copy the errors above and send them over.
    pause
    exit /b 1
)

echo.
echo Done: publish\RobloxModManager.exe
echo Run it to install. It needs the .NET 8 Desktop Runtime, which Windows will offer to download if it's missing.
pause
