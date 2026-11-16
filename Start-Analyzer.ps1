# Arranca el servicio de vídeo fuera de Docker solo como ANALIZADOR: sin compresor (eso lo hace el contenedor), con la
# GPU de este equipo y TercerTiempo.Analisis, contra la misma base del entorno y los mismos vídeos, que lee del volumen
# de Docker Desktop a través de \\wsl.localhost. Atiende la cola de análisis que la API y el compresor van llenando (al
# terminar cada compresión y con «Analizar el vídeo» en el acta interna), deja la propuesta en la base y, mientras
# corre, un latido: si no hay ningún analizador encendido, el acta interna lo avisa.
#   -Environment FakeDatos   (por defecto) o Development: el entorno de la API en Docker, con su .env y su base.
#   -Database Local|Container solo en Development: la base que usa la API de Docker (por defecto, TT_DEV_DATABASE de
#                            .env.development). Con Container se conecta al SQL Server del contenedor (127.0.0.1,TT_SQL_PORT).
#   -VideosDirectory         carpeta de los vídeos si no es la del volumen de Docker (p. ej. la de Visual Studio).
#   -Segment 20:00-25:00     analiza solo ese tramo de cada vídeo (para probar sin esperar el partido entero).
#   -Port                    puerto del proceso (5083 en FakeDatos, 5084 en Development; sus rutas /media no las usa
#                            nadie: está para la salud y la referencia).
#   -Background              lo lanza Start-Docker.ps1 en una ventana minimizada: si algo falla, la ventana espera a
#                            que se lea el mensaje.
# Antes de arrancar comprueba lo que hace falta y dice qué falta: uv, «uv sync --frozen» del analizador, que torch vea
# CUDA, los pesos de modelos/ y que la carpeta de los vídeos se pueda leer y escribir. Solo uno por entorno a la vez.
# La salida queda también en %LOCALAPPDATA%\TercerTiempo\analizador-<entorno>.log. Se para con Ctrl+C.
param(
    [ValidateSet('Development', 'FakeDatos')][string]$Environment = 'FakeDatos',
    [ValidateSet('Local', 'Container')][string]$Database,
    [string]$VideosDirectory,
    [string]$Segment,
    [int]$Port,
    [switch]$Background
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$Host.UI.RawUI.WindowTitle = "Analizador de vídeo ($Environment)"

$logFolder = Join-Path $env:LOCALAPPDATA 'TercerTiempo'
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
$logFile = Join-Path $logFolder "analizador-$($Environment.ToLowerInvariant()).log"
try { Start-Transcript -LiteralPath $logFile -Force | Out-Null } catch { }

function Read-EnvFile([string]$Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
    }
    return $values
}

# Ejecuta un comando nativo sin que Windows PowerShell 5.1 convierta sus avisos de stderr en errores; devuelve el código.
function Invoke-Quiet([scriptblock]$Command) {
    $ErrorActionPreference = 'Continue'
    & $Command 2>&1 | ForEach-Object { "$_" } | Out-Host
    return $LASTEXITCODE
}

