#!/usr/bin/env bash
set -euo pipefail

REMOTE_USER="angel"
REMOTE_HOST="192.168.0.175"
REMOTE_DIR="/var/www/triker"
PUBLISH_DIR="$(dirname "$0")/../publish/web"
SNIPPET_PATH="/etc/nginx/snippets/triker.conf"

echo "==> Publicando Triker.Web (Blazor WASM)..."
dotnet publish "$(dirname "$0")/../Triker.Web/Triker.Web.csproj" \
    -c Release \
    -o "$PUBLISH_DIR"

echo "==> Copiando archivos estáticos al servidor..."
ssh "${REMOTE_USER}@${REMOTE_HOST}" "rm -rf ~/deploy/web && mkdir -p ~/deploy/web"
scp -r "$PUBLISH_DIR/wwwroot/." "${REMOTE_USER}@${REMOTE_HOST}:~/deploy/web/"
ssh "${REMOTE_USER}@${REMOTE_HOST}" \
    "sudo mkdir -p ${REMOTE_DIR} && sudo rsync -a --delete ~/deploy/web/ ${REMOTE_DIR}/"

echo "==> Instalando snippet de nginx..."
scp "$(dirname "$0")/nginx-triker.conf" \
    "${REMOTE_USER}@${REMOTE_HOST}:/tmp/nginx-triker.conf"

ssh "${REMOTE_USER}@${REMOTE_HOST}" bash <<EOF
    sudo mkdir -p /etc/nginx/snippets
    sudo mv /tmp/nginx-triker.conf ${SNIPPET_PATH}

    ALREADY=\$(grep -rl "snippets/triker.conf" /etc/nginx/ 2>/dev/null | head -1 || true)
    if [ -n "\$ALREADY" ]; then
        echo "Include ya presente en \$ALREADY"
    else
        echo ""
        echo "ACCION MANUAL REQUERIDA:"
        echo "Añade esta línea dentro del bloque server{} de angelgf.com.es:"
        echo ""
        echo "    include ${SNIPPET_PATH};"
        echo ""
        echo "Luego ejecuta: sudo nginx -t && sudo systemctl reload nginx"
        exit 0
    fi

    sudo nginx -t && sudo systemctl reload nginx
    echo "nginx recargado correctamente."
EOF

echo "==> Frontend desplegado correctamente."
