---
name: update-deps
description: Revisa y actualiza todas las dependencias del proyecto Triker — paquetes NuGet de los cuatro proyectos .NET, plugins y librerías de Android/Gradle, wrapper de Gradle y las deps npm del driver. Úsalo cuando pidan actualizar dependencias, subir versiones de paquetes, mirar qué está desactualizado, revisar vulnerabilidades de NuGet, o poner el proyecto al día.
---

Un solo script recorre las cuatro superficies de dependencias del repo, dice
qué hay desactualizado, y opcionalmente lo aplica y **compila para comprobar
que no lo ha roto**.

Todo está en `.claude/skills/update-deps/check-deps.ps1`. Rutas relativas a la
raíz del repo (`C:\Desarrollo\Proyectos\Triker`), comandos PowerShell.

## Qué cubre

| Superficie | Fuente de la última versión |
|---|---|
| NuGet (Api, Web, Shared, Simulator) | `dotnet list package --outdated --format json` |
| Vulnerabilidades NuGet (incl. transitivas) | `dotnet list package --vulnerable --include-transitive` |
| Plugins Gradle (AGP, Kotlin) | `maven-metadata.xml` de Google Maven / Maven Central |
| Librerías de `android/app/build.gradle.kts` | ídem |
| Wrapper de Gradle | `services.gradle.org/versions/current` |
| npm del driver de `run-triker` | `npm outdated --json` |

No toca `compileSdk`/`targetSdk` (solo los informa): subirlos implica probar la
app en un dispositivo.

## Uso

Informe, no toca nada:

```powershell
.\.claude\skills\update-deps\check-deps.ps1
```

```
Area    Dependencia                                      Actual  Ultima  Salto  Aplicado
android androidx.core:core-ktx                           1.13.1  1.19.0  minor  no
android com.google.android.gms:play-services-ads         23.6.0  25.4.0  major  no
android gradle wrapper                                   9.0     9.6.1   minor  no
android plugin com.android.application                   8.10.0  9.3.1   major! no
android plugin org.jetbrains.kotlin.android              2.1.0   2.4.10  minor! no
dotnet  Microsoft.AspNetCore.OpenApi (Triker.Api.csproj) 10.0.7  10.0.10 patch  no
```

Aplicar solo minor/patch y verificar que compila:

```powershell
.\.claude\skills\update-deps\check-deps.ps1 -Apply
```

Si algo no compila, sale con código 1 y te dice cómo deshacerlo.

### Opciones

| flag | qué hace |
|---|---|
| *(nada)* | Solo informa. |
| `-Apply` | Aplica minor y patch. Al terminar corre `dotnet build` y `gradlew :app:assembleDebug`. |
| `-IncludeMajor` | Aplica también los `major` y los marcados `!`. |
| `-Exclude 'androidx.*','ClosedXML'` | Patrones comodín que no se aplican (siguen saliendo en el informe). |
| `-Only dotnet\|android\|npm` | Limita la revisión a una superficie. |
| `-SkipVerify` | No compila después de aplicar. |
| `-Rollback` | Restaura los ficheros desde la copia del último `-Apply`. |

### El salto `minor!`

`!` = semver lo llama minor pero **rompe este repo**. Está codificado en la
lista `RiskyMinor` del script y solo se aplica con `-IncludeMajor`, a
sabiendas. Hoy son Kotlin y AGP (ver Gotchas).

## Rollback

Antes de cada `-Apply` el script copia los ficheros que va a tocar a
`.claude/skills/update-deps/.backup/`. Para deshacer:

```powershell
.\.claude\skills\update-deps\check-deps.ps1 -Rollback
```

Se usa esa copia y no `git checkout`, porque `android/app/build.gradle.kts`
suele llevar cambios sin commitear (el `versionCode`/`versionName` que sube
[`release-android`](../release-android/SKILL.md)) que un checkout se llevaría
por delante. `-Rollback` no deshace un `npm install`.

## Estado real de este repo (27/07/2026)

Comprobado ejecutándolo:

- **Lo de .NET se aplica limpio.** Los cuatro paquetes `Microsoft.AspNetCore.*`
  10.0.7 → 10.0.10 y `ClosedXML` 0.104.2 → 0.105.1 compilan sin tocar nada más.
- **Gradle 9.0 → 9.6.1 funciona con AGP 8.10.0.** `:app:assembleDebug` pasa.
- **Todo lo demás de Android está bloqueado detrás de la migración a AGP 9.**
  Ver Gotchas.
- **La vulnerabilidad `Microsoft.OpenApi` 2.0.0 (High, GHSA-v5pm-xwqc-g5wc)
  tiene arreglo**, y no es subir `Microsoft.AspNetCore.OpenApi`: con 10.0.10
  sigue arrastrando la 2.0.0. Hay que fijar el transitivo a mano:

  ```powershell
  dotnet add Triker.Api/Triker.Api.csproj package Microsoft.OpenApi --version 2.11.0
  ```

  Con eso `dotnet build` da 0 avisos y `--vulnerable` sale limpio.

