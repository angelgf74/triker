---
name: release-android
description: Incrementa la versión de la app Android de Triker (versionCode/versionName) y genera el .aab firmado listo para subir a Google Play. Úsalo cuando pidan publicar una nueva versión en Play Store, sacar una release de Android, subir la versión de la app, generar el bundle/aab, o compilar el release firmado.
---

Sube la versión en `android/app/build.gradle.kts`, compila
`:app:bundleRelease` con Gradle, comprueba que el `.aab` resultante lleva de
verdad la versión nueva y está firmado, y lo deja con nombre claro en
`android/release/`.

Todo lo hace `.claude/skills/release-android/release.ps1`. Las rutas son
relativas a la raíz del repo (`C:\Desarrollo\Proyectos\Triker`) y los comandos
son PowerShell.

## Requisitos

Ya presentes en esta máquina — verificados:

- JDK 25 (Temurin) vía `JAVA_HOME`; Gradle wrapper 9.0, AGP 8.10.0.
- Android SDK en `C:\Users\angel\AppData\Local\Android\Sdk`, apuntado desde
  `android/local.properties` (`sdk.dir`).
- `android/keystore.properties` + `android/triker-release.jks` — **ninguno de
  los dos está en git**. Sin ellos el script se planta antes de compilar.

## Uso

Primero en seco, para ver qué versión saldría:

```powershell
.\.claude\skills\release-android\release.ps1 -DryRun
```

```
== Comprobaciones previas ==
OK  keystore: triker-release.jks
OK  SDK de Android localizable
OK  IDs de AdMob de produccion

== Version ==
  versionCode : 3 -> 4
  versionName : 1.2 -> 1.3
```

Y la release de verdad:

```powershell
.\.claude\skills\release-android\release.ps1
```

Tarda ~20 s (incluido `-Clean`); la primera vez, con el wrapper de Gradle sin
descargar, unos minutos. Termina con:

```
== Verificacion ==
OK  aab regenerado (07/27/2026 13:18:44)
OK  manifest del bundle: versionCode=4 versionName=1.3
OK  firmado (META-INF/TRIKER.RSA)

== Listo para subir a Google Play ==
  C:\Desarrollo\Proyectos\Triker\android\release\triker-1.3-vc4.aab
  4.85 MB | versionName 1.3 | versionCode 4
```

### Opciones

| flag | qué hace |
|---|---|
| *(nada)* | `versionCode +1`, `versionName` sube la minor: `1.2` → `1.3`. Es lo que ha hecho el repo históricamente. |
| `-Part major` | `1.2` → `2.0` |
| `-Part patch` | `1.2` → `1.2.1` |
| `-VersionName 2.0.1` | versionName explícito; `versionCode` sube igual. |
| `-DryRun` | Enseña el cambio y sale. No toca ni compila nada. |
| `-SkipBuild` | Solo sube la versión en el `.gradle.kts`. |
| `-Clean` | `gradlew clean` antes de compilar. |

`versionCode` **siempre** sube en 1: Play Console rechaza un bundle cuyo
versionCode ya se haya subido, aunque el versionName sea distinto.

## Qué comprueba antes de compilar

Estas guardas existen porque fallan en silencio si no las miras:

1. **`keystore.properties` existe.** Si falta, `app/build.gradle.kts` no aplica
   el `signingConfig` (está dentro de un `if (keystorePropsFile.exists())`) y
   Gradle genera un `.aab` **sin firmar sin dar ningún error**. Play Console lo
   rechaza al subirlo, después de haber esperado el build entero.
2. **El `.jks` al que apunta `storeFile` existe.**
3. **SDK localizable** (`local.properties` o `ANDROID_HOME`).
4. **IDs de AdMob.** Avisa (no bloquea) si `strings.xml` todavía tuviera los
   IDs de prueba de Google (`ca-app-pub-3940256099942544/...`). Ahora mismo
   están los reales.

