$ErrorActionPreference = 'Stop'

$useDriveE = Test-Path -LiteralPath 'E:\'
if ($useDriveE) {
    $cacheRoot = 'E:\DevCache'
    $toolsRoot = 'E:\DevTools'
} else {
    $base = if (-not [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
        $env:RUNNER_TEMP
    } else {
        Join-Path $env:LOCALAPPDATA 'MidnightVault'
    }
    $cacheRoot = Join-Path $base 'DevCache'
    $toolsRoot = Join-Path $base 'DevTools'
}

$env:NUGET_PACKAGES = Join-Path $cacheRoot 'nuget'
$env:GRADLE_USER_HOME = Join-Path $cacheRoot 'gradle'
$env:GOPATH = Join-Path $cacheRoot 'go'
$env:GOMODCACHE = Join-Path $cacheRoot 'go\pkg\mod'
$env:ANDROID_USER_HOME = Join-Path $cacheRoot 'android'
$env:ANDROID_AVD_HOME = Join-Path $cacheRoot 'android\avd'
$env:DOTNET_CLI_HOME = Join-Path $cacheRoot 'dotnet'

if ($useDriveE -or [string]::IsNullOrWhiteSpace($env:ANDROID_HOME)) {
    $env:ANDROID_HOME = Join-Path $toolsRoot 'AndroidSdk'
}
if ($useDriveE -or [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) {
    $env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
}

$java17Home = $env:MIDNIGHTVAULT_JAVA17_HOME
$portableJavaRoot = Join-Path $toolsRoot 'jdk-17-portable'
if ([string]::IsNullOrWhiteSpace($java17Home) -and
    (Test-Path -LiteralPath $portableJavaRoot)) {
    $java17Home = Get-ChildItem -LiteralPath $portableJavaRoot -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'bin\java.exe') } |
        Select-Object -ExpandProperty FullName -First 1
}
if (-not [string]::IsNullOrWhiteSpace($java17Home) -and
    (Test-Path -LiteralPath (Join-Path $java17Home 'bin\java.exe'))) {
    $env:JAVA_HOME = $java17Home
    $java17Bin = Join-Path $java17Home 'bin'
    if ($env:Path -notlike "$java17Bin;*") {
        $env:Path = "$java17Bin;$env:Path"
    }
}

$goBin = Join-Path $toolsRoot 'Go\bin'
$androidPlatformTools = Join-Path $env:ANDROID_HOME 'platform-tools'
foreach ($path in @($goBin, $androidPlatformTools)) {
    if ((Test-Path -LiteralPath $path) -and ($env:Path -notlike "*$path*")) {
        $env:Path = "$path;$env:Path"
    }
}

Write-Host "MidnightVault development caches use $cacheRoot."
