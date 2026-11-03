$ErrorActionPreference='Stop'
$folder='Documentation/Balance-2026-11-02'
$snapshot=Get-Content "$folder/after-first-pass.json" -Raw|ConvertFrom-Json
$catalog=Get-Content Documentation/ComponentLibrary-2026-10-25/Catalog.json -Raw|ConvertFrom-Json
$modules=@{};$weapons=@{}
foreach($entry in $catalog.entries){$modules[$entry.shape]=$entry.module|ConvertFrom-Json;if($entry.weapon){$weapons[$entry.shape]=$entry.weapon|ConvertFrom-Json}}
$modules['Thruster6']=Get-Content Documentation/Thruster6-Weapons-2026-10-25/Thruster6-Module.json -Raw|ConvertFrom-Json
$modules['FlareLauncher']=[pscustomobject]@{cost=0;massKg=150;maxHp=60;recoverySeconds=3}
$tuning=Get-Content Documentation/Thruster6-Weapons-2026-10-25/Before/Balance.json -Raw|ConvertFrom-Json
foreach($entry in $tuning.tuning){if($entry.weapon){$weapons[$entry.shape]=$entry.weapon|ConvertFrom-Json}}
$moduleWeapons=@{Laser='LaserRapidCannon';Laser2='Laser2Weapon';Laser3='Laser3Weapon';Gun1='Gun1Weapon';Gun2='Gun2Weapon';Gun3='Gun3Weapon';Railgun1='Railgun1Weapon';Railgun2='Railgun2Weapon';Railgun3='Railgun3Weapon';Missile1='Missile1Weapon';Missile2='Missile2Weapon';Missile3='Missile3Weapon';Sword1='Sword1Weapon';Sword2='Sword2Weapon'}
$weaponsByAsset=@{};foreach($shape in $moduleWeapons.Keys){$weaponsByAsset[$moduleWeapons[$shape]]=$weapons[$shape]}
$gitFlight=& git -c safe.directory=D:/software/unity/CGDD26107 show HEAD:Assets/_Starframe/Settings/Flight/FlightTuning.asset
$gitFlight=$gitFlight -join "`n"
$recovered=0
foreach($value in $snapshot.values){
    $asset=[IO.Path]::GetFileNameWithoutExtension($value.path);$source=$null
    if($modules.ContainsKey($asset)){$source=$modules[$asset]}
    elseif($weaponsByAsset.ContainsKey($asset)){$source=$weaponsByAsset[$asset]}
    if($source -and $source.PSObject.Properties.Name -contains $value.field){$raw=$source.($value.field);if($value.isBool){$value.boolean=[bool]$raw}else{$value.number=[float]$raw};$recovered++}
    if($asset -eq 'FlightTuning'){
        $pattern='(?m)^  '+[regex]::Escape($value.field)+': ([0-9.Ee+\-]+)\s*$'
        $match=[regex]::Match($gitFlight,$pattern);if(!$match.Success){throw "No original FlightTuning value for $($value.field)"};$value.number=[float]::Parse($match.Groups[1].Value,[cultureinfo]::InvariantCulture);$recovered++
    }
    # Original launch/guidance fields were added after the archived catalogue; unchanged defaults
    # from the pre-balance WeaponDefinition and the native combat audit are explicit below.
    if($weaponsByAsset.ContainsKey($asset)){
        if($value.field -eq 'missileTurnAcceleration'){$value.number=180}
        if($value.field -eq 'missileGuidanceSeconds'){$value.number=10}
        if($value.field -eq 'missileSeekerConeDegrees'){$value.number=65}
        if($value.field -eq 'missileSeekerLossSeconds'){$value.number=.35}
        if($value.field -eq 'firstShotImmediate'){$value.boolean=$asset.StartsWith('Railgun')}
        if($value.field -eq 'shotsPerSecond' -and $asset -eq 'LaserRapidCannon'){$value.number=10}
    }
    # These newer manual overrides were read directly in the native audit before changes.
    if($value.field -eq 'recoverySeconds' -and $asset -eq 'Railgun1'){$value.number=1}
    if($value.field -eq 'recoverySeconds' -and $asset -in @('Railgun2','Railgun3')){$value.number=2}
    if($asset -eq 'BattleTuning' -and $value.field -eq 'outOfCombatDelay'){$value.number=1}
    if($asset -eq 'BattleTuning' -and $value.field -eq 'unlimitedRangePower'){$value.boolean=$false}
    if($asset -eq 'FlareLauncherCountermeasure' -and $value.field -eq 'decoyChance'){$value.number=.45}
    if($value.category -eq 'mission'){
        if($value.field -eq 'useProvisionalPricing'){$value.boolean=$true}
        if($value.field -eq 'baseBudget'){$value.number=if($asset -eq 'ice'){6000}else{10000}}
    }
}
if($snapshot.values.Count -ne 802){throw 'Baseline field count changed'}
$snapshot|Add-Member -NotePropertyName provenance -NotePropertyValue 'Recovered parameter baseline: archived complete 36-module catalogue, Thruster6 snapshot, pre-balance native readings, tracked FlightTuning fields and authored mission defaults. Guidance fields use the pre-balance source defaults. This is a reconstructed rollback baseline, not an exact byte-for-byte capture.'
if(!(Test-Path "$folder/before-incomplete.json")){Copy-Item -LiteralPath "$folder/before.json" -Destination "$folder/before-incomplete.json"}
$snapshot|ConvertTo-Json -Depth 8|Set-Content "$folder/before.json" -Encoding utf8
"Recovered $recovered archived fields; complete rollback baseline has $($snapshot.values.Count) fields."
