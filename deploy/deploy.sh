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
# -E hace que el trap ERR se herede en funciones. El de deploy_api se define
# dentro de la propia funcion, asi que hoy saltaria igual; con -E el
# comportamiento deja de depender de ese detalle si el trap se mueve fuera.
set -Eeuo pipefail

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
    tar -C "$PUBLISH_WEB/wwwroot" -czf - . | ssh "$SSH_HOST" "tar -C '$REMOTE_APP' -xzf - --no-overwrite-dir --no-same-owner"

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
target="$REMOTE_API"
backup="$BACKUP_API"
mkdir -p "\$target" "\$backup"

sudo systemctl stop triker-api 2>/dev/null || true

# Guardar appsettings existentes
rm -f "\$backup"/appsettings*.json
find "\$target" -maxdepth 1 -name 'appsettings*.json' \
    -exec cp {} "\$backup/" \; 2>/dev/null || true

# Limpiar el destino (excepto appsettings)
find "\$target" -maxdepth 1 -mindepth 1 ! -name 'appsettings*.json' \
    -exec rm -rf {} + 2>/dev/null || true
SCRIPT_END

    # A partir de aqui la API esta parada. Si algo falla, set -e aborta el
    # script y la deja caida (paso de verdad: un fallo de tar tumbo produccion
    # hasta que se arranco a mano). El trap la vuelve a levantar antes de salir.
    trap 'echo "!! Deploy abortado; reintentando arrancar la API..." >&2; ssh "$SSH_HOST" "sudo systemctl start triker-api" || echo "!! No se pudo arrancar: la API sigue CAIDA" >&2' ERR

    log "Copiando archivos al destino..."
    # Mismos flags que en deploy_web, que ya los tenía: el destino es de
    # webapps y nos conectamos como angel, asi que sin --no-overwrite-dir
    # --no-same-owner tar intenta aplicar duenyo/permisos/fecha al propio
    # directorio ".", falla con "Cannot utime / Cannot change mode" y sale
    # con estado 2. --warning=no-timestamp calla el aviso por desfase de
    # reloj entre la maquina de desarrollo y el servidor, que tambien cuenta
    # como error para tar.
    tar -C "$PUBLISH_API" -czf - . \
        | ssh "$SSH_HOST" "tar -C '$REMOTE_API' -xzf - --no-overwrite-dir --no-same-owner --warning=no-timestamp"

    log "Restaurando appsettings de producción e iniciando servicio..."
    ssh "$SSH_HOST" bash <<SCRIPT_END
set -euo pipefail
target="$REMOTE_API"
backup="$BACKUP_API"

for f in "\$backup"/appsettings*.json; do
    [ -f "\$f" ] || continue
    cp "\$f" "\$target/"
    echo "    Conservado existente: \$(basename "\$f")"
done

rm -rf "\$backup"

SCRIPT_END

    log "Instalando servicio systemd de sistema..."
    scp "$SCRIPT_DIR/triker-api.service" "$SSH_HOST:/tmp/triker-api.service"
    ssh "$SSH_HOST" "sudo cp /tmp/triker-api.service /etc/systemd/system/triker-api.service && rm /tmp/triker-api.service"

    log "Iniciando servicio API..."
    ssh "$SSH_HOST" bash <<'SCRIPT_END'
sudo systemctl daemon-reload
sudo systemctl enable triker-api
sudo systemctl start triker-api
sleep 2
systemctl status triker-api --no-pager || true
SCRIPT_END

    trap - ERR

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
