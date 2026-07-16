# MidnightVault

MidnightVault 是一套私人使用的 Android → Windows 11 外接硬盘增量备份系统。两台 Android 手机每天北京时间 00:00 扫描照片、视频、下载、文档和用户授权目录，使用可断点续传的 8MB 分块直接上传到 PC；Termux 手机服务器只保存设备、公钥、任务摘要和远程指令，不中转文件内容。

## 当前完成度

代码已经形成可安装、可运行的私人 Beta 闭环：

- Android：MediaStore/SAF 扫描、SQLite 持久队列、SHA-256、断点续传、精确午夜闹钟、WorkManager 补偿、开机恢复、一次性二维码配对、Android Keystore 设备密钥和任务通知；
- Windows Receiver：设备签名与防重放、分块会话恢复、外接盘身份锁定、50GB/10% 空间保护、安全路径、日期归档、SQLite 索引、每日清单、Windows Service、23:55 唤醒和上传期间防睡眠；
- Windows 客户端：概览、资料库搜索与筛选、图片/视频/PDF/TXT 预览、Office 系统打开、E 盘预览缓存、二维码配对、双端设备撤销和控制面任务；
- Termux：Go ARM64 单文件控制面、SQLite 设备/任务/指令、P-256 签名、防重放和撤销；
- 工程化：三端测试、OpenAPI 契约、GitHub Actions CI/发行、Windows/Termux 发布与安装脚本、Android 私人签名配置。

尚需在你的真实两台手机、外接硬盘和网络环境完成一次 50GB 级物理验收；这类硬件验证无法由仓库内的自动测试替代。

## 目录

```text
android/     Android 客户端（Kotlin、Compose）
windows/     WinUI 3 客户端、ASP.NET Core Receiver 与测试
server/      Termux Go 控制面
protocol/    OpenAPI 3.1 契约
scripts/     验证、发布和 Windows Service 安装脚本
docs/        产品、架构、安全与部署文档
```

## 快速验证

在 Windows PowerShell 中运行：

```powershell
Set-Location E:\MidnightVault
.\scripts\verify.ps1
```

验证会运行 .NET 测试与构建、Go 测试与静态检查，以及 Android 单元测试、Lint 和 Debug APK 构建。开发工具和大体积缓存统一放在 `E:\DevCache` / `E:\DevTools`。

## 首次部署

1. 运行 `scripts\publish-windows.ps1` 生成 Windows x64 发行包。
2. 以管理员 PowerShell 运行 `scripts\install-receiver-service.ps1 -RootPath 'X:\手机备份'`；`X:` 必须是外接硬盘而非系统盘。
3. 启动 WinUI 客户端，在“设置”确认硬盘和手机服务器，在“设备”生成一次性二维码。
4. 运行 `scripts\build-termux.ps1`，把 `E:\MidnightVaultDist\termux` 复制到 Termux 并执行 `install.sh`。
5. 安装 Android APK，授权照片/视频和所需 SAF 目录，用系统相机扫描 PC 二维码。

详细命令、签名 Secrets、Cloudflare HTTPS 和恢复步骤见 [部署说明](docs/deployment.md)。安全边界见 [安全模型](docs/security.md)。

## 归档布局

```text
备份根目录/
  手机名称/
    首次备份_2026-07-16/
    2026-07-17/
      照片/DCIM/Camera/...
      视频/Movies/...
      下载/...
      文档/...
      自定义/...
  .midnight-vault/
    catalog.db
    staging/
    volume.json
```

备份只增不删：手机文件被删除或修改不会删除硬盘中的历史实体；文件保持原始字节、EXIF 和修改时间。
