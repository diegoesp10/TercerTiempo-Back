# Construye y arranca la API de Tercer Tiempo en Docker.
#   -Environment Development  (por defecto) API en Development; genera .env.development la primera vez y, si no
#                             está ahí, toma la contraseña de /jobs de los user-secrets del proyecto.
#   -Database Local           solo en Development: el SQL Server de Windows del equipo (la base de Visual Studio) con
#                             el login de scripts/local-sql-login.sql (TT_LOCAL_SQL_PASSWORD en .env.development).
#   -Database Container       solo en Development: un SQL Server propio en el contenedor tercer-tiempo-db.
#                             Sin -Database se usa TT_DEV_DATABASE de .env.development (Container si no está).
#   -WithJobs                 con -Database Local, la API del contenedor también lanza los scrapeos por horario (por
#                             defecto solo a mano desde /jobs, porque la API de Visual Studio comparte la base).
#   -Environment Production   API en Production con los secretos de .env.production (copia .env.example).
#   -WithDatabase             solo en Production: arranca también el SQL Server del equipo (perfil "database").
#   -SkipBuild                reutiliza la imagen existente.
#   -ReplaceData              vuelve a copiar al volumen los escudos y fotos de src/TercerTiempo.Api/data.
param(
    [ValidateSet('Development', 'Production')][string]$Environment = 'Development',
    [ValidateSet('Local', 'Container')][string]$Database,
    [switch]$WithDatabase,
    [switch]$WithJobs,
    [switch]$SkipBuild,
    [switch]$ReplaceData
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

# Ejecuta un comando nativo y falla con su código de salida (sus avisos van a stderr, que Windows PowerShell 5.1
# convierte en errores con 'Stop').
function Invoke-Native([scriptblock]$Command, [string]$Message) {
    $ErrorActionPreference = 'Continue'
    & $Command
    if ($LASTEXITCODE -ne 0) { throw $Message }
}

function Read-EnvFile([string]$Path) {
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=(.*)$') { $values[$Matches[1]] = $Matches[2].Trim() }
    }
    return $values
}

function New-Secret([int]$Length) {
    $chars = 'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    $bytes = New-Object byte[] $Length
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    # Siempre con mayúscula, minúscula y número: es la política de contraseñas de SQL Server.
    return 'Tt9' + (-join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] }))
}

$dockerOs = docker info --format '{{.OSType}}' 2>$null
if ($LASTEXITCODE -ne 0 -or $dockerOs -ne 'linux') { throw 'Docker Desktop debe estar iniciado con contenedores Linux.' }

$envName = $Environment.ToLowerInvariant()
$envFile = Join-Path $PSScriptRoot ".env.$envName"
if (-not (Test-Path -LiteralPath $envFile)) {
    if ($Environment -eq 'Production') { throw 'Falta .env.production: copia .env.example y rellena los secretos de publicación.' }
    @(
        '# Generado por Start-Docker.ps1 para el entorno de desarrollo en Docker. No se versiona.'
        'TT_DATA_VOLUME=tercer-tiempo-dev-data'
        'TT_API_PORT=8181'
        'TT_PUBLIC_URL=http://127.0.0.1:8180'
        'TT_ADMIN_USER=diego'
        '# Base de datos por defecto: Local (SQL Server de Windows) o Container.'
        'TT_DEV_DATABASE=Container'
        '# Login tercertiempo_app del SQL Server local (scripts/local-sql-login.sql).'
        'TT_LOCAL_SQL_PASSWORD='
        "TT_SQL_PASSWORD=$(New-Secret 20)"
        'TT_SQL_PORT=14333'
    ) | Set-Content -LiteralPath $envFile -Encoding ascii
    Write-Host "Creado $envFile con una contraseña de sa aleatoria." -ForegroundColor Cyan
}
$settings = Read-EnvFile $envFile

# La contraseña de /jobs del entorno de desarrollo es la de los user-secrets de la API (nunca se escribe en disco).
if ($Environment -eq 'Development' -and -not $settings['TT_ADMIN_PASSWORD']) {
    $secret = dotnet user-secrets list --project src/TercerTiempo.Api 2>$null | Select-String '^Admin:Password = '
    if ($secret) { $env:TT_ADMIN_PASSWORD = ($secret.Line -split ' = ', 2)[1] }
}

# Recursos compartidos: la red une API y frontal; los volúmenes guardan ficheros y base de datos.
$network = docker network ls --filter 'name=^tercer-tiempo-net$' --format '{{.Name}}'
if (-not $network) { Invoke-Native { docker network create tercer-tiempo-net | Out-Null } 'No se pudo crear la red tercer-tiempo-net.' }
$dataVolume = $settings['TT_DATA_VOLUME']
if (-not $dataVolume) { throw "Falta TT_DATA_VOLUME en $envFile." }
Invoke-Native { docker volume create $dataVolume | Out-Null } "No se pudo preparar el volumen $dataVolume."

