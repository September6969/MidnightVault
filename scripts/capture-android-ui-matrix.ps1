$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot 'dev-env.ps1')

$adb = Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'
if (-not (Test-Path -LiteralPath $adb)) {
    throw "ADB is unavailable: $adb"
}

function Invoke-AdbChecked {
    param([Parameter(Mandatory)] [string[]]$CommandArgs)
    $escapedArguments = $CommandArgs | ForEach-Object {
        '"' + $_.Replace('"', '\"') + '"'
    }
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $adb
    $startInfo.Arguments = $escapedArguments -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start ADB: $adb"
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = @(
            $standardOutput.GetAwaiter().GetResult()
            $standardError.GetAwaiter().GetResult()
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $exitCode = $process.ExitCode
    } finally {
        $process.Dispose()
    }
    if ($exitCode -ne 0) {
        throw "ADB command failed: adb $($CommandArgs -join ' ')`n$output"
    }
    return $output
}

$deviceLines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\tdevice$" }
if ($deviceLines.Count -ne 1) {
    throw 'Exactly one authorized Android device or emulator is required.'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputRoot = Join-Path $repositoryRoot "artifacts\ui-regression\android\$timestamp"
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

Push-Location (Join-Path $repositoryRoot 'android')
try {
    & .\gradlew.bat :app:assembleDebug
    if ($LASTEXITCODE -ne 0) { throw 'Android debug APK build failed.' }
} finally {
    Pop-Location
}

$apk = Join-Path $repositoryRoot 'android\app\build\outputs\apk\debug\app-debug.apk'
Invoke-AdbChecked @('install', '-r', $apk) | Out-Null

$matrix = @(
    @{ Name='360x800-light-overview-font100-gesture'; Width=1080; Height=2400; Density=480; Font='1.0'; Theme='no'; Page='overview'; Navigation='gestural' },
    @{ Name='360x800-light-connection-font100-gesture-keyboard'; Width=1080; Height=2400; Density=480; Font='1.0'; Theme='no'; Page='connection'; Navigation='gestural'; Keyboard=$true },
    @{ Name='412x915-dark-connection-font100-threebutton'; Width=1133; Height=2516; Density=440; Font='1.0'; Theme='yes'; Page='connection'; Navigation='threebutton' },
    @{ Name='412x915-light-sources-font160-gesture'; Width=1133; Height=2516; Density=440; Font='1.6'; Theme='no'; Page='sources'; Navigation='gestural' },
    @{ Name='600x960-dark-overview-font130-gesture'; Width=1200; Height=1920; Density=320; Font='1.3'; Theme='yes'; Page='overview'; Navigation='gestural' },
    @{ Name='800x1280-light-sources-font100-threebutton'; Width=1200; Height=1920; Density=240; Font='1.0'; Theme='no'; Page='sources'; Navigation='threebutton' },
    @{ Name='915x412-dark-connection-font130-gesture'; Width=2516; Height=1133; Density=440; Font='1.3'; Theme='yes'; Page='connection'; Navigation='gestural' }
)

$originalSizeText = (Invoke-AdbChecked @('shell', 'wm', 'size')) -join "`n"
$originalDensityText = (Invoke-AdbChecked @('shell', 'wm', 'density')) -join "`n"
$originalSizeMatch = [regex]::Match($originalSizeText, 'Override size:\s*(\d+x\d+)')
$originalDensityMatch = [regex]::Match($originalDensityText, 'Override density:\s*(\d+)')
$originalFontScale = ((Invoke-AdbChecked @('shell', 'settings', 'get', 'system', 'font_scale')) -join '').Trim()
$originalNightText = ((Invoke-AdbChecked @('shell', 'cmd', 'uimode', 'night')) -join "`n")
$originalNightMatch = [regex]::Match($originalNightText, 'Night mode:\s*(\S+)')
$originalNightMode = if ($originalNightMatch.Success) { $originalNightMatch.Groups[1].Value } else { 'auto' }
$originalOverlayText = ((Invoke-AdbChecked @('shell', 'cmd', 'overlay', 'list')) -join "`n")
$originalNavigationOverlay = ($originalOverlayText -split "`r?`n" |
    Where-Object { $_ -match '^\[x\].*com\.android\.internal\.systemui\.navbar\.' } |
    ForEach-Object { ($_ -replace '^\[x\]\s*', '').Trim() } |
    Select-Object -First 1)

try {
    foreach ($entry in $matrix) {
        Invoke-AdbChecked @('shell', 'wm', 'size', "$($entry.Width)x$($entry.Height)") | Out-Null
        Invoke-AdbChecked @('shell', 'wm', 'density', [string]$entry.Density) | Out-Null
        Invoke-AdbChecked @('shell', 'settings', 'put', 'system', 'font_scale', $entry.Font) | Out-Null
        Invoke-AdbChecked @('shell', 'cmd', 'uimode', 'night', $entry.Theme) | Out-Null
        Invoke-AdbChecked @(
            'shell', 'cmd', 'overlay', 'enable-exclusive', '--category',
            "com.android.internal.systemui.navbar.$($entry.Navigation)"
        ) | Out-Null
        Invoke-AdbChecked @('shell', 'am', 'force-stop', 'win.september.midnightvault') | Out-Null
        Invoke-AdbChecked @(
            'shell', 'am', 'start', '-W', '-n',
            'win.september.midnightvault/.MainActivity', '--es', 'ui_destination', $entry.Page
        ) | Out-Null
        Start-Sleep -Seconds 2
        if ($entry.Keyboard) {
            $remoteHierarchy = "/sdcard/$($entry.Name).xml"
            $localHierarchy = Join-Path $outputRoot "$($entry.Name).xml"
            Invoke-AdbChecked @('shell', 'uiautomator', 'dump', $remoteHierarchy) | Out-Null
            Invoke-AdbChecked @('pull', $remoteHierarchy, $localHierarchy) | Out-Null
            Invoke-AdbChecked @('shell', 'rm', $remoteHierarchy) | Out-Null
            [xml]$hierarchy = Get-Content -LiteralPath $localHierarchy -Raw -Encoding UTF8
            $firstTextField = $hierarchy.SelectNodes('//node[@class="android.widget.EditText"]') |
                Select-Object -First 1
            if (-not $firstTextField) {
                throw "No Android text field found for keyboard case: $($entry.Name)"
            }
            $bounds = [regex]::Match($firstTextField.bounds, '\[(\d+),(\d+)\]\[(\d+),(\d+)\]')
            if (-not $bounds.Success) {
                throw "Invalid Android text-field bounds: $($firstTextField.bounds)"
            }
            $tapX = ([int]$bounds.Groups[1].Value + [int]$bounds.Groups[3].Value) / 2
            $tapY = ([int]$bounds.Groups[2].Value + [int]$bounds.Groups[4].Value) / 2
            Invoke-AdbChecked @('shell', 'input', 'tap', [string]$tapX, [string]$tapY) | Out-Null
            Start-Sleep -Seconds 1
            $inputMethodState = (Invoke-AdbChecked @('shell', 'dumpsys', 'input_method')) -join "`n"
            if ($inputMethodState -notmatch 'mInputShown=true|mIsInputViewShown=true') {
                throw "Android soft keyboard did not open for: $($entry.Name)"
            }
        }
        $remote = "/sdcard/$($entry.Name).png"
        $local = Join-Path $outputRoot "$($entry.Name).png"
        Invoke-AdbChecked @('shell', 'screencap', '-p', $remote) | Out-Null
        Invoke-AdbChecked @('pull', $remote, $local) | Out-Null
        Invoke-AdbChecked @('shell', 'rm', $remote) | Out-Null
        if (-not (Test-Path -LiteralPath $local) -or (Get-Item $local).Length -lt 5000) {
            throw "Invalid Android screenshot: $local"
        }
        Write-Host "Saved $($entry.Name).png"
    }
} finally {
    if ($originalSizeMatch.Success) {
        & $adb shell wm size $originalSizeMatch.Groups[1].Value | Out-Null
    } else {
        & $adb shell wm size reset | Out-Null
    }
    if ($originalDensityMatch.Success) {
        & $adb shell wm density $originalDensityMatch.Groups[1].Value | Out-Null
    } else {
        & $adb shell wm density reset | Out-Null
    }
    & $adb shell settings put system font_scale $originalFontScale | Out-Null
    & $adb shell cmd uimode night $originalNightMode | Out-Null
    foreach ($navigation in @('gestural', 'threebutton')) {
        & $adb shell cmd overlay disable "com.android.internal.systemui.navbar.$navigation" | Out-Null
    }
    if ($originalNavigationOverlay) {
        & $adb shell cmd overlay enable-exclusive --category $originalNavigationOverlay | Out-Null
    }
}

$metadata = [ordered]@{
    capturedAt = (Get-Date).ToString('o')
    cases = $matrix
    original = [ordered]@{
        size = $originalSizeText
        density = $originalDensityText
        fontScale = $originalFontScale
        nightMode = $originalNightMode
        navigationOverlay = $originalNavigationOverlay
    }
}
$metadata | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $outputRoot 'matrix.json') -Encoding UTF8
Write-Host "Android UI captures saved to $outputRoot"
