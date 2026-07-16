param(
    [string]$OutputDirectory = 'E:\MidnightVaultDist\termux'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'dev-env.ps1')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$previousGoos = $env:GOOS
$previousGoarch = $env:GOARCH
$previousCgo = $env:CGO_ENABLED
try {
    $env:GOOS = 'linux'
    $env:GOARCH = 'arm64'
    $env:CGO_ENABLED = '0'
    Push-Location (Join-Path $repositoryRoot 'server')
    try {
        go build -trimpath -ldflags '-s -w' -o (Join-Path $OutputDirectory 'midnightvault-server') ./cmd/midnightvault-server
        if ($LASTEXITCODE -ne 0) { throw 'Termux ARM64 build failed.' }
    } finally {
        Pop-Location
    }
} finally {
    $env:GOOS = $previousGoos
    $env:GOARCH = $previousGoarch
    $env:CGO_ENABLED = $previousCgo
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'server\termux\install.sh') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'server\termux\run.sh') -Destination $OutputDirectory -Force
Write-Host "Termux ARM64 package written to $OutputDirectory"
