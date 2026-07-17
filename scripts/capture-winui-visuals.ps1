param(
    [switch]$Full,
    [switch]$UpdateBaselines
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repositoryRoot 'windows\src\MidnightVault.Desktop'
$baselineRoot = Join-Path $repositoryRoot 'tests\visual-baselines\windows'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputRoot = Join-Path $repositoryRoot "artifacts\ui-regression\windows\$timestamp"
$compareScript = Join-Path $repositoryRoot 'tools\compare_visual_baseline.py'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $baselineRoot -Force | Out-Null

$quickCases = @(
    @{ Width=640; Height=480; Theme='light'; Page='devices'; Scale=100 },
    @{ Width=800; Height=600; Theme='light'; Page='dashboard'; Scale=100 },
    @{ Width=1024; Height=768; Theme='dark'; Page='library'; Scale=125 },
    @{ Width=1024; Height=768; Theme='light'; Page='devices'; Scale=150 },
    @{ Width=1440; Height=900; Theme='dark'; Page='settings'; Scale=100 }
)

$fullCases = @(
    @{ Width=640; Height=480; Theme='light'; Page='devices'; Scale=100 },
    @{ Width=800; Height=600; Theme='light'; Page='dashboard'; Scale=100 },
    @{ Width=800; Height=600; Theme='dark'; Page='library'; Scale=150 },
    @{ Width=1024; Height=768; Theme='dark'; Page='library'; Scale=125 },
    @{ Width=1024; Height=768; Theme='light'; Page='devices'; Scale=150 },
    @{ Width=1280; Height=720; Theme='light'; Page='settings'; Scale=100 },
    @{ Width=1280; Height=720; Theme='dark'; Page='dashboard'; Scale=125 },
    @{ Width=1366; Height=768; Theme='light'; Page='library'; Scale=100 },
    @{ Width=1366; Height=768; Theme='dark'; Page='devices'; Scale=150 },
    @{ Width=1440; Height=900; Theme='dark'; Page='settings'; Scale=100 },
    @{ Width=1440; Height=900; Theme='light'; Page='dashboard'; Scale=125 },
    @{ Width=1920; Height=1080; Theme='light'; Page='devices'; Scale=100 },
    @{ Width=1920; Height=1080; Theme='dark'; Page='library'; Scale=150 },
    @{ Width=2560; Height=1440; Theme='dark'; Page='dashboard'; Scale=100 },
    @{ Width=2560; Height=1440; Theme='light'; Page='settings'; Scale=125 }
)
$cases = if ($Full) { $fullCases } else { $quickCases }

dotnet build (Join-Path $projectRoot 'MidnightVault.Desktop.csproj') `
    -c Debug -p:Platform=x64 -p:WindowsPackageType=None --no-restore
if ($LASTEXITCODE -ne 0) { throw 'WinUI Debug build failed.' }
$executable = Join-Path $projectRoot `
    'bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MidnightVault.Desktop.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Unpackaged WinUI executable is missing: $executable"
}

$results = @()
try {
    foreach ($case in $cases) {
        Get-Process -Name 'MidnightVault.Desktop' -ErrorAction SilentlyContinue |
            Stop-Process -Force

        $name = "$($case.Width)x$($case.Height)-$($case.Theme)-$($case.Page)-$($case.Scale)pct.png"
        $capturePath = Join-Path $outputRoot $name
        $errorPath = "$capturePath.error.txt"
        $baselinePath = Join-Path $baselineRoot $name

        $env:MIDNIGHTVAULT_VISUAL_TEST = '1'
        $env:MIDNIGHTVAULT_VISUAL_PAGE = $case.Page
        $env:MIDNIGHTVAULT_VISUAL_THEME = $case.Theme
        $env:MIDNIGHTVAULT_VISUAL_WIDTH = $case.Width
        $env:MIDNIGHTVAULT_VISUAL_HEIGHT = $case.Height
        $env:MIDNIGHTVAULT_VISUAL_SCALE = $case.Scale
        $env:MIDNIGHTVAULT_VISUAL_OUTPUT = $capturePath

        $launcher = Start-Process $executable `
            -WorkingDirectory (Split-Path -Parent $executable) `
            -PassThru

        $deadline = (Get-Date).AddSeconds(35)
        while (-not (Test-Path -LiteralPath $capturePath) -and
               -not (Test-Path -LiteralPath $errorPath) -and
               (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }

        if (Test-Path -LiteralPath $errorPath) {
            throw "In-app WinUI capture failed for ${name}: $(Get-Content -LiteralPath $errorPath -Raw)"
        }
        if (-not (Test-Path -LiteralPath $capturePath)) {
            throw "In-app WinUI capture timed out for $name"
        }

        python $compareScript $capturePath --validate-only --expected-theme $case.Theme
        if ($LASTEXITCODE -ne 0) { throw "Invalid WinUI capture: $name" }

        if ($UpdateBaselines) {
            Copy-Item -LiteralPath $capturePath -Destination $baselinePath -Force
            $status = 'baseline-updated'
        } else {
            if (-not (Test-Path -LiteralPath $baselinePath)) {
                throw "Missing baseline $baselinePath. Run with -UpdateBaselines after review."
            }
            python $compareScript $capturePath $baselinePath `
                --pixel-threshold 12 --max-changed-ratio 0.01
            if ($LASTEXITCODE -ne 0) { throw "Visual regression detected: $name" }
            $status = 'matched'
        }

        $results += [ordered]@{
            file = $name
            width = $case.Width
            height = $case.Height
            theme = $case.Theme
            page = $case.Page
            rasterScalePercent = $case.Scale
            status = $status
        }
        Write-Host "$status $name"

        Get-Process -Name 'MidnightVault.Desktop' -ErrorAction SilentlyContinue |
            Stop-Process -Force
        if (-not $launcher.HasExited) {
            Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue
        }
    }
} finally {
    Get-Process -Name 'MidnightVault.Desktop' -ErrorAction SilentlyContinue |
        Stop-Process -Force
    @(
        'MIDNIGHTVAULT_VISUAL_TEST',
        'MIDNIGHTVAULT_VISUAL_PAGE',
        'MIDNIGHTVAULT_VISUAL_THEME',
        'MIDNIGHTVAULT_VISUAL_WIDTH',
        'MIDNIGHTVAULT_VISUAL_HEIGHT',
        'MIDNIGHTVAULT_VISUAL_SCALE',
        'MIDNIGHTVAULT_VISUAL_OUTPUT'
    ) | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
}

$metadata = [ordered]@{
    capturedAt = (Get-Date).ToString('o')
    matrix = if ($Full) { 'full' } else { 'quick' }
    updateBaselines = [bool]$UpdateBaselines
    captureMethod = 'WinUI RenderTargetBitmap'
    results = $results
}
$metadata | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $outputRoot 'matrix.json') -Encoding UTF8
Write-Host "WinUI visual matrix passed: $outputRoot"
