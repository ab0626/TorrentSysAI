Write-Host "Building BitTorrent Client..." -ForegroundColor Green

# Check if .NET 8 is installed
try {
    $dotnetVersion = dotnet --version
    Write-Host "Using .NET version: $dotnetVersion" -ForegroundColor Yellow
}
catch {
    Write-Host "Error: .NET 8 SDK is not installed. Please install it from https://dotnet.microsoft.com/download" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Restore packages
Write-Host "Restoring packages..." -ForegroundColor Yellow
dotnet restore BitTorrentClient.sln

if ($LASTEXITCODE -ne 0) {
    Write-Host "Package restore failed!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Build the project
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build BitTorrentClient.sln --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "To run the application:" -ForegroundColor Cyan
Write-Host "  dotnet run --project BitTorrentClient" -ForegroundColor White
Write-Host ""
Write-Host "To run in console mode:" -ForegroundColor Cyan
Write-Host "  dotnet run --project BitTorrentClient -- --console" -ForegroundColor White
Write-Host ""
Read-Host "Press Enter to exit" 