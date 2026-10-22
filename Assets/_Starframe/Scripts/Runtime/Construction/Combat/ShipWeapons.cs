// @role Prepare weapon demand, enforce clearance and fire grouped weapon slots.
// @calls ProjectileWorld, WeaponActivationState, WeaponGroups, BlockPlacement

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Flight;
using Starframe.Construction;
namespace Starframe.Combat
{
    public sealed class ShipWeapons
    {
        sealed class Slot
        {
            public ShipPartState part;public WeaponDefinition definition;public ModuleDefinition module;
            public readonly WeaponActivationState activation=new WeaponActivationState();
            public int groupId,checkedRevision=-1;public bool blocked,ready;public Vector3 origin,direction;
        }
        sealed class AimGroup{public WeaponDefinition definition;public readonly List<Slot> slots=new List<Slot>();}
        readonly List<Slot> guns=new List<Slot>();FlightShipRuntime ship;ProjectileWorld projectiles;
        readonly List<AimGroup> aimGroups=new List<AimGroup>();
        readonly List<WeaponGroupRecord> weaponGroups=new List<WeaponGroupRecord>();
        readonly Dictionary<int,WeaponInputState> inputs=new Dictionary<int,WeaponInputState>();
        readonly HashSet<int> disabledGroups=new HashSet<int>();
        readonly List<int> inputIds=new List<int>();
        bool legacyPreviousHeld;
        public int ActiveGuns{get;private set;}
        public int BlockedGuns{get;private set;}
        public int ShotsFired{get;private set;}
        /// <summary>Legacy/AI all-weapons input, used while UseGroupedInput is false.</summary>
        public bool FireRequested;
        public bool UseGroupedInput{get;set;}
        public bool HasValidTargetLock{get;set;}
        public IReadOnlyList<WeaponGroupRecord> Groups=>weaponGroups;

