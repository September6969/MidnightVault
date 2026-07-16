#!/data/data/com.termux/files/usr/bin/sh
set -eu

PREFIX_DIR="$HOME/.local/lib/midnightvault"
DATA_DIR="$HOME/.local/share/midnightvault"
CONFIG_DIR="$HOME/.config/midnightvault"

mkdir -p "$PREFIX_DIR" "$DATA_DIR" "$CONFIG_DIR"
chmod 700 "$PREFIX_DIR" "$DATA_DIR" "$CONFIG_DIR"
cp ./midnightvault-server "$PREFIX_DIR/midnightvault-server"
cp ./run.sh "$PREFIX_DIR/run.sh"
chmod 700 "$PREFIX_DIR/midnightvault-server" "$PREFIX_DIR/run.sh"

if [ ! -f "$CONFIG_DIR/control.env" ]; then
    TOKEN="$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')"
    printf "MIDNIGHTVAULT_CONTROL_TOKEN='%s'\n" "$TOKEN" > "$CONFIG_DIR/control.env"
    chmod 600 "$CONFIG_DIR/control.env"
    printf "已生成控制令牌：%s\n" "$TOKEN"
    printf "请立即保存到 Android 与 PC，然后不要在聊天或代码仓库中发送。\n"
fi

printf "安装完成。启动：%s/run.sh\n" "$PREFIX_DIR"
