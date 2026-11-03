using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;
using Object = UnityEngine.Object;

// Editor-only, temporary shooting fixtures. Load with the Unity driver's stopped run_script.
// Nothing in this file is imported into Assets or changes a native tuning asset.
public static class BattleshipCombatProbe
{
    const string FlightPath = "Assets/_Starframe/Settings/Flight/FlightTuning.asset";
    const string BattlePath = "Assets/_Starframe/Settings/Flight/BattleTuning.asset";
    const float FixedDt = .02f;
    static readonly Vector3 FixtureOrigin = new Vector3(30000, 30000, 30000);

    [Serializable] public sealed class PartResult
    {
        public string id, shape;
        public bool present, detached;
        public float hp, maxHp, rebuildProgress;
    }
    [Serializable] public sealed class RunResult
    {
        public string note, attacker, target, aimReferencePartId;
        public float requestedDuration, simulatedSeconds, fixedDt, distance, burstSeconds, pauseSeconds, aimOffsetMeters, boostDuty, targetYawDegrees, aimJitterMeters, aimUpdateSeconds;
        public bool hullRecovery, shieldRecovery, unlimitedPower, targetAlive;
        public int seed, shots, spawnedProjectiles, confirmations, sceneryConfirmations, targetRebuilds, targetPresentParts, targetDetachedParts, aimSamples;
        public float firstHullDamageAt = -1, firstZeroShieldAt = -1, coreDeathAt = -1;
        public float attackerCost, targetCost, initialTargetHullHp, finalTargetHullHp, initialShieldCharge, finalShieldCharge;
        public float actualHullDamage, hullDamagePerSecond, shieldChargeLostIncludingCollapse, shieldChargeRecovered;
        public float attackerMeanPowerRatio, attackerMinEnergy, attackerFinalEnergy, attackerEnergySpent, attackerPeakRequestedWatts, targetEnergySpent;
        public List<PartResult> parts = new List<PartResult>();
    }
    [Serializable] public sealed class CadenceRow
    {
        public string shape, weapon, delivery;
        public int occupiedCells, volleys, spawnedProjectiles, immediateTruePulses, immediateFalsePulses;
        public bool nativeFirstShotImmediate;
        public float authoredRate, damage, reloadSeconds, lockSeconds, watts, firstVolleyAt = -1, meanVolleyInterval = -1, measuredVolleysPerSecond;
        public float immediateTrueRate, immediateFalseRate;
    }
    [Serializable] public sealed class CadenceResult
    {
        public string note;
        public float duration, ratio, dt;
        public List<CadenceRow> weapons = new List<CadenceRow>();
    }
    sealed class ShipFixture
    {
        public GameObject root;
        public ShipAssembly assembly;
        public FlightShipRuntime ship;
        public ShipWeapons weapons;
    }
    sealed class Scope : IDisposable
    {
        readonly List<Object> owned = new List<Object>();
        readonly ModuleDatabase previousDatabase = BlockModules.ConfiguredDatabase;
        readonly UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
        readonly Scene scene;
        readonly Scene previousActiveScene = SceneManager.GetActiveScene();
        public Scope(int seed)
        {
            if (Application.isPlaying) throw new InvalidOperationException("CombatProbe requires stopped Editor mode; it never advances the live range.");
            UnityEngine.Random.InitState(seed);
            // ProjectileWorld uses static Physics queries. A separate local physics scene would not be queried.
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.name = "__BalanceProbe_" + Guid.NewGuid().ToString("N");
            SceneManager.SetActiveScene(scene);
        }
        public T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        public GameObject Root(string label, Vector3 position)
        {
            var root = Own(new GameObject(label));
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.position = position;
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }
        public void Dispose()
        {
            try
            {
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded) SceneManager.SetActiveScene(previousActiveScene);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                Physics.SyncTransforms();
            }
            finally
            {
                BlockModules.Configure(previousDatabase);
                UnityEngine.Random.state = previousRandom;
            }
        }
    }

    /// <summary>
    /// One stationary attacker fires at one stationary native blueprint; the target does not fire back or respawn.
    /// recovery controls structural healing/reconstruction; shieldRecovery independently controls shield recovery.
    /// boostDuty requests native boost watts in the first duty fraction of each 10-second cycle, without moving ships.
    /// Gaussian aim samples rotate the complete ship. Zero jitter is the focused-fire upper bound;
    /// a nonzero jitter is a seeded stationary aim-noise/burst model, not human-validated accuracy.
    /// </summary>
    public static string Run(string attackerBlueprintPath, string targetBlueprintPath, float duration = 240,
        float distance = 600, float burstSeconds = 2, float pauseSeconds = 1, float aimOffsetMeters = 0,
        bool recovery = true, float boostDuty = 0, int seed = 261102, bool shieldRecovery = true, float targetYawDegrees = 180,
        float aimJitterMeters = 0, float aimUpdateSeconds = .25f)
    {
        RequireFinite(duration, "duration"); RequireFinite(distance, "distance");
        RequireFinite(burstSeconds, "burstSeconds"); RequireFinite(pauseSeconds, "pauseSeconds");
        RequireFinite(aimOffsetMeters, "aimOffsetMeters"); RequireFinite(boostDuty, "boostDuty"); RequireFinite(targetYawDegrees, "targetYawDegrees");
        RequireFinite(aimJitterMeters, "aimJitterMeters"); RequireFinite(aimUpdateSeconds, "aimUpdateSeconds");
        if (duration < FixedDt || duration > 1800 || distance < 2 || distance > 20000 || burstSeconds < 0 || pauseSeconds < 0 || boostDuty < 0 || boostDuty > 1 || aimJitterMeters < 0 || aimUpdateSeconds < FixedDt || aimUpdateSeconds > 60)
            throw new ArgumentOutOfRangeException("Fixture arguments exceed their supported finite ranges.");
        var tuning = AssetDatabase.LoadAssetAtPath<FlightTuning>(FlightPath);
        var rules = AssetDatabase.LoadAssetAtPath<BattleTuning>(BattlePath);
        if (!tuning || !tuning.database || !rules) throw new InvalidOperationException("Native flight, module database and battle tuning assets are required.");
        var attackerData = ReadBlueprint(attackerBlueprintPath, tuning.database);
        var targetData = ReadBlueprint(targetBlueprintPath, tuning.database);
        var result = new RunResult
        {
            note = "Stationary one-way native aim-noise/burst model, not human-validated. Gaussian offsets: X sigma=aimJitterMeters, Y sigma=.35*aimJitterMeters, resampled at aimUpdateSeconds quantized to .02s; each update rotates the entire ship instantaneously, with no turret convergence or flight steering simulation. Zero jitter is the focused-fire upper bound. No terrain, countermeasure deployment, wreck collision or respawn. Shield loss includes generator collapse. Native tuning assets were read only.",
            attacker = attackerData.name, target = targetData.name, requestedDuration = duration, fixedDt = FixedDt,
            distance = distance, burstSeconds = burstSeconds, pauseSeconds = pauseSeconds, aimOffsetMeters = aimOffsetMeters,
            aimJitterMeters = aimJitterMeters, aimUpdateSeconds = aimUpdateSeconds,
            hullRecovery = recovery, shieldRecovery = shieldRecovery, boostDuty = boostDuty, targetYawDegrees = targetYawDegrees, seed = seed, unlimitedPower = rules.unlimitedRangePower
        };
        using (var scope = new Scope(seed))
        {
            var world = new ProjectileWorld(); world.Initialize(null);
            try
            {
                var attacker = Create(scope, "Attacker", attackerData, FixtureOrigin, 0, tuning, rules, world);
                var target = Create(scope, "Target", targetData, FixtureOrigin + Vector3.forward * distance, 1, tuning, rules, world);
                target.root.transform.rotation = Quaternion.Euler(0, targetYawDegrees, 0);
                Physics.SyncTransforms();
                result.aimReferencePartId = AimAt(attacker, target.ship.CorePhysicsPosition + Vector3.right * aimOffsetMeters);
                attacker.weapons.Target = target.ship;
                attacker.weapons.HasValidTargetLock = true;
                result.attackerCost = Cost(attacker.ship); result.targetCost = Cost(target.ship);
                result.initialTargetHullHp = HullHp(target.ship); result.initialShieldCharge = target.ship.Shields.Charge;
                result.attackerMinEnergy = attacker.ship.Power.Energy;
                target.ship.PartChanged += part => { if (part.present) result.targetRebuilds++; };
                world.HitFeedbackConfirmed += (owner, scenery) =>
                {
                    if (owner != attacker.ship) return;
                    if (scenery) result.sceneryConfirmations++; else result.confirmations++;
                };
                float recoveryElapsed = 0, ratioIntegral = 0, nextAimTime = 0;
                var aimRandom = new System.Random(seed);
                int steps = Mathf.CeilToInt(duration / FixedDt);
                for (int step = 0; step < steps && target.ship.IsAlive; step++)
                {
                    float dt = Mathf.Min(FixedDt, duration - step * FixedDt);
                    if (dt <= 0) break;
                    float time = Mathf.Min(duration, (step + 1) * FixedDt);
                    if (aimJitterMeters > 0 && step * FixedDt + .000001f >= nextAimTime)
                    {
                        Vector2 noise = GaussianPair(aimRandom) * aimJitterMeters;
                        var aimPoint = target.ship.CorePhysicsPosition + Vector3.right * (aimOffsetMeters + noise.x) + Vector3.up * (noise.y * .35f);
                        result.aimReferencePartId = AimAt(attacker, aimPoint);
                        result.aimSamples++; nextAimTime += aimUpdateSeconds;
                    }
                    attacker.ship.BeginStep(dt); target.ship.BeginStep(dt);
                    float shieldBefore = target.ship.Shields.Charge;
                    world.Step(dt);
                    result.shieldChargeLostIncludingCollapse += Mathf.Max(0, shieldBefore - target.ship.Shields.Charge);
                    Observe(result, target.ship, time);
                    attacker.ship.FlushChanges(); target.ship.FlushChanges();
                    recoveryElapsed += dt;
                    if (recovery && recoveryElapsed + .000001f >= Mathf.Max(.02f, rules.recoveryTick))
                    {
                        attacker.ship.AdvanceRecovery(recoveryElapsed); target.ship.AdvanceRecovery(recoveryElapsed); recoveryElapsed = 0;
                    }
                    if (!target.ship.IsAlive) { result.simulatedSeconds = time; break; }
                    float cycle = burstSeconds + pauseSeconds;
                    attacker.weapons.FireRequested = burstSeconds > 0 && (pauseSeconds <= 0 || (step * FixedDt) % cycle < burstSeconds);
                    bool boost = boostDuty > 0 && (step * FixedDt) % 10 < boostDuty * 10;
                    float attackerDemand = attacker.weapons.Prepare();
                    if (shieldRecovery) attackerDemand += attacker.ship.Shields.PrepareRecovery(dt);
                    if (boost) attackerDemand += Mathf.Max(0, tuning.boostPowerWatts);
                    float attackerRatio = attacker.ship.Power.Allocate(dt, attackerDemand, rules.unlimitedRangePower);
                    if (shieldRecovery) attacker.ship.Shields.ExecuteRecovery(attackerRatio);
                    ratioIntegral += attackerRatio * dt;
                    result.attackerEnergySpent += attackerDemand * attackerRatio * dt;
                    result.attackerPeakRequestedWatts = Mathf.Max(result.attackerPeakRequestedWatts, attackerDemand);
                    result.attackerMinEnergy = Mathf.Min(result.attackerMinEnergy, attacker.ship.Power.Energy);
                    float targetDemand = shieldRecovery ? target.ship.Shields.PrepareRecovery(dt) : 0;
                    float targetRatio = target.ship.Power.Allocate(dt, targetDemand, rules.unlimitedRangePower);
                    shieldBefore = target.ship.Shields.Charge;
                    if (shieldRecovery) target.ship.Shields.ExecuteRecovery(targetRatio);
                    result.shieldChargeRecovered += Mathf.Max(0, target.ship.Shields.Charge - shieldBefore);
                    result.targetEnergySpent += targetDemand * targetRatio * dt;
                    shieldBefore = target.ship.Shields.Charge;
                    attacker.weapons.Execute(dt, attackerRatio);
                    result.shieldChargeLostIncludingCollapse += Mathf.Max(0, shieldBefore - target.ship.Shields.Charge);
                    Observe(result, target.ship, time);
                    result.simulatedSeconds = time;
                }
                result.shots = attacker.weapons.ShotsFired; result.spawnedProjectiles = world.TotalSpawned;
                result.actualHullDamage = target.ship.DamageTaken;
                result.hullDamagePerSecond = result.actualHullDamage / Mathf.Max(FixedDt, result.simulatedSeconds);
                result.attackerMeanPowerRatio = ratioIntegral / Mathf.Max(FixedDt, result.simulatedSeconds);
                result.attackerFinalEnergy = attacker.ship.Power.Energy;
                result.finalTargetHullHp = HullHp(target.ship); result.finalShieldCharge = target.ship.Shields.Charge;
                result.targetAlive = target.ship.IsAlive;
                foreach (var part in target.ship.Parts)
                {
                    if (part.present) result.targetPresentParts++;
                    if (part.detached) result.targetDetachedParts++;
                    result.parts.Add(new PartResult { id = part.record.id, shape = part.record.shape.ToString(), present = part.present,
                        detached = part.detached, hp = part.hp, maxHp = part.maxHp, rebuildProgress = target.ship.GetRebuildProgress(part.record.id) });
                }
                return ReportJson(result);
            }
            finally { world.Clear(); }
        }
    }

    /// <summary>
    /// Measures actual ShipWeapons firing pulses at a constant allocated fraction, without advancing projectile travel.
    /// Direct/beam rows also compare independent cloned activation states with immediate first shot on and off.
    /// Cadence fixtures use a connected native cube platform under the complete weapon footprint.
    /// Missile rows use actual lock/reload logic and report rack volleys separately from spawned missile count.
    /// </summary>
    public static string Cadence(float duration = 60, float ratio = 1, float dt = FixedDt)
    {
        RequireFinite(duration, "duration"); RequireFinite(ratio, "ratio"); RequireFinite(dt, "dt");
        if (duration < dt || duration > 1800 || ratio < 0 || ratio > 1 || dt < .001f || dt > .1f)
            throw new ArgumentOutOfRangeException("Cadence arguments exceed their supported finite ranges.");
        var tuning = AssetDatabase.LoadAssetAtPath<FlightTuning>(FlightPath);
        var rules = AssetDatabase.LoadAssetAtPath<BattleTuning>(BattlePath);
        if (!tuning || !tuning.database || !rules) throw new InvalidOperationException("Native tuning assets are required.");
        var result = new CadenceResult
        {
            note = "Actual native ShipWeapons pulses at constant allocated ratio; projectile travel/hits are not stepped. Direct activation comparisons clone only the weapon in memory. DPS inferred from pulse count is not effective hull DPS.",
            duration = duration, ratio = ratio, dt = dt
        };
        using (var scope = new Scope(261102))
        {
            int index = 0;
            foreach (var module in tuning.database.modules)
            {
                if (!module || !module.weapon) continue;
                var weapon = module.weapon;
                var world = new ProjectileWorld(); world.Initialize(null);
                try
                {
                    Vector3 origin = FixtureOrigin + Vector3.right * (index++ * 1000);
                    var gunData = CadenceBlueprint(module);
                    var targetData = new BlueprintData { name = "Cadence target" };
                    targetData.parts.Add(Record("cadence-target-core", BlockShape.Core, new Vector3(0, .5f, 0)));
                    var attacker = Create(scope, "Cadence attacker", gunData, origin, 0, tuning, rules, world);
                    var target = Create(scope, "Cadence target", targetData, origin + Vector3.forward * 100, 1, tuning, rules, world);
                    attacker.weapons.FireRequested = true; attacker.weapons.Target = target.ship; attacker.weapons.HasValidTargetLock = true;
                    var row = new CadenceRow { shape = module.shape.ToString(), weapon = weapon.name, delivery = weapon.delivery.ToString(),
                        occupiedCells = module.occupiedCells == null ? 0 : module.occupiedCells.Length, nativeFirstShotImmediate = weapon.firstShotImmediate,
                        authoredRate = weapon.shotsPerSecond, damage = weapon.damage, reloadSeconds = weapon.reloadSeconds,
                        lockSeconds = weapon.missileLockSeconds, watts = weapon.sustainedPowerWatts };
                    float lastShot = -1, intervalTotal = 0; int intervals = 0;
                    int steps = Mathf.CeilToInt(duration / dt);
                    for (int step = 0; step < steps; step++)
                    {
                        float tick = Mathf.Min(dt, duration - step * dt); if (tick <= 0) break;
                        int before = attacker.weapons.ShotsFired;
                        attacker.weapons.Prepare(); attacker.weapons.Execute(tick, ratio);
                        int pulses = attacker.weapons.ShotsFired - before;
                        float time = Mathf.Min(duration, (step + 1) * dt);
                        for (int pulse = 0; pulse < pulses; pulse++)
                        {
                            if (row.firstVolleyAt < 0) row.firstVolleyAt = time;
                            if (lastShot >= 0) { intervalTotal += time - lastShot; intervals++; }
                            lastShot = time;
                        }
                        // Missiles do not travel in this cadence-only method; clear every tick to bound storage.
                        world.Clear();
                    }
                    row.volleys = attacker.weapons.ShotsFired; row.spawnedProjectiles = world.TotalSpawned;
                    row.measuredVolleysPerSecond = row.volleys / duration;
                    if (intervals > 0) row.meanVolleyInterval = intervalTotal / intervals;
                    if (weapon.delivery != WeaponDelivery.GuidedMissile)
                    {
                        row.immediateTruePulses = ActivationPulses(scope, weapon, duration, ratio, dt, true);
                        row.immediateFalsePulses = ActivationPulses(scope, weapon, duration, ratio, dt, false);
                        row.immediateTrueRate = row.immediateTruePulses / duration;
                        row.immediateFalseRate = row.immediateFalsePulses / duration;
                    }
                    result.weapons.Add(row);
                }
                finally { world.Clear(); }
            }
            return ReportJson(result);
        }
    }

    [Serializable] public sealed class MatrixRow
    {
        public string attacker, target, reportPath, error;
        public int seed, shots, spawnedProjectiles, confirmations, rebuilds;
        public bool targetAlive;
        public float coreDeathAt, simulatedSeconds, firstHullDamageAt, actualHullDamage, hullDamagePerSecond;
        public float confirmedFractionOfSpawned, meanPowerRatio, minEnergy, remainingShield;
    }
    [Serializable] public sealed class MatrixResult
    {
        public string note, outputDirectory, matrixReportPath;
        public float aimJitterMeters, verticalSigmaMultiplier=.35f, aimUpdateSeconds, duration, distance, burstSeconds, pauseSeconds;
        public bool hullRecovery, shieldRecovery;
        public int seed, seedCount, completedRuns, failedRuns, kills, survivorsAtHorizon;
        public List<MatrixRow> rows=new List<MatrixRow>();
    }

    /// <summary>All nine ordered matchups between Standard, Maneuver and Heavy references; one seed by default.
    /// Full per-part reports are saved separately. The returned matrix summary never includes part arrays.
    /// A surviving target is right-censored at the duration; its coreDeathAt stays -1, not duration.
    /// </summary>
    public static string RunMatrix(string outputDirectory, float jitter = 5, float duration = 240, float distance = 600,
        float burstSeconds = 2, float pauseSeconds = 1, int seed = 261102, int seedCount = 1, float aimUpdateSeconds = .25f,
        bool recovery = true, bool shieldRecovery = true, float boostDuty = 0, float targetYawDegrees = 180)
    {
        if(Application.isPlaying)throw new InvalidOperationException("RunMatrix requires stopped Editor mode.");
        if(string.IsNullOrWhiteSpace(outputDirectory))throw new ArgumentException("An output directory is required.");
        if(seedCount<1||seedCount>8)throw new ArgumentOutOfRangeException("seedCount", "Use one to eight explicit seeds.");
        RequireFinite(jitter,"jitter");RequireFinite(duration,"duration");RequireFinite(aimUpdateSeconds,"aimUpdateSeconds");
        if(jitter<0||duration<FixedDt||duration>1800||aimUpdateSeconds<FixedDt||aimUpdateSeconds>60)
            throw new ArgumentOutOfRangeException("Invalid finite matrix duration or aim-noise settings.");
        var labels=new[]{"Assault","Maneuver","Heavy"};
        string blueprintDirectory=Path.GetFullPath("Documentation/Battleship-2026-11-02/Blueprints");
        foreach(string label in labels)if(!File.Exists(Path.Combine(blueprintDirectory,"Battleship-"+label+".json")))
            throw new FileNotFoundException("Missing reference blueprint: "+label);
        string directory=Path.GetFullPath(outputDirectory);Directory.CreateDirectory(directory);
        var matrix=new MatrixResult {
            note="Stationary one-way aim-noise/burst model, not human-validated. Entire-ship instantaneous aiming; Gaussian X sigma=jitter and Y sigma=.35*jitter. No turret convergence, flight movement, terrain or flare deployment. Missiles still use native guidance. Confirmation fraction counts shield hits; unresolved spawned projectiles remain in the denominator. Survivors are censored, not kills at 240 seconds. Full reports contain per-part results.",
            outputDirectory=directory, matrixReportPath=Path.Combine(directory,"matrix-summary.json"), aimJitterMeters=jitter,
            aimUpdateSeconds=aimUpdateSeconds, duration=duration, distance=distance, burstSeconds=burstSeconds, pauseSeconds=pauseSeconds,
            hullRecovery=recovery, shieldRecovery=shieldRecovery, seed=seed, seedCount=seedCount
        };
        foreach(string attacker in labels)foreach(string target in labels)for(int seedIndex=0;seedIndex<seedCount;seedIndex++)
        {
            int caseSeed=unchecked(seed+seedIndex);
            string reportPath=Path.Combine(directory,attacker+"-vs-"+target+"-seed-"+caseSeed+".json");
            var row=new MatrixRow{attacker=attacker,target=target,seed=caseSeed,reportPath=reportPath,coreDeathAt=-1,firstHullDamageAt=-1};
            try
            {
                string json=Run(Path.Combine(blueprintDirectory,"Battleship-"+attacker+".json"),
                    Path.Combine(blueprintDirectory,"Battleship-"+target+".json"),duration,distance,burstSeconds,pauseSeconds,0,
                    recovery,boostDuty,caseSeed,shieldRecovery,targetYawDegrees,jitter,aimUpdateSeconds);
                File.WriteAllText(reportPath,json);
                var result=(RunResult)ReadReportJson(json,typeof(RunResult));
                row.targetAlive=result.targetAlive;row.coreDeathAt=result.coreDeathAt;row.simulatedSeconds=result.simulatedSeconds;
                row.firstHullDamageAt=result.firstHullDamageAt;row.actualHullDamage=result.actualHullDamage;row.hullDamagePerSecond=result.hullDamagePerSecond;
                row.shots=result.shots;row.spawnedProjectiles=result.spawnedProjectiles;row.confirmations=result.confirmations;row.rebuilds=result.targetRebuilds;
                row.confirmedFractionOfSpawned=result.spawnedProjectiles>0?(float)result.confirmations/result.spawnedProjectiles:0;
                row.meanPowerRatio=result.attackerMeanPowerRatio;row.minEnergy=result.attackerMinEnergy;row.remainingShield=result.finalShieldCharge;
                matrix.completedRuns++;if(result.targetAlive)matrix.survivorsAtHorizon++;else matrix.kills++;
            }
            catch(Exception error)
            {
                row.error=error.GetType().Name+": "+error.Message;matrix.failedRuns++;
                File.WriteAllText(Path.ChangeExtension(reportPath,"error.txt"),error.ToString());
            }
            matrix.rows.Add(row);
            // Persist each completed case so an interrupted batch still leaves all finished evidence.
            File.WriteAllText(matrix.matrixReportPath,ReportJson(matrix));
        }
        return ReportJson(matrix);
    }
    static Vector2 GaussianPair(System.Random random)
    {
        double radius=Math.Sqrt(-2*Math.Log(Math.Max(double.Epsilon,random.NextDouble())));
        double angle=2*Math.PI*random.NextDouble();
        return new Vector2((float)(radius*Math.Cos(angle)),(float)(radius*Math.Sin(angle)));
    }
    static object ReadReportJson(string json,Type resultType)
    {
        var jsonType=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert",true);
        var deserialize=jsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)});
        if(deserialize==null)throw new InvalidOperationException("Newtonsoft DeserializeObject(string,Type) is unavailable.");
        return deserialize.Invoke(null,new object[]{json,resultType});
    }
    static int ActivationPulses(Scope scope, WeaponDefinition source, float duration, float ratio, float dt, bool immediate)
    {
        var weapon = scope.Own(Object.Instantiate(source)); weapon.firstShotImmediate = immediate;
        var state = new WeaponActivationState(); int pulses = 0;
        int steps = Mathf.CeilToInt(duration / dt);
        for (int step = 0; step < steps; step++)
        {
            float tick = Mathf.Min(dt, duration - step * dt); if (tick <= 0) break;
            if (state.Prepare(weapon, new WeaponInputState(true, step == 0), true)) pulses += state.Advance(weapon, tick, ratio);
        }
        return pulses;
    }
    static ShipFixture Create(Scope scope, string label, BlueprintData data, Vector3 position, int team,
        FlightTuning tuning, BattleTuning rules, ProjectileWorld world)
    {
        var root = scope.Root(label, position);
        var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        var assemblyObject = new GameObject("Assembly"); assemblyObject.transform.SetParent(root.transform, false);
        var assembly = assemblyObject.AddComponent<ShipAssembly>(); assembly.Configure(tuning.database); assembly.Import(data);
        var runtime = root.AddComponent<FlightShipRuntime>(); runtime.Initialize(assembly, assembly.Export(data.name), tuning, rules); runtime.Team = team;
        Quaternion correction = Quaternion.Inverse(runtime.CoreRotation);
        assembly.transform.localRotation = correction; assembly.transform.localPosition = -(correction * runtime.CenterOfMass());
        foreach (var t in assembly.GetComponentsInChildren<Transform>(true))
            if (t.name == "ShipForwardRay" || t.name == "PlacementCollisionProbe" || t.name == "ImportCollisionProbe") t.gameObject.SetActive(false);
        var weapons = new ShipWeapons(); weapons.Initialize(runtime, world);
        world.RegisterShields(runtime); Physics.SyncTransforms();
        return new ShipFixture { root = root, assembly = assembly, ship = runtime, weapons = weapons };
    }
    static string AimAt(ShipFixture attacker, Vector3 point)
    {
        string partId = null;
        var profiles = new List<WeaponAimProfile>();
        for (int iteration = 0; iteration < 3; iteration++)
        {
            attacker.weapons.GetAimProfiles(profiles); if (profiles.Count == 0) return null;
            int chosen = 0; float best = -2;
            for (int i = 0; i < profiles.Count; i++)
            {
                float facing = Vector3.Dot(profiles[i].direction, attacker.root.transform.forward);
                if (facing > best) { best = facing; chosen = i; }
            }
            var profile = profiles[chosen]; partId = profile.partId;
            Vector3 desired = point - profile.origin;
            if (desired.sqrMagnitude <= .000001f) break;
            attacker.root.transform.rotation = Quaternion.FromToRotation(profile.direction, desired.normalized) * attacker.root.transform.rotation;
            Physics.SyncTransforms();
        }
        return partId;
    }
    static BlueprintData ReadBlueprint(string path, ModuleDatabase database)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A blueprint JSON path is required.");
        var previous = BlockModules.ConfiguredDatabase;
        try
        {
            BlockModules.Configure(database);
            var data = BlueprintRepository.Upgrade(JsonUtility.FromJson<BlueprintData>(File.ReadAllText(Path.GetFullPath(path))));
            if (data == null || !data.hasCore) throw new InvalidDataException("Fixture blueprint requires one core.");
            return data;
        }
        finally { BlockModules.Configure(previous); }
    }
    static BlueprintData CadenceBlueprint(ModuleDefinition module)
    {
        var data = new BlueprintData { name = "Cadence " + module.shape };
        int minX = 0, maxX = 0, minY = 0, minZ = 0, maxZ = 0;
        if (module.occupiedCells != null) foreach (var cell in module.occupiedCells)
        {
            minX = Mathf.Min(minX, cell.x); maxX = Mathf.Max(maxX, cell.x); minY = Mathf.Min(minY, cell.y);
            minZ = Mathf.Min(minZ, cell.z); maxZ = Mathf.Max(maxZ, cell.z);
        }
        data.parts.Add(Record("cadence-core", BlockShape.Core, new Vector3(0, .5f, 0)));
        for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
            data.parts.Add(Record("cadence-support-" + x + "-" + z, BlockShape.Cube, new Vector3(x, 1.5f, z)));
        data.parts.Add(Record("cadence-gun", module.shape, new Vector3(0, 2.5f - minY, 0)));
        return data;
    }
    static BlockRecord Record(string id, BlockShape shape, Vector3 position)
    {
        return new BlockRecord { id = id, shape = shape, position = position, cell = BlockPlacement.GridHint(position), rotation = Quaternion.identity };
    }
    static void Observe(RunResult result, FlightShipRuntime target, float time)
    {
        if (result.firstHullDamageAt < 0 && target.DamageTaken > 0) result.firstHullDamageAt = time;
        if (result.firstZeroShieldAt < 0 && result.initialShieldCharge > 0 && target.Shields.Charge <= .0001f) result.firstZeroShieldAt = time;
        if (result.coreDeathAt < 0 && !target.IsAlive) result.coreDeathAt = time;
    }
    static float Cost(FlightShipRuntime ship)
    {
        float value = 0; foreach (var part in ship.Parts) { var definition = ship.Tuning.GetDefinition(part.record.shape); if (definition) value += definition.cost; } return value;
    }
    static float HullHp(FlightShipRuntime ship)
    {
        float value = 0; foreach (var part in ship.Parts) if (part.present) value += part.hp; return value;
    }
    static string ReportJson(object value)
    {
        // The dynamically compiled helper assembly is not registered with Unity's native serializer.
        // Resolve Newtonsoft by assembly to avoid Localization's embedded same-named JSON types.
        var jsonType=System.Reflection.Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
        var serialize=jsonType.GetMethod("SerializeObject",new[]{typeof(object)});
        if(serialize==null)throw new InvalidOperationException("Newtonsoft SerializeObject(object) is unavailable.");
        return (string)serialize.Invoke(null,new[]{value});
    }
    static void RequireFinite(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "A finite value is required.");
    }
}