## Qué comprueba después

`BUILD SUCCESSFUL` no basta:

1. **El `.aab` es posterior al inicio del build.** Si Gradle da
   `packageReleaseBundle` por `UP-TO-DATE` te deja el `.aab` de la release
   *anterior* en su sitio, con su fecha vieja y su versionCode viejo.
2. **`versionCode`/`versionName` del manifiesto que alimenta el bundle**
   (`app/build/intermediates/bundle_manifest/release/.../AndroidManifest.xml`,
   XML plano — el manifiesto de dentro del `.aab` es protobuf binario).
3. **Hay `META-INF/*.RSA` dentro del `.aab`** → está firmado de verdad.

Si algo de esto falla, el script sale con código 1 y no te deja subir un
bundle roto.

## Después de ejecutarlo

El script **no** hace commit. Deja el cambio en el working tree y te imprime el
comando, siguiendo la convención del repo (ver commit `7e445ec`):

```powershell
git add android/app/build.gradle.kts
git commit -m "Incrementa version a 1.3 (versionCode 4)"
```

Luego: Play Console → Producción → Crear nueva versión → subir el `.aab` de
`android/release/`.

El `.aab` no entra en git: `android/.gitignore` línea 11 ignora `*.aab`, así que
`android/release/` es invisible para git.

## Gotchas

- **`android/keystore.properties` y `triker-release.jks` no están versionados.**
  En una máquina nueva hay que recrearlos o el build sale sin firmar. La firma
  actual usa el alias que produce `META-INF/TRIKER.RSA`.

- **El build es incremental y silenciosamente engañoso.** Ejecutar
  `:app:bundleRelease` sin cambiar la versión da `BUILD SUCCESSFUL` con
  `packageReleaseBundle UP-TO-DATE` y **no** regenera el `.aab`: me dejó un
  fichero con fecha de cuatro días antes y el versionCode viejo. Por eso el
  script compara la fecha del fichero con el inicio del build.

- **`-Clean` no es tan caro como parece.** El rebuild completo (`46 executed,
  2 up-to-date`) tardó lo mismo que el incremental, ~21 s, porque el proyecto
  Android es pequeño. Ante la duda, úsalo.

- **El script es 100 % ASCII a propósito.** PowerShell 5.1 lee los `.ps1` sin
  BOM como ANSI: un `·` en un `Write-Host` sale como `Â·` por consola. Si
  añades mensajes, sin acentos (o guarda el fichero con BOM UTF-8).

- **`Where-Object` con una sola coincidencia devuelve un string, no un array.**
  `$lineas[0]` te da el primer *carácter*. El script envuelve en `@(...)` al
  parsear `keystore.properties`; la primera versión fallaba con
  `apunta a 's' pero no existe`.

- **`app/build.gradle.kts` está en UTF-8 sin BOM y con finales de línea LF.**
  El script lee/escribe con `System.IO.File` y reemplaza por posición exacta
  para no convertir el fichero entero a CRLF ni meterle un BOM. `Set-Content`
  a secas rompería las dos cosas.

- **El build escupe avisos que no son errores**: AGP 8.10.0 "was tested up to
  compile SDK version 36" (aquí `compileSdk = 37`), `kotlinOptions` deprecado,
  y avisos de `restricted method` del JDK 25. `BUILD SUCCESSFUL` igualmente.

## Troubleshooting

- **`ERROR: Falta android/keystore.properties`**: es la guarda del punto 1.
  Crea el fichero con `storeFile` / `storePassword` / `keyAlias` / `keyPassword`.

- **`ERROR: El .aab es anterior al build (...)`**: Gradle lo dio por
  `UP-TO-DATE`. Reejecuta con `-Clean`.

- **`ERROR: El .aab NO esta firmado`**: existe `keystore.properties` pero las
  credenciales no cuadran. Revisa `keyAlias` y las contraseñas.
