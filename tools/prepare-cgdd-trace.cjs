// Documentation-only setup for Starframe. Does not alter C# executable bodies or tuning values.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=path.resolve(__dirname,'..'),base=path.join(root,'Assets/_Starframe/Scripts/Runtime');
const roles={
AssemblyShieldPreview:'Preview shield-generator coverage while editing a ship.',
BlockConnectivity:'Check face-connected parts against the ship core.',
BlockGeometry:'Build reusable solid, outline and collision meshes for modular parts.',
BlockModules:'Resolve module occupancy, nozzle clearance and attachment geometry.',
BlockPlacement:'Convert grid poses and copy modular-part records.',
BlueprintData:'Store editable ship parts and weapon groups separately from battle damage.',
BlueprintRepository:'Validate, migrate, save and load player blueprints.',
ModuleGlowAccents:'Attach shared nozzle and weapon glow meshes.',
ShapeIconGraphic:'Draw modular-part icons in the construction interface.',
ShipAssembly:'Place and remove parts, validate occupancy and rebuild the assembled ship.',
ShipEditorController:'Handle placement, rotation, deletion and blueprint editing input.',
ShipEditorUI:'Present the parts catalogue, blueprint dialogs and construction feedback.',
WorkbenchEnvironment:'Create construction-bay floor markings and orientation references.',
WorkbenchOrbit:'Orbit, pan and zoom the construction camera.',
AssemblyAppNavigation:'Preserve the edited ship while navigating between construction and menus.',
FrameworkSmokeRun:'Run opt-in development checks using isolated preferences and blueprints.',
GameAppBootstrap:'Initialize the hangar, selected ship and application screens.',
GameAppScreens:'Implement deployment, blueprint selection and range-setup screens of the app.',
GameAudio:'Route gameplay sounds, music and engine loops through shared audio settings.',
GameAudioTuning:'Define sound cues, engine response, music levels and playback limits.',
GameInput:'Resolve configurable actions and capture valid input bindings.',
GameResultsScreen:'Present reported mission results without rewriting the saved blueprint.',
GameSettings:'Validate and persist display, audio, language and input preferences.',
HangarShipPreview:'Display and orbit the selected blueprint in the hangar.',
MenuSurface:'Build shared menu controls, dialogs and glass panels.',
RangeAppNavigation:'Open live range options and settings while suppressing player input.',
SettingsScreen:'Edit and apply the settings draft with conflict and storage feedback.',
ShipWorkspace:'Preserve the working ship and independent deployment snapshots across scenes.',
WeaponGroupConnections:'Draw links between weapon-group controls and selected weapons.',
WeaponGroupScreen:'Assign weapons to named groups and edit their input bindings.',
DeploymentValidator:'Validate ship structure, module configuration, budget and flight readiness.',
MissionCatalog:'Find configured missions by stable identifier.',
MissionDefinition:'Define mission identity and difficulty-dependent deployment budgets.',
MissionResults:'Record a level-reported outcome and return to the result screen.',
AimProjection:'Project an aiming direction into screen coordinates.',
BattleTuning:'Define recovery, target-lock, opponent behavior and respawn defaults.',
CombatVfxPool:'Render shared weapon-impact and ship-destruction effects.',
CoreVelocityEstimator:'Estimate target-core motion for projectile lead calculations.',
CylindricalHitQuery:'Intersect cylindrical projectile volumes with transformed collider meshes.',
DebrisCluster:'Track detached-part clusters and their remaining collision bounds.',
DebrisPartHitbox:'Apply projectile damage to detached wreckage parts.',
DebrisWorld:'Create and simulate persistent, destructible detached ship wreckage.',
InterceptSolver:'Solve a finite-speed projectile interception against a moving target.',
LaserVisualPool:'Reuse laser head and tail visuals independently of projectile damage.',
ProjectileWorld:'Advance projectiles and resolve shields, hull penetration and wreckage hits.',
RangeBotPilot:'Steer range opponents, avoid obstacles and plan firing opportunities.',
RangeBotWeaponInput:'Convert bot firing intent into weapon-group activation events.',
RangeCombatWorld:'Coordinate range actors, AI, shared power, weapons, damage and respawning.',
RangePracticeSettings:'Persist the selected stationary, flying or combat drill mode.',
RangeTargetRoster:'Load target blueprints and configured target counts for the range.',
ShieldSurfaceVisibility:'Control shield-surface renderer visibility for the active camera.',
ShipInstanceRenderer:'Render surviving hull parts with shared instanced geometry.',
ShipPartHitbox:'Bind a hull collider to its runtime part and cached collision mesh.',
ShipPowerBus:'Allocate generated and stored power proportionally across consumers.',
ShipShieldSystem:'Maintain component-owned shield fields and power-dependent recovery.',
ShipShieldVisual:'Render shield fields and hit feedback from runtime shield state.',
ShipWeapons:'Prepare weapon demand, enforce clearance and fire grouped weapon slots.',
TargetAimGraphic:'Draw target-selection and projectile-lead markers on the flight display.',
TargetIndicatorProjection:'Project offscreen targets onto the HUD boundary.',
TargetLockSystem:'Select valid range targets and update velocity-based lead points.',
VoxelFxGeometry:'Build shared voxel-effect meshes and glow materials.',
WeaponClearance:'Check a weapon muzzle against occupied ship geometry.',
FlightRangeBootstrap:'Create the range player, camera, flight systems and combat world.',
FlightSession:'Transfer a blueprint snapshot between the editor and test range.',
FlightShipRuntime:'Track surviving parts, detachments, recovery, power and current ship performance.',
FlightTuning:'Define flight limits, steering response, range bounds and performance aggregation.',
GlassUiPalette:'Provide shared UI colors, materials, glass effects and text setup.',
ModuleDatabase:'Index configured module definitions by part shape.',
ModuleDefinition:'Define per-part mass, durability, thrust, power and shield properties.',
RangeEnvironment:'Build and update spatial grid references around the range camera.',
RangeHUD:'Present speed, ship condition, target status, radar and range controls.',
ShipCollisionSweep:'Skip expensive compound-body sweeps when the broad phase finds clear space.',
ShipFlightController:'Apply six-axis movement, gradual steering, boost and collision-aware motion.',
ShipStatusGraphic:'Draw the blueprint and currently surviving ship parts as an isometric diagram.',
SphereRadarGraphic:'Draw range contacts, relative height and the selected target.',
ThrusterExhaust:'Display engine output with shared voxel exhaust meshes.',
WeaponDefinition:'Define shot cadence, damage, hit volume, range, power and activation mode.',
L10n:'Resolve UI language, localized strings and language-owned fonts.',
LocalizedUiFont:'Apply localized fallback fonts to user-supplied UI text.',
LocalizedUiText:'Bind localized table entries and formatting arguments to UI labels.',
ReticleHitFeedback:'Coalesce confirmed hits into bounded-duration reticle feedback.',
UiTheme:'Define visual styles and independently switchable interface features.',
WeaponActivationState:'Apply hold, press, charge and toggle modes using allocated power.',
WeaponGroupData:'Serialize weapon-group membership and control bindings with the blueprint.',
WeaponGroupInputRouter:'Read configured group controls and pass latched events to ship weapons.',
WeaponGroups:'Validate and maintain exactly one group assignment per weapon part.'
};
// Direct collaborators, not every type mentioned in a source file.
const calls={
AssemblyShieldPreview:'ShipAssembly ModuleDatabase ShipShieldVisual',BlockConnectivity:'BlockModules',BlockModules:'ModuleDatabase BlockPlacement',BlueprintRepository:'BlockPlacement BlockConnectivity WeaponGroups',
ModuleGlowAccents:'BlockModules VoxelFxGeometry',ShapeIconGraphic:'BlockGeometry',ShipAssembly:'BlockModules BlockConnectivity BlockPlacement BlockGeometry ModuleDatabase WeaponClearance WeaponGroups',ShipEditorController:'ShipAssembly WorkbenchOrbit ShipEditorUI BlueprintRepository FlightSession',ShipEditorUI:'ShipEditorController FlightTuning L10n GlassUiPalette',WorkbenchOrbit:'ShipEditorUI',
AssemblyAppNavigation:'ShipEditorController ShipWorkspace FlightSession GameAudio',GameAppBootstrap:'ShipWorkspace MenuSurface HangarShipPreview GameAppScreens GameResultsScreen',GameAppScreens:'DeploymentValidator ShipWorkspace RangeTargetRoster RangePracticeSettings',GameResultsScreen:'MissionResults MenuSurface L10n',GameAudio:'GameSettings GameAudioTuning',GameSettings:'GameInput GameAudio L10n',GameInput:'GameSettings',HangarShipPreview:'ShipAssembly BlueprintRepository',MenuSurface:'L10n GlassUiPalette',RangeAppNavigation:'RangeCombatWorld RangeHUD MenuSurface SettingsScreen',SettingsScreen:'GameSettings GameInput GameAudio',ShipWorkspace:'BlueprintRepository DeploymentValidator MissionResults FlightSession',WeaponGroupScreen:'WeaponGroups ShipAssembly WeaponGroupConnections GameInput',DeploymentValidator:'BlockPlacement MissionDefinition',MissionResults:'ShipWorkspace',
CombatVfxPool:'VoxelFxGeometry',DebrisCluster:'DebrisWorld',DebrisPartHitbox:'DebrisCluster DebrisWorld',DebrisWorld:'CombatVfxPool FlightTuning DebrisCluster DebrisPartHitbox',LaserVisualPool:'VoxelFxGeometry',ProjectileWorld:'CylindricalHitQuery ShipShieldSystem FlightShipRuntime DebrisPartHitbox LaserVisualPool',RangeBotPilot:'InterceptSolver CylindricalHitQuery ShipWeapons',RangeBotWeaponInput:'ShipWeapons',RangeCombatWorld:'FlightShipRuntime RangeBotPilot RangeBotWeaponInput ShipWeapons ProjectileWorld DebrisWorld ShipFlightController RangeTargetRoster TargetLockSystem',RangeTargetRoster:'BlueprintRepository',ShipInstanceRenderer:'FlightShipRuntime BlockModules',ShipPartHitbox:'FlightShipRuntime',ShipShieldSystem:'FlightShipRuntime',ShipShieldVisual:'FlightShipRuntime ShipShieldSystem',ShipWeapons:'ProjectileWorld WeaponActivationState WeaponClearance WeaponGroups',TargetAimGraphic:'TargetLockSystem TargetIndicatorProjection',TargetLockSystem:'RangeCombatWorld InterceptSolver CoreVelocityEstimator AimProjection',WeaponClearance:'BlockModules CylindricalHitQuery',
FlightRangeBootstrap:'FlightSession ShipAssembly FlightShipRuntime ShipFlightController RangeCombatWorld RangeHUD',FlightSession:'BlueprintRepository',FlightShipRuntime:'ShipAssembly BlockConnectivity FlightTuning ShipPowerBus ShipShieldSystem ShipInstanceRenderer',FlightTuning:'ModuleDatabase',GlassUiPalette:'UiTheme L10n',ModuleDatabase:'BlockGeometry',RangeHUD:'RangeCombatWorld ShipFlightController ShipStatusGraphic SphereRadarGraphic TargetAimGraphic ReticleHitFeedback',ShipFlightController:'FlightShipRuntime FlightTuning GameInput ShipCollisionSweep',ShipStatusGraphic:'BlockModules BlockGeometry',SphereRadarGraphic:'RangeCombatWorld',ThrusterExhaust:'FlightShipRuntime ShipFlightController VoxelFxGeometry',LocalizedUiFont:'L10n',LocalizedUiText:'L10n',WeaponGroupInputRouter:'ShipWeapons GameAudio',WeaponGroups:'ModuleDatabase BlockModules'
};
// Ranges use Inspector attributes or constraints in code. Defaults always come from C# initializers.
const tunes={
BattleTuning:[['outOfCombatDelay','float','0+','Seconds without hull damage before recovery.'],['recoveryTick','float','0.02-0.5','Seconds between recovery evaluations.'],['unlimitedRangePower','bool','false-true','Practice-only bypass for energy scarcity.'],['lockConeDegrees','float','1-80','Target acquisition cone in degrees.'],['lockRange','float','1+','Maximum target acquisition distance in metres.'],['botCruiseSpeedFraction','float','0.1-1','Cruise speed as a fraction of surviving ship capability.'],['botAttackSpeedFraction','float','0.1-1','Attack speed as a fraction of surviving ship capability.'],['botPilotDecisionSeconds','float','0.05-0.5','Opponent decision interval in seconds.'],['botReactionSeconds','float','0+','Delay before an opponent starts firing.'],['botFireBurstSeconds','float','0.05+','Duration of a firing burst.'],['botFirePauseSeconds','float','0+','Pause between firing bursts.'],['botRespawnSeconds','float','0+','Delay before an opponent respawns.'],['playerRespawnSeconds','float','0+','Delay before the player respawns in practice.'],['playerSpawnProtectionSeconds','float','0+','Practice respawn invulnerability duration.']],
FlightTuning:[['maneuverThrustFraction','float','0-0.8','Share of total thrust assigned to manoeuvre assistance.'],['maxSpeed','float','0+','Soft flight speed ceiling in metres per second.'],['maxLinearAcceleration','float','0+','Soft linear acceleration ceiling.'],['boostPowerWatts','float','0+','Requested power while holding boost.'],['boostMultiplier','float','1+','Boost multiplier at full allocated power.'],['mouseDegreesPerPixel','float','0+','Virtual-stick sensitivity before the settings multiplier.'],['maxAimOffsetDegrees','float','5-45','Maximum persistent virtual-stick offset.'],['aimInputDeadZone','float','0-0.4','Dead zone as a fraction of stick radius.'],['aimResponseExponent','float','0.5-3','Response curve around the stick centre.'],['aimInputSmoothing','float','0-0.15','Steering smoothing duration.'],['turnResponseTime','float','0.05-0.5','Turn response duration.'],['arenaHalfExtent','float','0+','Half-width of the test range in metres.']],
ModuleDefinition:[['cost','float','0+','Deployment cost; implicit field default is zero.'],['massKg','float','0+','Part mass in kilograms.'],['maxHp','float','0.01+','Independent part durability.'],['recoverySeconds','float','0.01+','Time needed to reconstruct a missing part.'],['thrustNewtons','float','0+','Nominal thrust for a propulsion part.'],['generationWatts','float','0+','Continuous shared power generation.'],['storageJoules','float','0+','Shared stored energy capacity.'],['thrusterPowerWatts','float','0+','Power request associated with propulsion.'],['shieldPowerWatts','float','0+','Power request associated with shield recovery.'],['shieldCapacity','float','0+','Maximum shield strength per generator.'],['shieldRadius','float','0.1+','Shield field radius in metres.'],['shieldRecoveryDelay','float','0+','Delay before a shield can recover.'],['shieldRecoveryPerSecond','float','0+','Shield recovery per second.']],
WeaponDefinition:[['shotsPerSecond','float','0.01+','Nominal shot cadence before energy allocation.'],['damage','float','0.01+','Damage of one complete projectile.'],['projectileSpeed','float','0.01+','Projectile speed used for travel and interception.'],['projectileDiameter','float','0.01+','Physical projectile hit-volume diameter.'],['penetrationDepth','float','0.01+','Maximum hull penetration depth.'],['secondLayerDamageMultiplier','float','0-1','Damage fraction for the second hit layer.'],['range','float','1+','Maximum projectile travel distance.'],['sustainedPowerWatts','float','0+','Shared power requested per firing weapon.'],['chargeSeconds','float','0.01+','Charge time for charge-release activation.']],
MissionDefinition:[['unpricedPartCost','float','0.01+','Optional provisional cost for an otherwise zero-cost part.'],['relaxedBudgetMultiplier','float','0+','Budget multiplier for relaxed difficulty.'],['standardBudgetMultiplier','float','0+','Budget multiplier for standard difficulty.'],['expertBudgetMultiplier','float','0+','Budget multiplier for expert difficulty.']]
};
// Resolve file-owned partial methods and remove collaborators not called by these implementations.
Object.assign(calls,{
 GameResultsScreen:'GameAppBootstrap MissionResults ShipWorkspace UiTheme',
 HangarShipPreview:'ShipAssembly',DebrisPartHitbox:'DebrisCluster',
 ShipInstanceRenderer:'FlightShipRuntime',ShipShieldVisual:'FlightShipRuntime',
 ShipWeapons:'ProjectileWorld WeaponActivationState WeaponGroups BlockPlacement',
 TargetLockSystem:'RangeCombatWorld InterceptSolver CoreVelocityEstimator ShipWeapons',
 WeaponClearance:'CylindricalHitQuery',GlassUiPalette:'UiTheme',ModuleDatabase:'',
 ShipStatusGraphic:'FlightShipRuntime'
});
function files(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?files(path.join(dir,e.name)):e.name.endsWith('.cs')?[path.join(dir,e.name)]:[]);}
let count=0;const manifest=[];
for(const file of files(base)){
 const name=path.basename(file,'.cs'),role=roles[name];if(!role)throw new Error('Missing reviewed role: '+name);
 const before=fs.readFileSync(file,'utf8');const body=before.replace(/^\uFEFF/,'').replace(/^(?:\/\/ @[^\r\n]*(?:\r?\n))+\r?\n/,'');
 const nl=body.includes('\r\n')?'\r\n':'\n';let header=['// @role '+role];
 const deps=(calls[name]||'').split(' ').filter(Boolean);for(const dep of deps)if(!roles[dep])throw new Error('Unknown dependency '+dep);
 if(deps.length)header.push('// @calls '+deps.join(', '));
 for(const tune of tunes[name]||[])header.push('// @tune '+tune.join(':'));
 const after=header.join(nl)+nl+nl+body;
 if(after.replace(/^(?:\/\/ @[^\r\n]*(?:\r?\n))+\r?\n/,'')!==body)throw new Error('Executable body changed: '+name);
 fs.writeFileSync(file,after);count++;
 manifest.push({file:path.relative(root,file).replaceAll('\\','/'),bodySha256:crypto.createHash('sha256').update(body).digest('hex'),calls:deps,tunables:(tunes[name]||[]).length});
}
fs.mkdirSync(path.join(root,'Documentation/CGDD'),{recursive:true});
fs.writeFileSync(path.join(root,'Documentation/CGDD/annotation-audit.json'),JSON.stringify({scope:'Documentation headers only. C# bodies and defaults unchanged.',scripts:manifest},null,2)+'\n');
console.log(JSON.stringify({annotated:count,tunables:manifest.reduce((n,s)=>n+s.tunables,0)}));
