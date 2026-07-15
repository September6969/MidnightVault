# MidnightVault

MidnightVault 是一套私人使用的 Android 到 Windows 外接硬盘增量备份系统。两台 Android 手机每天北京时间 00:00 扫描照片、视频、下载、文档及用户授权目录，通过 HTTPS 分块直传 Windows 11 PC；Termux 手机服务器只承担配对、设备管理与任务状态协调，不中转文件内容。

## 当前状态

仓库已经建立首个可开发纵向骨架：

- `android/`：Kotlin、Jetpack Compose、AlarmManager 与 WorkManager 调度入口；
- `windows/`：WinUI 3 管理界面、ASP.NET Core 接收服务、路径与完整性核心库；
- `server/`：面向 Termux ARM64 的 Go 控制面服务；
- `protocol/`：跨端 HTTP 协议定义；
- `docs/`：已确认需求、架构、安全模型、部署方案与路线图。

首个里程碑不是完整成品。当前重点是让“创建上传任务 → 8MB 分块 → SHA-256 校验 → 安全落盘 → 状态可见”成为可测试闭环，随后再接入 Android MediaStore/SAF 扫描和二维码配对。

## 快速验证

在 PowerShell 中运行：

```powershell
.\scripts\verify.ps1
```

本地启动 Windows 接收服务：

```powershell
$env:Receiver__RootPath = 'E:\MidnightVaultData'
dotnet run --project .\windows\src\MidnightVault.Receiver
```

健康检查地址为 `http://127.0.0.1:5188/healthz`。公网部署必须配置 HTTPS、Cloudflare Tunnel 和上传令牌，禁止直接暴露明文 HTTP。

## 资料布局

```text
备份根目录/
  设备名称/
    首次备份_2026-07-16/
    2026-07-17/
      照片/DCIM/Camera/...
      视频/Movies/...
      下载/课程资料/...
      文档/...
  .midnight-vault/
    catalog.db
    staging/
```

完整产品约束见 [产品需求](docs/PRD.md) 与 [架构说明](docs/architecture.md)。
