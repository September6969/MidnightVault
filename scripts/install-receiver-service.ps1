param(
    [Parameter(Mandatory)]
    [string]$RootPath,

    [string]$PublishedReceiverDirectory = '',
    [int]$Port = 5053,
    [ValidateRange(1, 1024)]
    [int]$MinimumFreeSpaceGB = 50,
    [string]$ServiceName = 'MidnightVaultReceiver'
)

$ErrorActionPreference = 'Stop'
$packagedReceiver = Join-Path $PSScriptRoot 'Receiver'
if ([string]::IsNullOrWhiteSpace($PublishedReceiverDirectory)) {
    $PublishedReceiverDirectory = if (Test-Path -LiteralPath $packagedReceiver) {
        $packagedReceiver
    } else {
        'E:\MidnightVaultDist\windows\Receiver'
    }
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请以管理员身份运行此脚本。'
}

$resolvedRoot = [IO.Path]::GetFullPath($RootPath)
$systemRoot = [IO.Path]::GetPathRoot($env:SystemRoot)
if ([IO.Path]::GetPathRoot($resolvedRoot) -eq $systemRoot) {
    throw '为防止误写系统盘，Receiver 根目录不能位于 Windows 系统盘。'
}
$receiverExe = Join-Path $PublishedReceiverDirectory 'MidnightVault.Receiver.exe'
if (-not (Test-Path -LiteralPath $receiverExe)) {
    throw "未找到已发布接收服务：$receiverExe。先运行 scripts\publish-windows.ps1。"
}

New-Item -ItemType Directory -Force -Path $resolvedRoot | Out-Null
$tokenBytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$uploadToken = [Convert]::ToHexString($tokenBytes).ToLowerInvariant()
$minimumFreeSpaceBytes = $MinimumFreeSpaceGB * 1GB
$binaryPath = "`"$receiverExe`" --urls `"http://0.0.0.0:$Port`" --Receiver:RootPath=`"$resolvedRoot`" --Receiver:UploadToken=`"$uploadToken`" --Receiver:MinimumFreeSpaceBytes=$minimumFreeSpaceBytes"

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '删除旧接收服务失败。' }
}
New-Service `
    -Name $ServiceName `
    -BinaryPathName $binaryPath `
    -DisplayName 'MidnightVault Receiver' `
    -Description '接收 Android 加密签名分块并写入指定备份硬盘。' `
    -StartupType Automatic | Out-Null

$firewallName = 'MidnightVault Receiver (Private)'
Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule `
    -DisplayName $firewallName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $Port `
    -Profile Private | Out-Null

Start-Service -Name $ServiceName
$health = $null
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try {
        $health = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/healthz" -TimeoutSec 2
        break
    } catch {
        Start-Sleep -Milliseconds 500
    }
}
if (-not $health) { throw '服务已安装，但健康检查未在 15 秒内通过。' }

$volumeId = $health.storage.volumeId
$pinnedBinaryPath = "$binaryPath --Receiver:ExpectedVolumeId=`"$volumeId`""
sc.exe config $ServiceName binPath= $pinnedBinaryPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw '写入硬盘卷身份失败。' }

$settingsDirectory = Join-Path $env:LOCALAPPDATA 'MidnightVault'
New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null
$settings = [ordered]@{
    backupRootPath = $resolvedRoot
    expectedVolumeId = $volumeId
    receiverPort = $Port
    controlPlaneUrl = $null
}
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $settingsDirectory 'desktop-settings.json') -Encoding utf8

$entropy = [Text.Encoding]::UTF8.GetBytes('MidnightVault.UploadToken.v1')
$plaintext = [Text.Encoding]::UTF8.GetBytes($uploadToken)
$protected = [Security.Cryptography.ProtectedData]::Protect(
    $plaintext,
    $entropy,
    [Security.Cryptography.DataProtectionScope]::CurrentUser)
[IO.File]::WriteAllBytes((Join-Path $settingsDirectory 'upload-token.bin'), $protected)

$action = New-ScheduledTaskAction -Execute 'PowerShell.exe' -Argument "-NoProfile -WindowStyle Hidden -Command `"Start-Service -Name '$ServiceName'`""
$trigger = New-ScheduledTaskTrigger -Daily -At '23:55'
$principalSettings = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet -WakeToRun -StartWhenAvailable
Register-ScheduledTask `
    -TaskName 'MidnightVault Receiver Wake' `
    -Action $action `
    -Trigger $trigger `
    -Principal $principalSettings `
    -Settings $taskSettings `
    -Force | Out-Null

Write-Host "Receiver 已安装并绑定卷 $volumeId。"
Write-Host '上传令牌已使用当前 Windows 用户 DPAPI 保存，WinUI 可直接生成一次性二维码。'
