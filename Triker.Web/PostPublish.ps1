<#
.SYNOPSIS
    Aplica fingerprinting real a los assets estaticos en el output de publish para
    hosting estatico (nginx/IIS sin middleware ASP.NET Core).

    El SDK de .NET calcula fingerprints y registra las rutas fingerprinted en el
    staticwebassets.endpoints.json, pero NO crea los archivos fisicos renombrados
    ni actualiza index.html cuando se publica como app WASM standalone.

    Este script:
      1. Lee el manifiesto de endpoints del directorio de publish.
      2. Para cada ruta fingerprinted de un asset permitido que esta referenciada
         en index.html, copia el archivo base -> nombre fingerprinted (+ .br/.gz).
      3. Actualiza index.html para que apunte al nombre fingerprinted.

    EXCLUYE intencionalmente:
      - Archivos .js   => service-worker.js tiene semantica especial de actualizacion;
                          los scripts de framework ya los gestiona el SDK de Blazor.
      - Archivos .json => manifest.json debe tener URL estable para PWAs instaladas.
#>
param(
    [Parameter(Mandatory)][string]$PublishDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Extensiones de archivo que se pueden fingerprinting con seguridad en index.html
$AllowedExtensions = @('.css', '.svg', '.png', '.jpg', '.jpeg', '.ico', '.gif', '.webp', '.woff', '.woff2')

$wwwroot       = Join-Path $PublishDir 'wwwroot'
$endpointsFile = Join-Path $PublishDir 'Triker.Web.staticwebassets.endpoints.json'
$indexFile     = Join-Path $wwwroot    'index.html'

if (-not (Test-Path $endpointsFile)) {
    Write-Warning "No se encontro el manifiesto de endpoints: $endpointsFile"
    exit 0
}

# Leer rutas unicas del manifiesto (ignorar variantes comprimidas)
$allRoutes = (Get-Content $endpointsFile -Raw | ConvertFrom-Json).Endpoints |
             Select-Object -ExpandProperty Route -Unique |
             Where-Object { $_ -notmatch '\.(br|gz)$' }

# Patron: detecta rutas con hash de 10 chars alfanumericos antes de la extension final
# Ej: css/app.quae7vefm1.css         =>  stem=css/app,       ext=.css
# Ej: Triker.Web.2h2kz2v1n4.styles.css =>  stem=Triker.Web,  ext=.styles.css
# Ej: favicon.ohl7gz2s07.svg          =>  stem=favicon,       ext=.svg
$fpPattern = '^(.+?)\.([a-z0-9]{10})((?:\.[a-z0-9]+)+)$'

# Construir mapa: nombre_base -> nombre_fingerprinted
$routeMap = @{}
foreach ($route in $allRoutes) {
    if ($route -notmatch $fpPattern) { continue }

    $stem = $Matches[1]
    $ext  = $Matches[3]  # incluye el punto inicial, ej: ".css" o ".styles.css"
    $base = "$stem$ext"

    # Solo extensiones permitidas (la ultima parte de $ext)
    $lastExt = '.' + ($ext -split '\.' | Select-Object -Last 1)
    if ($lastExt -notin $AllowedExtensions) { continue }

    $routeMap[$base] = $route
}

if ($routeMap.Count -eq 0) {
    Write-Host '[PostPublish] Sin rutas fingerprinted aplicables; nada que hacer.'
    exit 0
}

$html    = Get-Content $indexFile -Raw
$changed = $false

foreach ($base in $routeMap.Keys) {
    $fingerprinted = $routeMap[$base]
    $srcFile = Join-Path $wwwroot $base

    # Solo actuar si el archivo fisico existe Y esta referenciado en index.html
    if (-not (Test-Path $srcFile))              { continue }
    if ($html -notmatch [regex]::Escape($base)) { continue }

    $dstFile = Join-Path $wwwroot $fingerprinted
    Write-Host "  [fingerprint] $base -> $fingerprinted"

    # Copiar archivo + variantes comprimidas si existen
    if (-not (Test-Path $dstFile)) { Copy-Item $srcFile $dstFile }
    foreach ($enc in '.br', '.gz') {
        $src = "$srcFile$enc"
        $dst = "$dstFile$enc"
        if ((Test-Path $src) -and -not (Test-Path $dst)) { Copy-Item $src $dst }
    }

    # Reemplazar referencia en index.html
    $html    = $html -replace [regex]::Escape($base), $fingerprinted
    $changed = $true
}

if ($changed) {
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($indexFile, $html, $utf8NoBom)
    Write-Host '[PostPublish] index.html actualizado con referencias fingerprinted.'
} else {
    Write-Host '[PostPublish] Ninguna referencia actualizable encontrada; sin cambios.'
}
