# 私人部署说明

## 0. 前置条件

- Windows 11 x64、PowerShell 7、.NET 10 SDK；
- Android 10 或更高版本；
- Termux 手机建议安装 Termux:API，以便 `termux-wake-lock` 生效；
- 一块非系统盘的外接硬盘，至少预留 50GB 且保持 10% 空闲；
- 公网使用时准备两个 HTTPS 地址：PC Receiver 数据面、Termux 控制面。局域网首次配对可使用 HTTP。

仓库开发缓存和 SDK 已配置到 E 盘：`E:\DevCache` 与 `E:\DevTools`。

## 1. Windows 11

在普通 PowerShell 中发布：

```powershell
Set-Location E:\MidnightVault
.\scripts\publish-windows.ps1
```

输出：

```text
E:\MidnightVaultDist\windows\Receiver
E:\MidnightVaultDist\windows\Desktop
E:\MidnightVaultDist\windows\MidnightVault-Receiver-win-x64.zip
E:\MidnightVaultDist\windows\MidnightVault-Desktop-win-x64.zip
E:\MidnightVaultDist\windows\MidnightVault-Windows-win-x64.zip
```

随后以管理员身份打开 PowerShell，把 `X:` 换成你的外接硬盘：

```powershell
Set-Location E:\MidnightVault
.\scripts\install-receiver-service.ps1 `
  -RootPath 'X:\手机备份' `
  -Port 5053 `
  -MinimumFreeSpaceGB 50
```

脚本会完成：

- 安装自动启动的 `MidnightVaultReceiver` Windows Service；
- 仅为“专用网络”放行 TCP 5053；
- 在硬盘写入 `.midnight-vault\volume.json` 并锁定卷身份；
- 使用当前 Windows 用户 DPAPI 保存上传令牌；
- 建立每天 23:55、`WakeToRun` 的计划任务；
- 把 WinUI 设置写到 `%LOCALAPPDATA%\MidnightVault`。

Receiver 发现身份文件缺失或卷 ID 错误时会先拒绝，再写入任何目录；空间低于 50GB 或总容量 10% 时暂停新文件。收到上传分块后会保持系统唤醒 20 分钟，每个新分块继续延长。

启动客户端：

```powershell
& 'E:\MidnightVaultDist\windows\Desktop\MidnightVault.Desktop.exe'
```

在“设置”确认硬盘状态和控制面地址，再到“设备”生成五分钟有效的一次性二维码。

## 2. Termux 手机服务器

在 Windows 生成 ARM64 包：

```powershell
.\scripts\build-termux.ps1
```

把 `E:\MidnightVaultDist\termux` 中三个文件复制到 Termux 当前目录，然后执行：

```sh
pkg update
pkg install ca-certificates termux-api
chmod 700 install.sh run.sh midnightvault-server
./install.sh
$HOME/.local/lib/midnightvault/run.sh
```

首次安装会生成 32 字节随机控制令牌，保存在权限为 `600` 的：

```text
$HOME/.config/midnightvault/control.env
```

把令牌只填入 Android 与 WinUI；不要发到聊天、日志或提交到 Git。长期运行可将 `run.sh` 放入 Termux:Boot，或在 `tmux` 中运行。

## 3. HTTPS / Cloudflare Tunnel

Receiver 和 Termux 服务自身监听 HTTP，公网 TLS 应在 Cloudflare Tunnel 终止。不要在路由器开放入站端口。

建议的入口映射：

```text
backup.example.com  -> Windows http://127.0.0.1:5053
control.example.com -> Termux  http://127.0.0.1:8090
```

公网数据面只提供健康状态、配对领取和签名上传；资料库、预览、设备列表与撤销接口只接受 Windows 本机回环连接。Cloudflare 凭据和 tunnel 配置属于用户环境，不进入仓库。

## 4. Android

本地 Debug APK：

```powershell
Set-Location E:\MidnightVault\android
.\gradlew.bat lint test assembleDebug
```

位置：`E:\MidnightVault\android\app\build\outputs\apk\debug\app-debug.apk`。

正式私人签名包使用以下环境变量后运行 `assembleRelease`：

```text
MIDNIGHTVAULT_ANDROID_KEYSTORE
MIDNIGHTVAULT_ANDROID_STORE_PASSWORD
MIDNIGHTVAULT_ANDROID_KEY_ALIAS
MIDNIGHTVAULT_ANDROID_KEY_PASSWORD
```

首次运行：

1. 允许通知、照片和视频读取；
2. 用系统目录选择器分别添加下载、文档和其他目录；
3. 允许精确闹钟、自启动和后台运行；
4. 用系统相机扫描 PC 客户端二维码；
5. 如使用手机控制面，填写 HTTPS 地址和管理员令牌并登记设备；
6. 点“立即备份”完成一次小文件试传，再等待午夜任务。

应用不会请求 `MANAGE_EXTERNAL_STORAGE`，也不会删除手机原文件。

## 5. GitHub Actions 私人签名

CI 会自动构建和测试三端。发行工作流在未配置密钥时产出 Debug APK 和便携 Windows/Termux 包；设置下列仓库 Secrets 后会额外产出签名 Release APK 与 MSIX：

```text
ANDROID_KEYSTORE_BASE64
ANDROID_STORE_PASSWORD
ANDROID_KEY_ALIAS
ANDROID_KEY_PASSWORD
WINDOWS_PFX_BASE64
WINDOWS_PFX_PASSWORD
```

PFX 的 Subject 必须与 `Package.appxmanifest` 中的 Publisher 一致。所有二进制密钥只以 Base64 写入 GitHub Secrets，绝不提交文件本身。

## 6. 故障与恢复

- PC/网络离线：Android 队列保留，WorkManager 指数退避，稍后从已确认分块继续；
- 手机重启：开机广播恢复午夜闹钟和控制面轮询；
- Receiver 重启：`.midnight-vault\staging` 中的会话和分块仍可恢复；
- 外接盘断开或换错：健康检查失败，Receiver 不回退到 C 盘；
- Termux 离线：远程“立即备份”和任务摘要暂不可用，已配对手机仍可直连 Receiver；
- 索引/清单：实体文件始终是普通文件；先完整复制硬盘，再处理 `.midnight-vault` 管理目录。
