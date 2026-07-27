<#
.SYNOPSIS
  Sube la version de la app Android de Triker y genera el .aab firmado
  listo para subir a Google Play Console.

.EXAMPLE
  .\.claude\skills\release-android\release.ps1 -DryRun
  .\.claude\skills\release-android\release.ps1
  .\.claude\skills\release-android\release.ps1 -Part major
  .\.claude\skills\release-android\release.ps1 -VersionName 2.0.1 -Clean
#>
[CmdletBinding()]
param(
    # minor: 1.2 -> 1.3 (por defecto, es lo que hace el repo)
    # major: 1.2 -> 2.0
    # patch: 1.2 -> 1.2.1
    [ValidateSet('minor', 'major', 'patch')]
    [string]$Part = 'minor',

    # versionName explicito; tiene prioridad sobre -Part
    [string]$VersionName,

    # Muestra que cambiaria y sale sin tocar nada
    [switch]$DryRun,

    # Solo sube la version, no compila
    [switch]$SkipBuild,

    # gradlew clean antes de compilar
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- rutas

$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$AndroidDir = Join-Path $RepoRoot 'android'
$GradleFile = Join-Path $AndroidDir 'app\build.gradle.kts'
$Gradlew    = Join-Path $AndroidDir 'gradlew.bat'
$OutDir     = Join-Path $AndroidDir 'release'
$AabPath    = Join-Path $AndroidDir 'app\build\outputs\bundle\release\app-release.aab'
# Manifiesto que alimenta packageReleaseBundle: XML plano, refleja lo que
# acaba dentro del .aab (cuyo manifest si es protobuf binario).
$BundleManifest = Join-Path $AndroidDir 'app\build\intermediates\bundle_manifest\release\processApplicationManifestReleaseForBundle\AndroidManifest.xml'

function Fail([string]$msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }
function Info([string]$msg) { Write-Host $msg }
function Ok([string]$msg)   { Write-Host "OK  $msg" -ForegroundColor Green }
function Warn([string]$msg) { Write-Host "AVISO: $msg" -ForegroundColor Yellow }

if (-not (Test-Path $GradleFile)) { Fail "No existe $GradleFile" }
if (-not (Test-Path $Gradlew))    { Fail "No existe $Gradlew" }

# ------------------------------------------------- comprobaciones previas

Info '== Comprobaciones previas =='

# Sin keystore.properties, build.gradle.kts NO aplica signingConfig y Gradle
# genera un .aab SIN FIRMAR sin avisar. Play Console lo rechaza al subirlo.
$KeystoreProps = Join-Path $AndroidDir 'keystore.properties'
if (-not (Test-Path $KeystoreProps)) {
    Fail @"
Falta android/keystore.properties (no se versiona).
Sin ese fichero el build genera un .aab SIN FIRMAR y Play Console lo rechaza.
Crealo con: storeFile / storePassword / keyAlias / keyPassword
"@
}
# @() obligatorio: con una sola coincidencia Where-Object devuelve un string,
# y $ksLines[0] seria su primer caracter en vez de la linea entera.
$ksLines = @(Get-Content $KeystoreProps | Where-Object { $_ -match '^\s*storeFile\s*=' })
if ($ksLines.Count -gt 0) {
    $storeFile = ($ksLines[0] -replace '^\s*storeFile\s*=\s*', '').Trim()
    $storeFull = Join-Path $AndroidDir $storeFile
    if (-not (Test-Path $storeFull)) { Fail "keystore.properties apunta a '$storeFile' pero no existe en $AndroidDir" }
    Ok "keystore: $storeFile"
}

$LocalProps = Join-Path $AndroidDir 'local.properties'
if (-not (Test-Path $LocalProps) -and -not $env:ANDROID_HOME) {
    Fail 'Falta android/local.properties (sdk.dir) y ANDROID_HOME no esta definido.'
}
Ok 'SDK de Android localizable'

# Los IDs de prueba de Google empiezan por ca-app-pub-3940256099942544.
$StringsXml = Join-Path $AndroidDir 'app\src\main\res\values\strings.xml'
if (Test-Path $StringsXml) {
    if (Select-String -Path $StringsXml -Pattern '3940256099942544' -Quiet) {
        Warn 'strings.xml todavia tiene IDs de PRUEBA de AdMob: la release no generara ingresos.'
    } else {
        Ok 'IDs de AdMob de produccion'
    }
}

# ------------------------------------------------------- version actual

$text = [System.IO.File]::ReadAllText($GradleFile)

$mCode = [regex]::Match($text, '(?m)^(?<pre>\s*versionCode\s*=\s*)(?<val>\d+)\s*$')
$mName = [regex]::Match($text, '(?m)^(?<pre>\s*versionName\s*=\s*")(?<val>[^"]+)(?<post>")\s*$')
if (-not $mCode.Success) { Fail 'No se encontro "versionCode = N" en app/build.gradle.kts' }
if (-not $mName.Success) { Fail 'No se encontro "versionName = ..." en app/build.gradle.kts' }

$curCode = [int]$mCode.Groups['val'].Value
$curName = $mName.Groups['val'].Value

# ------------------------------------------------------- version nueva

$newCode = $curCode + 1

if ($VersionName) {
    $newName = $VersionName
} else {
    $parts = $curName.Split('.')
    $major = [int]$parts[0]
    $minor = 0
    if ($parts.Count -ge 2) { $minor = [int]$parts[1] }
    $patch = 0
    if ($parts.Count -ge 3) { $patch = [int]$parts[2] }

    switch ($Part) {
        'major' { $newName = "$($major + 1).0" }
        'minor' { $newName = "$major.$($minor + 1)" }
        'patch' { $newName = "$major.$minor.$($patch + 1)" }
    }
}

Info ''
Info '== Version =='
Info "  versionCode : $curCode -> $newCode"
Info "  versionName : $curName -> $newName"

if ($DryRun) {
    Info ''
    Info 'DryRun: no se ha modificado ni compilado nada.'
    exit 0
}

# --------------------------------------------------------- escribir bump

# Reemplazo por posicion exacta para no tocar nada mas del fichero
# (se lee/escribe con UTF-8 sin BOM y finales de linea LF, como esta en git).
$text = $text.Remove($mName.Groups['val'].Index, $mName.Groups['val'].Length).Insert($mName.Groups['val'].Index, $newName)
$mCode2 = [regex]::Match($text, '(?m)^(?<pre>\s*versionCode\s*=\s*)(?<val>\d+)\s*$')
$text = $text.Remove($mCode2.Groups['val'].Index, $mCode2.Groups['val'].Length).Insert($mCode2.Groups['val'].Index, "$newCode")

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($GradleFile, $text, $utf8NoBom)
Ok "app/build.gradle.kts actualizado"

if ($SkipBuild) {
    Info ''
    Info 'SkipBuild: version subida, sin compilar.'
    exit 0
}

# ---------------------------------------------------------------- build

$buildStart = Get-Date
Push-Location $AndroidDir
try {
    if ($Clean) {
        Info ''
        Info '== gradlew clean =='
        & $Gradlew clean --no-daemon
        if ($LASTEXITCODE -ne 0) { Fail "gradlew clean fallo (exit $LASTEXITCODE)" }
    }

    Info ''
    Info '== gradlew :app:bundleRelease =='
    Info '(la primera vez tarda varios minutos)'
    & $Gradlew :app:bundleRelease --no-daemon
    if ($LASTEXITCODE -ne 0) { Fail "gradlew :app:bundleRelease fallo (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

# --------------------------------------------------------- verificacion

Info ''
Info '== Verificacion =='

if (-not (Test-Path $AabPath)) { Fail "No se genero el .aab en $AabPath" }

$aab = Get-Item $AabPath
if ($aab.LastWriteTime -lt $buildStart) {
    Fail @"
El .aab es anterior al build ($($aab.LastWriteTime)): Gradle lo dio por UP-TO-DATE
y no lo ha regenerado. Reejecuta con -Clean.
"@
}
Ok "aab regenerado ($($aab.LastWriteTime))"

if (-not (Test-Path $BundleManifest)) { Fail "No se encontro el manifest de bundle en $BundleManifest" }
$manifest = Get-Content $BundleManifest -Raw
$vcOk = $manifest -match ('android:versionCode="{0}"' -f $newCode)
$vnOk = $manifest -match ('android:versionName="{0}"' -f [regex]::Escape($newName))
if (-not $vcOk) { Fail "El manifest del bundle no declara versionCode=$newCode" }
if (-not $vnOk) { Fail "El manifest del bundle no declara versionName=$newName" }
Ok "manifest del bundle: versionCode=$newCode versionName=$newName"

# El .aab firmado lleva META-INF/*.RSA; el sin firmar no.
$zip = $null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($aab.FullName)
    $signed = @($zip.Entries | Where-Object { $_.FullName -match '^META-INF/.*\.(RSA|DSA|EC)$' })
    if ($signed.Count -eq 0) {
        Fail 'El .aab NO esta firmado (sin META-INF/*.RSA). Revisa keystore.properties.'
    }
    Ok "firmado ($($signed[0].FullName))"
}
finally {
    if ($zip) { $zip.Dispose() }
}

# ---------------------------------------------------------------- copia

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$final = Join-Path $OutDir ("triker-{0}-vc{1}.aab" -f $newName, $newCode)
Copy-Item $aab.FullName $final -Force

$mb = [math]::Round($aab.Length / 1MB, 2)

Info ''
Info '== Listo para subir a Google Play =='
Info "  $final"
# El script se mantiene 100% ASCII: PowerShell 5.1 lee los .ps1 sin BOM como
# ANSI, y cualquier caracter no ASCII sale mojibake por consola.
Info "  $mb MB | versionName $newName | versionCode $newCode"
Info ''
Info 'Siguiente paso: Play Console -> Produccion -> Crear nueva version -> subir el .aab.'
Info 'Commit sugerido (siguiendo la convencion del repo):'
Info ''
Info "  git add android/app/build.gradle.kts"
Info "  git commit -m `"Incrementa version a $newName (versionCode $newCode)`""
