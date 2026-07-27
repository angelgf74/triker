<#
.SYNOPSIS
  Revisa (y opcionalmente actualiza) todas las dependencias del repo Triker:
  paquetes NuGet, plugins y librerias de Android/Gradle, wrapper de Gradle y
  las deps npm del driver de run-triker.

.EXAMPLE
  .\.claude\skills\update-deps\check-deps.ps1
  .\.claude\skills\update-deps\check-deps.ps1 -Only android
  .\.claude\skills\update-deps\check-deps.ps1 -Apply
  .\.claude\skills\update-deps\check-deps.ps1 -Apply -IncludeMajor
#>
[CmdletBinding()]
param(
    # Sin -Apply solo informa; no toca ningun fichero.
    [switch]$Apply,

    # Por defecto -Apply solo sube minor/patch. Con esto tambien los major.
    [switch]$IncludeMajor,

    [ValidateSet('all', 'dotnet', 'android', 'npm')]
    [string]$Only = 'all',

    # No compilar despues de aplicar.
    [switch]$SkipVerify,

    # Restaura los ficheros desde la copia del ultimo -Apply y sale.
    [switch]$Rollback,

    # Patrones comodin que NO se aplican (siguen saliendo en el informe).
    # Ej.: -Exclude 'androidx.*','ClosedXML'
    [string[]]$Exclude = @()
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest sin barra de progreso

$RepoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$AndroidDir = Join-Path $RepoRoot 'android'
$RootGradle = Join-Path $AndroidDir 'build.gradle.kts'
$AppGradle  = Join-Path $AndroidDir 'app\build.gradle.kts'
$WrapperCfg = Join-Path $AndroidDir 'gradle\wrapper\gradle-wrapper.properties'
$NpmPrefix  = Join-Path $RepoRoot '.claude\skills\run-triker'
$BackupDir  = Join-Path $PSScriptRoot '.backup'

function Head([string]$m) { Write-Host ''; Write-Host "== $m ==" -ForegroundColor Cyan }
function Info([string]$m) { Write-Host $m }
function Ok([string]$m)   { Write-Host "OK  $m" -ForegroundColor Green }
function Warn([string]$m) { Write-Host "AVISO: $m" -ForegroundColor Yellow }
function Bad([string]$m)  { Write-Host "$m" -ForegroundColor Red }

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:Findings = @()   # filas: Area, Name, Current, Latest, Kind, Applied

# PowerShell 5.1 convierte CADA linea de stderr de un .exe en un ErrorRecord
# (NativeCommandError); con $ErrorActionPreference='Stop' eso aborta el script
# aunque el proceso devuelva 0. gradlew y dotnet escriben avisos por stderr
# constantemente, asi que todo lo nativo pasa por aqui: se baja a 'Continue'
# mientras corre y NUNCA se redirige stderr (2>&1 / 2>$null lo empeoran).
function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [string[]]$Arguments = @(),
        [string]$WorkDir,
        [switch]$Capture
    )
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    if ($WorkDir) { Push-Location $WorkDir }
    try {
        $out = $null
        if ($Capture) { $out = @(& $Exe @Arguments) } else { & $Exe @Arguments | Out-Null }
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out }
    } finally {
        if ($WorkDir) { Pop-Location }
        $ErrorActionPreference = $prev
    }
}

# ---------------------------------------------------------------- versiones

# Solo estables: descarta 9.4.0-alpha06, 2.4.20-Beta2, 1.0.0-rc1, +metadata...
function Test-Stable([string]$v) { return $v -match '^\d+(\.\d+)*$' }

function ConvertTo-VerArray([string]$v) {
    $p = @($v -split '\.' | ForEach-Object { [int]$_ })
    while ($p.Count -lt 4) { $p += 0 }
    return $p
}

function Compare-Ver([string]$a, [string]$b) {
    $x = ConvertTo-VerArray $a; $y = ConvertTo-VerArray $b
    for ($i = 0; $i -lt 4; $i++) {
        if ($x[$i] -gt $y[$i]) { return 1 }
        if ($x[$i] -lt $y[$i]) { return -1 }
    }
    return 0
}

# major / minor / patch, para decidir que se aplica sin -IncludeMajor
function Get-BumpKind([string]$cur, [string]$new) {
    if (-not (Test-Stable $cur) -or -not (Test-Stable $new)) { return 'minor' }
    $x = ConvertTo-VerArray $cur; $y = ConvertTo-VerArray $new
    if ($y[0] -ne $x[0]) { return 'major' }
    if ($y[1] -ne $x[1]) { return 'minor' }
    return 'patch'
}

