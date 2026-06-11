#!/usr/bin/env bash
# =============================================================================
# deploy.sh — Despliega Triker en el servidor de producción
#
# Uso:  ./deploy/deploy.sh [web|api|all]   (por defecto: all)
#
# Prerrequisitos (una sola vez, en el servidor — requiere acceso de admin):
#   1. ~/.ssh/config debe tener el alias 'agfserver-angel' apuntando a webapps@host
#   2. Los directorios /apps/triker/app y /apps/triker/api deben ser de webapps
#   3. loginctl enable-linger webapps         (para servicios systemd de usuario)
#   4. Añadir al server block de nginx y recargar:
#        include /apps/triker/nginx-triker.conf;
# =============================================================================
set -euo pipefail

SSH_HOST="agfserver-angel"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

REMOTE_APP="/apps/triker/app"
REMOTE_API="/apps/triker/api"
NGINX_CONF="/apps/triker/nginx-triker.conf"

PUBLISH_WEB="$ROOT_DIR/publish/web"
PUBLISH_API="$ROOT_DIR/publish/api"

# Directorio temporal en el servidor sólo para los appsettings durante el deploy
BACKUP_APP="/tmp/triker-deploy/app-settings"
BACKUP_API="/tmp/triker-deploy/api-settings"

log() { echo "==> $*"; }

# ── Web ──────────────────────────────────────────────────────────────────────

deploy_web() {
    log "Compilando Triker.Web (Blazor WASM)..."
    dotnet publish "$ROOT_DIR/Triker.Web/Triker.Web.csproj" \
        -c Release \
        -o "$PUBLISH_WEB" \
        --nologo --verbosity minimal

    log "Preparando destino en el servidor..."
    ssh "$SSH_HOST" bash <<SCRIPT_END
set -euo pipefail
target="$REMOTE_APP"
backup="$BACKUP_APP"
mkdir -p "\$target" "\$backup"

# Guardar appsettings existentes
rm -f "\$backup"/appsettings*.json
find "\$target" -maxdepth 1 -name 'appsettings*.json' \
    -exec cp {} "\$backup/" \; 2>/dev/null || true

# Limpiar el destino (excepto appsettings, para no dejar la carpeta vacía si falla el scp)
find "\$target" -maxdepth 1 -mindepth 1 ! -name 'appsettings*.json' \
    -exec rm -rf {} + 2>/dev/null || true
SCRIPT_END

    log "Copiando archivos al destino..."
    scp -r "$PUBLISH_WEB/wwwroot/." "$SSH_HOST:$REMOTE_APP/"

    log "Parcheando base href para producción..."
    ssh "$SSH_HOST" "sed -i 's|<base href=\"/triker/\" />|<base href=\"/\" />|' $REMOTE_APP/index.html"

    log "Restaurando appsettings de producción..."
    ssh "$SSH_HOST" bash <<SCRIPT_END
set -euo pipefail
target="$REMOTE_APP"
backup="$BACKUP_APP"

for f in "\$backup"/appsettings*.json; do
    [ -f "\$f" ] || continue
    cp "\$f" "\$target/"
    echo "    Conservado existente: \$(basename "\$f")"
done

rm -rf "\$backup"
SCRIPT_END

    log "Subiendo nginx config..."
    scp "$SCRIPT_DIR/nginx-triker.conf" "$SSH_HOST:$NGINX_CONF"

    log "Web desplegada correctamente."
    echo ""
    echo "    NOTA: si nginx aún no incluye el config, añade esto al server block"
    echo "    y recarga nginx (pide al admin que ejecute: nginx -s reload):"
    echo ""
    echo "        include $NGINX_CONF;"
    echo ""
}

# ── API ──────────────────────────────────────────────────────────────────────

deploy_api() {
    log "Compilando Triker.Api (linux-x64)..."
    dotnet publish "$ROOT_DIR/Triker.Api/Triker.Api.csproj" \
        -c Release \
        -r linux-x64 \
        --no-self-contained \
        -o "$PUBLISH_API" \
        --nologo --verbosity minimal

    log "Deteniendo servicio API y preparando destino..."
    ssh "$SSH_HOST" bash <<SCRIPT_END
set -euo pipefail
export XDG_RUNTIME_DIR="/run/user/\$(id -u)"
target="$REMOTE_API"
backup="$BACKUP_API"
mkdir -p "\$target" "\$backup"

systemctl --user stop triker-api 2>/dev/null || true

# Guardar appsettings existentes
rm -f "\$backup"/appsettings*.json
find "\$target" -maxdepth 1 -name 'appsettings*.json' \
    -exec cp {} "\$backup/" \; 2>/dev/null || true

# Limpiar el destino (excepto appsettings)
find "\$target" -maxdepth 1 -mindepth 1 ! -name 'appsettings*.json' \
    -exec rm -rf {} + 2>/dev/null || true
SCRIPT_END

    log "Copiando archivos al destino..."
    scp -r "$PUBLISH_API/." "$SSH_HOST:$REMOTE_API/"

    log "Restaurando appsettings de producción e iniciando servicio..."
    ssh "$SSH_HOST" bash <<SCRIPT_END
set -euo pipefail
export XDG_RUNTIME_DIR="/run/user/\$(id -u)"
target="$REMOTE_API"
backup="$BACKUP_API"

for f in "\$backup"/appsettings*.json; do
    [ -f "\$f" ] || continue
    cp "\$f" "\$target/"
    echo "    Conservado existente: \$(basename "\$f")"
done

rm -rf "\$backup"

mkdir -p ~/.config/systemd/user
SCRIPT_END

    log "Instalando servicio systemd de usuario..."
    scp "$SCRIPT_DIR/triker-api.service" "$SSH_HOST:~/.config/systemd/user/triker-api.service"

    log "Iniciando servicio API..."
    ssh "$SSH_HOST" bash <<'SCRIPT_END'
export XDG_RUNTIME_DIR="/run/user/$(id -u)"
systemctl --user daemon-reload
systemctl --user enable triker-api
systemctl --user start triker-api
sleep 2
systemctl --user status triker-api --no-pager || true
SCRIPT_END

    log "API desplegada correctamente."
}

# ── Main ──────────────────────────────────────────────────────────────────────

case "${1:-all}" in
    web) deploy_web ;;
    api) deploy_api ;;
    all) deploy_web; deploy_api ;;
    *)
        echo "Uso: $0 [web|api|all]"
        exit 1
        ;;
esac
