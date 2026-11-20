param([string]$Patch)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$assets = Join-Path $PSScriptRoot '../src/Aumentos.App/Assets'
$catalog = Get-Content -LiteralPath (Join-Path $assets 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$Patch) { $Patch = $catalog.patch }
if ($Patch -ne $catalog.patch -or $Patch -notmatch '^\d{1,3}\.\d{1,3}$') { throw 'Usa el parche actual del catálogo.' }
$version = [version]$Patch
if ($version.Minor -le 1) { throw 'El catálogo no tiene un parche anterior de la misma temporada.' }
$previous = "$($version.Major).$($version.Minor - 1)"

function Read-Patch([string]$Version) {
    $base = "https://raw.communitydragon.org/$Version"
    $mode = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 30 "$base/game/maps/modespecificdata/kiwi.bin.json").Content | ConvertFrom-Json
    $pools = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 30 "$base/plugins/rcp-be-lol-game-data/global/es_mx/v1/augment-lists.json").Content | ConvertFrom-Json
    $pool = @($pools | Where-Object modeName -ceq 'KIWI')
    if ($pool.Count -ne 1 -or !$pool[0].augmentList.Count) { throw "Falta el catálogo de KIWI de $Version." }
    @{ Mode = $mode; Pool = $pool[0].augmentList }
}

function Test-Unchanged([string]$Name) {
    $before = $prior.Mode.PSObject.Properties[$Name].Value
    $after = $current.Mode.PSObject.Properties[$Name].Value
    $null -ne $before -and $null -ne $after -and
        (ConvertTo-Json -InputObject $before -Compress -Depth 100) -ceq (ConvertTo-Json -InputObject $after -Compress -Depth 100)
}

$prior = Read-Patch $previous
$current = Read-Patch $Patch
$stable = foreach ($card in $catalog.augments | Where-Object mode -ceq 'Caos') {
    $entry = "Maps/ModeSpecificData/Augments/$($card.key)"
    if ($entry -cnotin $prior.Pool -or $entry -cnotin $current.Pool -or !(Test-Unchanged $entry)) { continue }
    $definition = $current.Mode.PSObject.Properties[$entry].Value
    if ($definition.AugmentPlatformId -ne $card.id -or !$definition.RootSpell) { continue }
    $spells = @($definition.RootSpell) + @($definition.AdditionalSpells | Where-Object { $_ })
    if (@($spells | Where-Object { !(Test-Unchanged $_) }).Count -eq 0) { [int]$card.id }
}

$ids = @($stable | Sort-Object -Unique)
if (!$ids.Count) { throw "Sin aumentos con mecánicas verificables entre $previous y $Patch." }
$json = [ordered]@{ clientPatch = $Patch; sourcePatch = $previous; cards = $ids } | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $assets) 'aramhex-compatibility.json'), "$json`n")
Write-Output "$($ids.Count) aumentos de ARAM Caos verificados entre $previous y $Patch."
