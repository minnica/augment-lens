param(
    [string]$Patch = '16.23',
    [string]$ChampionVersion = "$Patch.1",
    [switch]$SkipCatalog,
    [switch]$OnlyMayhemItems
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

$itemSource = "https://ddragon.leagueoflegends.com/cdn/$ChampionVersion/data/es_MX/item.json"
$itemData = (Get-Json $itemSource).data
$itemStats = [ordered]@{
    armor='FlatArmorMod'; magicResist='FlatSpellBlockMod'; health='FlatHPPoolMod';
    attackDamage='FlatPhysicalDamageMod'; abilityPower='FlatMagicDamageMod'; crit='FlatCritChanceMod';
    attackSpeed='PercentAttackSpeedMod'
}
$items = foreach ($entry in $itemData.PSObject.Properties) {
    $item = $entry.Value
    if (!$item.maps.'12' -or !$item.name) { continue }
    $boots = $item.tags -contains 'Boots'
    $available = $item.gold.purchasable -and !$item.hideFromAll -and $item.inStore -ne $false
    $finished = ($boots -and $item.gold.total -ge 900) -or
        ($item.gold.total -ge 1800 -and ($item.from.Count -gt 0 -or !$item.gold.purchasable))
    $row = [ordered]@{ name = $item.name }
    if ($finished) { $row['finished'] = $true }
    if ($finished -and $available) { $row['available'] = $true }
    if ($boots) { $row['boots'] = $true }
    if ($item.tags -contains 'OnHit') { $row['onHit'] = $true }
    if ($item.tags -contains 'SpellVamp' -or $item.stats.PercentLifeStealMod -gt 0) { $row['sustain'] = $true }
    foreach ($stat in $itemStats.GetEnumerator()) {
        $value = $item.stats.($stat.Value)
        if ($value -gt 0) { $row[$stat.Key] = $value }
    }
    if ($finished -or $row.Count -gt 1) {
        '    "' + $entry.Name + '": ' + ($row | ConvertTo-Json -Compress)
    }
}
if (@($items).Count -lt 100) { throw 'Faltan datos de objetos de ARAM.' }
$itemCatalog = "{`n  `"patch`": `"$Patch`",`n  `"source`": `"$itemSource`",`n  `"items`": {`n" +
    ($items -join ",`n") + "`n  }`n}`n"
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $assets) 'mayhem-items.json'), $itemCatalog)
if ($OnlyMayhemItems) { return }

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
$arenaItemSource = "$localized/items.json"
$allItems = Get-Json $arenaItemSource
$arenaItems = @($allItems | Where-Object {
    (($_.id -ge 440000 -and $_.id -lt 450000) -or ($_.id -ge 220000 -and $_.id -lt 230000)) -and $_.name
} | Select-Object -ExpandProperty name | Sort-Object -Unique)
if (!$arenaItems.Count) { throw 'No se encontraron objetos de Arena.' }
$legendaryNames = @($allItems | Where-Object { $_.id -ge 220000 -and $_.id -lt 230000 -and $_.inStore } |
    Select-Object -ExpandProperty name)
$arenaPrismatics = @($allItems | Where-Object {
    $_.id -ge 443000 -and $_.id -lt 448000 -and $_.inStore -and $_.name -and
        ($_.priceTotal -eq 2750 -or $_.id -eq 446693)
} | Sort-Object id | ForEach-Object {
    [ordered]@{ id=$_.id; name=$_.name; sharedName=($_.name -in $legendaryNames) }
})
if (!$arenaPrismatics.Count) { throw 'No se encontraron objetos prismáticos de Arena.' }
$prismaticNames = @($arenaPrismatics | ForEach-Object { $_.name })
$arenaLegendaries = @($allItems | Where-Object {
    $_.id -ge 220000 -and $_.id -lt 230000 -and $_.inStore -and $_.name -and $_.priceTotal -eq 2500
} | Sort-Object id | ForEach-Object {
    [ordered]@{ id=$_.id; name=$_.name; sharedName=($_.name -in $prismaticNames); categories=@($_.categories) }
})
if (!$arenaLegendaries.Count) { throw 'No se encontraron objetos legendarios de Arena.' }
$catalog = [ordered]@{
    patch=$Patch
    locale='es_MX'
    sources=[ordered]@{ champions="Data Dragon $ChampionVersion"; augments=$cdn; arenaItems=$arenaItemSource }
    champions=@($champions | Sort-Object id | ForEach-Object {
        [ordered]@{ key=$_.id; name=$_.name; roles=@($_.tags) }
    })
    augments=@($cards | Sort-Object { $_.mode }, { [int]$_.id })
    arenaItems=$arenaItems
    arenaPrismatics=$arenaPrismatics
    arenaLegendaries=$arenaLegendaries
}
$json = $catalog | ConvertTo-Json -Depth 7
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $assets) 'catalog.json'), $json + [Environment]::NewLine)
