param(
    [string]$OutputDirectory = 'E:\MidnightVaultDist\windows',
    [ValidateSet('x64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'dev-env.ps1')

$receiverOutput = Join-Path $OutputDirectory 'Receiver'
$desktopOutput = Join-Path $OutputDirectory 'Desktop'
New-Item -ItemType Directory -Force -Path $receiverOutput, $desktopOutput | Out-Null

dotnet publish (Join-Path $repositoryRoot 'windows\src\MidnightVault.Receiver\MidnightVault.Receiver.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $receiverOutput `
    -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { throw 'Receiver publish failed.' }

dotnet publish (Join-Path $repositoryRoot 'windows\src\MidnightVault.Desktop\MidnightVault.Desktop.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $desktopOutput `
    -p:Platform=$Platform `
    -p:WindowsPackageType=None
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }

$receiverZip = Join-Path $OutputDirectory 'MidnightVault-Receiver-win-x64.zip'
$desktopZip = Join-Path $OutputDirectory 'MidnightVault-Desktop-win-x64.zip'
Compress-Archive -Path (Join-Path $receiverOutput '*') -DestinationPath $receiverZip -Force
Compress-Archive -Path (Join-Path $desktopOutput '*') -DestinationPath $desktopZip -Force
$installerPath = Join-Path $OutputDirectory 'Install-Receiver.ps1'
$deploymentPath = Join-Path $OutputDirectory 'DEPLOYMENT.md'
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\install-receiver-service.ps1') `
    -Destination $installerPath -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\deployment.md') `
    -Destination $deploymentPath -Force
$bundleZip = Join-Path $OutputDirectory 'MidnightVault-Windows-win-x64.zip'
Compress-Archive `
    -Path $receiverOutput, $desktopOutput, $installerPath, $deploymentPath `
    -DestinationPath $bundleZip `
    -Force
Write-Host "Windows packages written to $OutputDirectory"
