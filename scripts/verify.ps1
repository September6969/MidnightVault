$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot 'dev-env.ps1')

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command $Command failed with exit code $LASTEXITCODE."
    }
}

Invoke-CheckedCommand 'dotnet' @(
    'test',
    (Join-Path $repositoryRoot 'windows\tests\MidnightVault.Core.Tests\MidnightVault.Core.Tests.csproj'),
    '--configuration',
    'Debug'
)
Invoke-CheckedCommand 'dotnet' @(
    'test',
    (Join-Path $repositoryRoot 'windows\tests\MidnightVault.Receiver.Tests\MidnightVault.Receiver.Tests.csproj'),
    '--configuration',
    'Debug'
)
Invoke-CheckedCommand 'dotnet' @(
    'build',
    (Join-Path $repositoryRoot 'windows\src\MidnightVault.Receiver\MidnightVault.Receiver.csproj'),
    '--configuration',
    'Debug'
)
Invoke-CheckedCommand 'dotnet' @(
    'build',
    (Join-Path $repositoryRoot 'windows\src\MidnightVault.Desktop\MidnightVault.Desktop.csproj'),
    '--configuration',
    'Debug',
    '-p:Platform=x64'
)

Push-Location (Join-Path $repositoryRoot 'server')
try {
    Invoke-CheckedCommand 'go' @('test', './...')
    Invoke-CheckedCommand 'go' @('vet', './...')
} finally {
    Pop-Location
}

$gradleWrapper = Join-Path $repositoryRoot 'android\gradlew.bat'
if ((Test-Path -LiteralPath $gradleWrapper) -and (Test-Path -LiteralPath $env:ANDROID_HOME)) {
    Push-Location (Join-Path $repositoryRoot 'android')
    try {
        Invoke-CheckedCommand $gradleWrapper @('lint', 'test', 'assembleDebug')
    } finally {
        Pop-Location
    }
} else {
    Write-Warning 'Android SDK or Gradle Wrapper is unavailable; Android verification was skipped.'
}

Write-Host 'MidnightVault verification completed.'