        // Sortie-local selection: blueprint assignments/bindings are never rewritten by number keys.
        public bool IsGroupEnabled(int groupId)=>inputs.ContainsKey(groupId)&&!disabledGroups.Contains(groupId);
        public void SetGroupEnabled(int groupId,bool enabled)
        {
            if(!inputs.ContainsKey(groupId)||IsGroupEnabled(groupId)==enabled)return;
            if(enabled)disabledGroups.Remove(groupId);else disabledGroups.Add(groupId);
            inputs[groupId]=default;
            foreach(var slot in guns)if(slot.groupId==groupId){slot.activation.Clear();slot.ready=false;}
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
            foreach(var slot in guns){slot.activation.Clear();slot.ready=false;}
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

        public void GetAimProfiles(List<WeaponAimProfile> values,int? groupId=null)
        {
            if(values==null)throw new ArgumentNullException(nameof(values));
            values.Clear();if(!ship||!ship.IsAlive)return;
            bool filterHeld=false;
            if(!groupId.HasValue&&UseGroupedInput)foreach(var input in inputs)if(IsGroupEnabled(input.Key)&&input.Value.held){filterHeld=true;break;}
            foreach(var group in aimGroups){
                Vector3 centre=Vector3.zero;int count=0;float bestFacing=-2;
                foreach(var slot in group.slots)if(VisibleSlot(slot,groupId,filterHeld)){centre+=LocalMuzzle(slot);count++;bestFacing=Mathf.Max(bestFacing,Vector3.Dot(slot.part.record.rotation*Vector3.forward,ship.CoreRotation*Vector3.forward));}
                if(count==0)continue;centre/=count;Slot selected=null;float bestDistance=float.PositiveInfinity;
                foreach(var slot in group.slots){if(!VisibleSlot(slot,groupId,filterHeld)||Vector3.Dot(slot.part.record.rotation*Vector3.forward,ship.CoreRotation*Vector3.forward)<bestFacing-.0001f)continue;
                    float distance=(LocalMuzzle(slot)-centre).sqrMagnitude;
                    if(selected==null||distance<bestDistance-.0001f||(Mathf.Abs(distance-bestDistance)<.0001f&&string.CompareOrdinal(slot.part.record.id,selected.part.record.id)<0)){selected=slot;bestDistance=distance;}
                }
                var matrix=CylindricalHitQuery.PhysicsMatrix(selected.part.view.GetComponent<Collider>());
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
            ActiveGuns=BlockedGuns=ShotsFired=0;FireRequested=legacyPreviousHeld=UseGroupedInput=HasValidTargetLock=false;
            var snapshot=new BlueprintData();foreach(var part in owner.Parts)snapshot.parts.Add(BlockPlacement.Copy(part.record));
            Starframe.Construction.WeaponGroups.CopyTo(blueprint??owner.BlueprintSnapshot,snapshot);
            Starframe.Construction.WeaponGroups.Normalize(snapshot,owner.Tuning.database);
            foreach(var group in snapshot.weaponGroups){weaponGroups.Add(group);inputs.Add(group.id,default);inputIds.Add(group.id);}
            foreach(var part in owner.Parts){var d=owner.Tuning.GetDefinition(part.record.shape);if(!d||!d.weapon)continue;
                var slot=new Slot{part=part,module=d,definition=d.weapon,groupId=Starframe.Construction.WeaponGroups.GetGroupId(snapshot,part.record.id)};
                guns.Add(slot);var group=aimGroups.Find(g=>g.definition==d.weapon);
                if(group==null){group=new AimGroup{definition=d.weapon};aimGroups.Add(group);}group.slots.Add(slot);
            }
        }
        public float Prepare()
        {
            float demand=0;ActiveGuns=BlockedGuns=0;
            var legacyInput=new WeaponInputState(FireRequested,FireRequested&&!legacyPreviousHeld,!FireRequested&&legacyPreviousHeld);
            foreach(var slot in guns){
                slot.ready=false;if(!ship.IsAlive||!slot.part.present){slot.activation.Clear();continue;}ActiveGuns++;
                var collider=slot.part.view.GetComponent<Collider>();var matrix=CylindricalHitQuery.PhysicsMatrix(collider);
                slot.origin=matrix.MultiplyPoint3x4(slot.module.muzzleLocalPosition);slot.direction=matrix.MultiplyVector(Vector3.forward).normalized;
                if(slot.checkedRevision!=ship.StructureRevision){slot.blocked=projectiles.OwnPathBlocked(ship,slot.part,slot.origin,slot.direction,slot.module.muzzleClearanceDiameter);slot.checkedRevision=ship.StructureRevision;}
                if(slot.blocked)BlockedGuns++;
                if(!IsGroupEnabled(slot.groupId)){slot.activation.Clear();continue;}
                var input=UseGroupedInput?inputs[slot.groupId]:legacyInput;
                slot.ready=slot.activation.Prepare(slot.definition,input,!slot.blocked&&(!slot.definition.requireTargetLock||HasValidTargetLock));
                if(slot.activation.RequestsPower&&slot.definition.shotsPerSecond>0)demand+=Mathf.Max(0,slot.definition.sustainedPowerWatts);
            }
            foreach(int id in inputIds){var input=inputs[id];input.pressed=input.released=false;inputs[id]=input;}
            legacyPreviousHeld=FireRequested;
            return demand;
        }
        public void Execute(float dt,float powerRatio)
        {
            foreach(var slot in guns){
                if(!IsGroupEnabled(slot.groupId)||!slot.ready||!slot.part.present||!ship.IsAlive)continue;
                if(slot.definition.requireTargetLock&&!HasValidTargetLock){slot.activation.Clear();slot.ready=false;continue;}
                int count=slot.activation.Advance(slot.definition,dt,powerRatio);
                for(int i=0;i<count;i++){projectiles.Fire(ship,slot.definition,slot.origin,slot.direction,slot.part.view.transform,slot.module.muzzleLocalPosition);ShotsFired++;}
            }
        }
        public void Reset(){ClearGroupInputs();ShotsFired=0;HasValidTargetLock=false;foreach(var slot in guns)slot.checkedRevision=-1;}
    }
}
