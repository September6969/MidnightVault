$ErrorActionPreference = 'Stop'

$env:NUGET_PACKAGES = 'E:\DevCache\nuget'
$env:GRADLE_USER_HOME = 'E:\DevCache\gradle'
$env:GOPATH = 'E:\DevCache\go'
$env:GOMODCACHE = 'E:\DevCache\go\pkg\mod'
$env:ANDROID_HOME = 'E:\DevTools\AndroidSdk'
$env:ANDROID_SDK_ROOT = 'E:\DevTools\AndroidSdk'
$env:ANDROID_USER_HOME = 'E:\DevCache\android'
$env:ANDROID_AVD_HOME = 'E:\DevCache\android\avd'
$env:DOTNET_CLI_HOME = 'E:\DevCache\dotnet'

$goBin = 'E:\DevTools\Go\bin'
$androidPlatformTools = 'E:\DevTools\AndroidSdk\platform-tools'
foreach ($path in @($goBin, $androidPlatformTools)) {
    if ((Test-Path -LiteralPath $path) -and ($env:Path -notlike "*$path*")) {
        $env:Path = "$path;$env:Path"
    }
}

Write-Host 'MidnightVault development caches and tools use drive E.'
