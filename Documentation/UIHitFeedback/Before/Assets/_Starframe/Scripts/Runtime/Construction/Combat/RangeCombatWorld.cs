using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Starframe.Construction;
using Starframe.Flight;
namespace Starframe.Combat
{
    [DefaultExecutionOrder(-200)]
    public sealed class RangeCombatWorld:MonoBehaviour
    {
        public sealed class Actor
        {
            public FlightShipRuntime ship;public ShipFlightController flight;public Rigidbody body;public ShipWeapons weapons;
            public double deathTime=-1;public float recoveryElapsed;public bool cleared;public Vector3 initialPosition;public Quaternion initialRotation;
            public int spawnSlot;public string blueprintName;public Vector3 waypoint,look;public float nextDecision;public bool aimingAtPlayer,fire;
        }
        Actor player;BattleTuning rules;RangeHUD hud;Camera view;double clock;
        readonly List<Actor> actors=new List<Actor>();CombatVfxPool destruction;
        readonly List<Actor> bots=new List<Actor>();
        public IReadOnlyList<Actor> Actors=>actors;
        public IReadOnlyList<Actor> Bots=>bots;
        public TargetLockSystem Targeting{get;private set;}
        ProjectileWorld projectiles;ShipAssembly prefab;FlightTuning flightTuning;
        public Actor Player=>player;
        public Actor Bot=>bots.Count>0?bots[0]:null;
        public ProjectileWorld Projectiles=>projectiles;
        public DebrisWorld Debris{get;private set;}
        public int BotKills{get;private set;}
        public float PlayerRespawnRemaining=>player==null||player.ship.IsAlive?0:Mathf.Max(0,rules.playerRespawnSeconds-(float)(clock-player.deathTime));
        public float BotRespawnRemaining=>Bot==null||Bot.ship.IsAlive?0:Mathf.Max(0,rules.botRespawnSeconds-(float)(clock-Bot.deathTime));
        public void Initialize(FlightShipRuntime runtime,ShipFlightController controller,ShipAssembly assemblyPrefab,FlightTuning tuning,BattleTuning battle,Camera camera,RangeHUD display)
        {
            rules=battle;hud=display;view=camera;prefab=assemblyPrefab;flightTuning=tuning;
            var visuals=gameObject.AddComponent<LaserVisualPool>();visuals.Initialize(rules.laserShader,rules.particleScale);projectiles=new ProjectileWorld();projectiles.Initialize(visuals);
            var hull=tuning.GetDefinition(BlockShape.Cube);
            destruction=gameObject.AddComponent<CombatVfxPool>();destruction.Initialize(rules.destructionShader,hull&&hull.material?hull.material:assemblyPrefab.BodyMaterial,rules.particleScale);
            Debris=gameObject.AddComponent<DebrisWorld>();Debris.Initialize(rules,destruction);
            projectiles.Impact+=position=>destruction.EmitLaserHit(position);
            player=Register(runtime,controller,0);player.initialPosition=player.body.position;player.initialRotation=player.body.rotation;
            var roster=new RangeTargetRoster(System.IO.Path.Combine(Application.persistentDataPath,"Blueprints"));
            foreach(var plan in roster.Resolve(rules.defaultTargetBlueprint))SpawnTargets(plan.blueprint,plan.count);
            Targeting=gameObject.AddComponent<TargetLockSystem>();Targeting.Initialize(this,camera,display,rules);hud.BindCombat(this);
        }
        Actor Register(FlightShipRuntime runtime,ShipFlightController controller,int team)
        {
            runtime.Team=team;var a=new Actor{ship=runtime,flight=controller,body=runtime.GetComponent<Rigidbody>(),weapons=new ShipWeapons()};
            a.weapons.Initialize(runtime,projectiles);
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
            runtime.Died+=()=>{a.deathTime=clock;a.cleared=false;a.weapons.FireRequested=false;if(a.ship.Team!=0)BotKills++;destruction.EmitShipDestroyed(runtime.CorePhysicsPosition,1,a.flight.Velocity);};
            return a;
        }
        public void SpawnTargets(BlueprintData blueprint,int count)
        {
            if(count<0)throw new System.ArgumentOutOfRangeException(nameof(count));
            var validated=BlueprintRepository.Upgrade(blueprint);
            if(!validated.hasCore)throw new System.ArgumentException("靶机蓝图需要一个核心。",nameof(blueprint));
            for(int i=0;i<count;i++)bots.Add(CreateBot(validated,bots.Count));
        }
        Actor CreateBot(BlueprintData source,int slot)
        {
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
            var a=Register(runtime,flight,1);a.spawnSlot=slot;a.blueprintName=blueprint.name;
            Vector3 spawn=SpawnNearPlayer(a);var rotation=Quaternion.LookRotation(player.body.position-spawn,Vector3.up);flight.ResetMotion(spawn,rotation);a.initialPosition=spawn;a.initialRotation=rotation;
            return a;
        }
        Vector3 SpawnNearPlayer(Actor self)
        {
            float radius=self.ship.HullRadius,maxRadius=radius;
            foreach(var a in actors)maxRadius=Mathf.Max(maxRadius,a.ship.HullRadius);
            float spacing=Mathf.Max(14,2*maxRadius+4),distance=Mathf.Max(rules.botSpawnDistance,player.ship.HullRadius+radius+8);
            float limit=flightTuning.arenaHalfExtent-radius-4;
            for(int i=0;i<128;i++){
                int slot=self.spawnSlot+i,column=slot%5,row=slot/5;
                int side=(column+1)/2*(column%2==0?1:-1);
                var offset=new Vector3(8+side*spacing,5+(row%3)*spacing,distance+(row/3)*spacing);
                var candidate=player.body.position+player.body.rotation*offset;
                if(i>=32)candidate=player.body.position+Random.onUnitSphere*(distance+spacing*(1+i/8));
                candidate=new Vector3(Mathf.Clamp(candidate.x,-limit,limit),Mathf.Clamp(candidate.y,-limit,limit),Mathf.Clamp(candidate.z,-limit,limit));
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
        void FixedUpdate()=>Step(Time.fixedDeltaTime);
        public void Step(float dt)
        {
            if(player==null||dt<=0)return;clock+=dt;
            foreach(var a in actors)a.ship.BeginStep(dt);
            projectiles.Step(dt);
            foreach(var a in actors)UpdateActor(a,dt,a==player);
            Debris.Step(dt);
            player.weapons.FireRequested=player.ship.IsAlive&&Application.isFocused&&(!hud||!hud.ExitDialogOpen)&&Mouse.current!=null&&Mouse.current.leftButton.isPressed;
            foreach(var a in bots){BotIntent(a);a.weapons.FireRequested=a.ship.IsAlive&&a.fire;}
            foreach(var a in actors)Execute(a,dt);
        }
        void UpdateActor(Actor a,float dt,bool isPlayer)
        {
            a.ship.FlushChanges();
            if(!a.ship.IsAlive){
                if(!a.cleared){projectiles.RemoveOwned(a.ship);a.weapons.Reset();a.cleared=true;}
                float delay=isPlayer?rules.playerRespawnSeconds:rules.botRespawnSeconds;
                if(clock-a.deathTime>=delay){
                    Vector3 position=isPlayer&&SpawnIsClear(a,a.initialPosition)?a.initialPosition:SpawnNearPlayer(a);
                    Quaternion rotation=isPlayer?a.initialRotation:Quaternion.LookRotation(player.body.position-position,Vector3.up);
                    a.flight.ResetMotion(position,rotation);a.ship.Respawn();a.weapons.Reset();a.deathTime=-1;a.cleared=false;a.recoveryElapsed=0;
                    a.ship.InvulnerableUntil=(float)a.ship.CombatTime+(isPlayer?rules.playerSpawnProtectionSeconds:0);
                    if(isPlayer){var chase=view.GetComponent<FlightChaseCamera>();if(chase)chase.ResetFollow();}
                    else a.nextDecision=0;
                }return;
            }
            a.recoveryElapsed+=dt;if(a.recoveryElapsed>=rules.recoveryTick){float elapsed=a.recoveryElapsed;a.recoveryElapsed=0;a.ship.AdvanceRecovery(elapsed);}
        }
        void BotIntent(Actor bot)
        {
            if(!bot.ship.IsAlive){bot.flight.SetExternalInput(Vector3.zero,Vector2.zero);bot.fire=false;return;}
            if(clock>=bot.nextDecision){
                bot.nextDecision=(float)clock+Mathf.Max(.1f,rules.botDecisionSeconds);bot.waypoint=player.body.position+Random.onUnitSphere*Random.Range(20,45);
                bot.aimingAtPlayer=player.ship.IsAlive&&Random.value<rules.botAimProbability;bot.look=Random.onUnitSphere;bot.fire=Random.value>.3f;
            }
            Vector3 desiredVelocity=(bot.waypoint-bot.body.position).normalized*Mathf.Max(0,rules.botMovementSpeed);
            var inverse=Quaternion.Inverse(bot.body.rotation);Vector3 move=inverse*(desiredVelocity-bot.flight.Velocity)/3;
            move=new Vector3(Mathf.Clamp(move.x,-1,1),Mathf.Clamp(move.y,-1,1),Mathf.Clamp(move.z,-1,1));
            Vector3 desiredDirection=bot.look;
            if(bot.aimingAtPlayer&&player.ship.IsAlive){
                Vector3 muzzle=bot.body.position;
                WeaponDefinition weapon=null;
                foreach(var part in bot.ship.Parts){var d=flightTuning.GetDefinition(part.record.shape);if(part.present&&d&&d.weapon){muzzle=CylindricalHitQuery.PhysicsMatrix(part.view.GetComponent<Collider>()).MultiplyPoint3x4(d.muzzleLocalPosition);weapon=d.weapon;break;}}
                var point=player.body.position;
                if(weapon)InterceptSolver.TrySolve(muzzle,point,player.flight.Velocity,weapon.projectileSpeed,weapon.range,out point,out _);
                desiredDirection=(point-muzzle).normalized;
            }
            var error=Quaternion.FromToRotation(bot.body.rotation*Vector3.forward,desiredDirection);error.ToAngleAxis(out float degrees,out var axis);
            if(degrees>180)degrees-=360;if(float.IsNaN(axis.x))axis=Vector3.zero;var local=(inverse*axis)*degrees;
            float rate=Mathf.Max(1,Mathf.Min(bot.ship.Performance.angularRate,rules.botTurnDegreesPerSecond));
            var aim=new Vector2(local.y,-local.x)/Mathf.Max(1,rate*.35f);
            bot.flight.SetExternalInput(move,Vector2.ClampMagnitude(aim,rate/Mathf.Max(1,bot.ship.Performance.angularRate)));
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
        void OnDestroy(){projectiles?.Clear();}
    }
}
