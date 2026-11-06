// @role Fire grouped weapons with per-mount Gatling heat, powered audio heartbeat, peak-synchronized rails, fresh conventional locks and independent area-lock VLS magazines.
// @calls ProjectileWorld, WeaponActivationState, GatlingThermalState, WeaponGroups, BlockPlacement, SpecialWeaponVisualPool, DirectAimAssist, WeaponClearance

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;
using Starframe.Construction;
namespace Starframe.Combat
{
    /// <summary>A read-only view of the real mount state for combat instruments.</summary>
    public readonly struct WeaponHudState
    {
        public readonly ShipPartState Part;
        public readonly WeaponDefinition Definition;
        public readonly int GroupId;
        public readonly bool Enabled,Blocked,Overheated;
        public readonly float Heat,ReloadRemaining,ReloadProgress,LockProgress;
        public readonly int AmmoRemaining,AmmoCapacity;
        public WeaponHudState(ShipPartState part,WeaponDefinition definition,int groupId,bool enabled,bool blocked,bool overheated,float heat,float reloadRemaining,float reloadProgress,int ammoRemaining=0,int ammoCapacity=0,float lockProgress=0)
        {AmmoRemaining=ammoRemaining;AmmoCapacity=ammoCapacity;LockProgress=lockProgress;Part=part;Definition=definition;GroupId=groupId;Enabled=enabled;Blocked=blocked;Overheated=overheated;Heat=heat;ReloadRemaining=reloadRemaining;ReloadProgress=reloadProgress;}
    }
    /// <summary>Present Gatling mounts, including disabled groups and obstructed muzzles.</summary>
    public readonly struct GatlingHeatTelemetry
    {
        public readonly int Count;
        public readonly float HeatFraction,OverheatedHeatFraction;
        public readonly bool AnyOverheated;
        public float DisplayHeatFraction=>AnyOverheated?OverheatedHeatFraction:HeatFraction;
        public GatlingHeatTelemetry(int count,float heatFraction,bool anyOverheated,float overheatedHeatFraction)
        {Count=count;HeatFraction=heatFraction;AnyOverheated=anyOverheated;OverheatedHeatFraction=overheatedHeatFraction;}
    }
    public sealed class ShipWeapons
    {
        public static event Action<FlightShipRuntime,WeaponDefinition,Vector3> ShotWindupStarted;
        public static event Action<FlightShipRuntime,WeaponDefinition> ShotWindupCancelled;
        sealed class Slot
        {
            public ShipPartState part;public WeaponDefinition definition;public ModuleDefinition module;
            public readonly WeaponActivationState activation=new WeaponActivationState();
            public readonly GatlingThermalState thermal=new GatlingThermalState();
            public float reloadRemaining,lockElapsed,vlsSpacingRemaining;public bool emptyVisual;
            public int ammoRemaining,vlsQueueIndex;
            public readonly List<VlsRecipient> vlsQueue=new List<VlsRecipient>();
            public int nextMuzzle,missileTube,missileSalvoRemaining;
            public bool sustainingAudio;
            public bool windingUp;public float windupRemaining;
            public bool wasReady,pendingDischarge;public float pendingDischargeDelay;
            public int groupId,checkedRevision=-1;public bool blocked,ready;public Vector3 origin,direction;
        }
        readonly struct VlsRecipient
        {public readonly FlightShipRuntime ship;public readonly int life;public VlsRecipient(FlightShipRuntime value){ship=value;life=value.LifeId;}public bool IsAlive=>ship&&ship.IsAlive&&ship.LifeId==life;}
        sealed class VlsContact{public FlightShipRuntime ship;public int life;public float elapsed;}
        readonly List<VlsContact> vlsContacts=new List<VlsContact>();
        readonly List<FlightShipRuntime> vlsTargets=new List<FlightShipRuntime>();
        readonly List<Slot> vlsVolleySlots=new List<Slot>();
        readonly List<FlightShipRuntime> vlsVolleyTargets=new List<FlightShipRuntime>();
        public IReadOnlyList<FlightShipRuntime> VlsTargets=>vlsTargets;
        public bool IsVlsLockedTarget(FlightShipRuntime target)=>target&&target.IsAlive&&(vlsTargets.Contains(target)||projectiles!=null&&projectiles.HasVlsSeekerTarget(ship,target));
        public float VlsLockProgress{get{float best=0;foreach(var slot in guns)if(VlsAvailable(slot))best=Mathf.Max(best,VlsProgress(slot));return best;}}
        public void SetVlsVisibleTargets(IReadOnlyList<FlightShipRuntime> values)
        {
            for(int i=vlsContacts.Count-1;i>=0;i--){var contact=vlsContacts[i];bool found=false;if(values!=null)foreach(var target in values)if(target==contact.ship&&target&&target.IsAlive&&target.LifeId==contact.life){found=true;break;}if(!found)vlsContacts.RemoveAt(i);}
            if(values!=null)foreach(var target in values){if(!target||!target.IsAlive||target==ship||ship&&target.Team==ship.Team)continue;bool found=false;foreach(var contact in vlsContacts)if(contact.ship==target){found=true;break;}if(!found)vlsContacts.Add(new VlsContact{ship=target,life=target.LifeId});}
            RefreshVlsTargets();
        }
        sealed class AimGroup{public WeaponDefinition definition;public readonly List<Slot> slots=new List<Slot>();}
        readonly List<Slot> guns=new List<Slot>();FlightShipRuntime ship;ProjectileWorld projectiles;
        readonly List<AimGroup> aimGroups=new List<AimGroup>();
        readonly List<WeaponGroupRecord> weaponGroups=new List<WeaponGroupRecord>();
        readonly Dictionary<int,WeaponInputState> inputs=new Dictionary<int,WeaponInputState>();
        readonly HashSet<int> disabledGroups=new HashSet<int>();
        readonly List<int> inputIds=new List<int>();
        bool legacyPreviousHeld,alternatingDirectFire;
        float missileSpacingRemaining;
        int missileCursor;
        public float MissileLaunchesPerSecond{get;set;}=8;
        public bool AlternatingDirectFire
        {
            get=>alternatingDirectFire;
            set{if(alternatingDirectFire==value)return;alternatingDirectFire=value;foreach(var slot in guns){slot.wasReady=false;if(!value)slot.pendingDischargeDelay=0;if(slot.definition.delivery==WeaponDelivery.Projectile)slot.activation.SynchronizeCycle(slot.definition);}}
        }
        FlightShipRuntime trackedTarget;
        readonly DirectAimAssist directAim=new DirectAimAssist();
        bool hasDirectAimPoint;Vector3 directAimPoint;
        /// <summary>Player-only reticle ray. No target is selected unless this ray directly intersects hostile hull.</summary>
        public void SetDirectAimRay(Ray ray,bool enabled=true)
        {hasDirectAimPoint=enabled&&directAim.TryGetPoint(ship,ray,out directAimPoint);}
        int trackedLife=-1;

