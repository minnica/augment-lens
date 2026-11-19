[CmdletBinding()]
param(
    [ValidateRange(0, 2147483647)]
    [int]$Revision = 0,
    [string]$Assets
)

$ErrorActionPreference = 'Stop'
$page = 'Module:ArenaAugmentData/data'
$site = 'https://wiki.leagueoflegends.com/en-us'
$licenseUrl = 'https://creativecommons.org/licenses/by-sa/3.0/'
$selection = if ($Revision) { "revids=$Revision" } else { 'titles=' + [Uri]::EscapeDataString($page) }
$api = "$site/api.php?action=query&prop=revisions&$selection&rvprop=ids%7Ctimestamp%7Ccontent&rvslots=main&format=json&formatversion=2"
if (!$Assets) { $Assets = Join-Path $PSScriptRoot '../src/Aumentos.App/Assets' }
$assetsPath = (Resolve-Path -LiteralPath $Assets).Path
$catalog = Get-Content -LiteralPath (Join-Path $assetsPath 'catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$arenaCards = @($catalog.augments | Where-Object mode -eq 'Arena' | Sort-Object id)
if ([string]::IsNullOrWhiteSpace($catalog.patch) -or !$arenaCards.Count) {
    throw 'The local catalog does not contain an Arena patch and its card identities.'
}

$download = [IO.Path]::GetTempFileName()
try {
    & curl.exe --fail --silent --show-error --location --max-time 20 --output $download $api
    if ($LASTEXITCODE -ne 0) { throw 'The public League of Legends Wiki API is unavailable.' }
    $response = Get-Content -LiteralPath $download -Raw -Encoding UTF8 | ConvertFrom-Json
} finally {
    Remove-Item -LiteralPath $download -Force
}

$sourcePage = @($response.query.pages)[0]
$source = @($sourcePage.revisions)[0]
$lua = $source.slots.main.content
if ($sourcePage.title -cne $page -or $source.revid -le 0 -or !$source.timestamp -or
    ($Revision -and $source.revid -ne $Revision) -or [string]::IsNullOrWhiteSpace($lua)) {
    throw 'The Wiki response did not contain the expected Lua module revision.'
}

function Get-NameKey([string]$Value) {
    return $Value.Normalize([Text.NormalizationForm]::FormD).ToLowerInvariant() -replace '[^\p{L}\p{Nd}]', ''
}

# These entries cross-reference the English Wiki title with the stable Riot key.
$catalogAliases = @{
    Augment404 = '404 Augment Not Found'
    ARAM_LittleDevil = 'Devil on Your Shoulder'
    ARAM_Purist_Caster = 'Purist - Caster'
    GrievousVenom = 'Grevious Venom'
    NullAugment = 'Null'
    PinballSnowball = 'Pinball'
    Quest_VoidImmolation = "Quest: Icathia's Fall"
    Upgrade_SwordOfBlossom = 'Upgrade Sword of Blossoming Dawn'
    YouSpinMeRightRound = 'Spin Me Right Round'
}

# The Wiki publishes descriptive mechanics without a tag taxonomy. Each local flag
# below requires a named card and explicit evidence in that card's source fields.
$evidence = @{
    'Critical Rhythm' = @{ GrantsAttackSpeed = 'critical strikes.*grant you.*attack speed' }
    'Deft' = @{ GrantsAttackSpeed = 'Grants.*attack speed' }
    'Ethereal Weapon' = @{ AppliesOnHitViaAbilities = 'abilities apply .*on-hit.*effects' }
    'Firebrand' = @{ MaxHealthDamageOnHit = 'Basic attacks.*on-hit.*Burn.*magic damage.*maximum.*health' }
    'Heavy Hitter' = @{ OwnMaxHealthDamageOnAttacks = 'Basic attacks deal.*physical damage.*percentage of your.*maximum.*health' }
    'Jeweled Gauntlet' = @{ AbilitiesCanCrit = 'abilities can now .*critically strike' }
    "Light 'em Up!" = @{ AttacksForBonusMagicDamage = 'Basic attacks.*generate a stack.*fireworks.*magic damage' }
    'Lightning Strikes' = @{
        GrantsAttackSpeed = 'Increase your .*attack speed.*gained from all sources'
        AttackSpeedThresholdDamage = 'attack speed or higher.*basic attacks deal.*magic damage'
    }
    'Quest: Steel Your Heart' = @{ RequiresHeartsteel = 'Quest:.*Obtain .*Heartsteel.*accumulate' }
    'Tap Dancer' = @{ GrantsAttackSpeed = 'gain .*attack speed.*total.*movement speed' }
    'Typhoon' = @{ AppliesOnHitToAdditionalTarget = 'Basic attacks.*additional target.*applies on-hit effects' }
    'Twice Thrice' = @{ RepeatsOnHitOnAttacks = 'basic attack consumes all stacks to apply .*on-hit.*effects again' }
}

$entryPattern = '(?m)^[\t ]*\["(?<name>(?:[^"\\\r\n]|\\.)+)"\][\t ]*=[\t ]*\{[\t ]*(?:--[^\r\n]*)?\r?\n'
$fieldPattern = '(?ms)^[\t ]*\["(?<field>\w+)"\][\t ]*=[\t ]*(?:"(?<quoted>(?:[^"\\\r\n]|\\.)*)"|\[(?<delimiter>=*)\[(?<long>.*?)\]\k<delimiter>\])[\t ]*,?[\t ]*(?:--[^\r\n]*)?\r?$'
$entries = [regex]::Matches($lua, $entryPattern)
if ($entries.Count -lt 200) { throw 'The Wiki module schema or coverage changed unexpectedly.' }

$rows = [Collections.Generic.List[object]]::new()
$byTitle = @{}
for ($i = 0; $i -lt $entries.Count; $i++) {
    $entry = $entries[$i]
    $start = $entry.Index + $entry.Length
    $end = if ($i + 1 -lt $entries.Count) { $entries[$i + 1].Index } else { $lua.Length }
    $title = '"' + $entry.Groups['name'].Value + '"' | ConvertFrom-Json
    $fields = @{}
    foreach ($match in [regex]::Matches($lua.Substring($start, $end - $start), $fieldPattern)) {
        $name = $match.Groups['field'].Value
        $value = if ($match.Groups['quoted'].Success) {
            '"' + $match.Groups['quoted'].Value + '"' | ConvertFrom-Json
        } else { $match.Groups['long'].Value.Trim() }
        if ($fields.ContainsKey($name)) { throw "Duplicate Wiki field '$name' on '$title'." }
        $fields.Add($name, $value)
    }
    if (@('description', 'tier', 'level1', 'level2', 'level3').Where({ !$fields.ContainsKey($_) }).Count -ne 0 -or
        $fields.tier -cnotin @('Silver', 'Gold', 'Prismatic') -or [string]::IsNullOrWhiteSpace($fields.description)) {
        throw "Unsupported Wiki fields for '$title'."
    }

    $notes = if ($fields.notes) { $fields.notes } else { $fields.note }
    $signals = @()
    if ($evidence.ContainsKey($title)) {
        foreach ($signal in ($evidence[$title].Keys | Sort-Object)) {
            if ($fields.description -match $evidence[$title][$signal]) { $signals += $signal }
        }
    }
    $row = [ordered]@{
        name = $title
        rarity = $fields.tier
        description = $fields.description
        level1 = if ($fields.level1) { $fields.level1 } else { $null }
        level2 = if ($fields.level2) { $fields.level2 } else { $null }
        level3 = if ($fields.level3) { $fields.level3 } else { $null }
        notes = $notes
        signals = if ($signals.Count) { $signals -join ', ' } else { 'None' }
        disabled = if ("$($fields.description) $notes" -match "''(?:Temporarily )?Disabled (?:since|in) \[\[V") { $true } else { $null }
        removed = if ("$($fields.description) $notes" -match "''Removed (?:since|in) \[\[V") { $true } else { $null }
    }
    $rows.Add($row)
    $key = Get-NameKey $title
    if (!$key) { continue }
    if (!$byTitle.ContainsKey($key)) { $byTitle[$key] = [Collections.Generic.List[string]]::new() }
    $byTitle[$key].Add($title)
}

$bindings = @(foreach ($card in $arenaCards) {
    $wikiTitle = $catalogAliases[$card.key]
    $term = if ($wikiTitle) { $wikiTitle } else { $card.key -replace '^ARAM_', '' }
    $titles = $byTitle[(Get-NameKey $term)]
    if ($titles.Count -ne 1) {
        if ($wikiTitle) { throw "Explicit Wiki alias '$wikiTitle' for '$($card.key)' is no longer unique." }
        continue
    }
    [ordered]@{ id = $card.id; key = $card.key; name = $titles[0] }
})
$ambiguous = @($bindings | Group-Object -Property { $_.name } | Where-Object Count -gt 1)
if (!$bindings.Count -or $ambiguous.Count) {
    throw 'The Wiki bindings are empty or the same Wiki title maps to multiple Arena IDs.'
}

$hash = [Security.Cryptography.SHA256]::Create()
try {
    $sourceHash = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($lua))).Replace('-', '').ToLowerInvariant()
} finally { $hash.Dispose() }