function Find-Uv {
    $command = Get-Command uv -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($candidate in @(
            (Join-Path $env:APPDATA 'Python\Python314\Scripts\uv.exe'),
            (Join-Path $env:APPDATA 'Python\Python313\Scripts\uv.exe'),
            (Join-Path $env:USERPROFILE '.local\bin\uv.exe'))) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

$mutex = $null
try {
    # Solo un analizador por entorno: Start-Docker.ps1 mira este mismo nombre antes de lanzar otro.
    $created = $false
    $mutex = New-Object System.Threading.Mutex($true, "Local\TercerTiempo.Analizador.$Environment", [ref]$created)
    if (-not $created) {
        $mutex.Dispose()
        $mutex = $null
        Write-Host "Ya hay un analizador de vídeo de $Environment en marcha en este equipo: no se arranca otro." -ForegroundColor Yellow
        return
    }

    $envFile = Join-Path $PSScriptRoot ".env.$($Environment.ToLowerInvariant())"
    $settings = if (Test-Path -LiteralPath $envFile) { Read-EnvFile $envFile } else { @{} }

    if (-not $VideosDirectory) {
        if (-not (Test-Path -LiteralPath $envFile)) { throw "Falta ${envFile}: arranca antes el entorno con .\Start-Docker.ps1 -Environment $Environment, o indica -VideosDirectory." }
        $volume = $settings['TT_DATA_VOLUME']
        if (-not $volume) { throw "No hay TT_DATA_VOLUME en $envFile." }
        # Docker Desktop expone sus volúmenes a Windows por aquí (una sola distribución docker-desktop desde la 4.30).
        $VideosDirectory = "\\wsl.localhost\docker-desktop\mnt\docker-desktop-disk\data\docker\volumes\$volume\_data\videos"
    }
    if (-not (Test-Path -LiteralPath $VideosDirectory)) {
        throw "No se encuentra la carpeta de vídeos $VideosDirectory. ¿Está Docker Desktop en marcha con el entorno $Environment (y se ha subido algún vídeo)? Si los vídeos están en otro sitio, indícalo con -VideosDirectory."
    }
    # Las salidas del analizador van a analysis/ junto a los vídeos: tiene que poder escribir ahí.
    $outputFolder = Join-Path $VideosDirectory 'analysis'
    try {
        New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
        $probe = Join-Path $outputFolder ".escritura-$PID"
        Set-Content -LiteralPath $probe -Value 'ok' -Encoding ascii
        Remove-Item -LiteralPath $probe -Force
    } catch {
        throw "No se puede escribir en $outputFolder ($($_.Exception.Message)): el analizador deja ahí su salida."
    }

    $analyzer = Join-Path $PSScriptRoot 'src\TercerTiempo.Analisis'
    $uv = Find-Uv
    if (-not $uv) { throw 'No se encuentra uv: instálalo con «python -m pip install --user uv» (o «winget install astral-sh.uv») y vuelve a lanzar el script.' }
    Write-Host "uv: $uv" -ForegroundColor DarkGray

    # El entorno de Python, exactamente como dice uv.lock (--frozen: nada de resolver versiones nuevas; --inexact: no
    # quita lo instalado aparte, como pytest).
    Write-Host 'Preparando el entorno de Python del analizador (uv sync --frozen)...' -ForegroundColor Cyan
    Push-Location -LiteralPath $analyzer
    try {
        if ((Invoke-Quiet { & $uv sync --frozen --inexact }) -ne 0) { throw "«uv sync --frozen» falló en ${analyzer}: revisa la salida de arriba (¿sin conexión la primera vez?)." }

        Write-Host 'Comprobando que torch ve la GPU (CUDA)...' -ForegroundColor Cyan
        # Sin comillas dobles dentro: Windows PowerShell 5.1 las quita al pasar el argumento a un programa nativo.
        $probeCode = 'import sys, torch; ok = torch.cuda.is_available(); print((''GPU: '' + torch.cuda.get_device_name(0)) if ok else (''torch '' + torch.__version__ + '' sin CUDA'')); sys.exit(0 if ok else 3)'
        if ((Invoke-Quiet { & $uv run --frozen python -c $probeCode }) -ne 0) {
            throw 'torch no ve ninguna GPU con CUDA: actualiza el controlador de NVIDIA (nvidia-smi debe funcionar) y comprueba que uv.lock trae torch con CUDA (índice pytorch-cu128).'
        }
    } finally {
        Pop-Location
    }

    $missing = @('football-player-detection.pt', 'football-ball-detection.pt', 'football-pitch-detection.pt') |
        Where-Object { -not (Test-Path -LiteralPath (Join-Path $analyzer "modelos\$_")) }
    if ($missing) {
        throw "Faltan pesos en $analyzer\modelos: $($missing -join ', '). Descárgalos con «uv run --frozen tt-analisis descargar-modelos» desde esa carpeta."
    }

    # Development: la base que usa la API de Docker (la de Visual Studio o la del contenedor).
    if ($Environment -eq 'Development') {
        if (-not $Database) { $Database = if ($settings['TT_DEV_DATABASE'] -eq 'Local') { 'Local' } else { 'Container' } }
        if ($Database -eq 'Container') {
            $sqlPassword = $settings['TT_SQL_PASSWORD']
            if (-not $sqlPassword) { throw 'Falta TT_SQL_PASSWORD en .env.development (la base del contenedor).' }
            $sqlPort = if ($settings['TT_SQL_PORT']) { $settings['TT_SQL_PORT'] } else { '14333' }
            $env:ConnectionStrings__Default = "Server=127.0.0.1,$sqlPort;Database=TercerTiempo;User Id=sa;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=True"
            $env:ConnectionStrings__RealData = "Server=127.0.0.1,$sqlPort;Database=tercertiempo_DatosReales;User Id=sa;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=True"
        } else {
            Remove-Item Env:ConnectionStrings__Default, Env:ConnectionStrings__RealData -ErrorAction SilentlyContinue
        }
        Write-Host "Base de datos de desarrollo: $Database" -ForegroundColor DarkGray
    }

    if (-not $Port) { $Port = if ($Environment -eq 'FakeDatos') { 5083 } else { 5084 } }
    $env:ASPNETCORE_ENVIRONMENT = $Environment
    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    $env:Videos__Directory = $VideosDirectory
    $env:VideoProcessing__Enabled = 'false'
    $env:Analysis__Enabled = 'true'
    $env:Analysis__RunWorker = 'true'
    $env:Analysis__UvPath = $uv
    if ($Segment) { $env:Analysis__Segment = $Segment } else { Remove-Item Env:Analysis__Segment -ErrorAction SilentlyContinue }

    Write-Host "Analizador de vídeo ($Environment): vídeos en $VideosDirectory$(if ($Segment) { ", solo el tramo $Segment" })" -ForegroundColor Cyan
    Write-Host "Atiende la cola de análisis de la API; Ctrl+C para parar. Registro: $logFile" -ForegroundColor Cyan
    $ErrorActionPreference = 'Continue'
    dotnet run --project src\TercerTiempo.Videos --no-launch-profile
    # Un fallo al arrancar (sin base, sin compilar) deja el código distinto de 0; Ctrl+C para el script antes de llegar.
    if ($LASTEXITCODE) { throw "El servicio de vídeo terminó con el código $LASTEXITCODE (detalles arriba y en $logFile)." }
} catch {
    Write-Host "No se pudo arrancar el analizador de vídeo: $($_.Exception.Message)" -ForegroundColor Red
    if ($Background) { Read-Host 'Pulsa Intro para cerrar esta ventana' | Out-Null }
    exit 1
} finally {
    if ($mutex) { $mutex.ReleaseMutex(); $mutex.Dispose() }
    try { Stop-Transcript | Out-Null } catch { }
}
