// @role Coordinate shared combat, finite mission actors or practice respawns, AI weapons, power and persistent wreckage.
// @calls FlightShipRuntime, RangeBotPilot, RangeBotWeaponInput, ShipWeapons, ProjectileWorld, DebrisWorld, ShipFlightController, RangeTargetRoster, TargetLockSystem, TargetSensorWorld, FlareWorld, ShipCountermeasureController

using UnityEngine;
using Unity.Profiling;
using System.Collections.Generic;
using Starframe.Construction;
using Starframe.Flight;
namespace Starframe.Combat
{
    [DefaultExecutionOrder(-200)]
    public sealed class RangeCombatWorld:MonoBehaviour
    {
        static readonly ProfilerMarker AiMarker=new ProfilerMarker("Starframe.RangeAI"),ExecutionMarker=new ProfilerMarker("Starframe.PowerAndWeapons");
        public sealed class Actor
        {
            public FlightShipRuntime ship;public ShipFlightController flight;public Rigidbody body;public ShipWeapons weapons;
            public double deathTime=-1;public float recoveryElapsed;public bool cleared;public Vector3 initialPosition;public Quaternion initialRotation;
            public int spawnSlot;public string blueprintName;public Vector3 waypoint,look;public float nextDecision;public bool aimingAtPlayer,fire;
            public RangeBotPilot pilot;
            public EnemyArchetypeDefinition archetype;public double attackReadyAt;
            public RangeBotWeaponInput weaponInput;
            public ShipCountermeasureController countermeasures;
            public double incomingMissileSince=-1,nextCountermeasureCheck;
            public int countermeasureLife=-1;
        }
        Actor player;BattleTuning rules;RangeHUD hud;Camera view;double clock;
        readonly List<Actor> actors=new List<Actor>();CombatVfxPool destruction;
        readonly List<Actor> bots=new List<Actor>();
        readonly CoreVelocityEstimator playerCoreMotion=new CoreVelocityEstimator();int sampledPlayerLife=-1;
        public double MaximumBotCreationMilliseconds{get;private set;}
        public bool IsMission{get;private set;}
        public bool CombatEnabled{get;set;}=true;
        public double CombatTime=>clock;
        public event System.Action<Actor> ActorDied;
        public IReadOnlyList<Actor> Actors=>actors;
        public IReadOnlyList<Actor> Bots=>bots;
        public TargetLockSystem Targeting{get;private set;}
        public TargetSensorWorld Sensors{get;private set;}
        public FlareWorld Flares{get;private set;}
        ProjectileWorld projectiles;ShipAssembly prefab;FlightTuning flightTuning;
        public Actor Player=>player;
        public Actor Bot=>bots.Count>0?bots[0]:null;
        public ProjectileWorld Projectiles=>projectiles;
        public DebrisWorld Debris{get;private set;}
        public RangeBotMode BotMode{get;private set;}=RangeBotMode.Combat;
        public Vector3 PlayerCoreVelocity=>playerCoreMotion.Velocity;
        public int BotKills{get;private set;}
        public float PlayerRespawnRemaining=>player==null||player.ship.IsAlive?0:Mathf.Max(0,rules.playerRespawnSeconds-(float)(clock-player.deathTime));
        public float BotRespawnRemaining=>Bot==null||Bot.ship.IsAlive?0:Mathf.Max(0,rules.botRespawnSeconds-(float)(clock-Bot.deathTime));
        public void Initialize(FlightShipRuntime runtime,ShipFlightController controller,ShipAssembly assemblyPrefab,FlightTuning tuning,BattleTuning battle,Camera camera,RangeHUD display,bool mission=false)
        {
            IsMission=mission;rules=battle;hud=display;view=camera;prefab=assemblyPrefab;flightTuning=tuning;BotMode=RangePracticeSettings.Mode;
            var visuals=gameObject.AddComponent<LaserVisualPool>();visuals.Initialize(rules.laserShader,rules.particleScale);projectiles=new ProjectileWorld();projectiles.Initialize(visuals);
            Flares=gameObject.AddComponent<FlareWorld>();Flares.Initialize(rules.particleScale);projectiles.Flares=Flares;Sensors=new TargetSensorWorld(rules);
            var hull=tuning.GetDefinition(BlockShape.Cube);
            destruction=gameObject.AddComponent<CombatVfxPool>();destruction.Initialize(rules.destructionShader,hull&&hull.material?hull.material:assemblyPrefab.BodyMaterial,rules.particleScale);
            Debris=gameObject.AddComponent<DebrisWorld>();Debris.Initialize(rules,destruction);Debris.SetPhysicsObservers(actors);
            projectiles.WeaponImpact+=(position,weapon)=>destruction.EmitWeaponHit(position,weapon);
            projectiles.MissileExplosion+=(position,weapon)=>destruction.EmitMissileExplosion(position,weapon);
            projectiles.MuzzleFlash+=(position,direction,weapon)=>destruction.EmitMuzzle(position,direction,weapon);
            projectiles.HitFeedbackConfirmed+=(owner,scenery)=>{if(player!=null&&owner==player.ship&&hud)hud.NotifyWeaponHit(scenery);};
            player=Register(runtime,controller,0);player.initialPosition=player.body.position;player.initialRotation=player.body.rotation;
            runtime.InvulnerableUntil=(float)runtime.CombatTime+Mathf.Max(0,rules.playerSpawnProtectionSeconds);
            var roster=new RangeTargetRoster(Starframe.App.AppNavigation.HasAppContext?Starframe.App.ShipWorkspace.BlueprintDirectory:System.IO.Path.Combine(Application.persistentDataPath,"Blueprints"));
            if(!mission)foreach(var plan in roster.Resolve(rules.defaultTargetBlueprint))SpawnTargets(plan.blueprint,plan.count);
            Sensors.Update(actors,0);
            Targeting=gameObject.AddComponent<TargetLockSystem>();Targeting.Initialize(this,camera,display,rules);if(hud)hud.BindCombat(this);
        }
        Actor Register(FlightShipRuntime runtime,ShipFlightController controller,int team)
        {
            runtime.Team=team;var a=new Actor{ship=runtime,flight=controller,body=runtime.GetComponent<Rigidbody>(),weapons=new ShipWeapons()};
            a.weapons.Initialize(runtime,projectiles);
            a.countermeasures=runtime.gameObject.AddComponent<ShipCountermeasureController>();a.countermeasures.Initialize(runtime,Flares,hud);
            projectiles.RegisterShields(runtime);
            runtime.gameObject.AddComponent<ShipShieldVisual>().Initialize(runtime);
            actors.Add(a);
            runtime.PartsDetached+=parts=>Debris.Spawn(parts,flightTuning,a.flight.Velocity,runtime.CorePhysicsPosition,runtime);
            runtime.PartChanged+=part=>{
                if(part.present||part.detached||!runtime.IsAlive||!part.view||part.record.shape==BlockShape.Core)return;
                var filter=part.view.GetComponent<MeshFilter>();var mesh=filter?filter.sharedMesh:null;if(!mesh)return;
                var definition=flightTuning.GetDefinition(part.record.shape);var material=definition?definition.material:null;
                var color=material&&material.HasProperty("_BaseColor")?material.GetColor("_BaseColor"):Color.white;
                destruction.EmitPartDestroyed(part.view.transform.TransformPoint(mesh.bounds.center),part.view.transform.rotation,Vector3.Scale(mesh.bounds.size,part.view.transform.lossyScale),a.flight.Velocity,color);
            };
            runtime.Died+=()=>{a.deathTime=clock;a.cleared=false;a.weapons.FireRequested=false;if(a.ship.Team!=0)BotKills++;destruction.EmitShipDestroyed(runtime.CorePhysicsPosition,1,a.flight.Velocity);ActorDied?.Invoke(a);};
            return a;
        }
        public void SpawnTargets(BlueprintData blueprint,int count)
        {
            if(IsMission)throw new System.InvalidOperationException("Mission enemies use their finite reinforcement queue.");
            if(count<0)throw new System.ArgumentOutOfRangeException(nameof(count));
            var validated=BlueprintRepository.Upgrade(blueprint);
            if(!validated.hasCore)throw new System.ArgumentException("靶机蓝图需要一个核心。",nameof(blueprint));
            for(int i=0;i<count;i++)bots.Add(CreateBot(validated,bots.Count));
            Sensors?.Update(actors,0);
        }
        Actor CreateBot(BlueprintData source,int slot)
        {
            long creationStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            var blueprint=BlueprintRepository.Upgrade(source);
            var root=new GameObject("RangeBot_"+(slot+1)+"_"+blueprint.name);root.transform.SetParent(transform,false);
            var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
            var assembly=Instantiate(prefab,root.transform);assembly.name="BotShip";assembly.Configure(flightTuning.database);
            assembly.Import(blueprint);
            var runtime=root.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,flightTuning,rules);
            Quaternion correction=Quaternion.Inverse(runtime.CoreRotation);assembly.transform.localRotation=correction;assembly.transform.localPosition=-(correction*runtime.CenterOfMass());
            foreach(var t in assembly.GetComponentsInChildren<Transform>(true))if(t.name=="ShipForwardRay"||t.name=="PlacementCollisionProbe"||t.name=="ImportCollisionProbe")t.gameObject.SetActive(false);
            body.mass=Mathf.Max(1,runtime.Performance.totalMass);runtime.PerformanceChanged+=()=>{if(body)body.mass=Mathf.Max(1,runtime.Performance.totalMass);};
            var flight=root.AddComponent<ShipFlightController>();flight.Initialize(body,runtime,flightTuning,view,runtime.HullRadius);flight.ExternalControl=true;
            var exhaust=root.AddComponent<ThrusterExhaust>();exhaust.Initialize(runtime,flight,flightTuning,view,rules.particleScale);
            var a=Register(runtime,flight,1);a.spawnSlot=slot;a.blueprintName=blueprint.name;a.pilot=new RangeBotPilot();a.weaponInput=new RangeBotWeaponInput();a.weaponInput.Initialize(a.weapons,runtime);
            Vector3 spawn=SpawnNearPlayer(a);var rotation=Quaternion.LookRotation(player.body.position-spawn,Vector3.up);flight.ResetMotion(spawn,rotation);a.initialPosition=spawn;a.initialRotation=rotation;
            MaximumBotCreationMilliseconds=System.Math.Max(MaximumBotCreationMilliseconds,(System.Diagnostics.Stopwatch.GetTimestamp()-creationStarted)*1000.0/System.Diagnostics.Stopwatch.Frequency);
            return a;
        }
        Vector3 SpawnNearPlayer(Actor self)
        {
            float radius=self.ship.HullRadius,maxRadius=radius;
            foreach(var a in actors)maxRadius=Mathf.Max(maxRadius,a.ship.HullRadius);
            float spacing=Mathf.Max(14,2*maxRadius+4),distance=Mathf.Max(rules.botSpawnDistance,player.ship.HullRadius+radius+8);
            float limit=Mathf.Max(1,flightTuning.arenaHalfExtent-radius-4);
            for(int i=0;i<128;i++){
                int slot=self.spawnSlot+i,column=slot%5,row=slot/5;
                int side=(column+1)/2*(column%2==0?1:-1);
                var offset=new Vector3(8+side*spacing,5+(row%3)*spacing,distance+(row/3)*spacing);
                var candidate=player.body.position+player.body.rotation*offset;
                if(i>=32)candidate=player.body.position+Random.onUnitSphere*(distance+spacing*(1+i/8));
                candidate=new Vector3(Mathf.Clamp(candidate.x,-limit,limit),Mathf.Clamp(candidate.y,-limit,limit),Mathf.Clamp(candidate.z,-limit,limit));
                if(SpawnIsClear(self,candidate))return candidate;
            }
            // A formation near a corner, or accumulated wrecks, can exhaust local positions while the rest of the map is clear.
            for(int i=0;i<256;i++){
                var candidate=new Vector3(Random.Range(-limit,limit),Random.Range(-limit,limit),Random.Range(-limit,limit));
                if(SpawnIsClear(self,candidate))return candidate;
            }
            return self.initialPosition;
        }
        bool SpawnIsClear(Actor self,Vector3 position)
        {
            float radius=self.ship.HullRadius;
            foreach(var a in actors)if(a!=self&&a.ship.IsAlive&&(position-a.body.position).sqrMagnitude<Mathf.Pow(radius+a.ship.HullRadius+3,2))return false;
            foreach(var wreck in Debris.Clusters)if(wreck&&wreck.WorldBounds.SqrDistance(position)<Mathf.Pow(radius+2,2))return false;
            return true;
        }
        public void SetBotMode(RangeBotMode mode,bool persist=true)
        {
            if(!System.Enum.IsDefined(typeof(RangeBotMode),mode))throw new System.ArgumentOutOfRangeException(nameof(mode));
            BotMode=mode;if(persist)RangePracticeSettings.Mode=mode;
            Sensors?.ClearEnemyLocks();
            foreach(var bot in bots){bot.pilot.Reset();bot.weaponInput.Reset();bot.fire=false;bot.flight.SetExternalInput(Vector3.zero,Vector2.zero);}
        }
        public void ResetTargets()
        {
            if(player==null||IsMission)return;
            Targeting?.Clear();
            Sensors?.ClearEnemyLocks();
            foreach(var bot in bots){
                projectiles.RemoveOwned(bot.ship);bot.ship.Respawn();bot.weapons.Reset();bot.pilot.Reset();bot.weaponInput.Reset();
                Vector3 position=SpawnNearPlayer(bot),direction=player.body.position-position;
                Quaternion rotation=direction.sqrMagnitude>.001f?Quaternion.LookRotation(direction,Vector3.up):bot.initialRotation;
                bot.flight.ResetMotion(position,rotation);bot.initialPosition=position;bot.initialRotation=rotation;
                bot.deathTime=-1;bot.cleared=false;bot.recoveryElapsed=0;bot.nextDecision=0;bot.fire=false;
            }
            Sensors?.Update(actors,0);
        }
        void Update()=>UpdatePlayerWeaponInput();
        void UpdatePlayerWeaponInput()
        {
            if(player==null)return;
            bool allowed=CombatEnabled&&player.ship.IsAlive&&Application.isFocused&&(!hud||!hud.ExitDialogOpen);
            WeaponGroupInputRouter.Apply(player.weapons,player.ship.BlueprintSnapshot,Targeting!=null&&Targeting.HasTarget,allowed);
        }
        void FixedUpdate()=>Step(Time.fixedDeltaTime);
        public void Step(float dt)
        {
            if(player==null||dt<=0||!CombatEnabled)return;clock+=dt;
            foreach(var a in actors)a.ship.BeginStep(dt);
            projectiles.Step(dt);
            foreach(var a in actors)UpdateActor(a,dt,a==player);
            Debris.Step(dt);
            Sensors?.Update(actors,dt);
            player.weapons.HasValidTargetLock=Targeting!=null&&Targeting.HasTarget;
            player.weapons.Target=player.weapons.HasValidTargetLock?Targeting.LockedTarget:null;
            if(sampledPlayerLife!=player.ship.LifeId||!player.ship.IsAlive){sampledPlayerLife=player.ship.LifeId;playerCoreMotion.Reset(player.ship.CorePhysicsPosition,clock,player.flight.Velocity);}
            else playerCoreMotion.Sample(player.ship.CorePhysicsPosition,clock,rules.leadVelocitySampleWindow,rules.leadVelocityResponse,player.flight.Velocity);
            using(AiMarker.Auto())foreach(var a in bots)
            {
                bool detected=Sensors!=null&&Sensors.IsDetected(a.ship,player.ship);
                a.pilot.Step(a,detected?player:null,actors,Debris.Clusters,rules,flightTuning,BotMode,playerCoreMotion.Velocity,clock,dt);
                bool hostile=BotMode==RangeBotMode.Combat||BotMode==RangeBotMode.StationaryFire;
                bool locked=detected&&hostile&&Sensors.UpdateEnemyLock(a.ship,player.ship,true,dt);
                if(!hostile||!detected)Sensors?.ClearEnemyLock(a.ship);
                a.weapons.Target=detected?player.ship:null;
                a.weaponInput.Apply(a.ship.IsAlive&&detected&&a.pilot.Fire,locked,clock);
                UpdateCountermeasures(a,hostile);
            }
            using(ExecutionMarker.Auto())foreach(var a in actors)Execute(a,dt);
            if(IsMission)RetireDefeatedEnemies();
        }
        void UpdateCountermeasures(Actor a,bool enabled)
        {
            if(!a.ship||!a.countermeasures)return;
            if(a.countermeasureLife!=a.ship.LifeId){
                a.countermeasureLife=a.ship.LifeId;a.incomingMissileSince=-1;a.nextCountermeasureCheck=0;
            }
            if(!enabled||!a.ship.IsAlive||a.countermeasures.LauncherCount==0){a.incomingMissileSince=-1;return;}
            if(clock<a.nextCountermeasureCheck)return;
            a.nextCountermeasureCheck=clock+Mathf.Max(.05f,rules.botPilotDecisionSeconds);
            if(!projectiles.TryGetIncomingThreat(a.ship,out float distance,out _)){a.incomingMissileSince=-1;return;}
            if(a.incomingMissileSince<0)a.incomingMissileSince=clock;
            if(clock-a.incomingMissileSince+.000001<Mathf.Max(0,rules.botReactionSeconds)||a.countermeasures.ReadyCount==0)return;
            float decoyRange=0;
            foreach(var part in a.ship.Parts)if(part.present&&part.hp>0){
                var definition=flightTuning.GetDefinition(part.record.shape);
                if(definition&&definition.countermeasure&&a.countermeasures.GetCooldownRemaining(part.record.id)<=0)
                    decoyRange=Mathf.Max(decoyRange,definition.countermeasure.decoyRadius);
            }
            // Detect the real incoming missile early, but save each launcher until its burst can reach it.
            // The controller still checks surviving parts, muzzle clearance and its independent cooldown.
            if(decoyRange>0&&distance<=decoyRange+a.ship.HullRadius&&a.countermeasures.TryDeploy())a.incomingMissileSince=clock;
        }
        void UpdateActor(Actor a,float dt,bool isPlayer)
        {
            a.ship.FlushChanges();
            if(!a.ship.IsAlive){
                if(!a.cleared){projectiles.RemoveOwned(a.ship);a.weapons.Reset();a.cleared=true;}
                if(IsMission)return;
                float delay=isPlayer?rules.playerRespawnSeconds:rules.botRespawnSeconds;
                if(clock-a.deathTime>=delay){
                    Vector3 position=isPlayer&&SpawnIsClear(a,a.initialPosition)?a.initialPosition:SpawnNearPlayer(a);
                    Quaternion rotation=isPlayer?a.initialRotation:Quaternion.LookRotation(player.body.position-position,Vector3.up);
                    a.flight.ResetMotion(position,rotation);a.ship.Respawn();a.weapons.Reset();a.deathTime=-1;a.cleared=false;a.recoveryElapsed=0;
                    a.ship.InvulnerableUntil=(float)a.ship.CombatTime+(isPlayer?rules.playerSpawnProtectionSeconds:0);
                    if(isPlayer){var chase=view.GetComponent<FlightChaseCamera>();if(chase)chase.ResetFollow();}
                    else{a.nextDecision=0;a.pilot.Reset();a.weaponInput.Reset();}
                }return;
            }
            a.recoveryElapsed+=dt;if(a.recoveryElapsed>=rules.recoveryTick){float elapsed=a.recoveryElapsed;a.recoveryElapsed=0;a.ship.AdvanceRecovery(elapsed);}
        }
        void Execute(Actor a,float dt)
        {
            float weaponPower=a.weapons.Prepare();
            float shieldPower=a.ship.Shields.PrepareRecovery(dt);
            float boostPower=a.flight.RequestedBoost?Mathf.Max(0,flightTuning.boostPowerWatts):0;
            float ratio=a.ship.IsAlive?a.ship.Power.Allocate(dt,weaponPower+boostPower+shieldPower,rules.unlimitedRangePower):0;
            a.ship.Shields.ExecuteRecovery(ratio);
            a.flight.SetBoostPowerRatio(ratio);a.weapons.Execute(dt,ratio);
        }
        public void ConfigureMission(){IsMission=true;BotMode=RangeBotMode.Combat;}
        readonly Collider[] spawnOverlaps=new Collider[32];
        public Actor SpawnMissionEnemy(EnemyArchetypeDefinition type,int serial,float distance,float variation,int wave)
        {
            if(!IsMission||!type||!type.blueprint||player==null||!player.ship.IsAlive)return null;
            var blueprint=type.CreateBlueprint(wave==5);
            float radius=5;foreach(var p in blueprint.parts)radius=Mathf.Max(radius,p.position.magnitude+4);
            float limit=Mathf.Max(50,flightTuning.arenaHalfExtent-radius-24);
            Vector3 chosen=Vector3.zero;bool found=false;
            for(int trial=0;trial<40;trial++){
                float angle=(serial*2.399963f+wave*.83f+trial*.47f);
                var radial=new Vector3(Mathf.Sin(angle),Mathf.Sin(angle*.63f)*.25f,Mathf.Cos(angle));
                var candidate=player.body.position+radial.normalized*(distance+variation*((serial+trial)%7)/6f);
                for(int axis=0;axis<3;axis++)candidate[axis]=Mathf.Clamp(candidate[axis],-limit,limit);
                // Keep arriving craft in the open space above the mining base.
                candidate.y=Mathf.Max(-850,candidate.y);
                bool clear=true;foreach(var a in actors)if(a.ship.IsAlive&&(candidate-a.body.position).sqrMagnitude<Mathf.Pow(radius+a.ship.HullRadius+30,2)){clear=false;break;}
                if(!clear)continue;
                if(Physics.OverlapSphereNonAlloc(candidate,radius+4,spawnOverlaps,~((1<<2)|(1<<5)),QueryTriggerInteraction.Ignore)>0)continue;
                chosen=candidate;found=true;break;
            }
            if(!found)return null;
            var actor=CreateBot(blueprint,serial);bots.Add(actor);actor.archetype=type;actor.pilot.SetBehavior(type.behavior);actor.pilot.SetAttackPermission(false);
            var direction=player.body.position-chosen;var rotation=Quaternion.LookRotation(direction.normalized,Vector3.up);
            actor.flight.ResetMotion(chosen,rotation);actor.initialPosition=chosen;actor.initialRotation=rotation;
            Sensors?.Update(actors,0);return actor;
        }
        void RetireDefeatedEnemies()
        {
            // Detached living pieces already belong to DebrisWorld. Remove only the empty combat shell.
            for(int i=bots.Count-1;i>=0;i--){var a=bots[i];if(a.ship.IsAlive||clock-a.deathTime<.25)continue;
                projectiles.RemoveOwned(a.ship);projectiles.UnregisterShields(a.ship);a.weaponInput.Reset();
                actors.Remove(a);bots.RemoveAt(i);Destroy(a.ship.gameObject);
            }
        }
        void OnDestroy(){projectiles?.Clear();Sensors?.Dispose();}
    }
}

