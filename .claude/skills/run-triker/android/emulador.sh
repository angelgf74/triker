#!/usr/bin/env bash
# Utilidades para manejar la app en el emulador (o un móvil) por adb, desde Git Bash:
#
#   source emulador.sh
#   arriba                 qué actividad está delante
#   textos [n]             los textos que hay en pantalla, con su posición
#   pulsa "Texto"          pulsa el primer elemento cuyo texto lo contiene
#   conecta                abre en localhost:9333 la depuración del WebView (para cdp.mjs)
#   cierra_anuncio         cierra el intersticial de prueba, solo si está delante
#
# El dispositivo es $DEV (por defecto, el emulador). Para un móvil por depuración
# inalámbrica: DEV=<ip:puerto> antes del source.
#
# Avisos, aprendidos a base de tropezar:
#   - Nunca pulsar a ciegas: comprobar `arriba` antes. Con la app cerrada, el toque cae
#     en el escritorio. cierra_anuncio ya lo hace.
#   - Un emulador recién arrancado actualiza solo WebView y Google Play Services, y cada
#     instalación mata las apps que los usan. Si la app desaparece sin motivo, es eso.
#   - En Git Bash las rutas /sdcard/... se convierten en rutas de Windows: van con //sdcard
#     o dentro de una sola cadena para `adb shell "..."`.
#   - Antes de fiarse de lo que se ve, mirar qué versión hay instalada
#     (adb shell dumpsys package <paquete> | grep versionName). En el emulador quedó una
#     compilación antigua (1.0) que pinta por debajo de la barra de estado: parecía un
#     fallo de la web y era la app vieja. La de depuración actual se instala con
#     `gradlew :app:installDebug` desde android/.
#   - La de depuración enseña al arrancar el formulario de consentimiento de prueba, sin
#     textos legibles por uiautomator: se responde tocando por coordenadas.
#   - La compilación de Play usa anuncios reales: en ella no se piden ni se pulsan. Las
#     pruebas con anuncios, en la de depuración (com.angelgf.triker.debug), que usa los de prueba.
ADB="${ADB:-$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe}"
DEV="${DEV:-emulator-5554}"
PAQUETE="${PAQUETE:-com.angelgf.triker.debug}"

volcar() {
  "$ADB" -s "$DEV" shell uiautomator dump //sdcard/ui.xml >/dev/null 2>&1
  "$ADB" -s "$DEV" shell "cat /sdcard/ui.xml" | tr -d '\r'
}

textos() {
  volcar | grep -o 'text="[^"]\{1,60\}"[^>]*bounds="[^"]*"' | sed -E 's/" resource-id.*bounds=/" /' | head -"${1:-30}"
}

# centro <[x1,y1][x2,y2]> -> "x y"
centro() {
  echo "$1" | sed -E 's/\[([0-9]+),([0-9]+)\]\[([0-9]+),([0-9]+)\]/\1 \2 \3 \4/' | awk '{print int(($1+$3)/2), int(($2+$4)/2)}'
}

pulsa() {
  local b
  b=$(volcar | grep -o "text=\"[^\"]*$1[^\"]*\"[^>]*bounds=\"\[[0-9]*,[0-9]*\]\[[0-9]*,[0-9]*\]\"" | head -1 | grep -o '\[[0-9]*,[0-9]*\]\[[0-9]*,[0-9]*\]')
  if [ -z "$b" ]; then echo "  no encuentro «$1»"; return 1; fi
  # shellcheck disable=SC2046
  "$ADB" -s "$DEV" shell input tap $(centro "$b")
  echo "  pulsado «$1» en $(centro "$b")"
}

arriba() {
  "$ADB" -s "$DEV" shell dumpsys activity activities | grep -m1 -E "topResumedActivity|mResumedActivity" | sed -E 's/.* ([^ ]+\/[^ ]+) .*/\1/'
}

conecta() {
  local pid
  pid=$("$ADB" -s "$DEV" shell pidof "$PAQUETE" | tr -d '\r')
  [ -z "$pid" ] && { echo "  $PAQUETE no está abierta"; return 1; }
  "$ADB" -s "$DEV" forward --remove-all
  "$ADB" -s "$DEV" forward tcp:9333 "localabstract:webview_devtools_remote_$pid" >/dev/null
}

en_anuncio() { arriba | grep -q AdActivity; }

# El botón de cierre tarda unos segundos en salir: se espera hasta 15 s. No pulsa
# nunca fuera del anuncio.
cierra_anuncio() {
  local b i
  for i in $(seq 1 15); do
    en_anuncio || { echo "  no hay anuncio delante ($(arriba))"; return 1; }
    b=$(volcar | grep -o 'content-desc="Close"[^>]*bounds="\[[0-9]*,[0-9]*\]\[[0-9]*,[0-9]*\]"' | head -1 | grep -o '\[[0-9]*,[0-9]*\]\[[0-9]*,[0-9]*\]')
    if [ -n "$b" ]; then
      en_anuncio || return 1
      # shellcheck disable=SC2046
      "$ADB" -s "$DEV" shell input tap $(centro "$b")
      echo "  anuncio cerrado en $(centro "$b")"
      return 0
    fi
    sleep 1
  done
  echo "  no aparece el botón de cierre"
  return 1
}
