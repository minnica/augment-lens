param(
    [string]$Patch = '16.23',
    [string]$ChampionVersion = "$Patch.1",
    [string]$Assets = (Join-Path $PSScriptRoot '../src/Aumentos.App/Assets'),
    [switch]$SkipCatalog,
    [switch]$OnlyMayhemItems
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$ocrPath = Join-Path $assets 'tessdata'
New-Item -ItemType Directory -Force $ocrPath | Out-Null

$ocrSource = 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0'
foreach ($name in @('spa.traineddata', 'LICENSE')) {
    $destination = Join-Path $ocrPath $name
    if (!(Test-Path $destination)) {
        Invoke-WebRequest "$ocrSource/$name" -UseBasicParsing -TimeoutSec 45 -OutFile "$destination.tmp"
        Move-Item "$destination.tmp" $destination
    }
}

if ($SkipCatalog) { return }

$cdn = "https://raw.communitydragon.org/$Patch"
$localized = "$cdn/plugins/rcp-be-lol-game-data/global/es_mx/v1"
function Get-Json([string]$url) {
    $response = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 45
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

$newline = [string][char]10
function Convert-ItemText([string]$value) {
    $value = $value -replace '<br\s*/?>', $newline
    $value = [Net.WebUtility]::HtmlDecode(($value -replace '<[^>]+>', ''))
    ($value.Trim() -replace '[ \t]*\r?\n[ \t]*', $newline -replace '\n{3,}', ($newline + $newline))
}
$categoryLabels = [ordered]@{
    Boots='Botas'; Consumable='Consumible'; CriticalStrike='Golpe crítico'; Armor='Armadura';
    SpellBlock='Resistencia mágica'; LifeSteal='Robo de vida'; AttackSpeed='Velocidad de ataque';
    SpellDamage='Poder de habilidad'; Damage='Daño de ataque'; Health='Vida'; AbilityHaste='Aceleración de habilidad'
}
$shopRows = foreach ($entry in $itemData.PSObject.Properties) {
    $item = $entry.Value
    if ((!$item.maps.'453' -and !$item.maps.'12' -and !$item.maps.'30') -or !$item.name) { continue }
    $description = $item.description -replace '(?is)<(rules|flavorText)>.*?</\1>', ''
    $stats = [regex]::Match($description, '(?is)<stats>(.*?)</stats>').Groups[1].Value
    $effect = Convert-ItemText ($description -replace '(?is)<stats>.*?</stats>', '')
    $category = if ($item.tags -contains 'Active' -and $effect -match '(?i)\bte desplazas\b|\bdespl.zate\b') { 'Desplazamiento' }
        else { $categoryLabels.Keys | Where-Object { $_ -in $item.tags } | Select-Object -First 1 | ForEach-Object { $categoryLabels[$_] } }
    $row = [ordered]@{}
    if ($item.gold.purchasable -and $item.gold.total -gt 0) { $row['cost'] = [int]$item.gold.total }
    if ($category) { $row['category'] = $category }
    if ($stats) { $row['stats'] = Convert-ItemText $stats }
    if ($effect) { $row['effect'] = $effect }
    if ($description -match '(?i)<trueDamage>' -and $effect -notmatch 'contra súbditos' -and
        $effect -match '(?i)\b(?:inflige|infligen|infligirá)\b[^.\n]{0,140}\bdaño verdadero\b') {
        $row['hasTrueDamage'] = $true
    }
    if ($row.Count) { '    "' + $entry.Name + '": ' + ($row | ConvertTo-Json -Compress) }
}
$shopCatalog = ('{', ('  "patch": "' + $Patch + '",'), ('  "source": "' + $itemSource + '",'), '  "items": {') -join $newline
$shopCatalog += $newline + ($shopRows -join (',' + $newline)) + $newline + '  }' + $newline + '}' + $newline
[IO.File]::WriteAllText((Join-Path (Resolve-Path $assets) 'shop-items.json'), $shopCatalog)

$arena = (Get-Json "$cdn/cdragon/arena/es_mx.json").augments
$arenaModeSource = "$cdn/game/maps/modespecificdata/cherry.bin.json"
$arenaMode = Get-Json $arenaModeSource
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

# Entries from the common augment list can be missing from the localized Arena export.
# In that case the mode-specific overrides take precedence over the root spell defaults.
function Get-SharedArenaValues($card) {
    $augment = $arenaMode.("Maps/ModeSpecificData/Augments/" + $card.key)
    if ($augment.AugmentPlatformId -ne $card.id -or !$augment.RootSpell) { return }
    $spell = $arenaMode.($augment.RootSpell).mSpell
    $values = @{}
    foreach ($datum in @($spell.DataValues) + @($spell.DataValuesModeOverride.cherry.SpellDataValues)) {
        if ($datum.name) { $values[$datum.name] = $datum.values }
    }
    $values
}

# Only give numeric details for fields with confirmed units and meaning.
$upgradeStats = @{
    Deft = [ordered]@{ AttackSpeed = 'Velocidad de ataque', 100, ' %' }
    Eureka = [ordered]@{ APToHasteConversion = 'Conversión de PH en aceleración', 100, ' %' }
    LightemUp = [ordered]@{
        NumberOfFireworks = 'Fuegos artificiales', 1, ''
        FireworkDamageMod = 'Daño', 100, ' %'
    }
    Clothesline = [ordered]@{
        Healthcoefficient = 'Vida máxima', 100, ' %'
        ADcoefficient = 'Daño de ataque', 100, ' %'
        APcoefficient = 'Poder de habilidad', 100, ' %'
    }
    LightningStrikes = [ordered]@{
        AttackSpeedCap = 'Límite de ataques/s', 1, ''
        TotalASValue = 'Velocidad de ataque', 100, ' %'
    }
    VeilOfWarding = [ordered]@{ Cooldown = 'Enfriamiento', 1, ' s' }
    CriticalRhythm = [ordered]@{ MaxStack = 'Máximo de acumulaciones', 1, '' }
    BigBrain = [ordered]@{ ShieldBaseTooltip = 'Escudo por punto de poder de habilidad', 1, '' }
    ShadowRunner = [ordered]@{
        MSAmount = 'Velocidad de movimiento', 1, ''
        BuffDuration = 'Duración', 1, ' s'
    }
    Tormentor = [ordered]@{ PercentMaxHPDamagePer5Seconds = 'Quemadura de vida máxima en 5 s', 100, ' %' }
    IceCold = [ordered]@{ ExtraSlowAmount = 'Ralentización adicional', 1, '' }
    HeavyHitter = [ordered]@{ HealthPercent = 'Porcentaje de tu vida máxima por ataque', 100, ' %' }
    Repulsor = [ordered]@{ KnockbackDistance = 'Distancia de empuje', 1, '' }
    SlapAround = [ordered]@{ AdaptiveForce = 'Fuerza adaptable por inmovilización', 1, '' }
    ScopiestWeapons = [ordered]@{
        MeleeRangeIncrease = 'Alcance cuerpo a cuerpo', 1, ''
        RangedRangeIncrease = 'Alcance a distancia', 1, ''
    }
    TapDancer = [ordered]@{ MSPerHit = 'Velocidad de movimiento al impacto', 1, '' }
    Goliath = [ordered]@{
        HealthAmp = 'Vida', 100, ' %'
        AFAmp = 'Fuerza adaptable', 100, ' %'
    }
    JeweledGauntlet = [ordered]@{ CritDamage = 'Daño crítico de habilidades', 100, ' %' }
    OmniSoul = [ordered]@{ SoulsToAdd = 'Almas de Dragón', 1, '' }
    FanTheHammer = [ordered]@{ Cooldown = 'Enfriamiento por dirección', 1, ' s' }
    Deathtouch = [ordered]@{ OnHitDamageRatio = 'Daño al impacto del Poder Maldito', 100, ' %' }
}
$mexicanSpanish = [System.Globalization.CultureInfo]::GetCultureInfo('es-MX')
function Get-ArenaUpgradeEffects($card, $entry, $values, [int]$maximum) {
    $fields = $upgradeStats[$card.key]
    if (!$fields -or $maximum -lt 2) { return }
    $tooltipMultiplier = 1
    if ($card.key -eq 'Clothesline') {
        $calculation = $entry.calculations.BurnDamageTooltip
        if ($calculation.mModifiedGameCalculation -ne 'BurnDamage' -or $calculation.mMultiplier.mNumber -le 0) { return }
        $tooltipMultiplier = $calculation.mMultiplier.mNumber
    }

    foreach ($level in 2..$maximum) {
        $changes = foreach ($field in $fields.GetEnumerator()) {
            $series = $values.($field.Key)
            if ($series.Count -le $level -or $null -eq $series[$level - 1] -or $null -eq $series[$level]) { continue }
            $label, $scale, $unit = $field.Value
            $previous = ($series[$level - 1] * $scale * $tooltipMultiplier).ToString('0.##', $mexicanSpanish)
            $current = ($series[$level] * $scale * $tooltipMultiplier).ToString('0.##', $mexicanSpanish)
            if ($previous -ne $current) { "$label de $previous$unit a $current$unit" }
        }
        if ($changes) { $changes -join ' · ' } else { $null }
    }
}

$arenaById = @{}
foreach ($entry in $arena) { $arenaById[$entry.id] = $entry }
foreach ($card in $cards | Where-Object mode -eq 'Arena') {
    $entry = $arenaById[$card.id]
    $values = if ($entry.dataValues.MaxLevel) { $entry.dataValues } else { Get-SharedArenaValues $card }
    $levels = $values.MaxLevel
    if (!$levels.Count -or $levels[0] -lt 1 -or $levels[0] -ne [Math]::Floor($levels[0])) { continue }
    $card['maxLevel'] = [int]$levels[0]
    $effects = @(Get-ArenaUpgradeEffects $card $entry $values $card.maxLevel)
    if ($effects | Where-Object { $_ }) { $card['upgradeEffects'] = $effects }
    $counts = $values.AmountOfStatAnvils
    if ($counts.Count -gt $card.maxLevel) {
        $byLevel = @($counts[1..$card.maxLevel])
        if (!($byLevel | Where-Object { $_ -le 0 -or $_ -ne [Math]::Floor($_) })) {
            $card['anvilCount'] = [int]$byLevel[0]
            if ($byLevel.Count -gt 1) {
                $card['anvilUpgradeCounts'] = @($byLevel | Select-Object -Skip 1 | ForEach-Object { [int]$_ })
            }
        }
    }
}

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
# Goredrinker is offered by Prismatic Anvils under its older Arena item ID.
$legacyPrismaticIds = @(226630)
$legendaryNames = @($allItems | Where-Object {
    $_.id -ge 220000 -and $_.id -lt 230000 -and $_.inStore -and $_.id -notin $legacyPrismaticIds
} |
    Select-Object -ExpandProperty name)
$arenaPrismatics = @($allItems | Where-Object {
    (($_.id -ge 443000 -and $_.id -lt 448000) -or $_.id -in $legacyPrismaticIds) -and $_.inStore -and $_.name -and
        ($_.priceTotal -eq 2750 -or $_.id -eq 446693)
} | Sort-Object id | ForEach-Object {
    [ordered]@{ id=$_.id; name=$_.name; sharedName=($_.name -in $legendaryNames); categories=@($_.categories) }
})
if (!$arenaPrismatics.Count) { throw 'No se encontraron objetos prismáticos de Arena.' }
$prismaticNames = @($arenaPrismatics | ForEach-Object { $_.name })
function Test-ArenaShopItem($item) {
    # These client entries are granted by augments or quests rather than bought directly.
    $item.inStore -and !$item.requiredChampion -and !$item.requiredAlly -and !$item.requiredBuffCurrencyName -and
        !$item.isEnchantment -and $item.specialRecipe -eq 0 -and $item.id -notin @(223005, 224403, 228009)
}
$arenaLegendaries = @($allItems | Where-Object {
    $_.id -ge 220000 -and $_.id -lt 230000 -and $_.inStore -and $_.name -and $_.priceTotal -eq 2500
} | Sort-Object id | ForEach-Object {
    [ordered]@{ id=$_.id; name=$_.name; sharedName=($_.name -in $prismaticNames); categories=@($_.categories); buyable=(Test-ArenaShopItem $_) }
})
if (!$arenaLegendaries.Count) { throw 'No se encontraron objetos legendarios de Arena.' }
$rankedArenaIds = @($arenaPrismatics + $arenaLegendaries | ForEach-Object { $_.id })
$arenaEquipment = @($allItems | Where-Object {
    $_.name -and $_.id -notin $rankedArenaIds -and
        (($_.id -ge 222000 -and $_.id -lt 230000 -and ($_.categories -contains 'Boots' -or $_.priceTotal -ge 2500)) -or
         ($_.id -ge 443000 -and $_.id -lt 448000 -and $_.priceTotal -ge 2500)) -and
        $_.id -ne 223057 -and $_.categories -notcontains 'Consumable'
} | Sort-Object id | ForEach-Object {
    [ordered]@{ id=$_.id; name=$_.name; categories=@($_.categories); buyable=(($_.categories -contains 'Boots') -and (Test-ArenaShopItem $_)) }
})
if (!$arenaEquipment.Count) { throw 'No se encontraron datos del inventario de Arena.' }
$catalog = [ordered]@{
    patch=$Patch
    locale='es_MX'
    sources=[ordered]@{ champions="Data Dragon $ChampionVersion"; augments=$cdn; arenaItems=$arenaItemSource; arenaAugmentOverrides=$arenaModeSource }
    champions=@($champions | Sort-Object id | ForEach-Object {
        [ordered]@{
            key=$_.id; name=$_.name; roles=@($_.tags)
            magicResist=$_.stats.spellblock; magicResistPerLevel=$_.stats.spellblockperlevel
        }
    })
    augments=@($cards | Sort-Object { $_.mode }, { [int]$_.id })
    arenaItems=$arenaItems
    arenaPrismatics=$arenaPrismatics
    arenaLegendaries=$arenaLegendaries
    arenaEquipment=$arenaEquipment
}
$json = $catalog | ConvertTo-Json -Depth 7
[System.IO.File]::WriteAllText((Join-Path (Resolve-Path $assets) 'catalog.json'), $json + [Environment]::NewLine)