        public FlightShipRuntime Target{get;set;}
        static bool Conventional(Slot slot)=>slot.definition.delivery==WeaponDelivery.GuidedMissile&&!slot.definition.IsVls;
        bool ConventionalAvailable(Slot slot)=>Conventional(slot)&&slot.part.present&&IsGroupEnabled(slot.groupId)&&!slot.blocked&&slot.reloadRemaining<=0&&slot.missileSalvoRemaining<=0;
        public bool HasMissileWeapons{get{foreach(var slot in guns)if(Conventional(slot)&&slot.part.present&&IsGroupEnabled(slot.groupId))return true;return false;}}
        public float MissileLockProgress{get{
            if(!Target||!Target.IsAlive||!HasValidTargetLock||trackedTarget!=Target||trackedLife!=Target.LifeId)return 0;
            float best=0;
            foreach(var slot in guns)if(ConventionalAvailable(slot)&&MissileTargetValid(slot.definition))
                best=Mathf.Max(best,Mathf.Clamp01(slot.lockElapsed/Mathf.Max(.001f,slot.definition.missileLockSeconds)));
            return best;
        }}
        public int MissileWeaponCount{get{int count=0;foreach(var slot in guns)if(Conventional(slot)&&slot.part.present&&IsGroupEnabled(slot.groupId))count++;return count;}}
        public int ReadyMissileCount{get{int count=0;foreach(var slot in guns)if(ConventionalAvailable(slot))count++;return count;}}
        public float MissileReloadRemaining{get{float value=0;foreach(var slot in guns)if(Conventional(slot)&&slot.part.present&&IsGroupEnabled(slot.groupId))value=Mathf.Max(value,slot.reloadRemaining);return value;}}
        public float GetReloadProgress(string partId){foreach(var slot in guns)if(slot.part.record.id==partId)return 1-Mathf.Clamp01(slot.reloadRemaining/Mathf.Max(.1f,slot.definition.reloadSeconds));return 0;}
        /// <summary>Read actual mount state; selection never hides a hot or recovering present Gatling.</summary>
        public GatlingHeatTelemetry GatlingHeat
        {
            get
            {
                int count=0;float maximum=0,overheatedMaximum=0;bool anyOverheated=false;
                foreach(var slot in guns)if(slot.part.present&&slot.definition.family==WeaponFamily.Gatling)
                {
                    count++;float heat=slot.thermal.HeatFraction;maximum=Mathf.Max(maximum,heat);
                    if(slot.thermal.IsOverheated){anyOverheated=true;overheatedMaximum=Mathf.Max(overheatedMaximum,heat);}
                }
                return new GatlingHeatTelemetry(count,maximum,anyOverheated,overheatedMaximum);
            }
        }
        /// <summary>Authoritative powered firing state between discrete shots; drives the start, continuous and stop audio phases.</summary>
        public bool IsSustaining(WeaponFamily family)
        {
            if(!ship||!ship.IsAlive)return false;
            foreach(var slot in guns)if(slot.definition.family==family&&slot.sustainingAudio&&slot.part.present&&
                IsGroupEnabled(slot.groupId)&&slot.ready&&!slot.thermal.IsOverheated)return true;
            return false;
        }
        public float SustainingAudioGain(WeaponFamily family)
        {float gain=0;if(!ship||!ship.IsAlive)return gain;foreach(var slot in guns)if(slot.definition.family==family&&slot.sustainingAudio&&slot.part.present&&IsGroupEnabled(slot.groupId)&&slot.ready&&!slot.thermal.IsOverheated)gain=Mathf.Max(gain,slot.definition.shotAudioGain);return gain;}
        public int ActiveGuns{get;private set;}
        public int BlockedGuns{get;private set;}
        public int ShotsFired{get;private set;}
        /// <summary>Legacy/AI all-weapons input, used while UseGroupedInput is false.</summary>
        public bool FireRequested;
        public bool UseGroupedInput{get;set;}
        public bool HasValidTargetLock{get;set;}
        public IReadOnlyList<WeaponGroupRecord> Groups=>weaponGroups;
        public void GetHudWeaponStates(List<WeaponHudState> values)
        {
            values.Clear();
            foreach(var slot in guns)values.Add(new WeaponHudState(slot.part,slot.definition,slot.groupId,IsGroupEnabled(slot.groupId),slot.blocked,slot.thermal.IsOverheated,slot.thermal.HeatFraction,slot.reloadRemaining,1-Mathf.Clamp01(slot.reloadRemaining/Mathf.Max(.1f,slot.definition.reloadSeconds)),slot.definition.IsVls?slot.ammoRemaining:slot.reloadRemaining>0?0:1,slot.definition.IsVls?Mathf.Max(1,slot.definition.magazineSize):1,slot.definition.IsVls?VlsProgress(slot):Mathf.Clamp01(slot.lockElapsed/Mathf.Max(.001f,slot.definition.missileLockSeconds))));
        }