# Saltos que semver llama "minor" pero que en ESTE repo rompen el build.
# Kotlin 2.1 -> 2.4 convierte en error el `kotlinOptions { jvmTarget }` de
# app/build.gradle.kts (antes solo avisaba); AGP cambia de DSL entre minors.
# Se tratan como major: solo se aplican con -IncludeMajor y a sabiendas.
$script:RiskyMinor = @(
    'plugin org.jetbrains.kotlin.android',
    'plugin com.android.application'
)

# --------------------------------------------------------------- backups

function Backup-File([string]$path) {
    if (-not $Apply) { return }
    if (-not (Test-Path $path)) { Warn "no se pudo copiar (no existe): $path"; return }
    if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir | Out-Null }
    $full = (Resolve-Path $path).Path
    if (-not $full.StartsWith($RepoRoot)) { return }
    $rel = $full.Substring($RepoRoot.Length).TrimStart('\', '/')
    $dest = Join-Path $BackupDir ($rel -replace '[\\/]', '__')
    if (-not (Test-Path $dest)) { Copy-Item $path $dest -Force }
}

function Invoke-Rollback {
    if (-not (Test-Path $BackupDir)) { Bad 'No hay copia de seguridad de un -Apply previo.'; exit 1 }
    $files = @(Get-ChildItem $BackupDir -File)
    if ($files.Count -eq 0) { Bad 'La copia de seguridad esta vacia.'; exit 1 }
    Head 'Rollback'
    foreach ($f in $files) {
        $rel = $f.Name -replace '__', '\'
        $dest = Join-Path $RepoRoot $rel
        Copy-Item $f.FullName $dest -Force
        Ok "restaurado $rel"
    }
    Info ''
    Info 'Ojo: -Rollback no deshace un npm install (usa git checkout del package.json'
    Info 'y npm install --prefix .claude/skills/run-triker).'
    exit 0
}

function Add-Finding($area, $name, $cur, $latest, $kind, $applied) {
    $script:Findings += [pscustomobject]@{
        Area = $area; Name = $name; Current = $cur; Latest = $latest
        Kind = $kind; Applied = $applied
    }
}

function Should-Apply([string]$kind, [string]$name = '') {
    if (-not $Apply) { return $false }
    foreach ($pat in $Exclude) { if ($name -like $pat) { return $false } }
    if ($kind -eq 'major' -and -not $IncludeMajor) { return $false }
    if (($script:RiskyMinor -contains $name) -and -not $IncludeMajor) { return $false }
    return $true
}

# --------------------------------------------------------- repos de maven

# androidx.*, com.android.*, com.google.android.* viven en Google Maven;
# el resto (kotlin, etc.) en Maven Central.
function Get-MavenBase([string]$group) {
    if ($group -match '^(androidx|com\.android|com\.google\.android)') {
        return 'https://dl.google.com/dl/android/maven2'
    }
    return 'https://repo1.maven.org/maven2'
}

function Get-LatestMaven([string]$group, [string]$artifact) {
    $path = ($group -replace '\.', '/') + '/' + $artifact
    $url  = "$(Get-MavenBase $group)/$path/maven-metadata.xml"
    try {
        $xml = [xml](Invoke-WebRequest -UseBasicParsing -TimeoutSec 30 $url).Content
    } catch {
        Warn "no se pudo consultar $group`:$artifact ($url)"
        return $null
    }
    # maven-metadata.xml lista las versiones en orden de PUBLICACION, no
    # semantico: hay que ordenarlas de verdad.
    $stable = @($xml.metadata.versioning.versions.version | Where-Object { Test-Stable $_ })
    if ($stable.Count -eq 0) { return $null }
    $best = $stable[0]
    foreach ($v in $stable) { if ((Compare-Ver $v $best) -gt 0) { $best = $v } }
    return $best
}

# ------------------------------------------------------------------ .NET

function Invoke-DotnetJson([string[]]$extraArgs) {
    $r = Invoke-Native -Exe 'dotnet' -WorkDir $RepoRoot -Capture `
                       -Arguments (@('list', 'package') + $extraArgs + @('--format', 'json'))
    # dotnet mezcla los mensajes de restore con el JSON en stdout: quedarse
    # desde la primera linea que abre el objeto.
    $lines = @($r.Output)
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\{') { $start = $i; break }
    }
    if ($start -lt 0) { return $null }
    return (($lines[$start..($lines.Count - 1)]) -join "`n" | ConvertFrom-Json)
}

