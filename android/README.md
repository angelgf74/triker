# Triker — App Android (WebView + AdMob)

App Android que encapsula la web de Triker (`Triker.Web`) en un `WebView` y muestra
anuncios **intersticiales de AdMob entre partidas**.

## Cómo funciona

- `MainActivity` carga la URL de la web (`res/values/strings.xml` → `app_url`) en un `WebView`.
- `MobileAds` (SDK Google Mobile Ads) precarga un intersticial al arrancar.
- La web expone, dentro de la app, el puente `window.AndroidAds` (clase `AdsBridge`).
- Al terminar cada partida, `GameRoom.razor` llama a `window.TrikerAds.interstitial()`
  (`Triker.Web/wwwroot/js/ads.js`), que invoca el puente y muestra el anuncio.
- Hay un **cooldown de 60 s** (en JS y en `InterstitialAdManager`) para no saturar.
- Tras cerrarse un anuncio se precarga el siguiente automáticamente.

En la web normal (navegador) `window.AndroidAds` no existe, así que no se muestra nada:
el mismo código funciona dentro y fuera de la app.

## Requisitos

- Android Studio (recomendado) o JDK 17 + Android SDK (compileSdk 35).
- `minSdk 26` (Android 8.0+).
- Variables `ANDROID_HOME` / `local.properties` apuntando al SDK.

## Compilar

```bash
# APK de depuración
./gradlew :app:assembleDebug        # gradlew.bat en cmd de Windows

# Instalar en un dispositivo/emulador conectado
./gradlew :app:installDebug

# Bundle para publicar: compila sin firmar y lo firma con el keystore de la app
gplay compilar                      # deja release/triker-<version>-vc<code>.aab
```

El APK queda en `app/build/outputs/apk/debug/app-debug.apk`.

También puedes abrir la carpeta `android/` directamente en Android Studio y pulsar Run.

## Antes de publicar — IMPORTANTE

1. **Sustituye los IDs de prueba de AdMob** en `app/src/main/res/values/strings.xml`:
   - `admob_app_id` → App ID real de tu cuenta AdMob.
   - `admob_interstitial_id` → ID del bloque intersticial real.
   - Los valores actuales son los **IDs de prueba oficiales de Google** (no generan ingresos).
   El App ID también se referencia en `AndroidManifest.xml` (`com.google.android.gms.ads.APPLICATION_ID`).
2. Comprueba `app_url` en `strings.xml` (por defecto `https://triker-app.angelgf.com.es/`).
   Si la web se sirve en una subruta, ajústala (p. ej. `.../triker/`).
3. Sube la versión con `gplay version --menor`, compila y firma con `gplay compilar` y sube el
   **App Bundle** con `gplay subir` (ensayo; con `--publicar`, de verdad).
4. Si usas anuncios reales en pruebas, registra tu dispositivo como
   [dispositivo de prueba](https://developers.google.com/admob/android/test-ads) para no
   infringir las políticas de AdMob.

## Estructura

```
android/
├─ app/
│  ├─ build.gradle.kts
│  └─ src/main/
│     ├─ AndroidManifest.xml
│     ├─ java/com/angelgf/triker/
│     │  ├─ MainActivity.kt            # WebView + ciclo de vida
│     │  ├─ InterstitialAdManager.kt   # carga/muestra intersticiales + cooldown
│     │  └─ AdsBridge.kt               # window.AndroidAds (puente JS↔nativo)
│     └─ res/                          # strings, tema, icono adaptable
├─ build.gradle.kts
├─ settings.gradle.kts
└─ gradle/ (wrapper 8.9)
```