        // Sortie-local selection: blueprint assignments/bindings are never rewritten by number keys.
        public bool IsGroupEnabled(int groupId)=>inputs.ContainsKey(groupId)&&!disabledGroups.Contains(groupId);
        public void SetGroupEnabled(int groupId,bool enabled)
        {
            if(!inputs.ContainsKey(groupId)||IsGroupEnabled(groupId)==enabled)return;
            if(enabled)disabledGroups.Remove(groupId);else disabledGroups.Add(groupId);
            inputs[groupId]=default;
            foreach(var slot in guns)if(slot.groupId==groupId){slot.activation.Cancel();slot.ready=false;slot.lockElapsed=0;slot.vlsQueue.Clear();slot.vlsQueueIndex=0;slot.missileSalvoRemaining=0;CancelWindup(slot);}
        }
        public bool ToggleGroup(int groupId)
        {
            SetGroupEnabled(groupId,!IsGroupEnabled(groupId));return IsGroupEnabled(groupId);
        }

        /// <summary>Edges are latched until Prepare, so render-frame taps survive fixed-step routing.</summary>
        public void SetGroupInput(int groupId,bool held,bool pressed=false,bool released=false)
        {
            if(!IsGroupEnabled(groupId)||!inputs.TryGetValue(groupId,out var previous))return;
            inputs[groupId]=new WeaponInputState(held,previous.pressed||pressed,previous.released||released);
        }
        public void ClearGroupInputs()
        {
            FireRequested=legacyPreviousHeld=false;
            foreach(int id in inputIds)inputs[id]=default;
            foreach(var slot in guns){slot.activation.Cancel();CancelWindup(slot);slot.ready=slot.wasReady=slot.pendingDischarge=false;slot.missileSalvoRemaining=0;slot.vlsQueue.Clear();slot.vlsQueueIndex=0;}
        }
        public int GetGroupId(string partId)
        {
            foreach(var slot in guns)if(string.Equals(slot.part.record.id,partId,StringComparison.Ordinal))return slot.groupId;
            return Starframe.Construction.WeaponGroups.DefaultGroupId;
        }
        public int GetGroupGunCount(int groupId,bool presentOnly=false)
        {
            int count=0;foreach(var slot in guns)if(slot.groupId==groupId&&(!presentOnly||slot.part.present))count++;return count;
        }
        public float GetChargeProgress(string partId)
        {
            foreach(var slot in guns)if(string.Equals(slot.part.record.id,partId,StringComparison.Ordinal))return slot.activation.ChargeFraction;
            return 0;
        }