## Gotchas

- **`Microsoft.OpenApi` 3.x rompe la compilación.** Es tentador porque el
  informe lo ofrece como última versión (3.9.0), pero el source generator de
  `Microsoft.AspNetCore.OpenApi` no va con la API 3.x:
  `error CS0200: No se puede asignar a la propiedad 'IOpenApiMediaType.Example'
  porque es de solo lectura`. Quédate en la línea 2.x (2.11.0).

- **Kotlin 2.1.0 → 2.4.10 es "minor" y rompe el build.** En 2.4 el
  `kotlinOptions { jvmTarget = "17" }` de `android/app/build.gradle.kts` pasa
  de aviso a **error de compilación del script**
  (`Script compilation errors: 'fun ... kotlinOptions' is deprecated`). Hay que
  migrar al DSL `compilerOptions` primero. Por eso está en `RiskyMinor`.

- **Las androidx al día exigen AGP 9.1+.** `androidx.core:core-ktx:1.19.0` (y
  `core:1.19.0`) fallan en `:app:checkDebugAarMetadata` con
  `requires Android Gradle plugin 9.1.0 or higher. This build currently uses
  8.10.0`. O sea: androidx, AGP y Kotlin van juntos en una sola migración; no
  se pueden picotear. Mientras tanto, `-Exclude 'androidx.*'`.

- **No lances `gradlew wrapper --gradle-version X` desde el propio
  `gradlew.bat`.** La tarea reescribe `gradlew.bat` mientras cmd.exe lo está
  ejecutando; cmd sigue leyendo por offset de byte en el fichero nuevo y
  revienta con `"ASE_NAMECLASSPATHAPP_HOME*" no se reconoce como un comando
  interno o externo`. El script edita `distributionUrl` en
  `gradle-wrapper.properties` directamente; el wrapper se baja la distribución
  nueva solo en el siguiente `gradlew`.

- **`maven-metadata.xml` lista las versiones en orden de publicación, no
  semántico**, y la última suele ser una preview: la última de AGP era
  `9.4.0-alpha06` y la de Kotlin `2.4.20-Beta2`. Hay que filtrar por
  `^\d+(\.\d+)*$` y ordenar de verdad.

- **`$p.path` de `dotnet list package --format json` es una ruta ABSOLUTA.**
  Un `Join-Path $RepoRoot $path` la duplica y sale
  `C:\...\Triker\C:\...\Triker\Triker.Api\Triker.Api.csproj`.

- **`dotnet list package --format json` mezcla los mensajes de restore con el
  JSON en stdout.** Hay que quedarse desde la primera línea que empieza por
  `{`. Usar el JSON y no la tabla también evita depender de las cabeceras
  traducidas (`Solicitado` / `Resuelto` / `Más reciente`).

- **`-like '*.*'` no detecta un asterisco literal.** En PowerShell el `.` no es
  especial pero el `*` sí, así que `'10.0.7' -like '*.*'` es `$true`. La
  primera versión del script convirtió los tres paquetes de `Triker.Web` en
  `10.0.*` creyendo que respetaba un pin flotante. Es `.Contains('*')`.

- **`Where-Object` con una sola coincidencia devuelve un string, no un array**
  — mismo tropiezo que en `release-android`. `@(...)` siempre.

- **`Get-Content` en PS 5.1 lee sin BOM como ANSI.** `android/build.gradle.kts`
  se ve como `// Plugins declarados a nivel raÃ­z` y parece que el script ha
  corrompido el fichero. No es verdad: `git diff` lo muestra bien. Para leer y
  escribir se usa `[System.IO.File]::ReadAllText/WriteAllText` con UTF-8 sin
  BOM, que además preserva los finales de línea LF.

## Troubleshooting

- **El script muere en mitad de una tarea de Gradle con `NativeCommandError` /
  `RemoteException`**: PS 5.1 convierte cada línea de stderr de un `.exe` en un
  ErrorRecord y, con `$ErrorActionPreference = 'Stop'`, eso aborta el script
  aunque el proceso devuelva 0. gradlew escupe cuatro `WARNING: A restricted
  method in java.lang.System has been called` en cada arranque. Todo lo nativo
  pasa por el helper `Invoke-Native`, que baja el preference a `Continue` y
  **no** redirige stderr (`2>&1` lo empeora).

- **`FALLO gradlew :app:assembleDebug` tras un `-Apply`**: casi seguro es el
  choque androidx/AGP. `-Rollback` y luego `-Apply -Exclude 'androidx.*'`.

- **`AVISO: no se pudo consultar <grupo>:<artefacto>`**: el artefacto no está
  en el repositorio que le toca por el nombre del grupo (Google Maven para
  `androidx.*` / `com.android.*` / `com.google.android.*`, Maven Central para
  el resto). Se salta esa dependencia y sigue.
