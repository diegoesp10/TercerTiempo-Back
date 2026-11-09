# Arranca el servicio de vídeo fuera de Docker solo como ANALIZADOR: sin compresor (eso lo hace el contenedor), con la
# GPU de este equipo y TercerTiempo.Analisis, contra la misma base del entorno y los mismos vídeos, que lee del volumen
# de Docker Desktop a través de \\wsl.localhost. Atiende la cola de análisis que la API va llenando (al terminar cada
# compresión y con «Analizar el vídeo» en el acta interna) y deja la propuesta en la base.
#   -Environment FakeDatos   (por defecto) o Development: el entorno de la API en Docker, con su .env y su base.
#   -VideosDirectory         carpeta de los vídeos si no es la del volumen de Docker (p. ej. la de Visual Studio).
#   -Segment 20:00-25:00     analiza solo ese tramo de cada vídeo (para probar sin esperar el partido entero).
#   -Port 5083               puerto del proceso (sus rutas /media no las usa nadie; está para la salud y la referencia).
# Hace falta uv (python -m pip install --user uv) y haber hecho «uv sync» en src/TercerTiempo.Analisis. Se para con Ctrl+C.
param(
    [ValidateSet('Development', 'FakeDatos')][string]$Environment = 'FakeDatos',
    [string]$VideosDirectory,
    [string]$Segment,
    [int]$Port = 5083
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

if (-not $VideosDirectory) {
    $envFile = Join-Path $PSScriptRoot ".env.$($Environment.ToLowerInvariant())"
    if (-not (Test-Path -LiteralPath $envFile)) { throw "Falta ${envFile}: arranca antes el entorno con .\Start-Docker.ps1 -Environment $Environment, o indica -VideosDirectory." }
    $volume = (Get-Content -LiteralPath $envFile | Where-Object { $_ -match '^\s*TT_DATA_VOLUME\s*=(.+)$' } | ForEach-Object { $Matches[1].Trim() } | Select-Object -First 1)
    if (-not $volume) { throw "No hay TT_DATA_VOLUME en $envFile." }
    # Docker Desktop expone sus volúmenes a Windows por aquí (una sola distribución docker-desktop desde la 4.30).
    $VideosDirectory = "\\wsl.localhost\docker-desktop\mnt\docker-desktop-disk\data\docker\volumes\$volume\_data\videos"
}
if (-not (Test-Path -LiteralPath $VideosDirectory)) {
    throw "No se encuentra la carpeta de vídeos $VideosDirectory. ¿Está Docker Desktop en marcha con el entorno $Environment? Si los vídeos están en otro sitio, indícalo con -VideosDirectory."
}

$analyzer = Join-Path $PSScriptRoot 'src\TercerTiempo.Analisis'
if (-not (Test-Path -LiteralPath (Join-Path $analyzer '.venv'))) {
    Write-Warning "No hay entorno de Python en $analyzer (uv sync): el análisis fallará hasta que lo haya."
}

$env:ASPNETCORE_ENVIRONMENT = $Environment
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:Videos__Directory = $VideosDirectory
$env:VideoProcessing__Enabled = 'false'
$env:Analysis__Enabled = 'true'
if ($Segment) { $env:Analysis__Segment = $Segment } else { Remove-Item Env:Analysis__Segment -ErrorAction SilentlyContinue }

Write-Host "Analizador de vídeo ($Environment): vídeos en $VideosDirectory$(if ($Segment) { ", solo el tramo $Segment" })" -ForegroundColor Cyan
Write-Host 'Atiende la cola de análisis de la API; Ctrl+C para parar.' -ForegroundColor Cyan
dotnet run --project src\TercerTiempo.Videos --no-launch-profile
