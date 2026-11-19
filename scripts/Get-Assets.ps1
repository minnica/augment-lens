param(
    [string]$Patch = '16.23',
    [string]$ChampionVersion = "$Patch.1",
    [switch]$SkipCatalog
)
$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '../src/Aumentos.App/Assets'
$ocrPath = Join-Path $assets 'tessdata'
New-Item -ItemType Directory -Force $ocrPath | Out-Null

$ocrSource = 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0'
foreach ($name in @('spa.traineddata', 'LICENSE')) {
    $destination = Join-Path $ocrPath $name
    if (!(Test-Path $destination)) {
        Invoke-WebRequest "$ocrSource/$name" -UseBasicParsing -OutFile "$destination.tmp"
        Move-Item "$destination.tmp" $destination
    }
}

if ($SkipCatalog) { return }

$cdn = "https://raw.communitydragon.org/$Patch"
$localized = "$cdn/plugins/rcp-be-lol-game-data/global/es_mx/v1"
function Get-Json([string]$url) {
    $response = Invoke-WebRequest $url -UseBasicParsing
    $reader = New-Object System.IO.StreamReader($response.RawContentStream, [System.Text.Encoding]::UTF8)
    try { $reader.ReadToEnd() | ConvertFrom-Json }
    finally { $reader.Dispose() }
}

$arena = (Get-Json "$cdn/cdragon/arena/es_mx.json").augments
$names = Get-Json "$localized/cherry-augments.json"
$lists = Get-Json "$localized/augment-lists.json"
$kiwi = @($lists | Where-Object modeName -eq 'KIWI')
$cherry = @($lists | Where-Object modeName -eq 'CHERRY')
if ($kiwi.Count -ne 1) { throw 'No se encontró una lista KIWI única.' }
if ($cherry.Count -ne 1) { throw 'No se encontró una lista CHERRY única.' }
$byKey = @{}
foreach ($entry in $names) { $byKey[$entry.augmentNameId] = $entry }

$cards = foreach ($entry in $arena) {
    if ($entry.name -like 'Augment_*') { throw "Falta traducción de Arena para $($entry.apiName)." }
    [ordered]@{ mode='Arena'; id=$entry.id; key=$entry.apiName; name=$entry.name; description=$entry.desc }
}
function Get-ListedCards($list, [string]$mode, $existing) {
    foreach ($path in $list.augmentList) {
        $key = ($path -split '/')[-1]
        $entry = $byKey[$key]
        if (!$entry -or !$entry.nameTRA) { throw "Falta el aumento de $mode con clave $key." }
        if ($entry.id -in $existing) { continue }
        $aliases = @($entry.simpleNameTRA | Where-Object { $_ -and $_ -ne $entry.nameTRA })
        [ordered]@{ mode=$mode; id=$entry.id; key=$entry.augmentNameId; name=$entry.nameTRA; aliases=$aliases }
    }
}
$cards += @(Get-ListedCards $cherry[0] 'Arena' $arena.id)
$cards += @(Get-ListedCards $kiwi[0] 'Caos' @())
if (!$arena.Count -or !$kiwi[0].augmentList.Count) { throw 'El catálogo de un modo quedó vacío.' }
$duplicates = $cards | Group-Object { "$($_.mode)/$($_.id)" } | Where-Object Count -gt 1
if ($duplicates) { throw "IDs duplicados: $($duplicates.Name -join ', ')." }

$sharedTitles = @($cards | Where-Object mode -eq 'Caos' | Group-Object name | Where-Object Count -gt 1)
if ($sharedTitles.Count) {
    try {
        $mechanics = Get-Json "$cdn/game/maps/modespecificdata/kiwi.bin.json"
        foreach ($card in $sharedTitles.Group) {
            $augment = $mechanics.("Maps/ModeSpecificData/Augments/" + $card.key)
            if ($augment.AugmentPlatformId -ne $card.id -or !$augment.RootSpell) { continue }
            $spell = $mechanics.($augment.RootSpell)
            $counts = @($spell.mSpell.DataValues | Where-Object name -eq 'AmountOfStatAnvils' |
                ForEach-Object { $_.values } | Sort-Object -Unique)
            if ($counts.Count -eq 1 -and $counts[0] -gt 0 -and $counts[0] -eq [Math]::Floor($counts[0])) {
                $card['anvilCount'] = [int]$counts[0]
            }
        }
    }
    catch {
        Write-Warning "No se pudieron cargar los datos para distinguir aumentos con el mismo título. $($_.Exception.Message)"
    }
}

$champions = (Get-Json "https://ddragon.leagueoflegends.com/cdn/$ChampionVersion/data/es_MX/champion.json").data.PSObject.Properties.Value
$allItems = Get-Json "$localized/items.json"
$arenaItems = @($allItems | Where-Object {
    (($_.id -ge 440000 -and $_.id -lt 450000) -or ($_.id -ge 220000 -and $_.id -lt 230000)) -and $_.name
} | Select-Object -ExpandProperty name | Sort-Object -Unique)
if (!$arenaItems.Count) { throw 'No se encontraron objetos de Arena.' }
$catalog = [ordered]@{
    patch=$Patch
    locale='es_MX'
    sources=[ordered]@{ champions="Data Dragon $ChampionVersion"; augments=$cdn }
    champions=@($champions | Sort-Object id | ForEach-Object {
        [ordered]@{ key=$_.id; name=$_.name; roles=@($_.tags) }
    })
    augments=@($cards | Sort-Object { $_.mode }, { [int]$_.id })
    arenaItems=$arenaItems
}
$json = $catalog | ConvertTo-Json -Depth 7
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $assets) 'catalog.json'), $json + [Environment]::NewLine)
