#!/usr/bin/env bash
set -euo pipefail

REMOTE_USER="angel"
REMOTE_HOST="192.168.0.175"
REMOTE_DIR="/opt/triker/api"
PUBLISH_DIR="$(dirname "$0")/../publish/api"

echo "==> Publicando Triker.Api (linux-arm64)..."
dotnet publish "$(dirname "$0")/../Triker.Api/Triker.Api.csproj" \
    -c Release \
    -r linux-arm64 \
    --no-self-contained \
    -o "$PUBLISH_DIR"

echo "==> Copiando archivos al servidor..."
ssh "${REMOTE_USER}@${REMOTE_HOST}" "rm -rf ~/deploy/api && mkdir -p ~/deploy/api"
scp -r "$PUBLISH_DIR/"* \
    "${REMOTE_USER}@${REMOTE_HOST}:~/deploy/api/"
ssh "${REMOTE_USER}@${REMOTE_HOST}" "sudo rm -rf ${REMOTE_DIR} && sudo mkdir -p ${REMOTE_DIR} && sudo cp -r ~/deploy/api/. ${REMOTE_DIR}/"

echo "==> Instalando archivo de servicio systemd..."
scp "$(dirname "$0")/triker-api.service" \
    "${REMOTE_USER}@${REMOTE_HOST}:/tmp/triker-api.service"

ssh "${REMOTE_USER}@${REMOTE_HOST}" bash <<'EOF'
    sudo mv /tmp/triker-api.service /etc/systemd/system/triker-api.service
    sudo systemctl daemon-reload
    sudo systemctl enable triker-api
    sudo systemctl restart triker-api
    echo "Estado del servicio:"
    sudo systemctl status triker-api --no-pager
EOF

echo "==> API desplegada correctamente."
