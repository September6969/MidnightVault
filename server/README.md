# Termux 控制面

该服务只保存设备公钥与任务摘要，不接收文件、文件名、路径或缩略图。默认仅监听 `127.0.0.1:8090`，由手机上的 Cloudflare Tunnel 提供外部 HTTPS 入口。

```sh
export MIDNIGHTVAULT_CONTROL_TOKEN='请替换为随机长令牌'
./midnightvault-server -addr 127.0.0.1:8090 -data "$HOME/.local/share/midnightvault"
```

生产部署必须设置 `MIDNIGHTVAULT_CONTROL_TOKEN`，并将数据目录权限限制为当前 Termux 用户。
