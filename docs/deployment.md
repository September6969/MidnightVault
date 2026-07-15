# 私人部署说明

## Windows 11

开发阶段：

```powershell
.\scripts\dev-env.ps1
dotnet build .\MidnightVault.slnx -p:Platform=x64
$env:Receiver__RootPath = 'X:\手机备份'
$env:Receiver__UploadToken = '使用密码生成器创建的随机长令牌'
dotnet run --project .\windows\src\MidnightVault.Receiver
```

正式部署将包含：

- WinUI MSIX 私人安装包；
- 独立 Windows Service 接收进程；
- 23:55 唤醒计划任务；
- Cloudflare Tunnel 指向接收服务的回环 HTTPS 地址；
- 硬盘卷唯一 ID、空间阈值和根目录配置向导。

## Termux

```sh
pkg install ca-certificates
mkdir -p "$HOME/.local/share/midnightvault"
export MIDNIGHTVAULT_CONTROL_TOKEN='随机长令牌'
./midnightvault-server \
  -addr 127.0.0.1:8090 \
  -data "$HOME/.local/share/midnightvault"
```

建议使用 `termux-wake-lock`、`tmux` 与现有 `cloudflared tunnel run` 保持服务。Cloudflare 公网主机名和访问策略在部署时单独配置，凭据不得提交到仓库。

## Android

第一版通过私人签名 APK 安装。首次运行需要：

1. 允许通知；
2. 允许照片和视频读取；
3. 通过系统目录选择器授权下载、文档和其他目录；
4. 允许精确闹钟；
5. 根据手机厂商设置允许自启动、后台运行和忽略电池优化；
6. 扫描 PC 客户端显示的一次性配对二维码。

## 开发缓存

本机用户环境已经配置为：

```text
NUGET_PACKAGES=E:\DevCache\nuget
GRADLE_USER_HOME=E:\DevCache\gradle
GOPATH=E:\DevCache\go
GOMODCACHE=E:\DevCache\go\pkg\mod
ANDROID_USER_HOME=E:\DevCache\android
ANDROID_AVD_HOME=E:\DevCache\android\avd
DOTNET_CLI_HOME=E:\DevCache\dotnet
```

Android SDK 使用 `E:\DevTools\AndroidSdk`，避免在 C 盘生成大型 SDK 与模拟器缓存。
