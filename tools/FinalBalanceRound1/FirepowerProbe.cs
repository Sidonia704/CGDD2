// Stopped-Editor asynchronous native firepower fixture. No asset, blueprint or player-save writes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class FinalBalanceFirepowerProbe
{
    const string Evidence="Documentation/FinalBalanceRound1-2026-11-03";
    const string ReportPath=Evidence+"/three-t4-firepower.json";
    const string FixtureName="__FinalBalanceThreeT4Firepower";
    const float Dt=.02f,Horizon=1200,Distance=600;
    static Job job;

    public sealed class WeaponRow
    {
        public string weapon,delivery;public int gunsAcrossThreeShips,muzzleEvents;
        public float authoredVolleysPerSecond,authoredDamage,categoryScaledDamagePerVolley,range,watts,reloadSeconds,lockSeconds;
        public float emittedVolleys,emittedNominalDamage,measuredVolleysPerGunSecond,firstEmissionAt=-1,lastEmissionAt=-1;
    }
    public sealed class AttackerRow
    {
        public string name,aimReferencePartId;public int parts,shots,peakActiveGuns,peakBlockedGuns;
        public float cost,initialCoreDistance,generation,capacity,initialEnergy,minimumEnergy,finalEnergy,peakRequestedWatts,meanRequestedWatts,meanPowerRatio,meanBlockedGuns,allocatedEnergy;
        internal float ratioIntegral,demandIntegral,blockedIntegral,allocationSeconds;
    }
    public sealed class CaseRow
    {
        public string target,attackerSha256,targetSha256,status="running",error;
        public float simulatedSeconds,wallSeconds,firstEffectiveHitAt=-1,firstHullHitAt=-1,firstZeroShieldAt=-1,coreDeathAt=-1,deathSecondsAfterFirstEffectiveHit=-1;
        public bool targetAlive;public int targetParts,remainingPresentParts,targetRebuilds,shots,spawnedProjectilesAndBeamPulses,damageConfirmations;
        public float targetCost,initialHullHp,remainingHullHp,initialShieldCharge,remainingShieldCharge,hullDamage,shieldChargeLostIncludingGeneratorCollapse,shieldChargeRecovered,hullHpRestored;
        public List<AttackerRow> attackers=new List<AttackerRow>();public List<WeaponRow> weapons=new List<WeaponRow>();
    }
    public sealed class Report
    {
        public string status="running",error,startedUtc=DateTime.UtcNow.ToString("O"),completedUtc;
        public string note="Three exact approved t4 ships (Team0/player damage faction) fire continuously through native ShipWeapons.Prepare, separate finite ShipPowerBus allocations and Execute into one shared ProjectileWorld bound to the installed native CombatDamageTuning. Static kinematic ships at 600 m; the representative direct-gun axis of each complete attacker is initially aimed at the stationary target core. Target yaw is 180 degrees. Native shield and structural recovery remain active. No terrain, target return fire, flight, boost, human aim, countermeasure deployment, physical detached wrecks or respawn. Native collision, clearance, missile lock/reload and power cadence remain. This is an isolated one-way baseline comparison, not proof of human victory or mission duration. Shield loss includes generator collapse; emitted nominal damage includes misses and is not effective hull DPS. Simulation advances BeginStep and synchronizes collider transforms without advancing global scene physics.";
        public string damageAsset;public float fixedDt=Dt,horizonPerCase=Horizon,distance=Distance,nonMissileMultiplier,missileMultiplier,configuredEnemyMultiplier,appliedAttackerFactionMultiplier=1,blastBudgetMultiplier,recoveryQuietSeconds;
        public bool unlimitedPower=false;public int attackerCount=3,attackerTeam=0,targetTeam=1;
        public List<CaseRow> cases=new List<CaseRow>();
    }
    sealed class Ship
    {
        public GameObject root;public FlightShipRuntime runtime;public ShipWeapons weapons;public AttackerRow row;
    }
    sealed class Scope:IDisposable
    {
        readonly ModuleDatabase previousDatabase=BlockModules.ConfiguredDatabase;
        readonly UnityEngine.Random.State previousRandom=UnityEngine.Random.state;
        readonly Scene previousScene=SceneManager.GetActiveScene();
        readonly List<Object> owned=new List<Object>();
        readonly Scene scene;
        public Scope()
        {
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);scene.name=FixtureName;
            if(previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);
            UnityEngine.Random.InitState(261103);
        }
        public T Own<T>(T value)where T:Object{owned.Add(value);return value;}
        public GameObject Root(string name,Vector3 position)
        {
            var root=Own(new GameObject(name));root.hideFlags=HideFlags.HideAndDontSave;root.transform.position=position;
            SceneManager.MoveGameObjectToScene(root,scene);return root;
        }
        public void Dispose()
        {
            try
            {
                for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);
                if(SceneManager.GetActiveScene()==scene&&previousScene.IsValid()&&previousScene.isLoaded)SceneManager.SetActiveScene(previousScene);
                if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
                Physics.SyncTransforms();
            }
            finally{BlockModules.Configure(previousDatabase);UnityEngine.Random.state=previousRandom;}
        }
    }
    sealed class Job
    {
        public readonly Report report=new Report();public readonly Stopwatch overall=Stopwatch.StartNew();
        public readonly string[] targets={"t4","t6","t7"};
        public Scope scope;public FlightTuning tuning;public BattleTuning rules;public ProjectileWorld world;
        public Ship target;public readonly List<Ship> attackers=new List<Ship>();
        public readonly Dictionary<WeaponDefinition,WeaponRow> weapons=new Dictionary<WeaponDefinition,WeaponRow>();
        public CaseRow row;public int caseIndex,step;public float recoveryElapsed,nextWrite=5;public double caseStarted;
    }

    public static string Main()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run the firepower probe in stopped Editor mode.");
        if(job!=null||SceneManager.GetSceneByName(FixtureName).IsValid())throw new InvalidOperationException("A firepower fixture is already active.");
        Directory.CreateDirectory(Evidence);job=new Job();
        EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=PlayState;AssemblyReloadEvents.beforeAssemblyReload+=Cancel;
        Write();return "Started native three-t4 firepower comparison asynchronously; read "+ReportPath+". Cases t4/t6/t7, maximum 1200 simulated seconds each, 8 ms stepping chunks; start Play or call Cancel to stop and clean up.";
    }
    public static string Status()=>File.Exists(ReportPath)?File.ReadAllText(ReportPath):"Not started.";
    public static void Cancel(){if(job!=null)Finish("cancelled",null);}
    static void PlayState(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingEditMode)Cancel();}
    static void Tick()
    {
        if(job==null)return;
        try
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode){Cancel();return;}
            if(job.scope==null){StartCase();Write();return;}
            var chunk=Stopwatch.StartNew();
            while(job!=null&&job.scope!=null&&chunk.Elapsed.TotalMilliseconds<8)
            {
                Step();
                if(!job.target.runtime.IsAlive||job.step*Dt>=Horizon||job.overall.Elapsed.TotalSeconds-job.caseStarted>180)
                {
                    CompleteCase(!job.target.runtime.IsAlive?"core-destroyed":job.step*Dt>=Horizon?"survived-horizon":"wall-time-limit");
                    return;
                }
            }
            if(job!=null&&job.row.simulatedSeconds>=job.nextWrite){job.nextWrite=job.row.simulatedSeconds+5;Snapshot();Write();}
        }
        catch(Exception error){Finish("failed",error.ToString());}
    }
    static void StartCase()
    {
        job.scope=new Scope();
        job.tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
        var nativeRules=AssetDatabase.LoadAssetAtPath<BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");
        if(!job.tuning||!job.tuning.database||!nativeRules||!nativeRules.damageTuning)throw new InvalidOperationException("Install native flight/battle/damage tuning before this probe.");
        job.rules=job.scope.Own(Object.Instantiate(nativeRules));job.rules.unlimitedRangePower=false;
        job.world=new ProjectileWorld{DamageTuning=nativeRules.damageTuning,EnemyDamageMultiplier=nativeRules.enemyWeaponDamageMultiplier,MissileBlastBudgetMultiplier=nativeRules.missileBlastBudgetMultiplier};
        job.world.Initialize(null);
        var r=job.report;r.damageAsset=AssetDatabase.GetAssetPath(nativeRules.damageTuning);r.nonMissileMultiplier=nativeRules.damageTuning.nonMissileDamageMultiplier;
        r.missileMultiplier=nativeRules.damageTuning.missileDamageMultiplier;r.configuredEnemyMultiplier=nativeRules.enemyWeaponDamageMultiplier;
        r.blastBudgetMultiplier=nativeRules.missileBlastBudgetMultiplier;r.recoveryQuietSeconds=nativeRules.outOfCombatDelay;
        var attackerData=Read("t4");var targetData=Read(job.targets[job.caseIndex]);
        job.row=new CaseRow{target=job.targets[job.caseIndex],attackerSha256=Hash("t4"),targetSha256=Hash(job.targets[job.caseIndex]),targetParts=targetData.parts.Count};
        r.cases.Add(job.row);job.attackers.Clear();job.weapons.Clear();job.step=0;job.recoveryElapsed=0;job.nextWrite=5;job.caseStarted=job.overall.Elapsed.TotalSeconds;
        Vector3 origin=new Vector3(30000,30000,30000);
        job.target=Create("Target "+job.row.target,targetData,origin+Vector3.forward*Distance,1);
        job.target.root.transform.rotation=Quaternion.Euler(0,180,0);Physics.SyncTransforms();
        job.row.targetCost=Cost(job.target.runtime);job.row.initialHullHp=HullHp(job.target.runtime);job.row.initialShieldCharge=job.target.runtime.Shields.Charge;
        job.target.runtime.PartChanged+=part=>{if(part.present)job.row.targetRebuilds++;};
        for(int i=0;i<3;i++)
        {
            var ship=Create("Player baseline t4 #"+(i+1),attackerData,origin+Vector3.right*((i-1)*90),0);
            ship.row=new AttackerRow{name=ship.root.name,parts=ship.runtime.Parts.Count,cost=Cost(ship.runtime),generation=ship.runtime.Power.Generation,capacity=ship.runtime.Power.Capacity,initialEnergy=ship.runtime.Power.Energy,minimumEnergy=ship.runtime.Power.Energy};
            ship.row.aimReferencePartId=AimAt(ship,job.target.runtime.CorePhysicsPosition);
            ship.row.initialCoreDistance=Vector3.Distance(ship.runtime.CorePhysicsPosition,job.target.runtime.CorePhysicsPosition);
            ship.weapons.Target=job.target.runtime;ship.weapons.HasValidTargetLock=true;ship.weapons.FireRequested=true;
            job.attackers.Add(ship);job.row.attackers.Add(ship.row);
            foreach(var part in ship.runtime.Parts)
            {
                var module=job.tuning.GetDefinition(part.record.shape);if(!module||!module.weapon)continue;
                var weapon=module.weapon;
                if(!job.weapons.TryGetValue(weapon,out var weaponRow))
                {
                    weaponRow=new WeaponRow{weapon=weapon.name,delivery=weapon.delivery.ToString(),authoredVolleysPerSecond=weapon.shotsPerSecond,authoredDamage=weapon.damage,
                        categoryScaledDamagePerVolley=weapon.damage*job.world.DamageTuning.GetMultiplier(weapon.delivery),range=weapon.range,watts=weapon.sustainedPowerWatts,reloadSeconds=weapon.reloadSeconds,lockSeconds=weapon.missileLockSeconds};
                    job.weapons.Add(weapon,weaponRow);job.row.weapons.Add(weaponRow);
                }
                weaponRow.gunsAcrossThreeShips++;
            }
        }
        job.world.MuzzleFlash+=(position,direction,weapon)=>
        {
            if(!job.weapons.TryGetValue(weapon,out var row))return;
            int emitted=weapon.delivery==WeaponDelivery.GuidedMissile&&weapon.missileLaunchOffsets!=null&&weapon.missileLaunchOffsets.Length>0?weapon.missileLaunchOffsets.Length:1;
            row.muzzleEvents++;row.emittedVolleys+=1f/emitted;row.emittedNominalDamage+=row.categoryScaledDamagePerVolley/emitted;
            float time=(job.step+1)*Dt;if(row.firstEmissionAt<0)row.firstEmissionAt=time;row.lastEmissionAt=time;
        };
        job.world.HitConfirmed+=owner=>{foreach(var ship in job.attackers)if(ship.runtime==owner){job.row.damageConfirmations++;break;}};
        Physics.SyncTransforms();Snapshot();
    }
    static void Step()
    {
        float time=(job.step+1)*Dt;job.target.runtime.BeginStep(Dt);foreach(var ship in job.attackers)ship.runtime.BeginStep(Dt);
        float before=job.target.runtime.Shields.Charge;job.world.Step(Dt);LostShield(before);Observe(time);
        job.target.runtime.FlushChanges();foreach(var ship in job.attackers)ship.runtime.FlushChanges();
        job.recoveryElapsed+=Dt;
        if(job.recoveryElapsed+.000001f>=Mathf.Max(Dt,job.rules.recoveryTick))
        {
            float hp=HullHp(job.target.runtime);job.target.runtime.AdvanceRecovery(job.recoveryElapsed);job.row.hullHpRestored+=Mathf.Max(0,HullHp(job.target.runtime)-hp);
            foreach(var ship in job.attackers)ship.runtime.AdvanceRecovery(job.recoveryElapsed);job.recoveryElapsed=0;
        }
        if(job.target.runtime.IsAlive)
        {
            float demand=job.target.runtime.Shields.PrepareRecovery(Dt);float ratio=job.target.runtime.Power.Allocate(Dt,demand,false);
            before=job.target.runtime.Shields.Charge;job.target.runtime.Shields.ExecuteRecovery(ratio);job.row.shieldChargeRecovered+=Mathf.Max(0,job.target.runtime.Shields.Charge-before);
            foreach(var ship in job.attackers)
            {
                demand=ship.weapons.Prepare()+ship.runtime.Shields.PrepareRecovery(Dt);ratio=ship.runtime.Power.Allocate(Dt,demand,false);
                ship.runtime.Shields.ExecuteRecovery(ratio);var row=ship.row;row.allocationSeconds+=Dt;row.ratioIntegral+=ratio*Dt;row.demandIntegral+=demand*Dt;row.blockedIntegral+=ship.weapons.BlockedGuns*Dt;
                row.allocatedEnergy+=demand*ratio*Dt;row.minimumEnergy=Mathf.Min(row.minimumEnergy,ship.runtime.Power.Energy);row.peakRequestedWatts=Mathf.Max(row.peakRequestedWatts,demand);
                row.peakActiveGuns=Mathf.Max(row.peakActiveGuns,ship.weapons.ActiveGuns);row.peakBlockedGuns=Mathf.Max(row.peakBlockedGuns,ship.weapons.BlockedGuns);
                before=job.target.runtime.Shields.Charge;ship.weapons.Execute(Dt,ratio);LostShield(before);Observe(time);
                if(!job.target.runtime.IsAlive)break;
            }
        }
        job.target.runtime.FlushChanges();Physics.SyncTransforms();job.step++;job.row.simulatedSeconds=job.step*Dt;
    }
    static void LostShield(float before)=>job.row.shieldChargeLostIncludingGeneratorCollapse+=Mathf.Max(0,before-job.target.runtime.Shields.Charge);
    static void Observe(float time)
    {
        var row=job.row;var target=job.target.runtime;
        if(row.firstEffectiveHitAt<0&&(target.LastDamageTime>=0||target.DamageTaken>0||target.Shields.Charge<row.initialShieldCharge-.001f))row.firstEffectiveHitAt=time;
        if(row.firstHullHitAt<0&&target.DamageTaken>0)row.firstHullHitAt=time;
        if(row.firstZeroShieldAt<0&&row.initialShieldCharge>0&&target.Shields.Charge<=.0001f)row.firstZeroShieldAt=time;
        if(row.coreDeathAt<0&&!target.IsAlive){row.coreDeathAt=time;if(row.firstEffectiveHitAt>=0)row.deathSecondsAfterFirstEffectiveHit=time-row.firstEffectiveHitAt;}
    }
    static void Snapshot()
    {
        var row=job.row;var target=job.target.runtime;row.wallSeconds=(float)(job.overall.Elapsed.TotalSeconds-job.caseStarted);
        row.targetAlive=target.IsAlive;row.remainingHullHp=HullHp(target);row.remainingShieldCharge=target.Shields.Charge;row.hullDamage=target.DamageTaken;
        row.remainingPresentParts=0;foreach(var part in target.Parts)if(part.present)row.remainingPresentParts++;
        row.shots=0;row.spawnedProjectilesAndBeamPulses=job.world.TotalSpawned;
        foreach(var ship in job.attackers)
        {
            var a=ship.row;a.shots=ship.weapons.ShotsFired;row.shots+=a.shots;a.finalEnergy=ship.runtime.Power.Energy;
            float seconds=Mathf.Max(Dt,a.allocationSeconds);a.meanPowerRatio=a.ratioIntegral/seconds;a.meanRequestedWatts=a.demandIntegral/seconds;a.meanBlockedGuns=a.blockedIntegral/seconds;
        }
        foreach(var weapon in row.weapons)weapon.measuredVolleysPerGunSecond=weapon.emittedVolleys/Mathf.Max(Dt,row.simulatedSeconds)/Mathf.Max(1,weapon.gunsAcrossThreeShips);
    }
    static void CompleteCase(string status)
    {
        Snapshot();job.row.status=status;job.world.Clear();job.world=null;job.scope.Dispose();job.scope=null;job.caseIndex++;
        if(job.caseIndex>=job.targets.Length)Finish("complete",null);else Write();
    }
    static void Finish(string status,string error)
    {
        if(job==null)return;var ending=job;
        EditorApplication.update-=Tick;EditorApplication.playModeStateChanged-=PlayState;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;
        try
        {
            if(ending.row!=null&&ending.scope!=null){try{Snapshot();}catch{}ending.row.status=status;ending.row.error=error;}
            if(ending.world!=null)ending.world.Clear();if(ending.scope!=null)ending.scope.Dispose();
        }
        finally
        {
            ending.report.status=status;ending.report.error=error;ending.report.completedUtc=DateTime.UtcNow.ToString("O");
            try{Write();}finally{job=null;}
        }
    }
    static Ship Create(string label,BlueprintData data,Vector3 position,int team)
    {
        var root=job.scope.Root(label,position);var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
        var assemblyObject=new GameObject("Assembly");assemblyObject.transform.SetParent(root.transform,false);
        var assembly=assemblyObject.AddComponent<ShipAssembly>();assembly.Configure(job.tuning.database);assembly.Import(data);
        var ship=root.AddComponent<FlightShipRuntime>();ship.Initialize(assembly,assembly.Export(data.name),job.tuning,job.rules);ship.Team=team;
        Quaternion correction=Quaternion.Inverse(ship.CoreRotation);assembly.transform.localRotation=correction;assembly.transform.localPosition=-(correction*ship.CenterOfMass());
        foreach(var t in assembly.GetComponentsInChildren<Transform>(true))if(t.name=="ShipForwardRay"||t.name=="PlacementCollisionProbe"||t.name=="ImportCollisionProbe")t.gameObject.SetActive(false);
        var weapons=new ShipWeapons();weapons.Initialize(ship,job.world,data);job.world.RegisterShields(ship);Physics.SyncTransforms();
        return new Ship{root=root,runtime=ship,weapons=weapons};
    }
    static string AimAt(Ship attacker,Vector3 point)
    {
        attacker.weapons.Prepare(); // Populate native own-hull blockage before choosing a representative firing axis.
        var profiles=new List<WeaponAimProfile>();string partId=null;
        for(int iteration=0;iteration<3;iteration++)
        {
            attacker.weapons.GetAimProfiles(profiles);if(profiles.Count==0)return null;
            bool direct=profiles.Exists(p=>p.weapon.delivery==WeaponDelivery.Projectile);int chosen=0;float best=-2;
            for(int i=0;i<profiles.Count;i++){if(direct&&profiles[i].weapon.delivery!=WeaponDelivery.Projectile)continue;float facing=Vector3.Dot(profiles[i].direction,attacker.root.transform.forward);if(facing>best){best=facing;chosen=i;}}
            var profile=profiles[chosen];partId=profile.partId;Vector3 desired=point-profile.origin;if(desired.sqrMagnitude<.000001f)break;
            attacker.root.transform.rotation=Quaternion.FromToRotation(profile.direction,desired.normalized)*attacker.root.transform.rotation;Physics.SyncTransforms();
        }
        return partId;
    }
    static BlueprintData Read(string name)
    {
        BlockModules.Configure(job.tuning.database);var data=BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(File.ReadAllText(Evidence+"/approved-blueprints/"+name+".json")));
        if(data==null||!data.hasCore||data.name!=name)throw new InvalidDataException("Missing or mismatched approved blueprint "+name);return data;
    }
    static string Hash(string name){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(Evidence+"/approved-blueprints/"+name+".json"))).Replace("-","").ToLowerInvariant();}
    static float Cost(FlightShipRuntime ship){float value=0;foreach(var part in ship.Parts){var module=ship.Tuning.GetDefinition(part.record.shape);if(module)value+=module.cost;}return value;}
    static float HullHp(FlightShipRuntime ship){float value=0;foreach(var part in ship.Parts)if(part.present)value+=part.hp;return value;}
    static void Write()
    {
        var json=Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true).GetMethod("SerializeObject",new[]{typeof(object)});
        File.WriteAllText(ReportPath,(string)json.Invoke(null,new object[]{job.report}));
    }
}
