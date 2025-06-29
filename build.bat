@echo off
echo Building BitTorrent Client...

REM Check if .NET 8 is installed
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo Error: .NET 8 SDK is not installed. Please install it from https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

REM Restore packages
echo Restoring packages...
dotnet restore BitTorrentClient.sln

REM Build the project
echo Building project...
dotnet build BitTorrentClient.sln --configuration Release

if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)

echo Build completed successfully!
echo.
echo To run the application:
echo   dotnet run --project BitTorrentClient
echo.
echo To run in console mode:
echo   dotnet run --project BitTorrentClient -- --console
echo.
pause 