        public void GetAimProfiles(List<WeaponAimProfile> values,int? groupId=null,bool filterToHeld=true,bool renderPose=false)
        {
            if(values==null)throw new ArgumentNullException(nameof(values));
            values.Clear();if(!ship||!ship.IsAlive)return;
            bool filterHeld=false;
            if(filterToHeld&&!groupId.HasValue&&UseGroupedInput)foreach(var input in inputs)if(IsGroupEnabled(input.Key)&&input.Value.held){filterHeld=true;break;}
            foreach(var group in aimGroups){
                Vector3 centre=Vector3.zero;int count=0;float bestFacing=-2;
                foreach(var slot in group.slots)if(VisibleSlot(slot,groupId,filterHeld)){centre+=LocalMuzzle(slot);count++;bestFacing=Mathf.Max(bestFacing,Vector3.Dot(slot.part.record.rotation*Vector3.forward,ship.CoreRotation*Vector3.forward));}
                if(count==0)continue;centre/=count;Slot selected=null;float bestDistance=float.PositiveInfinity;
                foreach(var slot in group.slots){if(!VisibleSlot(slot,groupId,filterHeld)||Vector3.Dot(slot.part.record.rotation*Vector3.forward,ship.CoreRotation*Vector3.forward)<bestFacing-.0001f)continue;
                    float distance=(LocalMuzzle(slot)-centre).sqrMagnitude;
                    if(selected==null||distance<bestDistance-.0001f||(Mathf.Abs(distance-bestDistance)<.0001f&&string.CompareOrdinal(slot.part.record.id,selected.part.record.id)<0)){selected=slot;bestDistance=distance;}
                }
                var matrix=renderPose?selected.part.view.transform.localToWorldMatrix:CylindricalHitQuery.PhysicsMatrix(selected.part.view.GetComponent<Collider>());
                values.Add(new WeaponAimProfile{weapon=group.definition,origin=matrix.MultiplyPoint3x4(selected.module.muzzleLocalPosition),direction=matrix.MultiplyVector(Vector3.forward).normalized,partId=selected.part.record.id,gunCount=count});
            }
        }
        bool VisibleSlot(Slot slot,int? groupId,bool filterHeld)=>IsGroupEnabled(slot.groupId)&&slot.part.present&&!slot.blocked&&
            (!groupId.HasValue||slot.groupId==groupId.Value)&&(!filterHeld||inputs[slot.groupId].held);
        static Vector3 LocalMuzzle(Slot slot)=>slot.part.record.position+slot.part.record.rotation*slot.module.muzzleLocalPosition;

