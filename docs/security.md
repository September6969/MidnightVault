# 安全模型

## 受保护资产

- 照片、视频、文档与下载文件；
- 文件名、目录结构、时间与 EXIF；
- 设备私钥、上传凭据、Cloudflare 凭据；
- PC 备份索引与 Termux 任务摘要。

## 信任边界

1. Android 应用与系统 MediaStore/SAF；
2. Android 到 Cloudflare/PC 的公网传输；
3. Windows 接收服务到外接硬盘；
4. Android/PC 到 Termux 控制面；
5. WinUI 当前用户与 Windows 后台服务。

## 已实现基线

- HTTP 请求体限制；
- 上传令牌恒定时间比较；
- 8MB 分块与分块哈希；
- 整文件 SHA-256；
- Windows 非法文件名稳定转义；
- 路径标准化与根目录边界检查；
- 同名不同内容使用哈希后缀；
- Termux SQLite 仅保存设备和任务摘要；
- 无令牌时只允许回环地址进行本地开发。

## 上线前必须完成

- 用二维码一次性配对和每设备公私钥替换共享上传令牌；
- 每个请求加入时间戳、随机数与签名，防止重放；
- Cloudflare Tunnel 强制 HTTPS，不开放路由器入站端口；
- Windows 服务账号仅拥有指定卷目录的写权限；
- 设备撤销列表与凭据轮换；
- SQLite 索引备份、迁移和损坏恢复测试；
- 日志脱敏与 30 天自动轮换；
- 安装包和 APK 使用私人签名密钥签名，密钥不入库。

## 明确禁止

- 在代码、`appsettings.json`、Gradle 文件或 GitHub Actions 中写入真实令牌；
- 将 PC 上传接口扩展为远程下载或删除接口；
- 使用 `MANAGE_EXTERNAL_STORAGE` 绕过 Android 目录授权；
- 上传失败时回退到 Termux 中转文件；
- 硬盘不可用时静默写入 Windows 系统盘。