$compose = @('compose', '-f', 'compose.yaml', '-f', "compose.$envName.yaml", '--env-file', $envFile)
if ($Environment -eq 'Production' -and $WithDatabase) { $compose += @('--profile', 'database') }

# Desarrollo: la cadena de conexión depende de qué SQL Server se use (la variable del proceso manda sobre el .env).
if ($Environment -eq 'Development') {
    if (-not $Database) { $Database = if ($settings['TT_DEV_DATABASE'] -eq 'Local') { 'Local' } else { 'Container' } }
    if ($Database -eq 'Local') {
        $localPassword = $settings['TT_LOCAL_SQL_PASSWORD']
        if (-not $localPassword) { throw 'Falta TT_LOCAL_SQL_PASSWORD en .env.development (login de scripts/local-sql-login.sql).' }
        $env:TT_DB_CONNECTION = "Server=host.docker.internal,1433;Database=TercerTiempo;User Id=tercertiempo_app;Password=$localPassword;TrustServerCertificate=True;Encrypt=True"
        # El SQL del contenedor no hace falta: si estaba en marcha, se para (sus datos siguen en su volumen).
        if (docker ps -q --filter 'name=^tercer-tiempo-db$') { Invoke-Native { docker stop tercer-tiempo-db | Out-Null } 'No se pudo parar tercer-tiempo-db.' }
        if (-not $WithJobs) { $env:TT_SYNC_MINUTES = '0'; $env:TT_SHEETS_MINUTES = '0' }
    } else {
        if (-not $settings['TT_SQL_PASSWORD']) {
            $settings['TT_SQL_PASSWORD'] = New-Secret 20
            Add-Content -LiteralPath $envFile -Value "TT_SQL_PASSWORD=$($settings['TT_SQL_PASSWORD'])", 'TT_SQL_PORT=14333' -Encoding ascii
            $settings['TT_SQL_PORT'] = '14333'
        }
        $env:TT_DB_CONNECTION = "Server=db;Database=TercerTiempo;User Id=sa;Password=$($settings['TT_SQL_PASSWORD']);TrustServerCertificate=True;Encrypt=True"
        $compose += @('--profile', 'container-db')
    }
    Write-Host "Base de datos de desarrollo: $Database" -ForegroundColor Cyan
}

if (-not $SkipBuild) {
    Invoke-Native { docker @compose build api } 'No se pudo construir la imagen de la API.'
}

# Copia al volumen los escudos y fotos guardados en local cuando el volumen está vacío (o con -ReplaceData). La
# carpeta se monta de solo lectura y nunca entra en la imagen.
$localData = Join-Path $PSScriptRoot 'src/TercerTiempo.Api/data'
if (Test-Path -LiteralPath $localData) {
    $replace = if ($ReplaceData) { '1' } else { '0' }
    # Sin comillas dobles dentro: Windows PowerShell 5.1 no las escapa para los comandos nativos.
    $script = 'set -e; for d in crests photos; do mkdir -p /app/data/$d; ' +
        'if [ -d /import/$d ] && { [ $REPLACE = 1 ] || [ $(ls -A /app/data/$d | wc -l) = 0 ]; }; then ' +
        'cp -a /import/$d/. /app/data/$d/; echo Copiado $d al volumen.; fi; done; chown -R app:app /app/data'
    Invoke-Native {
        docker run --rm --user 0 --entrypoint sh -e "REPLACE=$replace" `
            --mount "type=volume,source=$dataVolume,target=/app/data" `
            --mount "type=bind,source=$localData,target=/import,readonly" `
            tercer-tiempo-api:local -c $script
    } 'No se pudieron copiar los ficheros locales al volumen.'
}

Invoke-Native { docker @compose up -d --wait --wait-timeout 240 } 'La API no alcanzó un estado saludable. Consulta docker compose logs.'
$apiPort = if ($settings['TT_API_PORT']) { $settings['TT_API_PORT'] } else { '8181' }
Write-Host "API Docker ($Environment) lista: http://127.0.0.1:$apiPort/api/health" -ForegroundColor Green
if ($Environment -eq 'Development') {
    Write-Host "Referencia de la API: http://127.0.0.1:$apiPort/docs" -ForegroundColor Green
    if ($Database -eq 'Container') {
        Write-Host "SQL Server del contenedor: 127.0.0.1,$($settings['TT_SQL_PORT']) (usuario sa, contraseña en .env.development)" -ForegroundColor Green
    } else {
        Write-Host 'Base de datos: el SQL Server local del equipo (TercerTiempo)' -ForegroundColor Green
    }
}
docker @compose ps
