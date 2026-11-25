# Abre el visor del Ojeador en una ventana propia de este equipo: el vídeo de un análisis con lo que vio el analizador
# encima (jugadores, balón, campo) y cómo van saliendo las estadísticas, instante a instante. Es un programa aparte de la
# app: solo lee las carpetas de los análisis y no cambia nada. Busca los que deja el analizador de la app
# (Start-Analyzer.ps1) en el volumen de Docker del entorno y los de prueba de src\TercerTiempo.Analisis\salidas.
#   -Environment FakeDatos   (por defecto) o Development: de qué entorno de Docker se leen los análisis.
#   -Folder <carpeta>        otra carpeta con análisis, o la de uno en concreto (se pueden dar varias).
#   -Port <n>                puerto fijo (por defecto, uno libre).
#   -NoWindow                sin ventana: escribe la dirección para abrirla en el navegador de siempre (se para con
#                            Ctrl+C).
#   -Pause                   si algo falla, la ventana espera a que se lea el mensaje (lo usa Visor-Ojeador.cmd).
# Un análisis hecho antes de que el Ojeador guardara la repetición sale en la lista, pero para verlo hay que volver a
# analizar el vídeo («Volver a analizar» en el acta interna). El visor se cierra al cerrar su ventana.
param(
    [ValidateSet('Development', 'FakeDatos')][string]$Environment = 'FakeDatos',
    [string[]]$Folder = @(),
    [int]$Port = 0,
    [switch]$NoWindow,
    [switch]$Pause
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$Host.UI.RawUI.WindowTitle = 'Visor del Ojeador'

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

# Del .env del entorno solo hace falta el nombre del volumen de datos: no se lee nada más.
function Get-DataVolume([string]$Path) {
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^\s*TT_DATA_VOLUME\s*=(.*)$') { return $Matches[1].Trim() }
    }
    return $null
}

try {
    $analyzer = Join-Path $PSScriptRoot 'src\TercerTiempo.Analisis'
    $uv = Find-Uv
    if (-not $uv) { throw 'No se encuentra uv: instálalo con «python -m pip install --user uv» (o «winget install astral-sh.uv») y vuelve a lanzar el script.' }

    $folders = New-Object System.Collections.Generic.List[string]
    $envFile = Join-Path $PSScriptRoot ".env.$($Environment.ToLowerInvariant())"
    $volume = if (Test-Path -LiteralPath $envFile) { Get-DataVolume $envFile } else { $null }
    if ($volume) {
        # Docker Desktop expone sus volúmenes a Windows por aquí; el analizador deja cada análisis en videos\analysis\<vídeo>.
        $dockerAnalyses = "\\wsl.localhost\docker-desktop\mnt\docker-desktop-disk\data\docker\volumes\$volume\_data\videos\analysis"
        if (Test-Path -LiteralPath $dockerAnalyses) { $folders.Add($dockerAnalyses) }
        else { Write-Warning "No se encuentran los análisis de $Environment ($dockerAnalyses): ¿está Docker Desktop en marcha y se ha analizado algún vídeo?" }
    } else {
        Write-Warning "Sin $envFile (o sin TT_DATA_VOLUME en él): no se buscan los análisis de la app de $Environment."
    }
    $tests = Join-Path $analyzer 'salidas'
    if (Test-Path -LiteralPath $tests) { $folders.Add($tests) }
    foreach ($extra in $Folder) {
        if (-not (Test-Path -LiteralPath $extra)) { throw "No existe la carpeta $extra." }
        $folders.Add((Resolve-Path -LiteralPath $extra).ProviderPath)
    }

    Write-Host 'Abriendo el visor del Ojeador (se cierra al cerrar su ventana)...' -ForegroundColor Cyan
    Push-Location -LiteralPath $analyzer
    try {
        $arguments = @('run', '--frozen', 'tt-analisis', 'visor') + $folders.ToArray() + @('--puerto', "$Port") + $(if ($NoWindow) { @('--sin-ventana') } else { @() })
        # Sin que Windows PowerShell 5.1 convierta los avisos de uv por stderr en errores.
        $ErrorActionPreference = 'Continue'
        & $uv @arguments
        if ($LASTEXITCODE) { throw "El visor terminó con el código $LASTEXITCODE (detalles arriba)." }
    } finally {
        Pop-Location
    }
} catch {
    Write-Host "No se pudo abrir el visor: $($_.Exception.Message)" -ForegroundColor Red
    if ($Pause) { Read-Host 'Pulsa Intro para cerrar esta ventana' | Out-Null }
    exit 1
}