function Update-Dotnet {
    Head '.NET / NuGet'

    $vuln = Invoke-DotnetJson @('--vulnerable', '--include-transitive')
    if ($vuln) {
        foreach ($p in $vuln.projects) {
            foreach ($f in @($p.frameworks)) {
                foreach ($pk in @($f.topLevelPackages) + @($f.transitivePackages)) {
                    if (-not $pk) { continue }
                    $sev = ($pk.vulnerabilities | ForEach-Object { $_.severity }) -join ','
                    Bad ("VULNERABLE  {0} {1} [{2}] ({3})" -f $pk.id, $pk.resolvedVersion, $sev, (Split-Path $p.path -Leaf))
                }
            }
        }
    }

    $out = Invoke-DotnetJson @('--outdated')
    if (-not $out) { Warn 'dotnet list package no devolvio JSON'; return }

    $any = $false
    foreach ($p in $out.projects) {
        $projPath = $p.path
        $projName = Split-Path $projPath -Leaf
        foreach ($f in @($p.frameworks)) {
            foreach ($pk in @($f.topLevelPackages)) {
                if (-not $pk) { continue }
                $any = $true
                $cur    = $pk.resolvedVersion
                $latest = $pk.latestVersion
                $kind   = Get-BumpKind $cur $latest
                $applied = 'no'

                if (Should-Apply $kind $pk.id) {
                    # OJO: $p.path de `dotnet list package --format json` ya es
                    # una ruta ABSOLUTA; un Join-Path la duplicaria.
                    Backup-File $projPath
                    # Si el csproj pinea con comodin ("9.0.*"), conservar el
                    # estilo en vez de clavar una version exacta.
                    # OJO: .Contains('*'), no -like '*.*' -- en -like el punto
                    # no es literal y '10.0.7' tambien casaria.
                    $requested = [string]$pk.requestedVersion
                    $target = $latest
                    if ($requested.Contains('*')) {
                        $lv = ConvertTo-VerArray $latest
                        $target = "$($lv[0]).$($lv[1]).*"
                    }
                    $r = Invoke-Native -Exe 'dotnet' -WorkDir $RepoRoot `
                            -Arguments @('add', $projPath, 'package', $pk.id, '--version', $target)
                    if ($r.ExitCode -eq 0) { $applied = $target } else { $applied = 'ERROR' }
                }

                Add-Finding 'dotnet' ("{0} ({1})" -f $pk.id, $projName) $cur $latest $kind $applied
            }
        }
    }
    if (-not $any) { Ok 'todos los paquetes NuGet al dia' }
}

# --------------------------------------------------------------- Android

function Update-Android {
    Head 'Android / Gradle'

    # --- wrapper de Gradle (PRIMERO) --------------------------------------
    # Se edita distributionUrl A MANO en vez de lanzar `gradlew wrapper`:
    #   1) esa tarea reescribe gradlew.bat MIENTRAS cmd.exe lo esta ejecutando;
    #      cmd sigue leyendo por offset de byte en el fichero nuevo y revienta
    #      con "ASE_NAMECLASSPATHAPP_HOME* no se reconoce como un comando".
    #   2) ademas necesita configurar el proyecto, asi que fallaria si un salto
    #      de Kotlin/AGP ya hubiera roto el DSL de los build scripts.
    # El wrapper se autodescarga la distribucion nueva en el siguiente gradlew.
    $wrapText = [System.IO.File]::ReadAllText($WrapperCfg)
    $wm = [regex]::Match($wrapText, 'gradle-(?<v>[\d.]+)-(bin|all)\.zip')
    if ($wm.Success) {
        $cur = $wm.Groups['v'].Value.TrimEnd('.')
        $latest = $null
        try {
            $latest = ((Invoke-WebRequest -UseBasicParsing -TimeoutSec 30 'https://services.gradle.org/versions/current').Content | ConvertFrom-Json).version
        } catch { Warn 'no se pudo consultar services.gradle.org' }
        if ($latest -and (Compare-Ver $latest $cur) -gt 0) {
            $kind = Get-BumpKind $cur $latest
            $applied = 'no'
            if (Should-Apply $kind 'gradle wrapper') {
                Backup-File $WrapperCfg
                $vg = $wm.Groups['v']
                $wrapText = $wrapText.Remove($vg.Index, $vg.Length).Insert($vg.Index, $latest)
                [System.IO.File]::WriteAllText($WrapperCfg, $wrapText, $utf8NoBom)
                $applied = $latest
            }
            Add-Finding 'android' 'gradle wrapper' $cur $latest $kind $applied
        }
    }

    # --- plugins del build.gradle.kts raiz -------------------------------
    $rootText = [System.IO.File]::ReadAllText($RootGradle)
    $pluginMap = @{
        'com.android.application'     = @('com.android.tools.build', 'gradle')
        'org.jetbrains.kotlin.android' = @('org.jetbrains.kotlin', 'kotlin-gradle-plugin')
    }
    foreach ($pluginId in $pluginMap.Keys) {
        $rx = 'id\("' + [regex]::Escape($pluginId) + '"\)\s+version\s+"(?<v>[^"]+)"'
        $m = [regex]::Match($rootText, $rx)
        if (-not $m.Success) { continue }
        $cur = $m.Groups['v'].Value
        $latest = Get-LatestMaven $pluginMap[$pluginId][0] $pluginMap[$pluginId][1]
        if (-not $latest) { continue }
        if ((Compare-Ver $latest $cur) -le 0) { continue }
        $name = "plugin $pluginId"
        $kind = Get-BumpKind $cur $latest
        if ($script:RiskyMinor -contains $name) { $kind = "$kind!" }
        $applied = 'no'
        if (Should-Apply (Get-BumpKind $cur $latest) $name) {
            Backup-File $RootGradle
            $g = $m.Groups['v']
            $rootText = $rootText.Remove($g.Index, $g.Length).Insert($g.Index, $latest)
            $applied = $latest
            [System.IO.File]::WriteAllText($RootGradle, $rootText, $utf8NoBom)
        }
        Add-Finding 'android' $name $cur $latest $kind $applied
    }

    # --- dependencias de app/build.gradle.kts ----------------------------
    $appText = [System.IO.File]::ReadAllText($AppGradle)
    $depRx = '(?<conf>implementation|api|compileOnly|runtimeOnly)\("(?<g>[^:"]+):(?<a>[^:"]+):(?<v>[^"]+)"\)'
    # De atras hacia delante: al reemplazar en sitio se mueven los indices.
    $matches = @([regex]::Matches($appText, $depRx))
    for ($i = $matches.Count - 1; $i -ge 0; $i--) {
        $m = $matches[$i]
        $g = $m.Groups['g'].Value; $a = $m.Groups['a'].Value; $cur = $m.Groups['v'].Value
        $latest = Get-LatestMaven $g $a
        if (-not $latest) { continue }
        if ((Compare-Ver $latest $cur) -le 0) { continue }
        $kind = Get-BumpKind $cur $latest
        $applied = 'no'
        if (Should-Apply $kind "$g`:$a") {
            Backup-File $AppGradle
            $vg = $m.Groups['v']
            $appText = $appText.Remove($vg.Index, $vg.Length).Insert($vg.Index, $latest)
            $applied = $latest
        }
        Add-Finding 'android' "$g`:$a" $cur $latest $kind $applied
    }
    if ($Apply) { [System.IO.File]::WriteAllText($AppGradle, $appText, $utf8NoBom) }

    # --- compileSdk / targetSdk (informativo) ----------------------------
    $sdkM = [regex]::Match($appText, 'compileSdk\s*=\s*(?<v>\d+)')
    if ($sdkM.Success) { Info "  compileSdk actual: $($sdkM.Groups['v'].Value) (no se toca automaticamente)" }
}

# ------------------------------------------------------------------- npm

function Update-Npm {
    Head 'npm (driver de run-triker)'
    if (-not (Test-Path (Join-Path $NpmPrefix 'package.json'))) { Info '  (no hay package.json)'; return }
    if (-not (Test-Path (Join-Path $NpmPrefix 'node_modules'))) {
        Info '  node_modules ausente; npm outdated no puede comparar. Ejecuta primero:'
        Info "    npm install --prefix .claude/skills/run-triker"
        return
    }

    # npm outdated sale con codigo 1 cuando ENCUENTRA algo desactualizado:
    # ExitCode aqui no es senal de error.
    $r = Invoke-Native -Exe 'npm' -Capture -Arguments @('outdated', '--prefix', $NpmPrefix, '--json')
    $txt = (@($r.Output) -join "`n").Trim()
    if (-not $txt -or $txt -eq '{}') { Ok 'deps npm al dia'; return }

    $o = $txt | ConvertFrom-Json
    foreach ($name in $o.PSObject.Properties.Name) {
        $e = $o.$name
        $cur = $e.current; if (-not $cur) { $cur = '(sin instalar)' }
        $kind = 'minor'
        if ((Test-Stable $cur) -and (Test-Stable $e.latest)) { $kind = Get-BumpKind $cur $e.latest }
        $applied = 'no'
        if (Should-Apply $kind $name) {
            $ri = Invoke-Native -Exe 'npm' -Arguments @('install', '--prefix', $NpmPrefix, "$name@$($e.latest)")
            if ($ri.ExitCode -eq 0) { $applied = $e.latest } else { $applied = 'ERROR' }
        }
        Add-Finding 'npm' $name $cur $e.latest $kind $applied
    }
}

# ------------------------------------------------------------ verificacion

function Invoke-Verify {
    Head 'Verificacion'

    Info 'dotnet build...'
    $rd = Invoke-Native -Exe 'dotnet' -WorkDir $RepoRoot -Capture -Arguments @('build')
    @($rd.Output) | Select-Object -Last 4 | ForEach-Object { Info "    $_" }
    $dotnetOk = ($rd.ExitCode -eq 0)
    if ($dotnetOk) { Ok 'dotnet build' } else { Bad 'FALLO dotnet build' }

    Info ''
    Info 'gradlew :app:assembleDebug...'
    $rg = Invoke-Native -Exe (Join-Path $AndroidDir 'gradlew.bat') -WorkDir $AndroidDir -Capture `
            -Arguments @(':app:assembleDebug', '--no-daemon')
    @($rg.Output) | Select-Object -Last 4 | ForEach-Object { Info "    $_" }
    $gradleOk = ($rg.ExitCode -eq 0)
    if ($gradleOk) { Ok 'gradlew :app:assembleDebug' } else { Bad 'FALLO gradlew :app:assembleDebug' }

    return ($dotnetOk -and $gradleOk)
}

# ------------------------------------------------------------------ main

if ($Rollback) { Invoke-Rollback }

# Cada -Apply parte de una copia limpia; si no, un segundo intento guardaria
# como "original" el fichero ya modificado por el primero.
if ($Apply -and (Test-Path $BackupDir)) { Remove-Item $BackupDir -Recurse -Force }

if ($Only -eq 'all' -or $Only -eq 'dotnet')  { Update-Dotnet }
if ($Only -eq 'all' -or $Only -eq 'android') { Update-Android }
if ($Only -eq 'all' -or $Only -eq 'npm')     { Update-Npm }

Head 'Resumen'
if ($script:Findings.Count -eq 0) {
    Ok 'Todo al dia. Nada que actualizar.'
    exit 0
}

$script:Findings |
    Sort-Object Area, Name |
    Format-Table @{L='Area';E={$_.Area}},
                 @{L='Dependencia';E={$_.Name}},
                 @{L='Actual';E={$_.Current}},
                 @{L='Ultima';E={$_.Latest}},
                 @{L='Salto';E={$_.Kind}},
                 @{L='Aplicado';E={$_.Applied}} -AutoSize |
    Out-String -Width 200 | Write-Host

# "minor!" = salto que semver llama minor pero esta en RiskyMinor.
$held = @($script:Findings | Where-Object { $_.Kind -eq 'major' -or $_.Kind -like '*!' })

if (-not $Apply) {
    Info 'Modo informe. Para aplicar minor/patch:'
    Info '  .\.claude\skills\update-deps\check-deps.ps1 -Apply'
    if ($held.Count -gt 0) {
        Info "Retenidos $($held.Count) (major o marcados '!'); revisa changelogs y luego:"
        Info '  .\.claude\skills\update-deps\check-deps.ps1 -Apply -IncludeMajor'
    }
    exit 0
}

if ($held.Count -gt 0 -and -not $IncludeMajor) {
    Warn "$($held.Count) sin aplicar (major o '!'). Revisa changelogs y repite con -IncludeMajor."
}

if ($SkipVerify) { Info ''; Info 'SkipVerify: no se ha compilado nada.'; exit 0 }

$okAll = Invoke-Verify
Info ''
if ($okAll) {
    Ok 'Compila todo. Revisa el diff y commitea:'
    Info '  git diff --stat'
} else {
    Bad 'Algo no compila. Para deshacer SOLO lo que ha tocado este script:'
    Info '  .\.claude\skills\update-deps\check-deps.ps1 -Rollback'
    Info ''
    Info '(usa la copia de .claude/skills/update-deps/.backup/, asi que respeta'
    Info ' el resto de cambios sin commitear que tengas en esos ficheros)'
    exit 1
}