# The source has a revision date rather than a complete assertion of game patch.
$snapshot = [ordered]@{
    schema = 1
    sourceName = 'League of Legends Wiki contributors'
    sourcePage = "$site/$page"
    sourceRevision = $source.revid
    sourceUpdatedAt = ([DateTimeOffset]$source.timestamp).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
    revisionUrl = "$site/Special:PermanentLink/$($source.revid)"
    sourceSha256 = $sourceHash
    license = 'CC BY-SA 3.0'
    licenseUrl = $licenseUrl
    modification = 'Lua fields converted to JSON, with card identity bindings and source-supported mechanic flags added. Wiki markup preserved.'
    catalogPatch = $catalog.patch
    cards = $rows
    bindings = $bindings
}

$path = Join-Path $assetsPath 'arena-wiki-mechanics.json'
$pending = "$path.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $json = ($snapshot | ConvertTo-Json -Depth 8) -replace '\r\n', "`n"
    [IO.File]::WriteAllText($pending, $json + "`n", [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($path)) { [IO.File]::Replace($pending, $path, [Management.Automation.Language.NullString]::Value) }
    else { [IO.File]::Move($pending, $path) }
} finally {
    if ([IO.File]::Exists($pending)) { [IO.File]::Delete($pending) }
}
Write-Output "Wiki revision $($source.revid): $($rows.Count) entries, $($bindings.Count) mapped to Arena, catalog $($catalog.patch)."