        public void Initialize(FlightShipRuntime owner,ProjectileWorld world,BlueprintData blueprint=null)
        {
            if(!owner)throw new ArgumentNullException(nameof(owner));if(world==null)throw new ArgumentNullException(nameof(world));
            ship=owner;projectiles=world;guns.Clear();aimGroups.Clear();weaponGroups.Clear();inputs.Clear();inputIds.Clear();disabledGroups.Clear();
            Target=trackedTarget=null;trackedLife=-1;vlsContacts.Clear();vlsTargets.Clear();missileSpacingRemaining=0;missileCursor=0;alternatingDirectFire=false;hasDirectAimPoint=false;
            ActiveGuns=BlockedGuns=ShotsFired=0;FireRequested=legacyPreviousHeld=UseGroupedInput=HasValidTargetLock=false;
            var snapshot=new BlueprintData();foreach(var part in owner.Parts)snapshot.parts.Add(BlockPlacement.Copy(part.record));
            Starframe.Construction.WeaponGroups.CopyTo(blueprint??owner.BlueprintSnapshot,snapshot);
            Starframe.Construction.WeaponGroups.Normalize(snapshot,owner.Tuning.database);
            foreach(var group in snapshot.weaponGroups){weaponGroups.Add(group);inputs.Add(group.id,default);inputIds.Add(group.id);}
            foreach(var part in owner.Parts){var d=owner.Tuning.GetDefinition(part.record.shape);if(!d||!d.weapon)continue;
                var slot=new Slot{part=part,module=d,definition=d.weapon,groupId=Starframe.Construction.WeaponGroups.GetGroupId(snapshot,part.record.id),ammoRemaining=Mathf.Max(1,d.weapon.magazineSize)};
                guns.Add(slot);var group=aimGroups.Find(g=>g.definition==d.weapon);
                if(group==null){group=new AimGroup{definition=d.weapon};aimGroups.Add(group);}group.slots.Add(slot);
            }
        }
        public float Prepare()
        {
            float demand=0;ActiveGuns=BlockedGuns=0;
            var legacyInput=new WeaponInputState(FireRequested,FireRequested&&!legacyPreviousHeld,!FireRequested&&legacyPreviousHeld);
            foreach(var slot in guns){
                slot.sustainingAudio=false;slot.ready=false;if(!ship.IsAlive||!slot.part.present){slot.activation.Clear();continue;}ActiveGuns++;
                var collider=slot.part.view.GetComponent<Collider>();var matrix=CylindricalHitQuery.PhysicsMatrix(collider);
                slot.origin=matrix.MultiplyPoint3x4(slot.module.muzzleLocalPosition);slot.direction=matrix.MultiplyVector(slot.definition.IsVls?Vector3.up:Vector3.forward).normalized;
                if(slot.checkedRevision!=ship.StructureRevision){
                    slot.blocked=false;
                    if(slot.definition.IsVls)foreach(var obstacle in ship.Parts)if(obstacle!=slot.part&&obstacle.present&&WeaponClearance.VerticalLaunchPathBlockedBy(slot.part.record,obstacle.record)){slot.blocked=true;break;}
                    var offsets=slot.definition.IsVls?slot.definition.missileLaunchOffsets:slot.definition.muzzleOffsets;
                    if(offsets==null||offsets.Length==0)slot.blocked|=projectiles.OwnPathBlocked(ship,slot.part,slot.origin,slot.direction,slot.module.muzzleClearanceDiameter);
                    else foreach(var offset in offsets)if(projectiles.OwnPathBlocked(ship,slot.part,matrix.MultiplyPoint3x4(slot.module.muzzleLocalPosition+offset),slot.direction,slot.module.muzzleClearanceDiameter)){slot.blocked=true;break;}
                    slot.checkedRevision=ship.StructureRevision;
                }
                if(slot.blocked)BlockedGuns++;
                if(!IsGroupEnabled(slot.groupId)){slot.activation.Cancel();continue;}
                var input=UseGroupedInput?inputs[slot.groupId]:legacyInput;
                bool missile=slot.definition.delivery==WeaponDelivery.GuidedMissile;
                bool lockReady=slot.definition.IsVls?VlsAvailable(slot)&&VlsProgress(slot)>=1:missile?MissileTargetValid(slot.definition)&&slot.lockElapsed+0.00001f>=slot.definition.missileLockSeconds:(!slot.definition.requireTargetLock||HasValidTargetLock);
                slot.ready=slot.activation.Prepare(slot.definition,input,!slot.blocked&&lockReady);
                if(missile){
                    // Lock acquisition is free. Only automatic reload or a gated,
                    // loaded launch can compete with other systems for power.
                    if(slot.reloadRemaining>0||slot.missileSalvoRemaining>0||(slot.ready&&slot.reloadRemaining<=0))demand+=Mathf.Max(0,slot.definition.sustainedPowerWatts);
                }else if((slot.activation.RequestsPower||slot.windingUp)&&!slot.thermal.IsOverheated&&slot.definition.shotsPerSecond>0)demand+=Mathf.Max(0,slot.definition.sustainedPowerWatts);
            }
            if(AlternatingDirectFire){
                int count=0;foreach(var slot in guns)if(IsAlternatingSlot(slot))count++;
                int rank=0;foreach(var slot in guns)if(IsAlternatingSlot(slot)){
                    if(!slot.wasReady&&count>1)slot.activation.DelayCycle(slot.definition,rank/(float)count/Mathf.Max(.01f,slot.definition.shotsPerSecond));
                    rank++;
                }
            }
            foreach(var slot in guns)slot.wasReady=IsAlternatingSlot(slot);
            foreach(int id in inputIds){var input=inputs[id];input.pressed=input.released=false;inputs[id]=input;}
            legacyPreviousHeld=FireRequested;
            return demand;
        }
        static bool IsAlternatingSlot(Slot slot)=>slot.ready&&slot.part.present&&slot.definition.delivery==WeaponDelivery.Projectile&&
            (slot.definition.activationMode==WeaponActivationMode.Hold||slot.definition.activationMode==WeaponActivationMode.Toggle);
        bool MissileTargetValid(WeaponDefinition definition)
        {
            return HasValidTargetLock&&Target&&Target.IsAlive&&Target!=ship&&Target.Team!=ship.Team&&
                trackedTarget==Target&&trackedLife==Target.LifeId&&
                Vector3.Distance(ship.CorePhysicsPosition,Target.CorePhysicsPosition)<=definition.range;
        }
        void AdvanceLock(float dt)
        {
            bool valid=ship&&ship.IsAlive&&HasValidTargetLock&&Target&&Target.IsAlive&&Target!=ship&&Target.Team!=ship.Team;
            bool changed=!valid||trackedTarget!=Target||trackedLife!=Target.LifeId;
            trackedTarget=valid?Target:null;trackedLife=valid?Target.LifeId:-1;
            foreach(var slot in guns)if(Conventional(slot)){
                if(changed||!ConventionalAvailable(slot)||!MissileTargetValid(slot.definition))slot.lockElapsed=0;
                if(valid&&ConventionalAvailable(slot)&&MissileTargetValid(slot.definition))slot.lockElapsed+=dt;
            }
            foreach(var contact in vlsContacts){
                bool eligible=false;foreach(var slot in guns)if(VlsAvailable(slot)&&VlsContactValid(contact,slot.definition)){eligible=true;break;}
                contact.elapsed=eligible?contact.elapsed+dt:0;
            }
            foreach(var slot in guns)if(slot.definition.IsVls){
                bool any=false;if(VlsAvailable(slot))foreach(var contact in vlsContacts)if(VlsContactValid(contact,slot.definition)){any=true;break;}
                slot.lockElapsed=any?slot.lockElapsed+dt:0;
            }
            RefreshVlsTargets();
        }
        bool VlsAvailable(Slot slot)=>slot.definition.IsVls&&ship&&ship.IsAlive&&slot.part.present&&IsGroupEnabled(slot.groupId)&&!slot.blocked&&slot.reloadRemaining<=0&&slot.ammoRemaining>0;
        bool VlsContactValid(VlsContact contact,WeaponDefinition definition)=>contact.ship&&contact.ship.IsAlive&&contact.ship.LifeId==contact.life&&contact.ship.Team!=ship.Team&&Vector3.Distance(ship.CorePhysicsPosition,contact.ship.CorePhysicsPosition)<=definition.range;
        float VlsProgress(Slot slot)
        {
            if(!VlsAvailable(slot))return 0;float best=0;
            foreach(var contact in vlsContacts)if(VlsContactValid(contact,slot.definition))best=Mathf.Max(best,Mathf.Min(contact.elapsed,slot.lockElapsed));
            return Mathf.Clamp01(best/Mathf.Max(.001f,slot.definition.missileLockSeconds));
        }
        void RefreshVlsTargets()
        {
            vlsTargets.Clear();if(!ship||!ship.IsAlive)return;
            foreach(var contact in vlsContacts)foreach(var slot in guns)if(VlsAvailable(slot)&&VlsContactValid(contact,slot.definition)&&Mathf.Min(contact.elapsed,slot.lockElapsed)+.00001f>=slot.definition.missileLockSeconds){vlsTargets.Add(contact.ship);break;}
        }
        static void ReloadVisual(Slot slot,bool empty)
        {
            if(slot.emptyVisual==empty||!slot.part.view||!slot.definition.launcherMesh)return;
            slot.emptyVisual=empty;
            var filter=slot.part.view.GetComponent<MeshFilter>();
            if(filter)filter.sharedMesh=empty?slot.definition.launcherMesh:slot.module.mesh;
            var edges=slot.part.view.transform.Find("Edges");if(edges)edges.gameObject.SetActive(!empty);
        }
        public void Execute(float dt,float powerRatio)
        {
            if(dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            AdvanceLock(dt);
            float poweredDt=dt*Mathf.Clamp01(powerRatio);
            float missileInterval=1/Mathf.Max(.01f,MissileLaunchesPerSecond);
            // Drop obsolete launch debt after a stall; retain only the small-step
            // phase correction needed to average eight tubes per second.
            float previousSpacing=missileSpacingRemaining;
            bool continuingMissileCycle=previousSpacing>0&&dt<=missileInterval;
            missileSpacingRemaining=Mathf.Max(continuingMissileCycle?-dt:0,previousSpacing-dt);
            foreach(var slot in guns){
                bool missile=slot.definition.delivery==WeaponDelivery.GuidedMissile;
                if(missile&&slot.part.present&&ship.IsAlive&&IsGroupEnabled(slot.groupId)&&slot.reloadRemaining>0){
                    slot.reloadRemaining=Mathf.Max(0,slot.reloadRemaining-poweredDt);
                    if(slot.reloadRemaining<=0){if(slot.definition.IsVls)slot.ammoRemaining=Mathf.Max(1,slot.definition.magazineSize);ReloadVisual(slot,false);}
                }
                if(missile)continue;
                AdvanceWindup(slot,dt,powerRatio);
                // The mount owns one persistent thermal clock, including disabled, blocked and unpowered steps.
                bool requested=IsGroupEnabled(slot.groupId)&&slot.ready&&slot.part.present&&ship.IsAlive&&slot.activation.RequestsPower;
                float cadenceMultiplier=slot.thermal.Advance(slot.definition,dt,requested,powerRatio);
                slot.sustainingAudio=requested&&powerRatio>0&&cadenceMultiplier>0&&!slot.thermal.IsOverheated;
                if(!IsGroupEnabled(slot.groupId)||!slot.ready||!slot.part.present||!ship.IsAlive){slot.activation.AdvanceInactive(dt);continue;}
                if(slot.definition.requireTargetLock&&!HasValidTargetLock){slot.activation.Cancel();slot.activation.AdvanceInactive(dt);slot.ready=false;continue;}
                int count=slot.activation.Advance(slot.definition,dt,powerRatio,cadenceMultiplier);
                bool edgeMode=slot.definition.activationMode==WeaponActivationMode.Press||slot.definition.activationMode==WeaponActivationMode.ChargeRelease;
                if(count>0&&AlternatingDirectFire&&slot.definition.delivery==WeaponDelivery.Projectile&&edgeMode){
                    slot.pendingDischarge=true;slot.pendingDischargeDelay=DischargePhase(slot);
                }else for(int i=0;i<count;i++)BeginDirect(slot);
            }
            foreach(var slot in guns)if(slot.pendingDischarge){
                if(!ship.IsAlive||!slot.part.present||!IsGroupEnabled(slot.groupId)||slot.blocked||(slot.definition.requireTargetLock&&!HasValidTargetLock)){slot.pendingDischarge=false;continue;}
                slot.pendingDischargeDelay-=dt;
                if(slot.pendingDischargeDelay<=0){slot.pendingDischarge=false;BeginDirect(slot);}
            }
            ExecuteMissiles(powerRatio);
            ExecuteVls(dt,powerRatio);
            RefreshVlsTargets();
        }
        float DischargePhase(Slot selected)
        {
            int rank=0,count=0;foreach(var slot in guns)if(slot.ready&&slot.part.present&&slot.definition.delivery==WeaponDelivery.Projectile){if(slot==selected)rank=count;count++;}
            return count>1?rank/(float)count/Mathf.Max(.01f,selected.definition.shotsPerSecond):0;
        }
        void AdvanceWindup(Slot slot,float dt,float powerRatio)
        {
            if(!slot.windingUp)return;
            bool sustained=slot.definition.activationMode==WeaponActivationMode.Hold||slot.definition.activationMode==WeaponActivationMode.Toggle;
            if(!ship.IsAlive||!slot.part.present||!IsGroupEnabled(slot.groupId)||slot.blocked||powerRatio<=0||
                (sustained&&!slot.ready)||(slot.definition.requireTargetLock&&!HasValidTargetLock))
            {CancelWindup(slot);return;}
            slot.windupRemaining-=dt;
            if(slot.windupRemaining<=.000001f){slot.windingUp=false;EmitDirect(slot);}
        }
        public bool HasPendingRailWindup(bool large)
        {
            var largeModule=ship&&ship.Tuning?ship.Tuning.GetDefinition(BlockShape.Railgun3):null;
            foreach(var slot in guns)if(slot.windingUp&&slot.definition.family==WeaponFamily.Railgun&&
                ((largeModule&&largeModule.weapon==slot.definition)==large))return true;
            return false;
        }
        void CancelWindup(Slot slot)
        {
            if(!slot.windingUp)return;
            slot.windingUp=false;
            // Audio merges simultaneous mounts into one rail cue. Keep it while any same-category mount remains committed.
            var large=ship&&ship.Tuning?ship.Tuning.GetDefinition(BlockShape.Railgun3):null;
            bool isLarge=large&&large.weapon==slot.definition;
            foreach(var other in guns)if(other.windingUp&&other.definition.family==slot.definition.family&&
                (slot.definition.family!=WeaponFamily.Railgun||((large&&large.weapon==other.definition)==isLarge)))return;
            ShotWindupCancelled?.Invoke(ship,slot.definition);
        }
        void BeginDirect(Slot slot)
        {
            float delay=slot.definition.delivery==WeaponDelivery.Projectile?Mathf.Max(0,slot.definition.fireWindupSeconds):0;
            if(delay<=0){EmitDirect(slot);return;}
            // Only one accepted discharge may wait on a mount. A stalled frame cannot pile up old sounds/shots.
            if(slot.windingUp)return;
            slot.windingUp=true;slot.windupRemaining=delay;
            ShotWindupStarted?.Invoke(ship,slot.definition,slot.origin);
        }
        void EmitDirect(Slot slot)
        {
            var offsets=slot.definition.muzzleOffsets;
            Vector3 local=slot.module.muzzleLocalPosition+(offsets!=null&&offsets.Length>0?offsets[slot.nextMuzzle++%offsets.Length]:Vector3.zero);
            var matrix=CylindricalHitQuery.PhysicsMatrix(slot.part.view.GetComponent<Collider>());
            Vector3 origin=matrix.MultiplyPoint3x4(local),direction=matrix.MultiplyVector(Vector3.forward).normalized;
            if(hasDirectAimPoint&&slot.definition.delivery!=WeaponDelivery.GuidedMissile)
            {
                var corrected=DirectAimAssist.Converge(origin,direction,directAimPoint,slot.definition.EffectiveRange);
                if(!projectiles.OwnPathBlocked(ship,slot.part,origin,corrected,slot.module.muzzleClearanceDiameter))direction=corrected;
            }
            projectiles.Fire(ship,slot.definition,origin,direction,slot.part.view.transform,local);ShotsFired++;
        }
        void ExecuteMissiles(float powerRatio)
        {
            if(!ship.IsAlive)return;
            // No backlog catch-up after a long frame: at most one real missile per simulation tick.
            for(int offset=0;offset<guns.Count;offset++){
                int index=(missileCursor+offset)%guns.Count;var slot=guns[index];
                if(!Conventional(slot))continue;
                bool valid=IsGroupEnabled(slot.groupId)&&slot.part.present&&!slot.blocked&&MissileTargetValid(slot.definition)&&(slot.missileSalvoRemaining>0||slot.lockElapsed+.00001f>=slot.definition.missileLockSeconds);
                if(!valid){slot.missileSalvoRemaining=0;continue;}
                if(missileSpacingRemaining>.00001f||powerRatio<=.00001f)continue;
                if(slot.missileSalvoRemaining<=0){
                    if(!slot.ready||slot.reloadRemaining>0)continue;
                    var offsets=slot.definition.missileLaunchOffsets;
                    slot.missileSalvoRemaining=offsets!=null&&offsets.Length>0?offsets.Length:1;slot.missileTube=0;
                    slot.reloadRemaining=Mathf.Max(.1f,slot.definition.reloadSeconds);slot.lockElapsed=0;ReloadVisual(slot,true);
                }
                projectiles.FireMissileTube(ship,slot.definition,slot.part.view.transform,slot.module.muzzleLocalPosition,slot.direction,Target,slot.missileTube++);
                slot.missileSalvoRemaining--;ShotsFired++;missileCursor=(index+1)%guns.Count;
                missileSpacingRemaining+=1/Mathf.Max(.01f,MissileLaunchesPerSecond);return;
            }
        }
        void ExecuteVls(float dt,float powerRatio)
        {
            if(!ship.IsAlive)return;
            // Commit one balanced group volley; each mount owns its own cadence afterwards.
            foreach(var group in weaponGroups){
                vlsVolleySlots.Clear();vlsVolleyTargets.Clear();
                foreach(var slot in guns)if(slot.groupId==group.id&&VlsAvailable(slot)&&slot.ready&&slot.missileSalvoRemaining<=0&&VlsProgress(slot)>=1)vlsVolleySlots.Add(slot);
                if(vlsVolleySlots.Count==0||powerRatio<=.00001f)continue;
                foreach(var contact in vlsContacts){bool all=true;foreach(var slot in vlsVolleySlots)if(!VlsContactValid(contact,slot.definition)||Mathf.Min(contact.elapsed,slot.lockElapsed)+.00001f<slot.definition.missileLockSeconds){all=false;break;}if(all)vlsVolleyTargets.Add(contact.ship);}
                if(vlsVolleyTargets.Count==0)continue;
                int recipient=0;
                foreach(var slot in vlsVolleySlots){slot.vlsQueue.Clear();slot.vlsQueueIndex=0;slot.missileSalvoRemaining=slot.ammoRemaining;slot.vlsSpacingRemaining=1/Mathf.Max(.01f,slot.definition.salvoLaunchesPerSecond);
                    for(int i=0;i<slot.ammoRemaining;i++)slot.vlsQueue.Add(new VlsRecipient(vlsVolleyTargets[recipient++%vlsVolleyTargets.Count]));}
            }
            foreach(var slot in guns){
                if(!slot.definition.IsVls||slot.missileSalvoRemaining<=0)continue;
                if(!VlsAvailable(slot)){slot.vlsQueue.Clear();slot.vlsQueueIndex=0;slot.missileSalvoRemaining=0;continue;}
                if(powerRatio<=.00001f)continue;
                slot.vlsSpacingRemaining-=dt;
                float interval=1/Mathf.Max(.01f,slot.definition.salvoLaunchesPerSecond);
                while(slot.vlsSpacingRemaining<=.00001f&&slot.missileSalvoRemaining>0){
                    var recipient=slot.vlsQueue[slot.vlsQueueIndex++];
                    if(!recipient.IsAlive){slot.missileSalvoRemaining--;continue;}
                    projectiles.FireMissileTube(ship,slot.definition,slot.part.view.transform,slot.module.muzzleLocalPosition,slot.direction,recipient.ship,Mathf.Max(1,slot.definition.magazineSize)-slot.ammoRemaining);
                    slot.ammoRemaining--;slot.missileSalvoRemaining--;ShotsFired++;slot.vlsSpacingRemaining+=interval;
                    if(slot.ammoRemaining<=0){slot.reloadRemaining=Mathf.Max(.1f,slot.definition.reloadSeconds);slot.lockElapsed=0;ReloadVisual(slot,true);}
                }
                if(slot.missileSalvoRemaining<=0){slot.vlsQueue.Clear();slot.vlsQueueIndex=0;}
            }
        }
        public void Reset(){hasDirectAimPoint=false;ClearGroupInputs();missileSpacingRemaining=0;missileCursor=0;ShotsFired=0;HasValidTargetLock=false;Target=trackedTarget=null;trackedLife=-1;vlsContacts.Clear();vlsTargets.Clear();foreach(var slot in guns){slot.sustainingAudio=false;slot.windingUp=false;slot.activation.Clear();slot.thermal.Clear();slot.checkedRevision=-1;slot.nextMuzzle=slot.missileTube=slot.missileSalvoRemaining=0;slot.wasReady=slot.pendingDischarge=false;slot.reloadRemaining=slot.lockElapsed=slot.vlsSpacingRemaining=0;slot.ammoRemaining=Mathf.Max(1,slot.definition.magazineSize);ReloadVisual(slot,false);}}
    }
}
