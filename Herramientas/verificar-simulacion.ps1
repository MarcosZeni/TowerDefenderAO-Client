# =============================================================================
#  Guarda de determinismo
#
#  Falla el build si adentro de Simulacion\ aparece algo que rompa la
#  reproducibilidad. Lo llama el .csproj antes de compilar.
#
#  Por que existe: las dos reglas que sostienen el determinismo son faciles de
#  romper sin darse cuenta —el autocompletado mete un `using Godot`, alguien
#  escribe `float velocidad` porque es lo natural— y el sintoma NO aparece al
#  compilar. Aparece meses despues, como una revalidacion del servidor que
#  acusa de trampa a un jugador honesto.
#
#  Es mas barato fallar aca.
# =============================================================================

param(
    [string]$Carpeta = "Simulacion"
)

$ErrorActionPreference = "Stop"

# Que se prohibe, y por que:
#
#   using Godot   la simulacion tiene que poder correr en el servidor, que no
#                 tiene Godot instalado
#   float/double  la aritmetica de punto flotante acumula deriva a lo largo de
#                 los ~216.000 ticks de una corrida larga
#   System.Random cambio de algoritmo en .NET 6 y no promete estabilidad entre
#                 versiones: el servidor revalidaria con otra secuencia
#   Sin/Cos/...   los resuelve la libm de cada sistema operativo, y ahi no hay
#                 ninguna garantia entre plataformas
$patrones = @(
    @{ Regex = 'using\s+Godot';                          Motivo = 'la simulacion no puede depender del motor' },
    @{ Regex = '\bfloat\b';                              Motivo = 'punto flotante: acumula deriva' },
    @{ Regex = '\bdouble\b';                             Motivo = 'punto flotante: acumula deriva' },
    @{ Regex = '\bdecimal\b';                            Motivo = 'no es entero' },
    @{ Regex = 'System\.Random|[^.\w]Random\s*\(';       Motivo = 'usar Aleatorio, el PRNG propio' },
    @{ Regex = 'Math\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Pow|Log|Log10|Exp|Sqrt|Cbrt)\b';
                                                         Motivo = 'trascendente: depende de la libm del sistema' }
)

if (-not (Test-Path $Carpeta)) {
    Write-Host "verificar-simulacion: no existe la carpeta '$Carpeta', nada que revisar."
    exit 0
}

$hallazgos = @()

foreach ($archivo in Get-ChildItem -Path $Carpeta -Filter *.cs -Recurse) {
    $numero = 0

    foreach ($linea in Get-Content -Path $archivo.FullName -Encoding UTF8) {
        $numero++

        # Los comentarios se saltean: este archivo mismo, y las XML docs de la
        # simulacion, NOMBRAN lo prohibido para explicar por que lo esta. Sin
        # esta guarda el chequeo se acusaria a si mismo.
        if ($linea -match '^\s*(//|\*|/\*)') { continue }

        foreach ($p in $patrones) {
            if ($linea -match $p.Regex) {
                $hallazgos += [PSCustomObject]@{
                    Archivo = $archivo.Name
                    Linea   = $numero
                    Motivo  = $p.Motivo
                    Texto   = $linea.Trim()
                }
            }
        }
    }
}

if ($hallazgos.Count -eq 0) {
    exit 0
}

Write-Host ""
Write-Host "PROHIBIDO EN LA SIMULACION  ($($hallazgos.Count) hallazgo/s)"
Write-Host "-----------------------------------------------------------"

foreach ($h in $hallazgos) {
    Write-Host ("  {0}({1}): {2}" -f $h.Archivo, $h.Linea, $h.Motivo)
    Write-Host ("      {0}" -f $h.Texto)
}

Write-Host ""
Write-Host "La simulacion tiene que dar el mismo resultado en cualquier maquina."
Write-Host "Ver JUGABILIDAD.md 4.2. Si de verdad hace falta un valor fraccionario,"
Write-Host "la salida es una unidad mas chica en enteros —milesimas—, no un float."
Write-Host ""

exit 1
