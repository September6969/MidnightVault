#!/data/data/com.termux/files/usr/bin/sh
set -eu

CONFIG="$HOME/.config/midnightvault/control.env"
if [ ! -f "$CONFIG" ]; then
    printf "缺少 %s，请先运行 install.sh。\n" "$CONFIG" >&2
    exit 1
fi
. "$CONFIG"
export MIDNIGHTVAULT_CONTROL_TOKEN

if command -v termux-wake-lock >/dev/null 2>&1; then
    termux-wake-lock
fi

exec "$HOME/.local/lib/midnightvault/midnightvault-server" \
    -addr 127.0.0.1:8090 \
    -data "$HOME/.local/share/midnightvault"
