# 安全模型

## 受保护资产与边界

- 实体资料：照片、视频、文档、下载文件、目录结构、修改时间和 EXIF；
- 凭据：Android 设备私钥、PC 上传令牌、Termux 控制令牌、Cloudflare 与签名证书；
- 元数据：PC SQLite 索引、每日清单、Termux 设备和任务摘要。

文件数据只走 Android → Windows Receiver。Termux 控制面不接收文件、文件名、路径或缩略图。

## 已实现的防护

- Android 每台设备在 Android Keystore 生成不可导出的 P-256 私钥；
- 上传和控制面设备请求使用 `SHA256withECDSA`，规范串覆盖方法、路径/查询、Unix 时间、随机 nonce 和请求体 SHA-256；
- Windows 与 Termux 使用五分钟时间窗和持久化 nonce 防重放；
- 一次性配对码五分钟过期、只可领取一次，二维码不含长期令牌；
- PC 撤销同时作用于 Windows 数据面和 Termux 控制面；
- 上传令牌在 Android Keystore 加密保存，在 Windows 以当前用户 DPAPI 保存；
- 分块和整文件均校验 SHA-256，完成落盘前使用临时文件和刷盘；
- 路径标准化、非法名称转义、根目录边界检查和请求体上限；
- 外接盘身份锁定；身份文件缺失/不匹配时先拒绝写入，不创建替代目录；
- 空间低于 50GB 或 10% 时拒绝新文件，绝不自动删除旧备份；
- 资料库、预览、设备列表、撤销和配对码创建只允许 Windows 本机回环连接；
- 公网数据面没有文件下载或删除接口；
- Termux 默认只监听 `127.0.0.1:8090`，无控制令牌时仅允许回环开发访问；
- 真实令牌、keystore 和 PFX 均被排除在仓库之外，GitHub Actions 只从 Secrets 注入。

## 部署者必须完成

- 公网必须通过 Cloudflare Tunnel 或等效反向代理强制 HTTPS；不要直连公开 HTTP；
- 把 Windows 网络配置为“专用”，不要放宽 Receiver 防火墙规则到公用网络；
- 保护 `%LOCALAPPDATA%\MidnightVault`、外接盘管理目录和 Termux 配置目录；
- 使用用户自己的 Android keystore 与 Windows PFX，并离线备份私钥；
- Cloudflare 建议对 Windows/管理员入口增加 Access 策略和速率限制；
- 手机丢失时在 WinUI 撤销设备，并视情况轮换上传/控制令牌；
- 第一次上线前在真实硬件执行 `docs/roadmap.md` 的物理验收。

## 已知取舍

- 局域网二维码配对允许 HTTP，以便无内网证书时使用；同一不可信 Wi-Fi 上存在窃听风险，建议仅在可信家庭网络配对，或直接使用 HTTPS Receiver 地址；
- 外接盘按用户要求不启用 BitLocker，盘被物理取走时文件是明文；
- Windows Service 命令行包含随机上传令牌，管理员账户可以读取；普通用户无法读取服务配置，但高权限账户本来就能访问备份内容；
- 备份是只增不删，磁盘占用会持续增长；低空间保护只停止写入，不做自动清理。

## 明确禁止

- 在代码、配置样例、日志或 GitHub Actions YAML 中写真实令牌；
- 使用 `MANAGE_EXTERNAL_STORAGE` 绕过 Android SAF 授权；
- 上传失败时改由 Termux 中转文件；
- 外接盘不可用时静默写入 Windows 系统盘；
- 自动永久删除手机或硬盘上的任何资料。
