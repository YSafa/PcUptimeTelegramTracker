# install-service.ps1
# Builds, publishes and registers PcUptimeTelegramTracker as a Windows Service.
# Must be run as Administrator.

$ErrorActionPreference = "Stop"

$serviceName = "PcUptimeTelegramTracker"
$publishPath = "C:\Services\PcUptimeTelegramTracker"
$projectPath = "PcUptimeTelegramTracker.Worker"

Write-Host "Publishing project (Release)..."
dotnet publish $projectPath -c Release -r win-x64 --self-contained false -o $publishPath

$localSettingsSource = Join-Path $projectPath "appsettings.Local.json"
$localSettingsDest = Join-Path $publishPath "appsettings.Local.json"

if (-Not (Test-Path $localSettingsDest)) {
    if (Test-Path $localSettingsSource) {
        Copy-Item $localSettingsSource $localSettingsDest
        Write-Host "appsettings.Local.json copied."
    } else {
        Write-Warning "appsettings.Local.json not found! Create it with your Telegram BotToken and ChatId before starting the service."
    }
}

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service already exists, stopping and removing it first..."
    Stop-Service $serviceName -ErrorAction SilentlyContinue
    sc.exe delete $serviceName
    Start-Sleep -Seconds 2
}

Write-Host "Creating service..."
sc.exe create $serviceName binPath= "$publishPath\PcUptimeTelegramTracker.Worker.exe" start= delayed-auto

Write-Host "Configuring automatic restart on failure..."
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/120000/restart/300000

Write-Host "Starting service..."
Start-Service $serviceName

Get-Service $serviceName